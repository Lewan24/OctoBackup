namespace Modules.Backup.Core.StaticClasses;

public sealed class DumpFailureException : InvalidOperationException
{
    public string SafeMessage { get; }

    public DumpFailureException(string tool, int? exitCode, string hint)
        : base($"{Path.GetFileName(tool)} failed{(exitCode is null ? "" : $" with exit code {exitCode}")}. {hint}")
    {
        SafeMessage = $"Database export failed. {hint}";
    }

    // Provider stderr may contain credentials, query text and filesystem paths.
    // Classify it into fixed messages; never attach the raw output to exceptions.
    public static string Explain(string diagnostic)
    {
        if (Contains(diagnostic, "insufficient privileges", "permission denied", "command denied", "access denied", "requires (at least one of)", "you need (at least one of)"))
        {
            if (Contains(diagnostic, "flush tables", "flush_tables", "reload privilege"))
                return "The dump client requested global table-flush privileges. For per-database logical archives, use a compatible MySQL client with --set-gtid-purged=OFF.";
            if (Contains(diagnostic, "show create procedure", "show create function", "mysql.proc", "show_routine"))
                return "The backup account cannot read stored routine definitions. Ask the database administrator to grant routine visibility (SHOW_ROUTINE on MySQL 8.0.20+, or SELECT on mysql.proc for MySQL 5.7). Routines were not omitted.";
            if (Contains(diagnostic, "1045", "using password"))
                return "Database authentication was rejected. Check the saved username/password and the account's permitted connection hosts.";
            return "The backup account lacks required permissions. Ask the database administrator to check SELECT, SHOW VIEW, TRIGGER, EVENT and stored routine visibility grants for this database.";
        }
        if (Contains(diagnostic, "column_statistics", "column statistics"))
            return "The dump client's column-statistics query is incompatible with this server. Use a compatible client with column statistics disabled.";
        if (Contains(diagnostic, "unknown variable", "unknown option", "unrecognized option"))
            return "The installed dump client does not support the selected options. Check the MySQL/MariaDB client installation and version on the API host.";
        if (Contains(diagnostic, "ssl", "tls", "certificate"))
            return "TLS negotiation or certificate verification failed. Check the server certificate, hostname and client CA trust. TLS verification was not disabled.";
        if (Contains(diagnostic, "authentication plugin", "caching_sha2_password", "sha256_password"))
            return "The dump client cannot use the server's authentication plugin. Install a compatible MySQL client and check its authentication/TLS configuration.";
        if (Contains(diagnostic, "can't connect", "cannot connect", "could not connect", "unknown mysql server host", "connection refused", "name or service not known"))
            return "The dump client could not reach the database. Check its hostname, TCP port, firewall and SSH tunnel configuration.";
        if (Contains(diagnostic, "unknown database")) return "The configured database does not exist or is not visible to the backup account.";
        return "Check the installed dump client's compatibility, database connectivity, TLS trust and backup-account permissions.";
    }

    private static bool Contains(string value, params string[] matches)
        => matches.Any(match => value.Contains(match, StringComparison.OrdinalIgnoreCase));
}
