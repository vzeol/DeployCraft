# Project status

> Where DeployCraft stands today, and where to pick up. Read it at the start of a work session (maintainer,
> contributor or AI assistant). To take part: [`CONTRIBUTING.md`](../CONTRIBUTING.md). Task list:
> [`FEATURES.md`](FEATURES.md). Rules: [`CLAUDE.md`](../CLAUDE.md).

## Goal

A free (GPL v3), agentless tool to deploy software across a Windows fleet, covering the features expected from a
tool of this kind ([`FEATURES.md`](FEATURES.md)), then sorting and additions at the end.

## How we work

**One topic at a time.** A topic is finished on its branch (commits, tests, this file, PR, installer), then merged
and released once CI is green; the maintainer installs and tests it on their fleet, and **the next topic only
starts once they have validated everything**.

Information specific to the maintainer's environment (servers, test computers, accounts) stays in their notes
**outside the repository**: never copy it here.

## Current version: 1.0.0

**The whole [`FEATURES.md`](FEATURES.md) list is covered**: 61 features done, 22 dropped by the maintainer, 6 set
aside. Only 8.1 remains partial: LAPS is planned after v1 (issue #106).

**The code reorganization (#127) is finished**: the code follows the architecture rules (`CLAUDE.md` §
"Architecture") and every build checks the code quality rules (#142). The refactoring PRs that touched the
deployment path got a regression run on the domain test environment before merging; the whole release was run
again there before 1.0.0-rc.2 (one package with every step type, including a restart), then pull mode end to
end and the Message step shown to a logged-on user.

**1.0.0 is the same code as 1.0.0-rc.2**, tested by the maintainer on their fleet. Still to check: updating
from the console (the installed rc.2 console offers 1.0.0 at startup, "Yes" → install).

New since 1.0.0-rc.1 (PRs #116 to #142):
- Options › Background service: service account, active sessions per computer (#116); wider package tree (#117)
- Package editor: Ctrl+Up / Down, warnings of an enabled step (#138)
- Deploy once: choose the start step from the right-click menu of the targets (#141)
- Code reorganization without behavior change (#127: #130 to #140), code quality rules checked by every build (#142)

New since 0.14.0 (PRs #61 to #114):
- Complete Install step: MSI options, command line, additional files (2.20–2.22)
- Package library from the public winget catalog, with automatic package updates (10.4)
- Package folders, cut / copy / paste in the tree, package Details tab, View menu, home page
- First-run wizard, built-in help (F1), About window
- Step warnings, step output (copy, open, save), "Cancelled" status and counters
- Scheduling from a deployment, adding / removing a package from a schedule from the tree
- DNS check of the computers typed in Deploy once (3.4)
- Adjustable parallel computers (1–100) and copy rate limit (8.4); copy progress (% and rate), runs and note per
  step (4.4)
- New version reported at startup, one-click install with a verified hash (8.14)
- More robust tracking: network loss tolerated during execution, slot lock recovered, readable UTF-16 output

Tested on the test domain: real deployments (Install, copies, cancellation), parallelism, rate limit, DNS check,
installer (`/RELAUNCH=1` reopens the console without elevation). For the maintainer to watch in daily use: the
whole UI, the library and automatic package updates.

## Shared models refactoring (#155)

- `DeployCraft.Shared` owns the previously linked console/service models and localized messages; both projects
  now reference the same assembly. The existing namespaces and JSON shapes are unchanged.
- Package expansion and default resolution moved from `Models.cs` to `Services/PackagePreparation.cs`.
  Extension methods preserve existing call sites, including the files being changed separately in #159.
- The test project no longer needs the console alias. All 436 tests pass, including two new tests
  checking shared type identity and dependency direction. Build and formatting pass; self-contained Windows
  publication includes the shared assembly, English resources and runner. The maintainer tested and merged #160.
- No behavior change; no real computer or domain deployment was run. — Codex

## Main window refactoring (#156): home screen first

- `Presentation/HomePage.cs` handles home loading, text choices and row data through injected API/time functions.
  `MainWindow.Home.cs` renders the result and forwards clicks. Existing FR/EN resource strings are reused.
- Schedules still load before the overview is displayed; library status and tree markers follow. A library API
  failure keeps the overview and previous markers, while a schedule failure propagates without changing the page.
- Only the home screen is extracted. Package lists, global refresh, navigation and the other screens remain
  future steps of #156. This first PR needs another session review and maintainer local validation before merge.
- All 464 tests pass, including 25 new home cases covering both languages, plural choices, links, load ordering,
  refresh and error handling without a window or real service. Locked restore, Release build (zero warnings) and
  formatting pass. No behavior or visual change intended; no real-computer test was run. — Codex

## After v1

- 8.1 LAPS (issue #106).
- Import packages from an XML file (issue #14, on hold).
- 8.9 service account: set aside, picked up only if the need arises.
- Repository: code comments in English (#147). English console done (#148, #150): texts in French and English,
  Préférences › Interface › Langue; service messages follow the console during a request and the server's Windows
  language for background work.

Build: `.\build.ps1 -Version x.y.z` → `dist\DeployCraft-Setup-x.y.z.exe` (requires the .NET 10 SDK and Inno Setup 6).
Each version is published in GitHub **Releases** with its installer (tag `vx.y.z`); a release candidate or beta is
a pre-release (the console does not offer it).

## Technical notes

- Claude Code sessions in the cloud (Linux): .NET installed at startup, build / tests / format available (except
  Windows-only tests), see `CLAUDE.md` § "Cloud session".
- Test without touching real data: `dotnet run --project src\DeployCraft.Server -- --Urls http://localhost:5091 --DataDir <folder>` and start the console with `DEPLOYCRAFT_URL=http://localhost:5091`.
- Close only your own test windows (by PID), never all DeployCraft instances.
- Windows PowerShell 5.1: save `.ps1` scripts as UTF-8 **with BOM** (otherwise accented characters break).
- Conditions are checked by the runner (`src\DeployCraft.Runner\Conditions.cs`, tested through a link in the test
  project); reading the real computer is in `LocalFacts.cs`.
