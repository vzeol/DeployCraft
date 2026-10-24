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

## Current version: 1.0.0 — next: 1.1.0-rc.1

**1.1.0-rc.1** is the release candidate of 1.1.0, built from `main` after #162. The maintainer updates their fleet
with it, uses it for a few days, then the same code is published as 1.1.0.

New since 1.0.0 (PRs #143 to #162):
- Main window start size fits small screens (#125).
- PowerShell step output preserves Unicode (#143).
- Dependabot groups monthly version updates into one pull request per ecosystem (NuGet and GitHub Actions), with manual merging (#174).
- Issue triage uses descriptive labels and an undated Backlog milestone (#179).
- **English console** (#148, #150, #151): every text in French and English, Préférences › Interface › Langue
  (applied at the next start); built-in help in both languages; service messages follow the console language during
  a request and the server's Windows language for background work (deployment results, schedules); the runner
  follows its job.
- **Clean stop** (#159): when the service stops, running computers are cancelled, the service waits for the cleanup
  on the computers and marks them "Interrupted: the DeployCraft server was stopped.". Tested on the domain.
- **Code** (no behavior change): comments in English (#147); service/runner contract tests (#158);
  `TargetSession` split into `SlotLock` and `RunnerJobBuilder` (#161, domain regression OK); shared project
  `DeployCraft.Shared` for the models used by console and service (#160); `PackageLibrary` and
  `XmlPackageImport` split (#165, #166); the main window's logic moved into testable classes under
  `Presentation/`, screen by screen (#162, #167, #168, #169, #170: home, packages, deployments, schedules, library,
  navigation and refresh).

**Next, after 1.1.0-rc.1 (on the maintainer's fleet):** the refactorings above (#164 to #170) go into 1.1.0-rc.2
or 1.1.0, after the maintainer's validation.

Contributors: the maintainer's Claude sessions and Codex (OpenAI), which works from its own clone through PRs
(`AGENTS.md`); every PR is reviewed by another session before merging.

## 1.0.0

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

## After v1

- 8.1 LAPS (issue #106).
- Import packages from an XML file (issue #14, on hold).
- 8.9 service account: set aside, picked up only if the need arises.

Build: `.\build.ps1 -Version x.y.z` → `dist\DeployCraft-Setup-x.y.z.exe` (requires the .NET 10 SDK and Inno Setup 6).
Each version is published in GitHub **Releases** with its installer (tag `vx.y.z`); a release candidate or beta is
a pre-release (the console does not offer it).

## Technical notes

- Support channels and stable/preview release guidance are documented in `SUPPORT.md` (#180).
- Claude Code sessions in the cloud (Linux): .NET installed at startup, build / tests / format available (except
  Windows-only tests), see `CLAUDE.md` § "Cloud session".
- Test without touching real data: `dotnet run --project src\DeployCraft.Server -- --Urls http://localhost:5091 --DataDir <folder>` and start the console with `DEPLOYCRAFT_URL=http://localhost:5091`.
- Close only your own test windows (by PID), never all DeployCraft instances.
- Windows PowerShell 5.1: save `.ps1` scripts as UTF-8 **with BOM** (otherwise accented characters break).
- Conditions are checked by the runner (`src\DeployCraft.Runner\Conditions.cs`, tested through a link in the test
  project); reading the real computer is in `LocalFacts.cs`.
