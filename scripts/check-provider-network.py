# Linux Docker smoke orchestration. Run only after building the test API image and
# publishing ProviderSmoke; see docs/DATABASE_CONNECTIONS.md. All source databases
# are generated fixtures. Existing containers/databases are never selected.
# Passwords travel through private temporary env files or process environments.
from pathlib import Path
import subprocess, os, json, time, uuid, secrets, sys
if set(sys.argv[1:]) - {'postgres', 'mysql', 'sqlserver'}:
    raise ValueError('Use postgres, mysql, sqlserver, or no arguments for all three.')
repo = Path(__file__).resolve().parents[1]
runid = uuid.uuid4().hex[:10]
prefix = 'octobackup-provider-' + runid
root = repo / 'Backend/artifacts' / prefix
root.mkdir()
stage = root / 'stage'
stage.mkdir()
network = prefix + '-network'
containers = []
password = 'Ob!' + secrets.token_hex(18)
env = os.environ.copy()
env.update(MYSQL_PWD=password, PGPASSWORD=password, SQLCMDPASSWORD=password)

def docker(args, **kw):
    return subprocess.run(['docker'] + args, env=env, text=True, capture_output=True, check=True, **kw).stdout.strip()

def sql(name, kind, query):
    args = ['exec', '-e', {'mysql': 'MYSQL_PWD', 'postgres': 'PGPASSWORD', 'sqlserver': 'SQLCMDPASSWORD'}[kind], name]
    args += {'mysql': ['mysql', '-h', '127.0.0.1', '-u', 'root', '--database', 'backup_probe', '--execute', query], 'postgres': ['psql', '-h', '127.0.0.1', '-U', 'postgres', '-d', 'backup_probe', '-v', 'ON_ERROR_STOP=1', '-Atqc', query], 'sqlserver': ['/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-b', '-Q', query]}[kind]
    return docker(args, timeout=20)
