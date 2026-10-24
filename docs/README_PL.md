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
- `Frontend/`: React, Nginx, testy i osobny Dockerfile. Warstwa `app` łączy funkcje
  serwerów i kolejki bez bezpośrednich importów między funkcjami.
- Prywatne dane i klucze (`Backend/data`) oraz lokalne artefakty
  (`Backend/artifacts`) pozostają oddzielone od kodu źródłowego.
- `docs/`: dokumentacja i diagramy; `scripts/`: uruchamianie, testy wdrożenia
  i publikowanie obrazów (`build-and-push.cmd`, kontekst budowania: katalog główny).
- `deploy/`: opcjonalne nakładki Compose; łącz je po głównym `compose.yaml`.
- `compose.yaml`: API i frontend; `compose.dbs.yaml`: same bazy.

Działają połączenia/SSH, ręczne kopie, harmonogramy, historia, pobieranie,
zarządzanie kontami i uprawnieniami. Testy odtwarzania uruchamiają jednorazowe kontenery
i zapisują podgląd próbek tabel oraz danych. Ręczny test jest dostępny przez **Test / preview**
w historii kopii. Administrator może włączyć automatyczne testy po udanej kopii.
Najpierw skonfiguruj środowisko wykonawcze: [instrukcja](OPERATIONS.md#restore-testing).
Panel pokazuje, gdy testowanie jest wyłączone we wdrożeniu. Zmiany prywatnej
konfiguracji JSON są przeładowywane; pobierz wybrany obraz i ponów test. Zapisane
raporty pozostają dostępne.
Nagłówki kopii wybierają zgodną, zatwierdzoną wersję obrazu. Obraz musi być wcześniej
pobrany; kopie PostgreSQL 17 wymagają serwera testowego 17 lub nowszego. Testy
MySQL/MariaDB odczytują właściwą bazę kopii i zachowują dane binarne.
Szyfrowanie kopii jest domyślnie wyłączone. Administrator może włączyć szyfrowanie
globalne w panelu po wpisaniu i potwierdzeniu własnego hasła. Indywidualne hasło
serwera działa niezależnie od przełącznika globalnego i można je ustawić podczas
tworzenia/edycji połączenia lub w ustawieniach szyfrowania serwera. Wyłączenie
szyfrowania dotyczy nowych kopii; istniejące zachowują stare klucze i wymagane hasła.
Hasła/klucze pozostają chronione przez .NET Data Protection; zachowaj wolumin kluczy.
SQLite obsługuje migawki plików z katalogu wskazanego przez wdrożenie, także z WAL.
SQL Server wymaga współdzielonego katalogu; konfiguracja mapuje różne ścieżki montowania.
[Szyfrowanie, zgodność silników i narzędzie offline](BACKUP_ENCRYPTION.md).
[Konfiguracja połączeń, narzędzi eksportu, prywatnych sieci Docker i katalogu
współdzielonego SQL Server](DATABASE_CONNECTIONS.md). API może łączyć się z bazą bez
opublikowanego portu hosta, jeśli oba kontenery należą do tej samej sieci Docker.
Klient konsolowy wymaga lokalnego mysqldump/mariadb-dump; można wskazać ścieżkę
w DatabaseTools. Połączenia PostgreSQL z hasłem pomijają negocjację GSS/Kerberos,
zachowując wybraną politykę TLS.
Integracja poczty i MFA pozostają w planach.

Zmiany ustawień wykonawczych w JSON (np. map obrazów testów przywracania) są
wczytywane bez restartu API. W Dockerze użyj deploy/compose.config-reload.yaml i montowanego
katalogu konfiguracji. Obraz wskazany w mapie pobierz na tym samym demonie Docker,
którego używa API, np. `docker image pull mysql:8.4`, a następnie ponów test.
[Przeładowywanie konfiguracji i pobieranie obrazów](CONFIGURATION_RELOAD.md).
Ścieżki danych/kluczy, HTTP/proxy i zmienne środowiskowe wymagają restartu wdrożenia.

[Architektura](ARCHITECTURE.md) | [Bezpieczeństwo](SECURITY.md) |
[Postęp i testy](MIGRATION.md) | [Plan](ROADMAP.md) | [Licencja MIT](../LICENSE)

## Odświeżony interfejs (2026-10-24)

Interfejs ma ciemnogranatowe powierzchnie, głębokie niebieskie i kobaltowe
gradienty oraz białe etykiety przycisków oraz większy tekst i ikony. Białe logo ośmiornicy jest wyśrodkowane w menu;
nazwa OctoBackup pojawia się po najechaniu lub uzyskaniu fokusu klawiaturą.
Edycja i istotne operacje korzystają z okien dialogowych z danymi zasobu,
potwierdzeniem, stanem oczekiwania i błędami API. Ustawienia aplikacji prezentują
podsumowania; przyciski **Edit workspace policies** i **Manage backup encryption**
otwierają edytory. Zobacz [opis interfejsu](DESIGN.md).
Uwierzytelnianie i autoryzacja API pozostają bez zmian.

Tło jest niemal czarne (`#04070D`), a logo ma 128px w menu i 104px na stronie logowania.

Historia kopii oddziela kolorowy status testu przywracania od kolumny Actions.
**Test / preview** otwiera raport, a **Download** udostępnia menu pobierania.
Pobranie odszyfrowanej kopii wymaga hasła archiwum; kopie niezaszyfrowane oferują plik oryginalny.
