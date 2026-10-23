# Reloading configuration and preloading restore images

## JSON configuration reload

After deploying the reload-capable API once, edits to these files are watched:

- `Backend/src/Server.Api/appsettings.json` for a console API.
- `appsettings.<Environment>.json`, such as `appsettings.Development.json`.
- `appsettings.Local.json` in Development only.
- Optional private `appsettings.Runtime.json`, or the startup-selected path in
  `Configuration:ReloadFile` / `Configuration__ReloadFile`.

Save valid JSON. Native file watchers apply changes after a short debounce; polling
on bind/network mounts can take several seconds. New restore tests read updated
image maps and limits; a running restore keeps its captured settings and still
cleans up if the runtime is disabled during execution. Enabling the runtime also
retries orphan recovery. Availability shown in the UI refreshes every five seconds.
A failed restore does not retry automatically: click **Test backup** again.

Reloadable values include `RestoreTests` (images, enablement, resource limits),
`DatabaseTools` executable paths, `SqlServer` staging paths, `Sqlite` source/test
settings, application `Backups` defaults, deployment registration/allowlist defaults,
and logging. Paths/binaries/mounts must already exist and be accessible to the API.
Reloading a path does not install software, create a share or change Docker mounts.

Storage/data roots, Data Protection locations, `CRYPTO_KEY`, bootstrap accounts,
HTTP binding, reverse-proxy trust, incoming hostnames, and session/security setup
remain startup settings. Changes to these JSON values are logged as requiring a
restart and are not partially applied. This is particularly important for preserving
access to existing credentials and metadata. Invalid, temporarily missing or unreadable
JSON keeps the last valid per-file settings and logs a fixed warning without values
or parser contents. To remove an override, edit valid JSON (for example remove its
key or save `{}`), rather than delete the whole watched file.

File precedence is base JSON < environment JSON < Development Local JSON < runtime
JSON < user-secrets < environment variables < command-line arguments. A value supplied
through `RestoreTests__Enabled` still overrides the corresponding JSON value. Process
environment variables and `.env`/Compose changes do not reload inside an already
running process; restart/recreate it when those inputs change.

Application settings saved in the administrator panel remain authoritative over
all deployment defaults in their persisted snapshot. Edit those values in the panel.
Before any admin snapshot has been saved, valid JSON changes update those defaults
with a new revision; invalid defaults retain the last valid snapshot. Global backup
encryption remains a separate administrator-managed database policy and cannot be
enabled by editing appsettings.

## Editable configuration for the Docker API

Files baked into an image do not follow edits to your checkout. Use a directory
bind mount so editors which replace files atomically remain visible:

```sh
mkdir -p config
cp Backend/docker/appsettings.Runtime.example.json config/appsettings.Runtime.json
# Edit config/appsettings.Runtime.json, preserving private values as needed.
docker compose -f compose.yaml -f deploy/compose.restore-tests.yaml \
  -f deploy/compose.config-reload.yaml up -d --build
```

Set `RESTORE_DOCKER_GID` as described in OPERATIONS.md first. The reload override
mounts `./config` read-only at `/config` and selects `/config/appsettings.Runtime.json`.
It enables `DOTNET_USE_POLLING_FILE_WATCHER=true` for bind-mount reliability. Once
running, later JSON saves need no API restart or image rebuild. The sample contains
only image maps; enabling the Docker runtime still requires the CLI-enabled build,
Docker access and the deployment opt-in. Changing those mounts/groups/build targets
requires recreating the container once.

The runtime file/config directory is ignored by Git and excluded from builds and
publishing. Development Local JSON remains excluded from production. Protect private
configuration permissions and continue persisting the data/key-ring volume.

## Pull the exact restore image on the test daemon

A mapping selects an image; it does not install it. Restore execution deliberately
uses `docker run --pull=never`. For example:

```json
{
  "RestoreTests": {
    "MySqlImages": {
      "8.4": "mysql:8.4"
    }
  }
}
```

Pull and verify **that exact value**, including tag or digest:

```sh
docker image pull mysql:8.4
docker image inspect mysql:8.4
```

Other examples, only when selected by your mappings:

```sh
docker image pull postgres:18-alpine
docker image pull mariadb:10.11
docker image pull mcr.microsoft.com/mssql/server:2022-latest
```

The image must be on the daemon which creates the disposable restore containers,
not necessarily the daemon which runs your production database. The repository's
socket-mounted Compose override uses the host's `/var/run/docker.sock`; explicitly
select that daemon if your terminal normally uses another Docker context:

```sh
docker --host unix:///var/run/docker.sock image pull mysql:8.4
docker --host unix:///var/run/docker.sock image inspect mysql:8.4
```

For a dedicated remote daemon/context:

```sh
docker --context restore-tests image pull mysql:8.4
docker --context restore-tests image inspect mysql:8.4
```

Use a context already configured to reach that daemon securely; `restore-tests`
is an example name. A console API's Docker CLI inherits its service account's
`DOCKER_CONTEXT` / `DOCKER_HOST` and Docker configuration. Pull using the same endpoint
and appropriate credentials. A Docker Desktop context and the host Unix socket can
refer to different engines. No API restart is needed after a successful pull.
Refresh/retry the failed test; saved failed reports remain historical until a new
request replaces the current result.

An exit 125 with “image is not preloaded” is resolved by preloading the selected
image. If `pull` itself says the tag/manifest does not exist, choose an actually
published image compatible with the recorded source version. Do not bypass version
checks or silently restore into another major. Private registries may require
`docker login` for the account performing the pull. Database login credentials are
unrelated to registry authentication.

References: [Docker pull](https://docs.docker.com/reference/cli/docker/image/pull/),
[Docker contexts and endpoint options](https://docs.docker.com/reference/cli/docker/),
[.NET file-based configuration change tokens](https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/change-tokens.md).