try:
    docker(['network', 'create', '--internal', network])
    print('Created isolated database network; no source ports will be published.', flush=True)
    docker(['run', '--rm', '--network=none', '--user=0:0', '--entrypoint', '/bin/sh', '-v', str(stage) + ':/stage', 'mcr.microsoft.com/mssql/server:2022-latest', '-c', 'chown 10001:0 /stage && chmod 2770 /stage'])
    for kind, image in [('postgres', 'postgres:18-alpine'), ('mysql', 'mysql:8.4'), ('sqlserver', 'mcr.microsoft.com/mssql/server:2022-latest')]:
        if len(sys.argv) > 1 and kind not in sys.argv[1:]:
            continue
        name = prefix + '-' + kind
        containers.append(name)
        data = root / kind
        data.mkdir()
        inputs = root / (kind + '.env')
        inputs.write_text('\n'.join([f'POSTGRES_PASSWORD={password}', f'MYSQL_ROOT_PASSWORD={password}', f'MSSQL_SA_PASSWORD={password}', f'FIXTURE_PASSWORD={password}', f'FIXTURE_HOST={name}', f'FIXTURE_TYPE={kind}', 'POSTGRES_DB=backup_probe', 'MYSQL_DATABASE=backup_probe', 'ACCEPT_EULA=Y', 'MSSQL_PID=Developer', 'FIXTURE_LOCAL_STAGE=/sqlserver-stage', 'FIXTURE_REMOTE_STAGE=/var/opt/mssql/backup']) + '\n')
        inputs.chmod(0o600)
        args = ['run', '-d', '--name', name, '--network', network, '--env-file', str(inputs), '--memory=4g', '--pids-limit=512', '--log-driver=none']
        if kind == 'sqlserver':
            args += ['-v', str(stage) + ':/var/opt/mssql/backup']
        args += [image]
        docker(args)
        deadline = time.monotonic() + 180
        while True:
            try:
                sql(name, kind, 'SELECT 1')
                break
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
                if time.monotonic() > deadline:
                    raise RuntimeError(kind + ' fixture did not become ready')
                time.sleep(2)
        if kind == 'sqlserver':
            sql(name, kind, 'CREATE DATABASE backup_probe;')
            sql(name, kind, "USE backup_probe; CREATE TABLE probe (id int PRIMARY KEY, value nvarchar(100)); INSERT INTO probe VALUES (1,'verified-row'); CREATE TABLE empty_probe(id int);")
        else:
            sql(name, kind, "CREATE TABLE probe(id int PRIMARY KEY,value varchar(100)); INSERT INTO probe VALUES (1,'verified-row'); CREATE TABLE empty_probe(id int);")
        app = ['run', '--rm', '--network', network, '--read-only', '--tmpfs', '/tmp', '--user', str(os.getuid()) + ':0', '--env-file', str(inputs), '--entrypoint', 'dotnet', '-v', str(repo / 'Backend/artifacts/provider-smoke-runner') + ':/runner:ro', '-v', str(data) + ':/smoke-data', '-v', str(stage) + ':/sqlserver-stage', 'octobackup-provider-check:local', '/runner/ProviderSmoke.dll']
        result = docker(app, timeout=180)
        print(result, flush=True)
        if kind == 'sqlserver':
            remaining = docker(['run', '--rm', '--network=none', '--user=0:0', '--entrypoint', '/bin/sh', '-v', str(stage) + ':/stage', 'mcr.microsoft.com/mssql/server:2022-latest', '-c', 'find /stage -name "*.bak" -print'])
            assert not remaining, 'Plaintext staging file remained'
        restoreenv = env.copy()
        restoreenv['OCTOBACKUP_SMOKE_POSTGRES_IMAGE'] = 'postgres:18-alpine'
        command = ['dotnet', 'run', '--no-build', '--no-restore', '--project', str(repo / 'Backend/tests/RestoreSmoke'), '--', {'postgres': 'postgres', 'mysql': 'mysql', 'sqlserver': 'sqlserver'}[kind]]
        if kind == 'sqlserver':
            command.append(str(data / 'fixture.bak'))
        else:
            command = ['dotnet', 'run', '--no-build', '--no-restore', '--project', str(repo / 'Backend/tests/RestoreSmoke'), '--', 'artifact', kind, str(data / 'fixture.zip')]
        restore = subprocess.run(command, env=restoreenv, text=True, capture_output=True, timeout=240)
        if restore.returncode:
            raise RuntimeError(kind + ' restore failed: ' + restore.stdout[-3000:] + restore.stderr[-1500:])
        print(restore.stdout[-1200:], flush=True)
        if kind == 'mysql':
            local_settings = repo / 'Backend/src/Server.Api/appsettings.Local.json'
            client = json.loads(local_settings.read_text()).get('DatabaseTools', {}).get('MySqlDumpPath') if local_settings.exists() else None
            if client:
                localname = prefix + '-host-client'
                containers.append(localname)
                docker(['run', '-d', '--name', localname, '--env-file', str(inputs), '-p', '127.0.0.1::3306', 'mysql:8.4'])
                deadline = time.monotonic() + 120
                while True:
                    try:
                        sql(localname, 'mysql', 'SELECT 1')
                        break
                    except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
                        if time.monotonic() > deadline:
                            raise RuntimeError('local client fixture did not become ready')
                        time.sleep(2)
                sql(localname, 'mysql', "CREATE TABLE probe(id int PRIMARY KEY,value varchar(100)); INSERT INTO probe VALUES (1,'verified-row'); CREATE TABLE empty_probe(id int);")
                endpoint = json.loads(docker(['inspect', localname]))[0]['NetworkSettings']['Ports']['3306/tcp'][0]['HostPort']
                localdata = root / 'host-client'
                localdata.mkdir()
                localenv = env.copy()
                localenv.update(FIXTURE_TYPE='mysql', FIXTURE_HOST='127.0.0.1', FIXTURE_ALLOW_LOOPBACK='true', FIXTURE_PORT=endpoint, FIXTURE_PASSWORD=password, FIXTURE_DATA=str(localdata), FIXTURE_MYSQL_DUMP=client)
                result = subprocess.run(['dotnet', str(repo / 'Backend/artifacts/provider-smoke-runner/ProviderSmoke.dll')], env=localenv, text=True, capture_output=True, timeout=90)
                if result.returncode:
                    raise RuntimeError('local configured dump client failed: ' + result.stderr[-1500:])
                print('Host-local configured MariaDB client: actual MySQL 8.4 export/encryption/recovery passed.', flush=True)
                result = subprocess.run(['dotnet', 'run', '--no-build', '--no-restore', '--project', str(repo / 'Backend/tests/RestoreSmoke'), '--', 'artifact', 'mysql', str(localdata / 'fixture.zip')], env=env, text=True, capture_output=True, timeout=240)
                if result.returncode:
                    raise RuntimeError('local dump restore failed: ' + result.stdout[-1000:] + result.stderr[-1000:])
                print(result.stdout[-1000:], flush=True)
    print('SELECTED_PROVIDER_CHECKS_PASSED', flush=True)
finally:
    for container in containers:
        try:
            docker(['rm', '-f', '-v', container])
        except Exception:
            pass
    try:
        docker(['network', 'rm', network])
    except Exception:
        pass
    try:
        docker(['run', '--rm', '--network=none', '--user=0:0', '--entrypoint', '/bin/sh', '-v', str(stage) + ':/stage', 'mcr.microsoft.com/mssql/server:2022-latest', '-c', f'chown {os.getuid()}:{os.getgid()} /stage && chmod 700 /stage'])
    except Exception:
        pass
    for file in root.glob('*.env'):
        file.unlink()
    print('Disposable provider containers/network removed; fixture outputs: ' + str(root), flush=True)
