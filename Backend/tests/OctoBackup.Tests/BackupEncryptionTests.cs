using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Application.Services;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Enums;
using Xunit;

public class BackupEncryptionFormatTests
{
    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(1048576)] [InlineData(2097161)]
    public async Task Portable_Format_RoundTrips_And_Rejects_Tampering_Truncation_And_Wrong_Keys(int size)
    {
        var bytes = RandomNumberGenerator.GetBytes(size);
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = BackupEncryptionFormat.Derive("a long enough recovery password", salt);
        var id = Guid.NewGuid();
        using var encrypted = new MemoryStream();
        await BackupEncryptionFormat.Encrypt(new MemoryStream(bytes), encrypted, id, salt, key, default);
        using var recovered = new MemoryStream(); encrypted.Position = 0;
        await BackupEncryptionFormat.Decrypt(encrypted, recovered, key, id, default);
        Assert.Equal(bytes, recovered.ToArray());
        var original = encrypted.ToArray();
        var broken = original.ToArray(); broken[^1] ^= 1;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryptionFormat.Decrypt(new MemoryStream(broken), new MemoryStream(), key, id, default));
        await Assert.ThrowsAnyAsync<Exception>(() => BackupEncryptionFormat.Decrypt(new MemoryStream(original[..^20]), new MemoryStream(), key, id, default));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryptionFormat.Decrypt(new MemoryStream(original), new MemoryStream(), RandomNumberGenerator.GetBytes(32), id, default));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryptionFormat.Decrypt(new MemoryStream(original.Concat(new byte[] { 0 }).ToArray()), new MemoryStream(), key, id, default));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryptionFormat.Decrypt(new MemoryStream(original), new MemoryStream(), key, Guid.NewGuid(), default));
        broken = original.ToArray(); broken[40] ^= 1;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => BackupEncryptionFormat.Decrypt(new MemoryStream(broken), new MemoryStream(), key, id, default));
    }
}

