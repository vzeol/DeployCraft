# DeployCraft — instructions for Claude Code

> This file is read automatically by Claude Code, the AI coding assistant used on this project. It holds the same
> rules as [`CONTRIBUTING.md`](CONTRIBUTING.md), written for an assistant: anyone contributing with an AI
> assistant gets them too.

The project maintainer (GitHub account `vzeol`) does not code: be autonomous (design, code, test), explain simply
what you do, and ask before any risky action on a real fleet. These rules apply to **everyone**: maintainer,
contributors, and every Claude session.

## Language

- **The repository is in English**: documents, code comments, test messages, commit messages, branches, issues,
  PRs and GitHub comments. `README.fr.md` is the only French page (a secondary translation of `README.md`).
- Talk to the maintainer in **French** (chat only). Issues opened by others in French get an answer in English,
  or in both languages if that helps.
- The console UI exists in French (default) and English: every new UI text goes into both
  `src/DeployCraft.Console/Resources/Strings*.resx` (service messages: `src/DeployCraft.Shared/Texts/Msg*.resx`,
  runner: `RunnerTexts.cs`); the tests check both languages have the same keys.

At the start of each session, check who you are (`gh api user --jq .login`) and read:
1. `docs/STATUS.md`: current state and where to resume
2. `docs/FEATURES.md`: list of features to cover (= task list)
3. `CONTRIBUTING.md`: independent work, isolated testing, branches / PRs, conventions, releases

## Authority over the repository

