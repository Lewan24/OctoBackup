using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Auth.Shared.Static.Entities;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;

namespace Modules.Backup.Api.Security;

// Applied to all server and backup endpoints; arguments have already been bound.
public sealed class ResourceAccessFilter(BackupsDbContext db, Modules.Shared.Settings.IApplicationSettings settings) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        var user = c.HttpContext.User;
        var action = c.HttpContext.Request.Path.Value!.TrimEnd('/').Split('/').Last().ToUpperInvariant();
        var admin = user.IsInRole(AppRoles.Admin);
        if (action is "CREATESERVER" or "EDITSERVER" or "EDITAUTOTESTCONFIG" or "FETCHAUTOTESTCONFIG")
        {
            if (!admin) return Results.Forbid();
        }
        if (c.Arguments.OfType<ServerConnectionDto>().FirstOrDefault() is { } server)
        {
            if (!Modules.Shared.Settings.DatabaseHostPolicy.AllowsConnection(settings.Current, server.ServerHost,
                server.IsTunnelRequired, server.Tunnel?.ServerHost, server.Tunnel?.RemoteHost))
                return Results.BadRequest(Modules.Shared.Settings.DatabaseHostPolicy.DeniedMessage);
            server.ServerHost = Modules.Shared.Settings.DatabaseHostPolicy.Normalize(server.ServerHost);
            if (server.IsTunnelRequired && server.Tunnel is not null)
            {
                server.Tunnel.ServerHost = Modules.Shared.Settings.DatabaseHostPolicy.Normalize(server.Tunnel.ServerHost);
                server.Tunnel.RemoteHost = Modules.Shared.Settings.DatabaseHostPolicy.Normalize(server.Tunnel.RemoteHost);
            }
            if (!Enum.IsDefined(server.DbType) || server.ServerPort == 0 ||
                server.ConnectionName.Length > 120 || server.DbName.Length > 128 ||
                !System.Text.RegularExpressions.Regex.IsMatch(server.DbName, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z"))
                return Results.BadRequest("Invalid server configuration or database name.");
        }
        if (!admin && c.Arguments.OfType<Guid>().FirstOrDefault() is var id && id != Guid.Empty)
        {
            var serverId = action == "PERFORMBACKUP" || c.HttpContext.Request.Path.StartsWithSegments("/api/servers")
                ? id : await db.Backups.Where(b => b.Id == id).Select(b => b.ServerConnectionId).FirstOrDefaultAsync();
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ||
                !await db.UsersServers.AnyAsync(x => x.UserId == uid && x.ServerId == serverId) ||
                !await db.DbConnections.AnyAsync(x => x.Id == serverId && !x.IsDisabled))
                return Results.Forbid();
        }
        return await next(c);
    }
}