public class BackupEncryptionTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static ClaimsPrincipal Principal(bool admin, Guid? userId = null) => new(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
        new Claim(ClaimTypes.Role, admin ? "Admin" : "User") }, "test"));

    [Fact]
    public async Task Rotation_Reset_And_Inheritance_Keep_Older_Artifacts_Readable_Only_With_Their_Own_Password()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<BackupEncryptionService>();
        var admin = Principal(true); var operatorId = Guid.NewGuid(); var user = Principal(false, operatorId);
        var server = new DbServerConnection { ServerHost = "localhost", ServerPort = 1, DbName = "fixture", DbUser = "fixture", DbPasswd = "" };
        db.DbConnections.Add(server); db.UsersServers.Add(new() { ServerId = server.Id, UserId = operatorId }); await db.SaveChangesAsync();
        var global = await encryption.Effective(null, default);
        Assert.Null(global);
        Assert.False(await encryption.GlobalEnabled());
        await Assert.ThrowsAsync<BackupQueueException>(() => encryption.ExportGenerated(user, default));
        var inherited = await encryption.Status(server.Id, user, default); Assert.True(inherited.UsesGlobalKey);
        await encryption.Change(server.Id, new(inherited.KeyId, null, "individual password first", "individual password first"), user, default);
        var first = (await encryption.Effective(server.Id, default))!; Assert.NotNull(first);
        Directory.CreateDirectory(SafeBackupPath.Root);
        var plaintext = Path.Combine(SafeBackupPath.Root, Guid.NewGuid() + ".zip");
        await File.WriteAllTextAsync(plaintext, "synthetic backup content");
        var artifact = await encryption.Encrypt(plaintext, server.Id, default);
        Assert.False(File.Exists(plaintext)); Assert.DoesNotContain("synthetic backup content", await File.ReadAllTextAsync(artifact.Path));
        var backup = new PerformedBackup { Name = "test", FilePath = artifact.Path, EncryptionKeyId = artifact.KeyId };
        await Assert.ThrowsAsync<BackupQueueException>(() => encryption.OpenDecrypted(backup, "incorrect", true, default));
        await Assert.ThrowsAsync<BackupQueueException>(() => encryption.Change(server.Id, new(first.Id, "incorrect", "second individual password", "second individual password"), user, default));
        await Assert.ThrowsAsync<BackupQueueException>(() => encryption.Change(server.Id, new(first.Id, null, "second individual password", "second individual password", Reset: true), user, default));
        await encryption.Change(server.Id, new(first.Id, "individual password first", "second individual password", "second individual password"), user, default);
        var second = (await encryption.Effective(server.Id, default))!;
        await Assert.ThrowsAsync<BackupQueueException>(() => encryption.Change(server.Id, new(first.Id, "individual password first", "third individual password", "third individual password"), user, default));
        await encryption.Change(server.Id, new(second.Id, null, "administrator reset password", "administrator reset password", Reset: true), admin, default);
        await Assert.ThrowsAsync<BackupQueueException>(() => encryption.OpenDecrypted(backup, "administrator reset password", true, default));
        await using (var decrypted = await encryption.OpenDecrypted(backup, "individual password first", true, default))
            Assert.Equal("synthetic backup content", await new StreamReader(decrypted).ReadToEndAsync());
        var third = (await encryption.Effective(server.Id, default))!;
        await encryption.Change(server.Id, new(third.Id, "administrator reset password", null, null, UseGlobal: true), user, default);
        Assert.Null(await encryption.Effective(server.Id, default));
        await Assert.ThrowsAsync<BackupQueueException>(() => encryption.Status(server.Id, Principal(false), default));
        await using (var decrypted = await encryption.OpenDecrypted(backup, null, false, default)) Assert.True(decrypted.Length > 0);
        Assert.False(Directory.GetFiles(SafeBackupPath.Root, ".decrypt-*").Any());
    }

    [Fact]
    public async Task Sqlite_Online_Snapshot_Includes_Wal_Then_Encrypts_And_Reopens_With_Integrity_Check()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var sourceRoot = Path.Combine(Path.GetTempPath(), "octobackup-sqlite-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceRoot); config["Sqlite:SourceDirectory"] = sourceRoot;
        try
        {
            var sourcePath = Path.Combine(sourceRoot, "fixture.sqlite");
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Pooling = false }.ToString());
            await source.OpenAsync();
            await using var command = source.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE probe (id INTEGER PRIMARY KEY, value TEXT); INSERT INTO probe(value) VALUES ('verified-row'); CREATE TABLE empty_table(id INTEGER);";
            await command.ExecuteNonQueryAsync(); Assert.True(File.Exists(sourcePath + "-wal"));
            var server = new DbServerConnection { ServerHost = "localhost", ServerPort = 1, DbName = "fixture.sqlite", DbUser = "sqlite", DbPasswd = "", DbType = DatabaseType.Sqlite };
            db.DbConnections.Add(server); await db.SaveChangesAsync();
            var encryptionService = scope.ServiceProvider.GetRequiredService<BackupEncryptionService>();
            var initial = await encryptionService.Status(server.Id, Principal(true), default);
            Assert.False(initial.IsEncrypted);
            await encryptionService.Change(server.Id,new(initial.KeyId,null,"SQLite individual password","SQLite individual password"),Principal(true),default);
            var executor = scope.ServiceProvider.GetRequiredService<IBackupExecutor>();
            var artifact = await executor.Execute(new BackupRequest { ServerId = server.Id, ServerName = "fixture", DatabaseName = server.DbName, TargetKey = BackupTarget.Key(server, null) }, default);
            Assert.EndsWith(".sqlite.enc", artifact.FilePath); Assert.NotNull(artifact.EncryptionKeyId);
            var encryption = scope.ServiceProvider.GetRequiredService<BackupEncryptionService>();
            var backup = new PerformedBackup { Id = artifact.Id, Name = "snapshot", ServerConnectionId = server.Id, DatabaseType = DatabaseType.Sqlite, DatabaseName = server.DbName, FilePath = artifact.FilePath, EncryptionKeyId = artifact.EncryptionKeyId };
            await using var decrypted = await encryption.OpenDecrypted(backup, null, false, default);
            var report = await SqliteBackup.Verify(decrypted.Name, default);
            Assert.Equal(2, report.TableCount); Assert.Contains(report.Tables, t => t.Name == "probe" && t.Rows.Any(r => r.Contains("verified-row")));
            Assert.Contains(report.Tables, t => t.Name == "empty_table" && t.Rows.Count == 0);
            db.Backups.Add(backup); await db.SaveChangesAsync();
            var restore = scope.ServiceProvider.GetRequiredService<RestoreTestService>();
            // SQLite snapshots can be verified without enabling the Docker runtime.
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Modules.Auth.Core.Entities.AppUser>>();
            var identity = new Modules.Auth.Core.Entities.AppUser { UserName = "sqlite-check@test.example", Email = "sqlite-check@test.example", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(identity, "SQLiteTestPassword123!")).Succeeded);
            await users.AddToRoleAsync(identity, "Admin");
            var test = await restore.Enqueue(backup.Id, Principal(true, Guid.Parse(identity.Id)), default);
            Assert.True(await restore.ProcessNext(default));
            var saved = await restore.Get(backup.Id, Principal(true), default);
            Assert.Equal(ETestStatus.Success, saved!.Status); Assert.Equal(2, saved.Preview!.TableCount);
            Assert.Throws<BackupQueueException>(() => SqliteBackup.Source(config, "../fixture.sqlite", false));
            Assert.Throws<BackupQueueException>(() => SqliteBackup.Source(config, "fixture.sqlite", true));
            if (!OperatingSystem.IsWindows())
            {
                File.CreateSymbolicLink(Path.Combine(sourceRoot, "linked.sqlite"), sourcePath);
                Assert.Throws<BackupQueueException>(() => SqliteBackup.Source(config, "linked.sqlite", false));
                await File.WriteAllTextAsync(Path.Combine(sourceRoot, "sidecar.sqlite"), "fixture");
                File.CreateSymbolicLink(Path.Combine(sourceRoot, "sidecar.sqlite-wal"), sourcePath);
                Assert.Throws<BackupQueueException>(() => SqliteBackup.Source(config, "sidecar.sqlite", false));
            }
            config["Sqlite:SourceDirectory"] = Path.GetDirectoryName(Modules.Shared.Common.DbCommon.DbPath);
            Assert.Throws<BackupQueueException>(() => SqliteBackup.Source(config, "OctoBackup.db", false));
        }
        finally { config["Sqlite:SourceDirectory"] = null; Directory.Delete(sourceRoot, true); }
    }
}

