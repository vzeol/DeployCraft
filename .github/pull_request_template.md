## What

<!-- One or two sentences: what this PR does. -->

## Why

<!-- The original request or problem. -->

## docs/FEATURES.md rows

<!-- E.g. 5.1, 5.2 — and they are updated in this PR. -->

## How to test

<!-- Automated tests added; and manually: isolated instance (port 5091)? On which computer? Screenshots if the UI changes. -->

## Checks

- [ ] `dotnet build DeployCraft.slnx` passes with no error or warning
- [ ] `dotnet test DeployCraft.slnx` passes; tests added or updated for the changed behavior
- [ ] `dotnet format DeployCraft.slnx --verify-no-changes` passes
- [ ] `docs/FEATURES.md` (and `docs/SPEC.md` if needed) updated
- [ ] Independent work: nothing copied from another software (code, icons, texts)
- [ ] No secret or internal information (servers, computers, domains, accounts) in `git diff --staged`, the code or the screenshots; `.gitignore` intact
- [ ] Commits formatted as `feat:` / `fix:` / `docs:` …, one logical change per commit