- **The maintainer's Claude sessions** (account `vzeol`) **manage the repository end to end**, without waiting for
  approval: branches, PRs, reviews (including contributors' PRs), merges, tags, releases, settings.
- They follow the good practices of this file: everything goes through a branch and a PR (never a direct commit on
  `main`), merge only once the PR is reviewed and CI is green, release after the merge.
- Still ask the maintainer before: installing a version on their server, deploying to a real computer.
  And at the slightest doubt or question, ask rather than assume.
- **Any other account** (contributor, and their Claude sessions): fork or branch + PR only. Do not merge any PR,
  do not create tags or releases, do not change repository settings. The maintainer decides.

## Security (top priority)

- The repository is **public**: everything committed is visible to all and stays in the history.
- **No internal information** in the repository (code, docs, tests, screenshots, commit messages, issues):
  names of servers, computers, domains, accounts, IP addresses, organization names, personal e-mail addresses.
  Use examples: `SERVEUR-01`, `PC-TEST-01`, `DOMAINE.LOCAL`, `admin`.
  Notes specific to the maintainer's environment stay **outside the repository**.
- Signed commits with the masked GitHub address (`…@users.noreply.github.com`), never a personal or work address.
- Never ask for, read or write a password: the user types it themselves in the console.
- Never commit a secret: `.env`, key, token, password, `.pem` / `.key` file, database (`deploycraft.db`).
  Never display the content of a `.env` (code, logs, commits, messages).
- `.gitignore`: never delete or weaken it.
- Secret or internal information committed by mistake: warn the maintainer right away, recommend revoking /
  changing what needs to be; removing it from the last commit is not enough (it stays in the history).
- Never commit installed dependencies or generated files (`bin/`, `obj/`, `publish/`, `dist/`).
- No `.env` in this project: configuration goes through `appsettings.json` and the `--DataDir` / `--Urls`
  options (documented in the README).

## Independent work

DeployCraft is written from scratch. Never any code, icon, image, text or file coming from another software;
labels are written in our own words.

We may **observe** other deployment tools at work (what they let you do, how the UI is organized) and **name
their features** to build the task list (`docs/FEATURES.md`). We never describe their internals and never reproduce
their texts or visuals: `docs/SPEC.md` only describes what DeployCraft does, in our own words.

## Taking a topic

- Before starting a topic, open (or reopen) its **issue** and write in it **who takes it**; a topic already taken
  is not touched. A need (a test, a check) is asked for in the related issue or PR.
- `main` is protected: PR required and green CI to merge, even for administrators.

## Testing on a fleet

Test on an isolated instance (`--DataDir`, port 5091). For a deployment to a real computer, first ask the
maintainer which one.

Who tests what before merging:
- **What depends on a domain** (Active Directory, credentials, deployment to a domain computer, shares, pull mode,
  Windows service, wake-on-LAN): tested on the test domain, with a precise scenario in the PR.
- **Everything else** (UI, options, package library…) is tested without a domain: the maintainer tries it locally
  with a test build, then the PR is merged after their approval and green CI, without a domain test. The PR says
  which case it is in.

## Commits

- **Conventional Commits** format, in English, imperative present tense:
  `feat: multiple credentials`, `fix: truncated time field`, `docs: …`, `refactor: …`, `test: …`,
  `chore: …`, `style: …`, `ci: …`. Forbidden: vague messages (`update`, `fix`, `wip`).
- One commit = one logical, consistent change.
- No `git add -A` / `git add .`: run `git status` and `git diff`, then add files **by name**.
- Never `git push --force` on `main` (on your own branch: `--force-with-lease` only).

## Workflow

- One branch per topic (`feat/…`, `fix/…`, `docs/…`, `chore/…`), never work directly on `main`.
- Small, easy-to-review PRs, with: what, why, how to test (template `.github/pull_request_template.md`).
  Polite, precise, professional issues and PRs.

## Architecture

Rules for all new code. Existing code is being brought in line step by step (issue #127);
when you touch a file that does not follow them yet, do not make it worse.

- **Layers, one job each.** HTTP endpoints only read the request, call a service and return the result: no
  business logic, no SQL. Deployment logic lives in services (`Deployment/`, `Library/`…). SQLite access lives in
  the data layer only. Network and Windows calls (shares, remote services, Active Directory) live in
  `Remote/` only.
- **Depend on interfaces at the boundaries.** Services use the database and the network through interfaces
  registered in dependency injection, so they can be tested with in-memory fakes, without a real computer.
- **One file per area.** Endpoints are grouped by area (packages, deployments, schedules…), one file each; the
  data layer has one repository per area. `Program.cs` only wires things together.
- **Models are data.** Logic that is more than a simple computed property goes into a service or a dedicated
  class, not into the models.
- **Step types are separate classes.** Each step type (Install, Command, File copy, Restart…) has its own
  class behind a shared interface; adding a step type should not mean editing a big `switch`.
- **Console: keep the UI thin.** Windows display data and forward user actions; loading, saving and decisions go
  into separate classes (view models or services) that can be tested without opening a window.
- **Small units.** As a rough guide: a class above ~400 lines or a method above ~60 lines is a sign it should be
  split. Prefer simple code over patterns: add an abstraction only when it removes a real problem.
- **Refactoring keeps behavior.** A refactoring PR changes no behavior, says so, and relies on the existing tests
  (plus new ones where coverage is missing). Never mix a refactoring with a feature in the same PR.

## Quality

- Add or update **tests** (`tests/DeployCraft.Tests`) for any behavior change, even basic ones. What touches a
  real computer is still tested by hand: the PR says how.
- Formatting: `dotnet format` (rules in `.editorconfig`).
- Dependencies pinned by `packages.lock.json` (always committed); no dependency added without a reason.
- GitHub Actions CI (`.github/workflows/ci.yml`) builds, tests and checks the formatting of every PR.
- Code quality rules (`.editorconfig`: unused usings, methods too complex, disposable fields, ignored results) are
  checked by every build; CI fails on any finding. Fix the code rather than silence a rule.
- No dead code, no large commented-out blocks, no leftover debugging `Console.WriteLine` / `Debug.WriteLine`.

## Before each commit or push, check

1. No secret, no internal information or sensitive file in `git diff --staged`.
2. `.gitignore` intact.
3. `dotnet build DeployCraft.slnx`, `dotnet test DeployCraft.slnx` and
   `dotnet format DeployCraft.slnx --verify-no-changes` pass.
4. Commit message follows the rules above.

## Cloud session (Linux)

`.claude/hooks/session-start.sh` installs the .NET 10 SDK at startup and enables `EnableWindowsTargeting`:
build, tests and `dotnet format` run as above. Tests that use Windows-only features (`CredentialsTests`:
encryption, folder permissions; `File_ExpandsEnvironmentVariables`) fail there with
`PlatformNotSupportedException` or similar: this is expected, the Windows CI is the reference. Nothing is started
there (no service, no console, no deployment).

## End of session

Update `docs/STATUS.md` (on the current topic's branch, never directly on `main`).