public class BackupEncryptionApiTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static async Task<System.Net.Http.HttpResponseMessage> Post(System.Net.Http.HttpClient client, string path, object body)
    {
        var csrf = await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<System.Text.Json.JsonElement>(client, "/api/auth/csrf");
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, path) { Content = System.Net.Http.Json.JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Download_Requires_Access_Csrf_And_The_Original_Password_While_Key_Reset_Is_Admin_Only()
    {
        var serverId = Guid.NewGuid(); var backupId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Modules.Auth.Core.Entities.AppUser>>();
            foreach (var email in new[] { "crypto-owner@test.example", "crypto-outsider@test.example" })
            {
                var user = new Modules.Auth.Core.Entities.AppUser { UserName = email, Email = email, EmailConfirmed = true };
                Assert.True((await users.CreateAsync(user, "ApiCryptoPassword123!")).Succeeded);
                if (email.Contains("owner"))
                {
                    var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
                    db.DbConnections.Add(new DbServerConnection { Id = serverId, ServerHost = "localhost", ServerPort = 1, DbName = "api-fixture", DbUser = "fixture", DbPasswd = "" });
                    db.UsersServers.Add(new() { ServerId = serverId, UserId = Guid.Parse(user.Id) });
                    await db.SaveChangesAsync();
                    var service = scope.ServiceProvider.GetRequiredService<BackupEncryptionService>();
                    var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"));
                    var status = await service.Status(serverId, admin, default);
                    await service.Change(serverId, new(status.KeyId, null, "DownloadFixturePassword123!", "DownloadFixturePassword123!"), admin, default);
                    Directory.CreateDirectory(SafeBackupPath.Root);
                    var plaintext = Path.Combine(SafeBackupPath.Root, "api-" + backupId + ".zip");
                    await File.WriteAllTextAsync(plaintext, "fixture-download");
                    var artifact = await service.Encrypt(plaintext, serverId, default);
                    db.Backups.Add(new PerformedBackup { Id = backupId, Name = "API fixture", ServerConnectionId = serverId, FilePath = artifact.Path, EncryptionKeyId = artifact.KeyId });
                    await db.SaveChangesAsync();
                }
            }
        }
        using var owner = factory.CreateClient(); using var outsider = factory.CreateClient(); using var anonymous = factory.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await Post(owner, "/api/auth/Login", new { email = "crypto-owner@test.example", password = "ApiCryptoPassword123!" })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await Post(outsider, "/api/auth/Login", new { email = "crypto-outsider@test.example", password = "ApiCryptoPassword123!" })).StatusCode);
        var download = "/api/backups/DownloadDecrypted?id=" + backupId;
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await Post(anonymous, download, new { password = "DownloadFixturePassword123!" })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await owner.PostAsync(download, System.Net.Http.Json.JsonContent.Create(new { password = "DownloadFixturePassword123!" }))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await Post(outsider, download, new { password = "DownloadFixturePassword123!" })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await Post(owner, download, new { password = "wrong" })).StatusCode);
        var decrypted = await Post(owner, download, new { password = "DownloadFixturePassword123!" });
        Assert.Equal(System.Net.HttpStatusCode.OK, decrypted.StatusCode); Assert.Equal("fixture-download", await decrypted.Content.ReadAsStringAsync());
        var encrypted = await owner.GetAsync("/api/backups/Download?id=" + backupId);
        Assert.Equal(System.Net.HttpStatusCode.OK, encrypted.StatusCode);
        Assert.Contains(".zip.enc", encrypted.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("OCTOENC1", System.Text.Encoding.ASCII.GetString((await encrypted.Content.ReadAsByteArrayAsync())[..8]));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await owner.GetAsync("/api/backup-encryption/global")).StatusCode);
        var reset = await Post(owner, "/api/backup-encryption/servers/" + serverId, new { reset = true, newPassword = "NewDownloadPassword123!", confirmPassword = "NewDownloadPassword123!" });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, reset.StatusCode);
    }
}

public class GlobalEncryptionKeyTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Global_Is_Disabled_Until_Custom_Password_And_Disabling_Preserves_Historical_Backups()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BackupEncryptionService>();
        var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"));
        Assert.Null(await service.Effective(null, default));
        Assert.Empty(db.BackupEncryptionKeys);
        Directory.CreateDirectory(SafeBackupPath.Root);
        var source = Path.Combine(SafeBackupPath.Root, Guid.NewGuid() + ".bak");
        await File.WriteAllTextAsync(source,"global-native-fixture");
        var plaintext = await service.Encrypt(source,Guid.NewGuid(),default);
        Assert.Null(plaintext.KeyId); Assert.Equal(source,plaintext.Path);
        var initial = await service.Status(null,admin,default);
        await Assert.ThrowsAsync<BackupQueueException>(() => service.Change(null,new(null,null,null,null,Enabled:true),admin,default));
        await Assert.ThrowsAsync<BackupQueueException>(() => service.Change(null,new(null,null,"custom global password","does not match",Enabled:true),admin,default));
        await service.Change(null,new(null,null,"custom global password","custom global password",Enabled:true,ExpectedRevision:initial.Revision),admin,default);
        var first = (await service.Effective(null,default))!;
        Assert.Null(first.ProtectedRecoveryPassword);
        var server = new DbServerConnection { ServerHost="localhost",ServerPort=1,DbName="independent",DbUser="fixture",DbPasswd="" };
        db.DbConnections.Add(server); await db.SaveChangesAsync();
        await service.Change(server.Id,new(first.Id,null,"independent server password","independent server password"),admin,default);
        var individual=(await service.Effective(server.Id,default))!.Id;
        var encrypted = await service.Encrypt(source,Guid.NewGuid(),default);
        var backup = new PerformedBackup { Name="fixture",FilePath=encrypted.Path,EncryptionKeyId=encrypted.KeyId };
        var enabled = await service.Status(null,admin,default);
        await service.Change(null,new(enabled.KeyId,null,null,null,Enabled:false,ExpectedRevision:enabled.Revision),admin,default);
        Assert.Null(await service.Effective(null,default));
        Assert.Equal(individual,(await service.Effective(server.Id,default))!.Id);
        await Assert.ThrowsAsync<BackupQueueException>(()=>service.Change(null,new(null,null,"another custom password","another custom password",Enabled:true,ExpectedRevision:initial.Revision),admin,default));
        using (var restarted = factory.Services.CreateScope())
        {
            var next = restarted.ServiceProvider.GetRequiredService<BackupEncryptionService>();
            await next.Initialize(); Assert.False(await next.GlobalEnabled());
            await using var decrypted = await next.OpenDecrypted(backup,"custom global password",true,default);
            Assert.Equal("global-native-fixture",await new StreamReader(decrypted).ReadToEndAsync());
        }
        var disabled = await service.Status(null,admin,default);
        await service.Change(null,new(null,null,"replacement custom password","replacement custom password",Enabled:true,ExpectedRevision:disabled.Revision),admin,default);
        Assert.NotEqual(first.Id,(await service.Effective(null,default))!.Id);
        await using (var old = await service.OpenDecrypted(backup,"custom global password",true,default)) Assert.True(old.Length>0);
        await Assert.ThrowsAsync<BackupQueueException>(()=>service.OpenDecrypted(backup,"replacement custom password",true,default));
        var unrelatedRing=Path.Combine(Path.GetTempPath(),"octobackup-wrong-ring-"+Guid.NewGuid().ToString("N"));
        var wrong=new BackupEncryptionService(db,Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(unrelatedRing)),Microsoft.Extensions.Logging.Abstractions.NullLogger<BackupEncryptionService>.Instance);
        await Assert.ThrowsAnyAsync<CryptographicException>(()=>wrong.Initialize());
    }
}

