# OctoBackup

[English](../README.md)

Kopie ręczne i zaplanowane trafiają do trwałej kolejki. Panel pokazuje stan zadań,
blokuje duplikaty i pozwala anulować lub usunąć żądanie z listy. Domyślny czas na
anulowanie wynosi 20 sekund; administrator zmienia go oraz odstęp między zadaniami
w ustawieniach aplikacji. Szczegóły: [OPERATIONS.md](OPERATIONS.md).

Samodzielnie hostowana aplikacja do zarządzania kopiami baz danych: **API .NET 10**
i **React + TypeScript**. Połączenia, uprawnienia, harmonogramy i historia w jednym panelu.
Kompaktowy, ciemny interfejs dla zespołów technicznych łączy fioletowe gradienty,
limonkowe akcje i responsywne widoki.

## Uruchomienie

W katalogu głównym wykonaj:

```powershell
node scripts/dev.mjs init
```

Powstanie prywatny plik `.env` z losowymi hasłami i kluczem szyfrowania. Istniejący
plik nie jest nadpisywany. Dane logowania to `ADMIN_EMAIL` i `ADMIN_PASSWORD`.

**Obie aplikacje w Dockerze:**

```powershell
docker compose up --build -d --wait
```

Otwórz **https://localhost:8443**. Domyślny certyfikat lokalny jest samopodpisany.
Własny certyfikat i klucz można umieścić w ignorowanym katalogu `certs/`.
Frontend Nginx obsługuje React i przekazuje żądania API oraz SignalR.

**Tylko bazy w Dockerze, aplikacje w konsoli:**

```powershell
docker compose -f compose.dbs.yaml up -d --wait
dotnet restore OctoBackup.sln
npm ci --prefix Frontend
node scripts/dev.mjs
```

Panel: **http://127.0.0.1:5173**, API: port 5080. Argument `api` lub `frontend`
uruchamia wybraną aplikację w osobnym terminalu. Wymagane .NET SDK 10.0.302+ i
Node.js 22.12+. Eksport z API na hoście wymaga lokalnych narzędzi dump.

Domyślnie uruchamiane są PostgreSQL (55432) i MySQL (53306). Opcjonalny SQL Server
(51433): `docker compose -f compose.dbs.yaml --profile sqlserver up -d --wait`.
To bazy docelowe kopii; konfiguracja OctoBackup pozostaje w SQLite.

**API bezpośrednio z konsoli:**

```powershell
dotnet run --project Backend/src/Server.Api
```

W katalogu API wystarczy `dotnet run`. Adres: **http://127.0.0.1:5080**.
Pierwsze uruchomienie tworzy prywatny, ignorowany `appsettings.Local.json` w katalogu
API, z kluczem szyfrowania i danymi administratora. Istniejący klucz z `.env` jest
zachowywany. Dane aplikacji trafiają do `Backend/data`. Produkcja wymaga jawnego
dostarczenia sekretów. React uruchom osobno: `npm run dev --prefix Frontend`.

Szczegóły konfiguracji, certyfikatów, uruchamiania i odzyskiwania:
[OPERATIONS.md](OPERATIONS.md).

Administrator może zarządzać dozwolonymi hostami baz danych i SSH w **Application
settings**. Jeśli dodawanie serwera jest blokowane, dodaj tam dokładny host lub IP
i zapisz. Panel udostępnia też rejestrację i zatwierdzanie kont, domyślną retencję
dla nowych serwerów oraz interwał harmonogramów. Zmiany pozostają po restarcie API.
Eksport MySQL wykrywa możliwości klienta i zwraca konkretne wskazówki przy błędzie.
[Diagnostyka kopii MySQL](OPERATIONS.md#mysql-dump-failures) opisuje wymagane
narzędzia, TLS i uprawnienia do odczytu definicji procedur.

## Struktura

- `Backend/`: API, moduły, testy i Dockerfile. `Backend/artifacts/publish` zawiera
  wygenerowane pliki publikacji, nie kod źródłowy ani dane produkcyjne.
- `Frontend/`: React, Nginx, testy i osobny Dockerfile.
- `docs/`: dokumentacja i diagramy; `scripts/`: narzędzia lokalnego uruchomienia.
- `compose.yaml`: API i frontend; `compose.dbs.yaml`: same bazy.

Działają połączenia/SSH, ręczne kopie, harmonogramy, historia, pobieranie,
zarządzanie kontami i uprawnieniami. Weryfikacja odtwarzania jako funkcja aplikacji,
integracja poczty, MFA i szyfrowanie plików kopii pozostają w planach. ZIP nie szyfruje
danych. SQL Server wymaga wspólnej ścieżki plików dla tworzenia i pobierania kopii.

[Architektura](ARCHITECTURE.md) | [Bezpieczeństwo](SECURITY.md) |
[Postęp i testy](MIGRATION.md) | [Plan](ROADMAP.md) | [Licencja MIT](../LICENSE)
