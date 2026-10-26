# OctoBackup

[English](../README.md) · [Uruchamianie i konfiguracja](OPERATIONS.md)

Samodzielnie hostowany panel kopii baz danych: **.NET 10**, **React i TypeScript**.
Połączenia, uprawnienia, harmonogramy, trwała kolejka, historia i pobieranie kopii
w jednym interfejsie. Obsługiwane źródła: MySQL/MariaDB, PostgreSQL, SQL Server
oraz lokalnie zamontowane pliki SQLite.

## Docker za reverse proxy

W katalogu głównym repozytorium:

```sh
node scripts/dev.mjs init
docker compose pull
docker compose up -d --wait
```

Dodaj publiczną nazwę aplikacji do `ALLOWED_HOSTS` w prywatnym `.env`, zachowując
`localhost;127.0.0.1` dla sondy zdrowia kontenera. Przykład:
`ALLOWED_HOSTS=backups.example.com;localhost;127.0.0.1`.
Skieruj własne reverse proxy z **HTTPS** na **http://127.0.0.1:8080** i włącz
WebSocket. Otwórz publiczny adres HTTPS. Kontenery aplikacji obsługują HTTP i nie
potrzebują certyfikatów; TLS oraz przekierowanie z HTTP konfiguruje reverse proxy.
Produkcyjne ciasteczka wymagają HTTPS w przeglądarce.

Dla **izolowanych testów HTTP w sieci prywatnej** ustaw `ALLOW_INSECURE_HTTP=true`
w `.env`, odtwórz kontener API i otwórz `http://<host>:<FRONTEND_PORT>`. Opcja usuwa
flagę Secure z ciasteczek sesji/CSRF. Dla publicznego HTTPS pozostaw `false`.
Walidacja CSRF pozostaje obowiązkowa w obu trybach.
[Diagnostyka logowania](OPERATIONS.md#login-and-runtime-troubleshooting).

Dane logowania to `ADMIN_EMAIL` i `ADMIN_PASSWORD` w `.env`. Inicjalizacja nie
nadpisuje istniejących sekretów. `FRONTEND_PORT` zmienia port upstream na hoście;
stary `.env` zachowuje dotychczasowy port. Proxy w Dockerze może korzystać z
`frontend:8080` we wspólnej sieci. [Przykład proxy](OPERATIONS.md#reverse-proxy).

## Uruchamianie ze źródeł

Wymagane: SDK .NET 10 zgodne z `global.json` oraz Node.js 22.12+.

```sh
node scripts/dev.mjs init
dotnet restore OctoBackup.sln
npm ci --prefix Frontend
node scripts/dev.mjs
```

Panel: **http://127.0.0.1:5173**, API: **http://127.0.0.1:5080**.
Argument `api` lub `frontend` uruchamia wybraną aplikację. API uruchamiane na hoście
wymaga lokalnych narzędzi `pg_dump` i `mysqldump`/`mariadb-dump`.
Opcjonalne bazy testowe: `docker compose -f compose.dbs.yaml up -d --wait`.
API można też uruchomić bez launchera: `dotnet run --project Backend/src/Server.Api`;
pierwszy start tworzy prywatny `appsettings.Local.json` z danymi administratora.

## Budowanie i publikowanie

Kontekstem obu Dockerfile jest katalog główny:

```sh
docker build -f Backend/Dockerfile -t lewan24/octobackup-api:latest .
docker build -f Frontend/Dockerfile -t lewan24/octobackup-web:latest .
docker compose up -d --wait
```

Publikowanie działa tak samo na **Linux i Windows** (wymagany Node.js):

```sh
docker login
node scripts/build-and-push.mjs
```

Helper buduje oba obrazy przed publikacją, używa katalogu repozytorium niezależnie
od bieżącego katalogu i przerywa po błędzie. `--build-only` pomija publikowanie,
a `--dry-run` tylko pokazuje komendy. Skróty przekazują te same opcje:

- Linux / Bash: `./buildAndPushDockerImages.sh`.
- Windows PowerShell: `.\buildAndPushDockerImages.bat`.

Plik `.bat` wymaga interpretera Windows. W Linux/WSL/Bash użyj `.sh` lub komendy
`node` powyżej; Bash nie obsługuje poleceń `@echo`, `rem` ani `call`.

Obrazy zawierają kontenery Linux. Na Linux użyj Docker Engine, a na Windows
[Docker Desktop w trybie Linux containers](https://docs.docker.com/desktop/setup/install/windows-install/).
Architektura CPU zależy od demona Docker; helper nie tworzy manifestu wielu
architektur. Natywne kontenery Windows nie są obsługiwane. Zwykły Compose korzysta
z obrazów; samo `up --build` nie buduje standardowych usług.

Odpowiedź 401 z `POST /api/auth/Login` oznacza odrzucenie danych logowania lub stanu
konta. API zapisuje bezpieczny powód `Sign-in rejected` z identyfikatorem śladu;
[diagnostyka](OPERATIONS.md#login-and-runtime-troubleshooting). Wartości Bootstrap
nie resetują haseł istniejących kont na zachowanym woluminie.

## Podstawowa obsługa

1. Zaloguj się jako administrator i zatwierdź dokładne hosty baz/SSH w **Application settings**.
2. Dodaj połączenie w **Servers**, sprawdź je i przydziel operatorom dostęp.
3. Opcjonalnie ustaw indywidualne hasło kopii lub włącz szyfrowanie globalne.
4. Wybierz **Queue backup** albo dodaj harmonogram. Przyjęcie zadania nie oznacza zakończenia kopii.
5. W **Backup history** pobierz plik lub wybierz **Test / preview**. Testy Docker wymagają osobnej konfiguracji wdrożenia i pobranych obrazów.

Szyfrowanie nowych kopii jest domyślnie wyłączone. Zmiana hasła nie zmienia starych
plików. Zachowaj cały wolumin `/data`, klucze Data Protection, `CRYPTO_KEY` i hasła
odzyskiwania. Jeden katalog danych może obsługiwać tylko jedna instancja API.
SQL Server wymaga współdzielonego katalogu staging, a SQLite osobnego montowania.
Podgląd próbek po odtworzeniu nie zastępuje pełnego testu odzyskiwania.

## Dokumentacja i rozwój

[Architektura i diagramy](ARCHITECTURE.md) · [Bezpieczeństwo](SECURITY.md) ·
[Połączenia i SQL Server](DATABASE_CONNECTIONS.md) · [Kopie i odzyskiwanie](BACKUPS.md) ·
[Interfejs](DESIGN.md)

Propozycje rozwoju: osobny worker odtwarzania, MFA i odzyskiwanie kont,
manifesty/sumy kontrolne, obsługa stref czasowych, retry i porządkowanie osieroconych
plików, magazyny obiektowe oraz szersze testy zgodności silników. To plany, nie
aktualnie dostępne funkcje. Komendy testów i pełna lista propozycji są w
[README](../README.md#test).

[Licencja MIT](../LICENSE)