public class ServerEncryptionFormTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static async Task<System.Net.Http.HttpResponseMessage> Post(System.Net.Http.HttpClient client,string path,object body)
    {
        var csrf=await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<System.Text.Json.JsonElement>(client,"/api/auth/csrf");
        using var request=new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,path) { Content=System.Net.Http.Json.JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString()); return await client.SendAsync(request);
    }

    [Fact]
    public async Task Create_Edit_And_Key_Changes_Are_Atomic_And_Global_Toggle_Is_Admin_Only()
    {
        using (var scope=factory.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Modules.Auth.Core.Entities.AppUser>>();
            foreach(var role in new[]{"Admin","User"})
            {
                var user=new Modules.Auth.Core.Entities.AppUser { UserName=$"form-{role}@test.example",Email=$"form-{role}@test.example",EmailConfirmed=true };
                Assert.True((await users.CreateAsync(user,"FormFixturePassword123!")).Succeeded); await users.AddToRoleAsync(user,role);
            }
        }
        using var admin=factory.CreateClient(); using var userClient=factory.CreateClient();
        await Post(admin,"/api/auth/Login",new { email="form-Admin@test.example",password="FormFixturePassword123!" });
        await Post(userClient,"/api/auth/Login",new { email="form-User@test.example",password="FormFixturePassword123!" });
        var connection=new Modules.Backup.Shared.Dtos.ServerConnectionDto { ServerHost="localhost",ServerPort=5432,DbName="fixture",DbUser="fixture",DbPasswd="synthetic-password",ConnectionName="Atomic fixture",Encryption=new("individual","individual fixture password","mismatch") };
        Assert.Equal(System.Net.HttpStatusCode.BadRequest,(await Post(admin,"/api/servers/CreateServer",connection)).StatusCode);
        using (var scope=factory.Services.CreateScope()) Assert.Empty(scope.ServiceProvider.GetRequiredService<BackupsDbContext>().DbConnections);
        connection.Encryption=new("individual","individual fixture password","individual fixture password");
        Assert.Equal(System.Net.HttpStatusCode.Created,(await Post(admin,"/api/servers/CreateServer",connection)).StatusCode);
        Guid id;Guid first;
        using (var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<BackupsDbContext>(); var row=db.DbConnections.Single(); id=row.Id;
            var service=scope.ServiceProvider.GetRequiredService<BackupEncryptionService>(); Assert.False(await service.GlobalEnabled());
            first=(await service.Effective(id,default))!.Id;
        }
        connection=connection with { Id=id,ConnectionName="Changed only on success",DbPasswd=null,Encryption=new("individual","replacement fixture password","replacement fixture password","wrong password",first) };
        Assert.Equal(System.Net.HttpStatusCode.BadRequest,(await Post(admin,"/api/servers/EditServer",connection)).StatusCode);
        using(var scope=factory.Services.CreateScope()) Assert.Equal("Atomic fixture",scope.ServiceProvider.GetRequiredService<BackupsDbContext>().DbConnections.Single().ConnectionName);
        connection.Encryption=new("individual","replacement fixture password","replacement fixture password","individual fixture password",first);
        Assert.Equal(System.Net.HttpStatusCode.OK,(await Post(admin,"/api/servers/EditServer",connection)).StatusCode);
        Guid second;
        using(var scope=factory.Services.CreateScope()) second=(await scope.ServiceProvider.GetRequiredService<BackupEncryptionService>().Effective(id,default))!.Id;
        Assert.NotEqual(first,second);
        connection.Encryption=new("inherit",null,null,"replacement fixture password",second);
        Assert.Equal(System.Net.HttpStatusCode.OK,(await Post(admin,"/api/servers/EditServer",connection)).StatusCode);
        using(var scope=factory.Services.CreateScope()) Assert.Null(await scope.ServiceProvider.GetRequiredService<BackupEncryptionService>().Effective(id,default));
        var availability=await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<System.Text.Json.JsonElement>(userClient,"/api/backup-encryption/availability");
        Assert.False(availability.GetProperty("globalEnabled").GetBoolean());
        var enable=new { enabled=true,newPassword="global form fixture password",confirmPassword="global form fixture password" };
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,(await Post(userClient,"/api/backup-encryption/global",enable)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent,(await Post(admin,"/api/backup-encryption/global",enable)).StatusCode);
        using(var scope=factory.Services.CreateScope()) Assert.NotNull(await scope.ServiceProvider.GetRequiredService<BackupEncryptionService>().Effective(id,default));
    }
}

