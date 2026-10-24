# Contributing to DeployCraft

Procedure for anyone taking part in development, whether or not they work with an AI coding assistant.
Read it in full before the first commit.

Everyone participating follows the [community conduct rules](CODE_OF_CONDUCT.md).

## 1. What we are building

DeployCraft is a free (GPL v3) tool for deploying software across a Windows fleet, with no agent:
a Windows service on a domain server, a WPF console, and a small runner temporarily copied
to each computer. Deliverable: **a single installer**.

Current goal: cover the features expected from a deployment tool of this kind (`docs/FEATURES.md`),
then sort things out at the end.

Repository documents:

| File | Content |
|---|---|
| `docs/STATUS.md` | Project status, what remains to be tested, resume point |
| `docs/FEATURES.md` | List of features, with their status: **this is the task list** |
| `docs/SPEC.md` | Specification: what DeployCraft does and how |
| `CONTRIBUTING.md` | This guide |
| `CLAUDE.md` | Instructions loaded automatically by Claude Code (apply to everyone) |

## 2. Independent implementation (non-negotiable)

DeployCraft is written from scratch. We **never** reuse the code, icons, logo, images,
texts or files of another piece of software. Labels are written in our own words.
Approach: describe the desired behavior in `docs/SPEC.md` → code **from the spec**.
Observing other tools in operation and citing their features (`docs/FEATURES.md`) is allowed; describing their
internal workings or reusing their texts and visuals is not.
Our visual identity: orange accent, Windows icons (Segoe Fluent / MDL2).

**No internal information** in the public repository: names of servers, computers, domains, accounts,
IP addresses, organization names, personal emails — including in screenshots and commit
messages. Use examples (`SERVEUR-01`, `PC-TEST-01`, `DOMAINE.LOCAL`).

## 3. Access and tools

1. Install: Git, **.NET 10 SDK**, **Inno Setup 6** (`winget install JRSoftware.InnoSetup`),
   GitHub CLI (`gh auth login`, then `gh auth setup-git`).
2. External contributor: **fork** the repository, work on a branch of your fork, open a PR.
   Collaborator invited by the maintainer: `gh repo clone vzeol/DeployCraft`, branch + PR.
3. Set up Git to sign with your hidden GitHub address (`git config user.email "…@users.noreply.github.com"`).

Structure:

| Folder | Role |
|---|---|
| `src/DeployCraft.Server` | Windows service: local HTTP API (port 5080), SQLite database, deployment engine, AD reading |
| `src/DeployCraft.Console` | WPF console; references `DeployCraft.Shared` |
| `src/DeployCraft.Runner` | **.NET Framework 4.8** runner (present on every Windows), launched as a temporary service on the computers |
| `src/DeployCraft.Shared` | Common models, pure package preparation and localized messages; no service or UI dependency |
| `installer/` | Inno Setup script |

## 4. Build and test safely

```powershell
dotnet build DeployCraft.slnx
```

**Never test on real data.** Run an isolated instance:

```powershell
# Test server (database in a throwaway folder, port 5091)
dotnet run --project src\DeployCraft.Server -- --Urls http://localhost:5091 --DataDir C:\Temp\dc-test

# Console connected to that server (another terminal)
$env:DEPLOYCRAFT_URL = "http://localhost:5091"
.\src\DeployCraft.Console\bin\Debug\net10.0-windows\DeployCraft.exe
```

- The server must run **as administrator** to deploy to the local machine.
- Credentials (deployment accounts) are entered in the console (Options › Credentials).
  Never write a password in code, a commit, an issue or a conversation with an AI.
- Close only **your own** test windows (by PID), never all DeployCraft instances.
- Real tests on a domain: only on test computers agreed with the maintainer
  (test computer, non-production machine). Start with a step that has no effect (`hostname`).

Automated tests: `dotnet test DeployCraft.slnx` (project `tests\DeployCraft.Tests`). Every change in
behavior adds or updates a test, even a basic one. Anything that touches a real computer (network, temporary
service) is tested by hand on the isolated instance: the PR says how.

## 5. Choose a task

1. Take one or more ⬜ / 🟡 rows from `docs/FEATURES.md`, preferably in the same block
   (e.g. "5. Schedules").
2. Open a **GitHub issue** titled with the numbers (`[5.1–5.3] Schedules: page + triggers`)
   and assign it to yourself: this is what prevents two people working on the same thing.
3. If the desired behavior is not yet in `docs/SPEC.md`: describe it first
   (in the same PR).

## 6. Branches, commits, PRs

In short: everyone works on **their own branch** (their copy of the project) and proposes their changes
through a **pull request** (PR). Nothing enters `main` without review: nobody breaks
the work of others, and several people can move forward at the same time.

```powershell
git switch main ; git pull                 # start from the latest version
git switch -c feat/schedules               # create your branch
# … code, test …
git status ; git diff                      # review before adding
git add src\DeployCraft.Shared\Data\Models.cs …   # files named one by one, never `git add -A`
git commit -m "feat: schedules, page and triggers"
git push -u origin feat/schedules
gh pr create --fill                        # opens the PR (the template is filled in)
```

