# Database connections, export tools and SQL Server storage

## Connection address depends on where the API runs

| API location | Database location | Host and port in OctoBackup |
| --- | --- | --- |
| Console on your computer | Remote database with a published/listening port | Remote hostname/IP and that TCP port |
| API container on the same Docker network | Database container without host ports | Database service name/network alias and its **container** port |
| API container on another Compose project | Private database container | Attach only the API to the database's external network, then use service alias/container port |
| Console on another computer | Container with no published port | Not directly reachable: deploy the API on that Docker host/network, publish a restricted port, or provide an approved SSH gateway/tunnel |

Docker service names work inside their shared Docker network, not in an unrelated
host console process. `localhost` inside the API container means the API container.
`host.docker.internal` means its Docker host, not another private database container.
Ordinary bridge networks do not extend across Docker hosts. Do not save container
IP addresses: recreating containers changes them. A container still needs a database
listener on its internal TCP port; absence of `ports:` is fine, but a database
listening only on its own loopback/socket is not reachable from another container.
No Docker socket or container-execution API is required for source backups.

For an existing stack network:

```sh
docker network ls
# Set DATABASE_NETWORK in your private .env, for example finance_default.
docker compose -f compose.yaml -f deploy/compose.private-network.yaml up -d --build
```

The override attaches only `api` to that existing network. The frontend stays on
its existing proxy network. Approve the exact database service alias in
**Application settings > Allowed database hosts**. Enter, for example:

| Engine | Host example | Port | Connection fields |
| --- | --- | --- | --- |
| PostgreSQL | `finance-postgres` | 5432 | Existing database and backup role/password |
| MySQL / MariaDB | `finance-mysql` | 3306 | Existing database and backup account/password |
| SQL Server | `finance-sqlserver` | 1433 | Existing database and SQL login/password; shared staging also required |

