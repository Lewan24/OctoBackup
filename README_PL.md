# 💾 OctoBackup  

[🇵🇱 Read in English](README.md)

---

![.NET](https://img.shields.io/badge/.NET-9-blueviolet)
![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-purple?logo=blazor&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Ready-blue?logo=docker&logoColor=white)
![Database](https://img.shields.io/badge/Databases-MySQL%20%7C%20PostgreSQL%20%7C%20SqlServer-red)
![License](https://img.shields.io/badge/License-MIT-green)

[![.NET](https://github.com/Lewan24/DatabasesBackupServiceDotNet/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Lewan24/DatabasesBackupServiceDotNet/actions/workflows/dotnet.yml)

---

**OctoBackup** to otwarto-źródłowa aplikacja konteneryzowana w **Dockerze**, napisana w **.NET 9 Blazor WebAssembly** w architekturze modularnego monolitu.  
Aplikacja umożliwia **centralne zarządzanie kopiami zapasowymi wielu baz danych** – obecnie wspiera **MySQL**, **PostgreSQL** oraz **MS SQL Server**.  

---

## 🔑 Najważniejsze funkcje  

- 📅 Tworzenie harmonogramów backupów  
- 🔐 Szyfrowanie oraz kompresja plików kopii  
- 📊 Historia backupów i statystyki  
- 📧 Powiadomienia mailowe o statusach zadań  
- 👥 **System grup i ról użytkowników** – administrator może przypisywać użytkowników do serwerów oraz nadawać użytkownikom dostęp np. podglądu kopii danej bazy, konfiguracji serwera, tunelów itd.
- 🗄️ Konfiguracja i wszystkie dane zarządzające są przechowywane w **SQLite** (prosta, lekka baza do obsługi aplikacji)  

---

## 🌟 Dlaczego ta aplikacja?  

🔹 Większość darmowych narzędzi backupowych ogranicza się do obsługi 1–2 baz danych i wymaga płatnych rozszerzeń.  
🔹 Ta aplikacja została stworzona jako **w pełni darmowe i elastyczne rozwiązanie** do obsługi wielu baz danych w jednym miejscu.  
🔹 Dodatkowo zapewnia **bezpieczeństwo (szyfrowanie, autoryzacja)** i **łatwość zarządzania użytkownikami**.  

---

## 🏗️ Architektura systemu  

- **UI** – Blazor WebAssembly  
- **API/Server** – logika aplikacji, obsługa backupów, komunikacja z bazami danych  
- **Baza aplikacji** – SQLite (użytkownicy, grupy, konfiguracje, historia backupów)  
- **Autoryzacja** – użytkownicy, role i grupy z przypisaniem do konkretnych baz danych  
- **Backup Engine** – generowanie, szyfrowanie, kompresja i archiwizacja kopii  
- **Powiadomienia** – system e-mail (statusy, przypomnienia, błędy)  
- **Testowanie** – możliwość automatycznego odtworzenia kopii w środowisku testowym  

---

## 🚀 Uruchomienie i konfiguracja

Przejdź do sekcji **[WIKI](https://github.com/Lewan24/OctoBackup/wiki)** i zapoznaj się z instrukcjami.

## 🗺️ Roadmap  

### ✅ Zrealizowane  
- Obsługa **MySQL**  
- Obsługa **PostgreSQL**  
- Panel **UI** w Blazor  
- System użytkowników, ról i grup  
- Harmonogramy backupów  
- Powiadomienia e-mail  
- Przechowywanie konfiguracji w **SQLite**  
- Obsługa **MS SQL Server**  
- Statystyki i raporty w UI  
- Kompatybilność z tunelami do zewnętrznych zabezpieczonych serwerów
- SignalR 

### 🛠️ W trakcie / Do zrobienia  
- Zaawansowane testowanie backupów (tymczasowe kontenery + SQL check queries)  

## 📜 Licencja
Projekt udostępniany jest na licencji MIT.<br>
Możesz go używać komercyjnie i prywatnie, rozwijać i dostosowywać do własnych potrzeb.

## 🤝 Kontrybucja
Chcesz dołożyć swoją cegiełkę?<br>
Otwórz zgłoszenie 🐛 w [Issues](../../issues)<br>
Zaproponuj funkcję 💡<br>
Wyślij pull request 🚀