If `main` has moved on in the meantime: `git switch main ; git pull ; git switch feat/… ; git merge main`,
resolve any conflicts, re-test, push.

- `main` must **always build** and produce a working installer.
- One branch per task: `feat/schedules`, `fix/ad-tree`, `docs/spec-reboot`…
- Commits in Conventional Commits format, with the description in English, in the present tense (`feat: weekly trigger`,
  `fix: …`, `docs: …`, `test: …`, `chore: …`). One commit = one logical change. Never a vague message.
  Commit messages, PRs and issues are written in English.
- PR to `main` with: the `docs/FEATURES.md` rows concerned, what was tested, screenshots for the interface.
  Update `docs/FEATURES.md` (and `docs/SPEC.md` if needed) **in the PR**.
- **Who decides**: the maintainer (GitHub account `vzeol`), helped by AI coding assistants. They review PRs,
  request fixes, merge and publish, always through a PR (never a direct commit to `main`) and with CI
  green. Every other contributor proposes through a PR. Merge as "squash".
- `main` is protected on GitHub: PR required, CI (`build`) green and branch up to date to merge,
  including for administrators; no force-push or deletion.
- Never `git push --force` on `main`; on your own branch, `--force-with-lease` only.
- Before each commit: no secret in `git diff --staged`, `.gitignore` intact, build + tests + format OK.

## 7. Code conventions

- Follow the architecture rules in [`CLAUDE.md`](CLAUDE.md) § "Architecture" (layers, interfaces at the
  boundaries, one file per area, thin UI). They apply to everyone, with or without an AI assistant.
- Follow the style of the surrounding code: modern C#, code names in English, **comments and test
  messages in English**; UI texts in French and English (`Resources/Strings*.resx` for the console,
  `DeployCraft.Shared/Texts/Msg*.resx` for shared and service messages, `RunnerTexts.cs` for the runner).
  Few comments, and only to explain *why*.
- Error messages: readable by a technician, with the probable cause
  ("Access denied to the ADMIN$ share of PC01: is the deployment account an admin of this computer?").
- SQLite database: new columns via `AddColumnIfMissing` in `Store.Migrate()`. An existing
  database must never lose data. Nested structures stored as JSON.
- API: routes in `src/DeployCraft.Server/Program.cs`, client in `src/DeployCraft.Console/ApiClient.cs`.
- Shared models: reference `DeployCraft.Shared`, never compile service files into the console. Existing namespaces
  (`DeployCraft.Server.Data`, `DeployCraft.Server.Msg`) are retained to keep this refactoring focused. Package
  expansion and option resolution live in `Services/PackagePreparation.cs`, exposed as extension methods used by
  both deployment and console previews.
- Runner: .NET Framework 4.8 **with no external dependency**. Exchange with the server goes through
  `job.json` / `status.json`: change `Runner/Job.cs` and `Server/Remote/RunnerContract.cs` together.
- Console: never a hard-coded color. Add the key in `Themes/Clair.xaml` **and** `Themes/Sombre.xaml`,
  use it as `{DynamicResource …}` (otherwise it does not follow the theme change), and check the
  window in both themes. An implicit style for a standard control builds on the Fluent one
  (`BasedOn="{StaticResource {x:Type TextBox}}"`, see `Themes/Styles.xaml`).
- No new NuGet dependency without a good reason (explained in the PR). Versions pinned by
  `packages.lock.json` (committed; regenerated by `dotnet restore` when a dependency changes).
- Format: `dotnet format DeployCraft.slnx` before committing (rules in `.editorconfig`, checked by CI).
- `.ps1` scripts saved as **UTF-8 with BOM** (otherwise Windows PowerShell 5.1 breaks accents).

## 8. Version and release

Number `MAJOR.MINOR.PATCH`: new feature → minor, fix → patch.

```powershell
.\build.ps1 -Version 0.7.0                  # → dist\DeployCraft-Setup-0.7.0.exe
git tag v0.7.0 ; git push origin v0.7.0
gh release create v0.7.0 dist\DeployCraft-Setup-0.7.0.exe --title "DeployCraft 0.7.0" --notes "…"
```

The release is made after the merge into `main`, by the maintainer's sessions.
Then update `docs/STATUS.md` (current version, to test, next steps).

## 9. Working with an AI coding assistant

AI assistants are welcome. Claude Code reads `CLAUDE.md` on its own when opening the repository. At the start
of a session, ask it to read `docs/STATUS.md`, `docs/FEATURES.md` and this guide. The same rules apply to it:
independent implementation, no internal information, one branch per task, never handle a password. A contributor's assistant has the same rights as they do: it proposes through a PR,
it does not merge. The complete rules are in `CLAUDE.md`.

At the end of a session: update `docs/STATUS.md` (where we stand, what remains) so that the
next person, human or AI, can pick up without guessing.
