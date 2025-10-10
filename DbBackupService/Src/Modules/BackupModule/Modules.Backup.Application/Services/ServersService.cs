using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Administration.Shared.Interfaces;
using Modules.Auth.Core.Entities;
using Modules.Auth.Infrastructure.DbContexts;
using Modules.Auth.Shared.Static.Entities;
using Modules.Backup.Application.Static;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;
using Modules.Backup.Shared.Requests;
using Modules.Crypto.Shared.Interfaces;
using OneOf;
using OneOf.Types;

namespace Modules.Backup.Application.Services;

public class ServersService(
    BackupsDbContext db,
    AppIdentityDbContext appIdentityDbContext,
    UserManager<AppUser> userManager,
    ILogger<ServersService> logger,
    NotifyService notifyService,
    ICryptoService cryptoService,
    IAdminModuleApi adminApi)
{
    public async Task<OneOf<List<ServerConnectionDto>, string>> GetServers(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName))
            return "Can't access user name";

        var user = await userManager.FindByNameAsync(identityName);
        if (user is null)
            return "Can't find user";

        var isAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);

        List<DbServerConnection> dbServers;

        if (!isAdmin)
        {
            var userServers = db.UsersServers
                .AsNoTracking()
                .Where(x => x.UserId == Guid.Parse(user.Id))
                .Select(x => x.ServerId)
                .ToList();

            if (userServers.Count == 0)
                return new List<ServerConnectionDto>();

            dbServers = db.DbConnections
                .AsNoTracking()
                .Where(x => userServers.Contains(x.Id))
                .ToList();

            dbServers.RemoveAll(x => x.IsDisabled);
        }
        else
        {
            dbServers = db.DbConnections
                .AsNoTracking()
                .ToList();
        }

        var dtoServers = new List<ServerConnectionDto>();

        foreach (var server in dbServers)
        {
            var dto = new ServerConnectionDto
            {
                Id = server.Id,
                ConnectionName = server.ConnectionName,
                ServerHost = server.ServerHost,
                ServerPort = server.ServerPort,
                DbName = server.DbName,
                DbType = server.DbType,
                DbUser = server.DbUser,
                IsTunnelRequired = server.IsTunnelRequired,
                AutoTestBackupsConfigId = server.AutoTestBackupsConfigId
            };

            var dbTunnel = db.DbServerTunnels.FirstOrDefault(x => x.Id == server.TunnelId);

            if (dbTunnel is not null)
                dto.Tunnel = new ServerTunnelDto
                {
                    Id = dbTunnel.Id,
                    ServerHost = dbTunnel.ServerHost,
                    SshPort = dbTunnel.SshPort,
                    Username = dbTunnel.Username,
                    LocalPort = dbTunnel.LocalPort,
                    RemoteHost = dbTunnel.RemoteHost,
                    RemotePort = dbTunnel.RemotePort,
                    Description = dbTunnel.Description
                };

            dtoServers.Add(dto);
        }

        return dtoServers;
    }

    public async Task<OneOf<List<ServerNameIdDto>, string>> GetAvailableServersBasic(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName))
            return "Can't access user name";

        var user = await userManager.FindByNameAsync(identityName);
        if (user is null)
            return "Can't find user";

        var isAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);

        List<DbServerConnection> dbServers;

        if (!isAdmin)
        {
            var userServers = db.UsersServers
                .AsNoTracking()
                .Where(x => x.UserId == Guid.Parse(user.Id))
                .Select(x => x.ServerId)
                .ToList();

            if (userServers.Count == 0)
                return new List<ServerNameIdDto>();

            dbServers = db.DbConnections
                .AsNoTracking()
                .Where(x => userServers.Contains(x.Id))
                .ToList();

            dbServers.RemoveAll(x => x.IsDisabled);
        }
        else
        {
            dbServers = db.DbConnections
                .AsNoTracking()
                .ToList();
        }

        var dtoServers = dbServers
            .Select(x => new ServerNameIdDto(x.Id, x.ConnectionName))
            .ToList();

        return dtoServers;
    }

    public async Task<OneOf<Success, string>> CreateServer(ServerConnectionDto newServer, string? identityName)
    {
        List<string> errors = new();

        if (string.IsNullOrWhiteSpace(newServer.ConnectionName))
            errors.Add("Connection name is required");
        if (string.IsNullOrWhiteSpace(newServer.ServerHost))
            errors.Add("Server host is required");
        if (newServer.ServerPort <= 0)
            errors.Add("Invalid server port");
        if (string.IsNullOrWhiteSpace(newServer.DbName))
            errors.Add("Database name is required");
        if (string.IsNullOrWhiteSpace(newServer.DbUser))
            errors.Add("Database user is required");
        if (string.IsNullOrWhiteSpace(newServer.DbPasswd))
            errors.Add("Database password is required");

        if (newServer.IsTunnelRequired)
        {
            if (newServer.Tunnel is null)
                errors.Add("Tunnel configuration required");

            if (newServer.Tunnel is not null)
            {
                if (string.IsNullOrWhiteSpace(newServer.Tunnel.ServerHost))
                    errors.Add("Tunnel host is required");
                if (string.IsNullOrWhiteSpace(newServer.Tunnel.Username))
                    errors.Add("Tunnel username is required");

                if (string.IsNullOrWhiteSpace(newServer.Tunnel.PrivateKeyContent))
                {
                    if (string.IsNullOrWhiteSpace(newServer.Tunnel.Password))
                        errors.Add("Tunnel password is required");
                }
                else if (string.IsNullOrWhiteSpace(newServer.Tunnel.PrivateKeyPassphrase))
                {
                    errors.Add("Tunnel private key password is required");
                }

                if (newServer.Tunnel.LocalPort is <= 0 or > 65535)
                    errors.Add("Invalid tunnel local port");
                if (string.IsNullOrWhiteSpace(newServer.Tunnel.RemoteHost))
                    errors.Add("Tunnel remote host is required");
                if (newServer.Tunnel.RemotePort is <= 0 or > 65535)
                    errors.Add("Invalid tunnel remote port");
            }
        }

        if (errors.Any())
            return string.Join(", ", errors);

        var encryptedDbPasswd = cryptoService.Encrypt(newServer.DbPasswd!);
        // 2. Tworzymy obiekt encji serwera
        var dbServer = new DbServerConnection
        {
            Id = Guid.CreateVersion7(),
            ConnectionName = newServer.ConnectionName,
            ServerHost = newServer.ServerHost,
            ServerPort = newServer.ServerPort,
            DbName = newServer.DbName,
            DbType = newServer.DbType,
            DbUser = newServer.DbUser,
            DbPasswd = encryptedDbPasswd ?? throw new ArgumentException("Db password can't be null"),
            IsTunnelRequired = newServer.IsTunnelRequired,
            IsDisabled = false
        };

        var serverAutoTestBackupConfig = new AutomaticBackupTestConfig
        {
            Id = Guid.CreateVersion7(),
            // TODO: Change this to global setting that is configurable in admin's config panel
            IsEnabled = false,
            ServerId = dbServer.Id
        };
        
        dbServer.AutoTestBackupsConfigId = serverAutoTestBackupConfig.Id;
        
        // 3. Obsługa tunelu (opcjonalna)
        if (newServer.IsTunnelRequired)
        {
            var encryptedPassword = cryptoService.Encrypt(newServer.Tunnel?.Password);
            var encryptedPem = cryptoService.Encrypt(newServer.Tunnel?.PrivateKeyContent);
            var encryptedPemPassphrase = cryptoService.Encrypt(newServer.Tunnel?.PrivateKeyPassphrase);

            var dbTunnel = new DbServerTunnel
            {
                Id = Guid.CreateVersion7(),
                ServerHost = newServer.Tunnel!.ServerHost!,
                SshPort = newServer.Tunnel.SshPort,
                Username = newServer.Tunnel.Username!,
                UsePasswordAuth = string.IsNullOrWhiteSpace(newServer.Tunnel.PrivateKeyContent),
                Password = encryptedPassword,
                PrivateKeyContent = encryptedPem,
                PrivateKeyPassphrase = encryptedPemPassphrase,
                LocalPort = newServer.Tunnel.LocalPort,
                RemoteHost = newServer.Tunnel.RemoteHost!,
                RemotePort = newServer.Tunnel.RemotePort,
                Description = newServer.Tunnel.Description,
                IsActive = true
            };

            // zapisujemy tunel
            db.DbServerTunnels.Add(dbTunnel);
            dbServer.TunnelId = dbTunnel.Id;
        }

        // 4. Zapis serwera
        db.DbConnections.Add(dbServer);
        db.AutomaticBackupTestConfigs.Add(serverAutoTestBackupConfig);
        
        if (string.IsNullOrWhiteSpace(identityName))
            return "Can't access username";

        var user = await userManager.FindByNameAsync(identityName);

        if (user is null)
            return "Can't find user";

        var isAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);

        var serverUser = new ServersUsers
        {
            UserId = Guid.Parse(user.Id),
            ServerId = dbServer.Id
        };

        if (!isAdmin)
            db.UsersServers.Add(serverUser);

        var serverBackupConfig = new ServerBackupsConfiguration
        {
            Id = Guid.CreateVersion7(),
            ServerId = dbServer.Id,
            TimeInDaysToHoldBackups = 4
        };

        db.Configurations.Add(serverBackupConfig);

        await db.SaveChangesAsync();

        await notifyService.CallServerCreatedEvent(user.UserName!);

        return new Success();
    }

    public async Task<OneOf<Success, string>> EditServer(ServerConnectionDto server)
    {
        // TODO: Implement checking if user has access to server or is admin
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(server.ConnectionName))
            errors.Add("Connection name is required");
        if (string.IsNullOrWhiteSpace(server.ServerHost))
            errors.Add("Server host is required");
        if (server.ServerPort <= 0)
            errors.Add("Invalid server port");
        if (string.IsNullOrWhiteSpace(server.DbName))
            errors.Add("Database name is required");
        if (string.IsNullOrWhiteSpace(server.DbUser))
            errors.Add("Database user is required");

        if (server.IsTunnelRequired)
        {
            if (server.Tunnel is null)
                errors.Add("Tunnel configuration required");

            if (server.Tunnel is not null)
            {
                if (string.IsNullOrWhiteSpace(server.Tunnel.ServerHost))
                    errors.Add("Tunnel host is required");
                if (string.IsNullOrWhiteSpace(server.Tunnel.Username))
                    errors.Add("Tunnel username is required");

                if (server.Tunnel.LocalPort is <= 0 or > 65535)
                    errors.Add("Invalid tunnel local port");
                if (string.IsNullOrWhiteSpace(server.Tunnel.RemoteHost))
                    errors.Add("Tunnel remote host is required");
                if (server.Tunnel.RemotePort is <= 0 or > 65535)
                    errors.Add("Invalid tunnel remote port");
            }
        }

        if (errors.Any())
            return string.Join(", ", errors);

        // 2. Szukamy istniejącego serwera
        var dbServer = await db.DbConnections
            .FirstOrDefaultAsync(s => s.Id == server.Id);

        if (dbServer is null)
            return $"Server with id {server.Id} not found";

        // 3. Aktualizacja danych serwera
        dbServer.ConnectionName = server.ConnectionName;
        dbServer.ServerHost = server.ServerHost;
        dbServer.ServerPort = server.ServerPort;
        dbServer.DbName = server.DbName;
        dbServer.DbType = server.DbType;
        dbServer.DbUser = server.DbUser;

        if (!string.IsNullOrWhiteSpace(server.DbPasswd))
        {
            var encryptedDbPasswd = cryptoService.Encrypt(server.DbPasswd);
            dbServer.DbPasswd = encryptedDbPasswd!;
        }

        dbServer.IsTunnelRequired = server.IsTunnelRequired;

        // 4. Obsługa tunelu
        if (server.IsTunnelRequired)
        {
            var dbTunnel = await db.DbServerTunnels
                .FirstOrDefaultAsync(t => t.Id == server.Tunnel!.Id);

            if (dbTunnel is null)
            {
                // jeśli tunel nie istnieje, tworzymy nowy
                dbTunnel = new DbServerTunnel
                {
                    Id = Guid.CreateVersion7(),
                    IsActive = true,
                    ServerHost = "",
                    Username = "",
                    RemoteHost = ""
                };

                db.DbServerTunnels.Add(dbTunnel);
            }

            // aktualizacja wartości tunelu
            dbTunnel.ServerHost = server.Tunnel!.ServerHost!;
            dbTunnel.SshPort = server.Tunnel.SshPort;
            dbTunnel.Username = server.Tunnel.Username!;

            if (server.Tunnel.OverridePasswordsAndPem)
            {
                var encryptedPassword = cryptoService.Encrypt(server.Tunnel?.Password);
                var encryptedPem = cryptoService.Encrypt(server.Tunnel?.PrivateKeyContent);
                var encryptedPemPassphrase = cryptoService.Encrypt(server.Tunnel?.PrivateKeyPassphrase);

                dbTunnel.Password = encryptedPassword;
                dbTunnel.PrivateKeyContent = encryptedPem;
                dbTunnel.PrivateKeyPassphrase = encryptedPemPassphrase;
            }

            dbTunnel.LocalPort = server.Tunnel!.LocalPort;
            dbTunnel.RemoteHost = server.Tunnel!.RemoteHost!;
            dbTunnel.RemotePort = server.Tunnel.RemotePort;
            dbTunnel.Description = server.Tunnel.Description;

            dbTunnel.UsePasswordAuth = string.IsNullOrWhiteSpace(dbTunnel.PrivateKeyContent);

            dbServer.TunnelId = dbTunnel.Id;
        }

        await db.SaveChangesAsync();

        await notifyService.CallServerHasChangedEvent(dbServer.Id);

        return new Success();
    }

    public async Task<OneOf<Success, string>> ToggleDisabledStatus(Guid serverId, string? username)
    {
        if (serverId == Guid.Empty)
            return "Id can't be empty";

        var server = await db.DbConnections.FirstOrDefaultAsync(x => x.Id == serverId);

        if (server is null)
            return "Can't find specified server";

        if (string.IsNullOrWhiteSpace(username))
            return "Can't access username";

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
            return "Can't find user";

        var isAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);
        if (isAdmin)
        {
            server.IsDisabled = !server.IsDisabled;
        }
        else
        {
            var access = await db.UsersServers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == Guid.Parse(user.Id) && x.ServerId == serverId);

            if (access is null)
                return "User does not have required access to this server";

            server.IsDisabled = true;
        }

        await db.SaveChangesAsync();

        if (server.IsDisabled)
        {
            await notifyService.CallServerHasChangedEvent(server.Id);
        }
        else
        {
            var usersWithAccess = db.UsersServers
                .AsNoTracking()
                .Where(x => x.ServerId == serverId)
                .ToList();

            foreach (var userAccess in usersWithAccess)
            {
                var tempUser = await userManager.FindByIdAsync(userAccess.UserId.ToString());
                await notifyService.CallServerCreatedEvent(tempUser?.UserName!);
            }
        }

        return new Success();
    }

    public Task<List<ServersUsersListDto>> GetServersUsers()
    {
        var servers = db.DbConnections
            .AsNoTracking()
            .Select(x => new ServersUsersListDto
            {
                ServerId = x.Id,
                IsServerDisabled = x.IsDisabled,
                ServerConnectionName = x.ConnectionName
            }).ToList();

        foreach (var server in servers)
        {
            var usersWithAccess = db.UsersServers
                .Count(x => x.ServerId == server.ServerId);

            server.UsersWithAccess = usersWithAccess;
        }

        return Task.FromResult(servers);
    }

    public Task<OneOf<List<string>, string>> GetUsersThatAccessServer(Guid serverId)
    {
        if (serverId == Guid.Empty)
            return Task.FromResult<OneOf<List<string>, string>>("Id cant be empty");

        try
        {
            var serversUsersIds = db.UsersServers
                .AsNoTracking()
                .Where(x => x.ServerId == serverId)
                .Select(x => x.UserId)
                .ToList();

            var usersEmails = new List<string>();

            foreach (var userId in serversUsersIds)
                usersEmails.Add(appIdentityDbContext.Users.FirstOrDefaultAsync(x => x.Id == userId.ToString()).Result
                    ?.Email!);

            return Task.FromResult<OneOf<List<string>, string>>(usersEmails!);
        }
        catch (Exception e)
        {
            return Task.FromResult<OneOf<List<string>, string>>(e.Message);
        }
    }

    public async Task<OneOf<List<string>, string>> GetAllUsersThatDoesNotHaveAccessToServer(Guid serverId)
    {
        if (serverId == Guid.Empty)
            return "Id cant be empty";

        try
        {
            var serversUsersIds = await db.UsersServers
                .AsNoTracking()
                .Where(x => x.ServerId == serverId)
                .Select(x => x.UserId)
                .ToListAsync();

            var allUsers = await appIdentityDbContext.Users
                .AsNoTracking().Where(x => !x.IsBlocked)
                .Select(u => new { u.Id, u.Email })
                .ToListAsync();

            var usersWithoutAccess = new List<string>();

            foreach (var user in allUsers)
            {
                var dbUser = await userManager.FindByIdAsync(user.Id);
                if (await adminApi.AmIAdmin(dbUser?.UserName))
                    continue;

                if (!serversUsersIds.Contains(Guid.Parse(user.Id)))
                    usersWithoutAccess.Add(user.Email!);
            }

            return usersWithoutAccess;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }


    public async Task<OneOf<Success, string>> RemoveUserAccessFromServer(ModifyServerAccessRequest request)
    {
        if (request.ServerId == Guid.Empty || string.IsNullOrWhiteSpace(request.UserEmail))
            return "Invalid request";

        var user = await userManager.FindByEmailAsync(request.UserEmail);
        if (user is null)
            return "Can't find user";

        var serverUserAccess = await db.UsersServers
            .FirstOrDefaultAsync(x => x.ServerId == request.ServerId && x.UserId == Guid.Parse(user.Id));

        if (serverUserAccess is null)
            return "Can't find specified user access";

        db.UsersServers.Remove(serverUserAccess);
        await db.SaveChangesAsync();

        await notifyService.CallServerHasChangedEvent(request.ServerId);

        return new Success();
    }

    public async Task<OneOf<Success, string>> GiveUserAccessToServer(ModifyServerAccessRequest request)
    {
        if (request.ServerId == Guid.Empty || string.IsNullOrWhiteSpace(request.UserEmail))
            return "Invalid request";

        var user = await userManager.FindByEmailAsync(request.UserEmail);
        if (user is null)
            return "Can't find user";

        var newServerUser = new ServersUsers
        {
            ServerId = request.ServerId,
            UserId = Guid.Parse(user.Id)
        };

        db.UsersServers.Add(newServerUser);
        await db.SaveChangesAsync();

        await notifyService.CallServerCreatedEvent(user.UserName!);

        return new Success();
    }

    public async Task<OneOf<Success, string>> TestServerConnection(Guid serverId)
    {
        var serverConn = db.DbConnections.FirstOrDefault(x => x.Id == serverId);

        if (serverConn is null)
            return "Can't find server";

        DbServerTunnel? tunnel = null;

        if (serverConn.IsTunnelRequired)
            tunnel = db.DbServerTunnels.FirstOrDefault(x => x.Id == serverConn.TunnelId);

        var testResult = await ServerConnectionTester.TestConnectionAsync(serverConn, tunnel, cryptoService);

        return testResult.Result ? new Success() : testResult.ErrorMsg!;
    }

    public async Task<OneOf<Success, string>> DeleteServer(Guid serverId)
    {
        var server = db.DbConnections.FirstOrDefault(x => x.Id == serverId);
        if (server is null)
            return "Can't access server or server does not exist";

        try
        {
            if (db.UsersServers.Any())
            {
                var usersAccess = db.UsersServers.Where(x => x.ServerId == serverId);
                if (usersAccess.Any())
                {
                    db.UsersServers.RemoveRange(usersAccess);
                    await db.SaveChangesAsync();

                    await notifyService.CallServerHasChangedEvent(serverId);
                }
            }

            if (db.Schedules.Any())
            {
                var schedules = db.Schedules.Where(x => x.DbConnectionId == serverId);
                if (schedules.Any())
                {
                    db.Schedules.RemoveRange(schedules);
                    await db.SaveChangesAsync();

                    await notifyService.CallScheduleHasChangedEvent(schedules.First().Id);
                }
            }

            var serverBackupConfig = db.Configurations.FirstOrDefault(x => x.ServerId == serverId);
            if (serverBackupConfig is not null) db.Configurations.Remove(serverBackupConfig);
            
            var serverAutoTestConfig = db.AutomaticBackupTestConfigs.FirstOrDefault(x => x.ServerId == serverId);
            if (serverAutoTestConfig is not null) db.AutomaticBackupTestConfigs.Remove(serverAutoTestConfig);

            var backups = db.Backups.Where(x => x.ServerConnectionId == server.Id);
            if (backups.Any())
                foreach (var backup in backups)
                    backup.ServerConnectionId = null;

            db.DbConnections.Remove(server);
            await db.SaveChangesAsync();

            await notifyService.CallServerHasChangedEvent(serverId);
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return e.Message;
        }

        return new Success();
    }


    public async Task<OneOf<AutoTestBackupConfigDto, string>> GetAutoTestConfig(Guid configId)
    {
        if (configId == Guid.Empty)
            return "Invalid config id";

        logger.LogInformation("Checking if config [{ConfigId}] exists...", configId);
        
        var config = await db.AutomaticBackupTestConfigs.FindAsync(configId);
        if (config is null)
            return "Can't find config";

        return new AutoTestBackupConfigDto
        {
            Id = config.Id,
            IsEnabled = config.IsEnabled
        };
    }

    public async Task<OneOf<Success, string>> EditAutoTestConfig(AutoTestBackupConfigDto config)
    {
        var dbConfig = await db.AutomaticBackupTestConfigs.FindAsync(config.Id);
        if (dbConfig is null)
            return "Can't find config";

        dbConfig.IsEnabled = config.IsEnabled;
        await db.SaveChangesAsync();
        
        return new Success();
    }
}