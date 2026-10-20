<div align="center">

**English** · [Français](README.fr.md)

# DeployCraft

**Deploy software to a whole Windows fleet, agentless, from a single console.**

[![CI](https://github.com/vzeol/DeployCraft/actions/workflows/ci.yml/badge.svg)](https://github.com/vzeol/DeployCraft/actions/workflows/ci.yml)
[![Version](https://img.shields.io/github/v/release/vzeol/DeployCraft?include_prereleases&label=version)](https://github.com/vzeol/DeployCraft/releases)
[![License: GPL v3](https://img.shields.io/badge/license-GPL%20v3-blue)](LICENSE)
![Windows](https://img.shields.io/badge/platform-Windows-0078D6)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

[Download](https://github.com/vzeol/DeployCraft/releases) ·
[Report an issue](https://github.com/vzeol/DeployCraft/issues/new/choose) ·
[Features](docs/FEATURES.md) ·
[Contribute](CONTRIBUTING.md)

![DeployCraft console: following deployments](docs/console.png)

</div>

DeployCraft is a **free and open-source** tool (GPL v3) for system administrators: a service on a domain server, a
Windows console, and a small program copied to each computer only for the time of a deployment, then removed.
Nothing has to be installed on the computers beforehand.

Its layout follows the habits of the deployment tools on the market; it is an independent work, written from
scratch.

> The console is in **French** for now. An English version of the console is planned after 1.0.0. Menu names below
> are given in French, with their meaning in English.

## Features

**Packages**
- Steps: installer (`.msi`, `.exe`, `.msu`, `.msp` with options), command, PowerShell, file copy, pause, restart or
  shutdown, message to the user, nested package
- Conditions per package and per step: Windows version and architecture, file, registry, logged-on user…
- Run-as account per step (deployment account, System, logged-on user), success codes, timeout, behaviour on error
- Library of ready-to-use packages, verified download and **automatic updates**
- Folders, drag and drop, cut / copy / paste, export and import

**Deployment**
- Computers typed, pasted or picked from **Active Directory**, names checked in DNS
- Live follow-up computer by computer: current step, copy progress, output of each step
- **Schedules** (retry until success), redeploy failed computers, start again from a given step
- Offline computers: **Wake-on-LAN** and new attempts
- Adjustable number of computers in parallel and copy bandwidth

**Administration**
- Multiple credentials, encrypted on the server; console access limited to allowed accounts
- Automatic history cleanup, first-run wizard, built-in help (F1)
- New DeployCraft versions offered at startup, installed in one click
- Light or dark theme

Full list and status of each feature: [`docs/FEATURES.md`](docs/FEATURES.md).

## Requirements

| | |
|---|---|
| **Server** | 64-bit Windows, member of the domain (preferably a dedicated server or VM), administrator account |
| **Computers** | Windows 10 or 11, `ADMIN$` share and service manager reachable from the server (file sharing, port 445) |
| **Deployment account** | Administrator of the computers (for example a domain account member of the local Administrators group) |

## Installation

1. Download `DeployCraft-Setup-x.y.z.exe` from the [**Releases**](https://github.com/vzeol/DeployCraft/releases).
2. Run it as administrator on the server. It installs the **DeployCraft** Windows service (automatic start, Local
   System account) and the console. Nothing else to install: .NET is included.
3. Open the **DeployCraft** console: the first-run wizard walks you through the first settings (Aide › Assistant de
   démarrage — *Help › Setup wizard* — to come back to it).

By default, only the server's administrators can open the console. To give access to other accounts or groups:
Options › **Utilisateurs de la console…** (*Console users*).

## First deployment

1. **Options › Identifiants…** (*Credentials*): add the deployment account. The password is typed in the console and
   encrypted on the server.
2. **Nouveau paquet** (*New package*), or a package from the **Bibliothèque** (*Library*): for example an Install
   step with a `.msi`.
3. **Déployer une fois** (*Deploy once*): pick a **test computer**, then follow the deployment live.
4. Once it works on a test computer, deploy to the others or create a **planification** (*schedule*).

## Try the release candidate

**1.0.0** is on its way: the release candidate (`1.0.0-rc.1`) is available in the
[Releases](https://github.com/vzeol/DeployCraft/releases). Thank you for trying it!

- Install it on a test server or VM, and start with **test computers**.
- A problem, a remark? [Open an issue](https://github.com/vzeol/DeployCraft/issues/new/choose): the form only asks
  for what is needed, in French or English. Remember to remove the names of your servers, computers and domain.
- A security vulnerability: privately, see [`SECURITY.md`](SECURITY.md).

## Configuration

No `.env` file: the service is set with command-line options (or `appsettings.json`).

| Setting | Default | Purpose |
|---|---|---|
| `--Urls` | `http://localhost:5080` | Address of the service's local API |
| `--DataDir` | `C:\ProgramData\DeployCraft` | SQLite database and data (restricted to System + Administrators) |
| `DEPLOYCRAFT_URL` (console variable) | `http://localhost:5080` | Service the console connects to |

## Contributing

Contributions are welcome: reports, ideas, code. Before your first commit, read
[`CONTRIBUTING.md`](CONTRIBUTING.md) (isolated test instance, branches, PRs, rules).
Issues can be written in English or French.

Requirements: .NET 10 SDK and Inno Setup 6.

```powershell
dotnet build DeployCraft.slnx                    # build
dotnet test DeployCraft.slnx                     # automated tests
dotnet format DeployCraft.slnx                   # formatting
.\build.ps1 -Version 1.0.0                       # installer → dist\
```

| Folder / file | Purpose |
|---|---|
| `src/DeployCraft.Server` | Windows service: local API, SQLite database, deployment engine, AD lookup |
| `src/DeployCraft.Console` | WPF console (the desktop application) |
| `src/DeployCraft.Runner` | .NET Framework 4.8 runner, run as a temporary service on the computers |
| `tests/DeployCraft.Tests` | Automated tests |
| `installer/` | Inno Setup script |
| [`docs/SPEC.md`](docs/SPEC.md) | Functional specification |
| [`docs/FEATURES.md`](docs/FEATURES.md) | Feature list and status: the task list |
| [`docs/STATUS.md`](docs/STATUS.md) | Project status and where to pick up |

## License

Copyright (C) 2026 vzeol.

DeployCraft is free software: you can redistribute it and/or modify it under the terms of the
**GNU General Public License version 3** (see [`LICENSE`](LICENSE)). It comes **without any warranty**: try it on
test computers first.
