# Features — work list

> **Goal**: cover the features expected from a deployment tool
> (behavior + interface layout), and only at the end remove what is not useful and
> add our own features. DeployCraft's labels and code are entirely our own.

Legend: ✅ done · 🟡 partial · ⬜ to do · ❌ dropped (maintainer's decision) · ⛔ out of scope for now (to be replaced or dropped, maintainer's decision)

To take a task: see [`CONTRIBUTING.md`](../CONTRIBUTING.md) (§ "Choose a task"). A row moves to ✅
in the same PR as the code that implements it.

Maintenance (#155): console and service reference `DeployCraft.Shared` for their common models and messages.
Package preparation is a separate shared service; no feature status or behavior changes.

---

## 1. Main window

| # | Feature | Status | Notes |
|---|---|---|---|
| 1.1 | Navigation tree on the left: Packages, Target lists, Schedules, All deployments, Retry queue | ✅ | Packages, deployments, schedules (0.8.0). Target lists and retry queue dropped (maintainer's choice) |
| 1.2 | Folders in the tree (packages and target lists), subfolders, drag and drop | ✅ | Package folders and subfolders (Ctrl+Shift+F), rename, delete an empty folder, drag and drop of packages and folders. Target lists dropped (1.1) |
| 1.3 | Package page: details, steps, the package's deployments, attached schedules | ✅ | Schedules tab in 0.8.0; Details tab (version, folder, settings with their origin, conditions, offline, packages that use it) and Conditions column in the steps |
| 1.4 | Folder page, "several packages selected" page | ❌ | Dropped: maintainer's choice |
| 1.5 | Rename (F2), duplicate, cut / copy / paste, delete in the tree | ✅ | Packages: cut, copy, paste (Ctrl+X / C / V) into a folder; folders: cut / paste |
| 1.6 | Tree node appearance (color / formatting) | ❌ | Dropped: maintainer's choice (the current tree suits them) |
| 1.7 | File / Edit / View / Options / Help menus + right-click everywhere | ✅ | View: home, deployments, schedules, library, packages (Ctrl+0 to 4), find a package (Ctrl+F), refresh |
| 1.8 | Keyboard shortcuts window | ❌ | Dropped: maintainer's choice (to be reconsidered based on usage feedback) |
| 1.9 | Light / dark themes | ✅ | 0.7.0: Light, Dark or Follow Windows; Preferences › Interface, applied live |
| 1.10 | Home page | ✅ | Opens at startup: right now, last 24 hours (clickable failures), upcoming schedules, library, shortcuts |
| 1.11 | Import / export (packages, folders, target lists, schedules, settings) | ✅ | Packages: export / import of our own JSON, import of package XML files with preview. The rest dropped (maintainer's choice) |
| 1.12 | Print, print preview, page setup, save a grid as CSV | ❌ | Dropped: maintainer's choice (the result in the console is enough) |
| 1.13 | About | ✅ | Help › About: console and service versions, signed-in account, license, links, copy of the information |

## 2. Packages

| # | Feature | Status | Notes |
|---|---|---|---|
| 2.1 | Name, description (rich text), version, folder | ✅ | Name, version, description. Rich text dropped (maintainer's choice); folders: see 1.2 |
| 2.2 | Tabbed package window: Details, Conditions, Options, Offline | ✅ | 0.10.0; the step also has Details, Conditions, Options |
| 2.3 | Package options: custom timeout, copy mode, run as, retry | ✅ | 0.10.0: each option can follow the Preferences; retry in the Offline tab |
| 2.4 | Package conditions: OS version + architecture, PowerShell version, logged-on user, file, registry | ✅ | 0.10.0: checked by the runner; unmet = computer succeeded without running anything |
| 2.5 | Step sections: before / main / after | ❌ | Dropped: maintainer's choice, a failure stops the package. Done in 0.11.0, removed afterwards |
| 2.6 | Per step: title, enabled / disabled, success codes | ✅ | Disabled step: "Skipped", the package continues |
| 2.7 | Per step: error → stop with failure / stop with success / continue | ✅ | Stop with success: computer counted as succeeded, following steps "Skipped" |
| 2.8 | Per step: run as (deployment account / deployment account on the desktop / local system / logged-on user / inherit from package) | ✅ | Package setting / deployment account / Local System / logged-on user, in "slices" on the computer. "Deployment account on the desktop": dropped by the maintainer (deployments are always silent, no password on the computer) |
| 2.9 | Per step: same conditions as the package | ✅ | 0.10.0: unmet = step "Skipped", the package continues |
| 2.10 | Copy / cut / paste / reorder steps, including between packages | ✅ | 0.10.0: clipboard (Ctrl+X / C / V), toolbar and right-click |
| 2.11 | Warnings: file not found, no silent switch, conflict with "pull" mode | ✅ | At the top of the step and repeated on save; files checked by the service with the default credential |
| 2.12 | Duplicate, export a package | ✅ | Export with its nested packages |
| 2.13 | Public / private package folders | ❌ | Dropped: maintainer's choice (a single console) |

### Step types

| # | Step | Status | Details to cover |
|---|---|---|---|
| 2.20 | Install | ✅ | exe, bat/cmd, msi, msp, msu, ps1, vbs, reg; MSI install / repair / uninstall, restart, silent; generated or custom command line; MSI log; additional files; keep the file |
| 2.21 | Command | ✅ | Additional files |
| 2.22 | PowerShell | ✅ | Additional files. Dropped: custom command line (maintainer's choice); useless `Write-Host` alert (its output is included in the result anyway) |
| 2.23 | File copy | ✅ | File or folder (push or pull), patterns, subfolders, overwrite or keep, succeed despite files that cannot be replaced; copy on the computer with robocopy |
| 2.24 | Restart | ✅ | Message, delay (30 s to 1 h), shut down instead of restarting; waits for the computer to come back (the step's max timeout) before the next step |
| 2.25 | Pause | ✅ | N seconds (1 s to 24 h), interrupted by cancellation |
| 2.26 | Message | ✅ | Title, text, display duration (0 = until clicked), wait for the OK click before the next step; shown in every open session |
| 2.27 | User logoff | ❌ | Dropped: maintainer's choice (silent deployment tool) |
| 2.28 | Nested package | ✅ | Replaced by its steps at launch; combined conditions, loops and more than 5 levels refused |
| 2.29 | Inventory scan | ⛔ | Requires a computer inventory |

## 3. Deploy once

| # | Feature | Status | Notes |
|---|---|---|---|
| 3.1 | Several packages, ordered | ✅ | 0.6.0: one deployment per package; on a computer, the next one waits for the previous one to finish (even on failure) |
| 3.2 | Targets: typing, pasting, Active Directory | ✅ | Expandable AD tree (0.5.1) |
| 3.3 | Targets: target list, text file | ✅ | Text file in 0.6.0. Target lists dropped (maintainer's choice): computers are chosen from the AD |
| 3.4 | DNS resolution of typed names (live check) | ✅ | Deploy once: each added computer is looked up in the server's DNS (hourglass, check or cross; full name in the tooltip); does not prevent deploying |
| 3.5 | Start at step N, **per computer** | ✅ | 0.6.0; applies to the first package. "Resume at the failed step" = a single deployment |
| 3.6 | Options: account, copy mode, run as | ✅ | 0.6.0; credential of choice in 0.9.0; "Package setting" by default. Deployment account / Local System / Logged-on user; push / pull (UNC) |
| 3.7 | Options: notifications, remember targets, high priority | ✅ | Remember + high priority in 0.6.0. Notifications dropped (maintainer's choice) |
| 3.8 | Offline tab: ping first, Wake-on-LAN, retry queue | ✅ | 0.10.0: set in the package (and the Preferences). In Deploy once: dropped (maintainer's choice, computers are powered on beforehand; wake-up exists) |
| 3.9 | "Schedule" button (creates a schedule from the window) | ✅ | 0.8.0: reuses packages, targets, options |
| 3.10 | Targets from a computer inventory | ⛔ | |

## 4. Deployment tracking

| # | Feature | Status | Notes |
|---|---|---|---|
| 4.1 | Master / detail list: deployments, computers, steps, output | ✅ | |
| 4.2 | Detailed computer statuses: pending, ping, wake-up (WoL), connecting, copying, running, reconnecting, canceling, finalizing… | ✅ | Current step shown (waiting for a slot, wake-up, name resolution, copy, execution, computer unreachable, restart, cleanup); "Canceled" status |
| 4.3 | Counters per deployment (pending / running / succeeded / failed) + "partially failed", "canceled" | ✅ | Pending, Running, Succeeded, Failed, Canceled columns; "Partial" and "Canceled" statuses |
| 4.4 | Copy progress (% and throughput), note per step, execution account per step | ✅ | During the copy to a computer: "Copying files: 45% (12.3 MB/s)" (push mode). Computer detail: Runs columns (per deployment, retries included) and Note |
| 4.5 | Cancel, redeploy, redeploy failures, resume at the failed step | ✅ | |
| 4.6 | Prioritize a pending computer | ❌ | Dropped: maintainer's choice |
| 4.7 | Create a schedule from a deployment | ✅ | Right-click a deployment: package, computers and options reused, triggers to choose |
| 4.8 | Automatic history cleanup after N days | ✅ | Preferences › Deployment: disabled by default, from 1 to 3650 days; at service startup then once a day; only finished deployments are deleted (never pending or running ones, nor the schedules' history) |
| 4.9 | Full output file downloadable per step | ✅ | Step details: copy, open in Notepad or save the output (with the Windows Installer log) |

## 5. Schedules

| # | Feature | Status | Notes |
|---|---|---|---|
| 5.1 | "All schedules" page, schedule window | ✅ | 0.8.0: grid + the schedule's deployments; tabbed window Triggers / Targets / Packages / Options / Target history |
| 5.2 | Triggers: once, daily, weekly, monthly (day N or "the 1st Monday"), interval | ✅ | 0.8.0: several per schedule, time window for the interval, optional end, "last day / last Monday" |
| 5.3 | "Heartbeat" trigger (computer coming back online) | ⛔ | Requires a computer inventory; a home-grown equivalent remains possible |
| 5.4 | Ordered packages, targets, options (account, copy, run as, timeout, notifications, offline, retry) | ✅ | 0.8.0: packages, targets, copy, run as, timeout; credential in 0.9.0. Notifications and offline dropped (maintainer's choice) |
| 5.5 | Stop after N failures, stop after one success (per computer) | ✅ | 0.8.0: target history per computer × package, can be cleared |
| 5.6 | Enable / disable, next and last run, last error | ✅ | 0.8.0: "Last result" |
| 5.7 | Attach / detach to a package, duplicate, deploy now | ✅ | Duplicate, start now; right-click a package › Add to a schedule…, and Remove from the package's Schedules tab |

## 6. Target lists and filters

| # | Feature | Status | Notes |
|---|---|---|---|
| 6.1 | Reusable target lists (Ctrl+Shift+T), in folders | ❌ | Dropped: maintainer's choice (computers are chosen from the AD) |
| 6.2 | Sources: computers, Active Directory (OU, group, container), another target list | ❌ | Dropped: maintainer's choice |
| 6.3 | Global include / exclude filters (name, IPv4, IPv6) | ❌ | Dropped: maintainer's choice (to be reconsidered much later, based on feedback) |

## 7. Retry queue (offline)

| # | Feature | Status | Notes |
|---|---|---|---|
| 7.1 | Ping before deployment, Wake-on-LAN | ✅ | 0.10.0: DNS + share test always done; wake-up with the MAC address learned at the last deployment |
| 7.2 | Retry queue: enabled, max attempts, interval, expiry; inheritance preferences → package → deployment | ✅ | Removed in 0.5.0, brought back in 0.10.0 as an **option disabled by default**: number of attempts and interval, preferences → package; lost when the service restarts. Expiry dropped (maintainer's choice) |
| 7.3 | Queue page: computer × package, remaining attempts, next attempt, "try now", delete | ❌ | Dropped: maintainer's choice (offline not used; "Redeploy to failed computers" exists) |

## 8. Preferences

| # | Section | Status | Content |
|---|---|---|---|
| 8.1 | Accounts | 🟡 | 0.9.0: Options › Credentials, several accounts (domain or local `.\user`), description, default account, test against the domain; chosen in Deploy once and the schedules. Remaining: LAPS (each computer's local admin password read from the AD, a Microsoft feature), after v1 (#106) |
| 8.2 | Variables | ❌ | Dropped: maintainer's choice (added in 0.12.0, removed afterwards; a `$(…)` is left as is) |
| 8.3 | Deployment | ✅ | 0.10.0: run as, timeout per step, default offline behavior; history cleanup (4.8) |
| 8.4 | Performance | ✅ | 0.10.0: default copy mode. Adjustable parallel computers (1 to 100, 8 by default) and throughput limit for copies to computers (Mbit/s, shared by all running copies) in Preferences › Deployment |
| 8.5 | File repository (`$(Repository)`) | ❌ | Dropped: maintainer's choice (files are on a server share) |
| 8.6 | Target filters | ❌ | Dropped: maintainer's choice (see 6.3) |
| 8.7 | Mail server + notifications | ❌ | Dropped: maintainer's choice |
| 8.8 | Interface, printing | ✅ | 0.7.0: Interface page (theme); Preferences window as a list of pages. Printing dropped (maintainer's choice) |
| 8.9 | Background service | ✅ | Options › Background service: status, restart, open Windows services, service account (Local System or a server administrator account, changed from the console on the server) |
| 8.10 | Console users, active sessions | ✅ | Windows authentication; server administrators + list of accounts and groups; Active sessions tab (account, computer, last activity) |
| 8.11 | Central server mode (local / server / client) | ❌ | Dropped: maintainer's choice (console and service on the same machine) |
| 8.12 | Service on the target | ❌ | Dropped: maintainer's choice |
| 8.13 | Proxy, database, logging | ❌ | Dropped: maintainer's choice |
| 8.14 | Automatic updates | ✅ | At startup (and via Help), the console looks for a new stable version in our Releases and offers to install it (Yes / No): installer downloaded, checksum verified, installed as administrator, console reopened. From the server's console only |

## 9. Reports and notifications

| # | Feature | Status | Notes |
|---|---|---|---|
| 9.1 | Reports: deployments, packages, schedules, target lists | ❌ | Dropped: maintainer's choice |
| 9.2 | Email notification after deployment or schedule: recipients (to / cc / bcc), subject, body, attachment, details, only if changed, not for retries | ❌ | Dropped: maintainer's choice |

## 10. Miscellaneous

| # | Feature | Status | Notes |
|---|---|---|---|
| 10.1 | Command line (deploy a package to targets, list…) | ❌ | Dropped: maintainer's choice |
| 10.2 | First-run wizard | ✅ | Opens as long as the server has no credential or package, and via Help › Getting started wizard: credential, library folder, first package, preferences |
| 10.3 | Built-in help | ✅ | Help › DeployCraft Help (F1, opens on the topic of the current view): 9 topics, search |
| 10.4 | Online package library, automatic package updates | ✅ | Based on the public winget catalog: the whole catalog with search, verified download, package created, one-click update. Catalog re-read daily by the service; new version flagged (library, home, marker in the tree); manual update, automatic right away or after N days |
| 10.5 | Computer inventory integration | ⛔ | |
| 10.6 | License, trial, usage statistics | ⛔ | Not applicable |