public class EncryptionUpgradeTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Upgrade_Disables_Global_Without_Losing_Previous_Generated_Key_Or_Artifacts()
    {
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<BackupEncryptionService>();
        var provider=scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        var protector=provider.CreateProtector("OctoBackup.BackupKeys.v1");
        var password="legacy generated fixture password"; var salt=RandomNumberGenerator.GetBytes(16);
        var raw=BackupEncryptionFormat.Derive(password,salt);
        var old=new BackupEncryptionKey { Scope="global",ActiveScope="global",Salt=Convert.ToBase64String(salt),ProtectedKey=Convert.ToBase64String(protector.Protect(raw)),ProtectedRecoveryPassword=protector.Protect(password) };
        Directory.CreateDirectory(SafeBackupPath.Root);
        var path=Path.Combine(SafeBackupPath.Root,Guid.NewGuid()+".zip.enc");
        await using(var output=File.Create(path)) await BackupEncryptionFormat.Encrypt(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("legacy content")),output,old.Id,salt,raw,default);
        db.BackupEncryptionKeys.Add(old);
        db.Backups.Add(new PerformedBackup { Id=Guid.NewGuid(),Name="legacy",FilePath=path,EncryptionKeyId=old.Id });
        await db.SaveChangesAsync();
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ExecuteDeleteAsync(db.BackupEncryptionPolicies);
        await service.Initialize();
        Assert.False(await service.GlobalEnabled()); Assert.Null(await service.Effective(null,default));
        var admin=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,"Admin")},"test"));
        Assert.Equal(password,await service.ExportGenerated(admin,default));
        var backup=db.Backups.Single(); await using var decrypted=await service.OpenDecrypted(backup,password,true,default);
        Assert.Equal("legacy content",await new StreamReader(decrypted).ReadToEndAsync());
        Assert.Single(db.BackupEncryptionKeys);
    }
}