The alias must actually exist on the shared network. Assign distinct aliases when
multiple stacks contain a service named `db`. Do not publish database ports just to
connect containers already sharing a network. Docker networking documentation:
[service discovery and external networks](https://docs.docker.com/compose/how-tos/networking/).

## MySQL/MariaDB: the API needs the dump client

A connection test uses the .NET driver; a backup uses an external dump executable.
Installing a database container or a client only on the database host does not put
that executable on a console API's PATH. Install clients on the **API host**:

```sh
# Arch/EnigmarsOS (administrator privileges)
sudo pacman -S --needed mariadb-clients
# Debian/Ubuntu console host
sudo apt-get install default-mysql-client
```

Alternatively set deployment settings in `appsettings.Local.json` (Development),
user-secrets, or environment. These paths are never browser connection fields:

```json
{
  "DatabaseTools": {
    "MySqlDumpPath": "/usr/bin/mariadb-dump",
    "PostgresDumpPath": "/usr/bin/pg_dump"
  }
}
```

Windows example: `C:\Program Files\MySQL\MySQL Workbench 8.0\mysqldump.exe`.
Use valid JSON escaping for backslashes. Common Windows installations are also
searched when no explicit path is configured. A configured invalid executable fails
explicitly rather than silently falling back. Restart the API after installing
clients/changing PATH or local startup settings. Do not replace encryption keys.
`mysqldump --version` / `mariadb-dump --no-defaults --help` must succeed for the API
service account. TLS, authentication plugins and export grants must match the server.
See [dump grants and troubleshooting](#mysql-dump-grants).

### MySQL dump grants

Exports include routines, events and triggers. Check SELECT, SHOW VIEW, TRIGGER
and EVENT grants on the selected database. Routine visibility can require a global
metadata grant reviewed by the DBA:

```sql
-- MySQL 8.0.20+; replace both account placeholders.
GRANT SHOW_ROUTINE ON *.* TO 'backup_user'@'allowed_client_host';
-- MySQL 5.7 instead:
GRANT SELECT ON mysql.proc TO 'backup_user'@'allowed_client_host';
```

The application detects client capabilities, forces TCP, disables supported column
statistics, omits tablespaces and uses GTID_PURGED=OFF for logical per-database
exports. It does not silently omit routines to report success. Bounded stderr
becomes safe error categories; partial output is removed on failure. A connection
test does not prove export grants or dump-client availability. Selected TLS
verification is preserved; configure a trusted CA for the service/client as needed.

## PostgreSQL password connections

Npgsql 10 defaults to preferring GSS/Kerberos encryption. These connection forms use
username/password authentication, so checks now explicitly disable GSS negotiation;
`pg_dump` uses `PGGSSENCMODE=disable` too. This leaves the selected SSL/TLS policy
unchanged: verified TLS uses VerifyFull/verify-full; explicit TLS-off stays off.
There is no automatic certificate bypass or downgrade.

Safe errors distinguish DNS/TCP/timeouts, credential/key-ring failures, TLS,
authentication, missing database and pg_hba.conf rejection. Provider exception
text, SQL and passwords are not returned in diagnostics.

The export client must be at least as new as the PostgreSQL server major. The API
image now installs pg_dump 18 from the signed official PostgreSQL apt repository,
so PostgreSQL 17/18 sources do not fail with Noble's default pg_dump 16. Host installs
can select another reviewed binary with `DatabaseTools:PostgresDumpPath`.
A normal `SELECT 1` connection test does not verify full dump grants or export tools.

## SQL Server: TCP connection plus a shared staging directory

SQL Server executes `BACKUP DATABASE` on its own host. It does not return the `.bak`
through the SQL connection. The error “backup is not visible locally” means that
SQL Server could create a file which the API cannot see at its configured local
path. Do not solve this with `xp_cmdshell`, arbitrary file-read SQL, or a Docker
socket in the API. Set up a dedicated shared directory instead.

Recommended configuration:

```json
{
  "SqlServer": {
    "LocalStagingDirectory": "/sqlserver-stage",
    "RemoteBackupRoot": "/var/opt/mssql/backup"
  }
}
```

Both directories above must refer to the **same underlying storage**. Mount one
named volume/bind mount at `/sqlserver-stage` in the API and
`/var/opt/mssql/backup` in SQL Server. For different machines, provision an SMB/NFS
share first. For a Windows SQL Server, `RemoteBackupRoot` might instead be
`D:\OctoBackupStage`; the API uses its local mount of that directory. Merely setting
two paths does not create a share or copy a remote file.

The SQL Server OS service account needs write access; the API OS account needs
read/delete access. SQL backup permissions and filesystem permissions are separate.
The implementation uses a generated filename directly in staging, avoiding nested
private API directories. After `COPY_ONLY, CHECKSUM` succeeds, the API copies the
native file into private storage, encrypts it, and removes the generated staging
file. SQL Server never needs access to application metadata, the key ring, or final
encrypted backups. A failed/missing staging copy fails the backup truthfully.
Interrupted/remote backups still require operational orphan inspection.

For the repository's disposable SQL Server Compose profile:

```sh
# Supply private .env credentials first; SQL Server Developer is for non-production use.
docker compose -f compose.yaml -f compose.dbs.yaml \
  -f deploy/compose.sqlserver-storage.yaml --profile sqlserver up -d --build
```

The override initializes a dedicated volume with SQL Server UID 10001/group 0 and
mode 2770, grants the non-root API access through group 0, and mounts only staging
in both services. It does not change an existing database's password or initialize
your real database. Create/select a database first. In OctoBackup use host
`sqlserver`, port `1433`, that database name, and a SQL login with backup permission.
For a console API connecting through the example host binding, use the Docker host
and published port `51433`; it also needs its own mount of the staging share.
The default `compose.dbs.yaml` alone does not mount staging into a console process.

Production example grants (DBA must review database/login names and host policy):

```sql
USE [YourDatabase];
CREATE USER [octobackup] FOR LOGIN [octobackup]; -- if absent
ALTER ROLE [db_backupoperator] ADD MEMBER [octobackup];
```

Provision the SQL login/password separately. Do not use `sa` for production backup
jobs. Configure trusted TLS and approve the actual server hostname in Application
settings. TLS off is only for explicitly isolated development fixtures.
[SQL Server container storage permissions](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-docker-container-security?view=sql-server-linux-ver17).

Older deployments sharing the entire backup root remain compatible when
`LocalStagingDirectory` is null; `RemoteBackupRoot` maps the generated relative path.
The dedicated staging mode is preferred to sharing application backup storage.

## Scope of live checks

ProviderSmoke is restricted to synthetic `octobackup-provider-*` fixture hosts and
a `backup_probe` database. It tests saved-credential connections, actual provider
exports, encryption and authenticated recovery. RestoreSmoke's `artifact` mode
restores only those disposable provider fixture archives and checks inventories
and representative rows. SQL Server fixtures exercise native backup restoration.
Use these fixtures only with disposable data.

On Linux with the selected database images already available, reproduce the fixture
checks with:

```sh
docker build --target runtime -f Backend/Dockerfile -t octobackup-provider-check:local .
dotnet build OctoBackup.sln
dotnet publish Backend/tests/ProviderSmoke -o Backend/artifacts/provider-smoke-runner
python3 scripts/check-provider-network.py
```

Optional arguments select `postgres`, `mysql`, or `sqlserver`. The main source
containers have no published ports. If a private local MySQL dump executable is
configured, an additional distinct fixture uses a random loopback-only port to
check that host executable. Temporary credentials are removed, and only generated
containers/networks are cleaned up; synthetic encrypted/decrypted artifacts remain
under ignored Backend/artifacts for inspection. Require adequate local memory for
source and restore containers. This runner is not intended for production data.

Source-server headers take precedence over dump-client branding during MySQL/MariaDB
restore selection. This supports a MariaDB client exporting a MySQL source without
misrouting its archive to a MariaDB server. SQL is preserved without filtering.
The native SQL Server restore container retains only NET_BIND_SERVICE after dropping
all capabilities, because the official executable declares that file capability.
It still runs non-root, without networking, published ports, host mounts, or root
filesystem writes; no SYS_PTRACE or privileged mode is enabled.
