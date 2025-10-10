# 💾 OctoBackup  

[🇵🇱 Read in Polish](README_PL.md)

---

![.NET](https://img.shields.io/badge/.NET-9-blueviolet)
![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-purple?logo=blazor&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Ready-blue?logo=docker&logoColor=white)
![Database](https://img.shields.io/badge/Databases-MySQL%20%7C%20PostgreSQL%20%7C%20SqlServer-red)
![License](https://img.shields.io/badge/License-MIT-green)


[![.NET](https://github.com/Lewan24/DatabasesBackupServiceDotNet/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Lewan24/DatabasesBackupServiceDotNet/actions/workflows/dotnet.yml)

---

**OctoBackup** is an open-source, containerized application built with **.NET 9 Blazor WebAssembly** using a modular monolith architecture.  
The application allows **centralized management of database backups across multiple servers** – currently supporting **MySQL**, **PostgreSQL** and **MS SQL Server**.  

---

## 🔑 Key Features  

- 📅 Schedule automatic backups  
- 🔐 Encryption and compression of backup files  
- 📊 Backup history and statistics  
- 📧 Email notifications about backup status  
- 👥 **Roles system and Server groups** – the administrator can assign users to servers, and allow users to access (e.g., view the latest backup of a given database, server configuration, tunnel settings etc.)  
- 🗄️ Configuration and management data are stored in **SQLite** (lightweight embedded database)  

---

## 🌟 Why this app?  

🔹 Most free backup tools only support 1–2 databases and require paid upgrades for more advanced features.  
🔹 This application was created as a **fully free and flexible solution** for managing multiple databases in one place.  
🔹 It also provides **security (encryption, authorization)** and **easy user management**.  

---

## 🏗️ System Architecture  

- **UI** – Blazor WebAssembly  
- **API/Server** – business logic, backup engine, database communication  
- **Application Database** – SQLite (users, groups, configurations, backup history)  
- **Authorization** – users, roles, servers groups 
- **Backup Engine** – generation, encryption, compression, archiving of backups  
- **Notifications** – email system (statuses, reminders, errors)  
- **Testing** – option to restore and verify backups in a temporary test container environment (if set, tests runs automatically)

---

## 🚀 Setup & Configuration  

Go to the **[WIKI](https://github.com/Lewan24/OctoBackup/wiki)** section and follow the setup instructions.  

---

## 🗺️ Roadmap  

### ✅ Completed  
- Support for **MySQL**  
- Support for **PostgreSQL**  
- **UI Panel** in Blazor  
- User, role & group management system  
- Backup scheduling  
- Email notifications  
- Configuration storage in **SQLite**
- Support for **MS SQL Server**
- Statistics and reporting in UI
- Tunnels compability
- SignalR 

### 🛠️ In Progress / Planned  
- Advanced backup testing (temporary containers + SQL check queries)  
- Multilanguage

---

## 📜 License  
This project is released under the MIT license.<br>
You are free to use it commercially and privately, extend it, and adapt it to your needs.  

---

## 🤝 Contributing  
Want to contribute?<br>
Open an issue 🐛 in [Issues](../../issues)<br>
Propose a feature 💡<br>
Submit a pull request 🚀
