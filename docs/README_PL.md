# OctoBackup

[English](../README.md) · [Uruchamianie i konfiguracja](OPERATIONS.md)

Samodzielnie hostowany panel kopii baz danych: **.NET 10**, **React i TypeScript**.
Połączenia, uprawnienia, harmonogramy, trwała kolejka, historia i pobieranie kopii
w jednym interfejsie. Obsługiwane źródła: MySQL/MariaDB, PostgreSQL, SQL Server
oraz lokalnie zamontowane pliki SQLite.

## Docker za reverse proxy

Wymagane: Docker Engine z Compose/Buildx oraz Node.js. W katalogu głównym repozytorium:

```sh
node scripts/dev.mjs init
docker compose up --build -d --wait
```

Dodaj publiczną nazwę aplikacji do `ALLOWED_HOSTS` w prywatnym `.env`, zachowując
`localhost;127.0.0.1` dla sondy zdrowia kontenera. Przykład:
`ALLOWED_HOSTS=backups.example.com;localhost;127.0.0.1`.

Jedyny wymagany plik produkcyjny to `compose.yaml`. Domyślnie buduje API z Docker
CLI, montuje socket i włącza testy odtwarzania. `init` wykrywa grupę socketa
(`RESTORE_DOCKER_GID`) oraz tworzy `config/appsettings.Runtime.json`. Przy
przygotowywaniu projektu na innym komputerze wpisz GID uzyskany poleceniem
`stat -c %g /var/run/docker.sock` na hoście docelowym. Dla istniejącego wdrożenia
uruchom ponownie `node scripts/dev.mjs init`; zachowa sekrety i ustawienia.
Usługa `restore-images` pobiera skonfigurowane obrazy baz przed startem API;
pierwszy start może pobrać kilka GB. Socket daje API kontrolę nad Dockerem hosta,
dlatego użyj dedykowanego hosta/VM. [Szczegóły](OPERATIONS.md#restore-testing).
Starszy Docker na hoście może wymagać tymczasowego wymuszenia wersji API;
opis obejścia znajduje się w powyższej instrukcji.
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

Aby opublikować zbudowane obrazy:

```sh
docker login
docker push lewan24/octobackup-api:latest
docker push lewan24/octobackup-web:latest
```

Dla własnego konta rejestru zmień tagi w komendach budowania/publikowania
oraz `API_IMAGE` / `WEB_IMAGE` w `.env`.

Obrazy zawierają kontenery Linux. Na Linux użyj Docker Engine, a na Windows
[Docker Desktop w trybie Linux containers](https://docs.docker.com/desktop/setup/install/windows-install/).
Architektura CPU zależy od demona Docker; te komendy nie tworzą manifestu wielu
architektur. Natywne kontenery Windows nie są obsługiwane. Compose domyślnie buduje
obrazy ze źródeł. Dla wcześniej pobranych obrazów użyj
`docker compose up --no-build --pull never -d --wait`.

Odpowiedź 401 z `POST /api/auth/Login` oznacza odrzucenie danych logowania lub stanu
konta. API zapisuje bezpieczny powód `Sign-in rejected` z identyfikatorem śladu;
[diagnostyka](OPERATIONS.md#login-and-runtime-troubleshooting). Wartości Bootstrap
nie resetują haseł istniejących kont na zachowanym woluminie.

## Podstawowa obsługa

1. Zaloguj się jako administrator i zatwierdź dokładne hosty baz/SSH w **Application settings**.
2. Dodaj połączenie w **Servers**, sprawdź je i przydziel operatorom dostęp.
3. Opcjonalnie ustaw indywidualne hasło kopii lub włącz szyfrowanie globalne.
4. Wybierz **Queue backup** albo dodaj harmonogram. Przyjęcie zadania nie oznacza zakończenia kopii.
5. W **Backup history** pobierz plik lub wybierz **Test / preview**. Produkcyjny Compose domyślnie obsługuje testy Docker i przygotowuje obrazy baz.

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
