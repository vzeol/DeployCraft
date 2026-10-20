# DeployCraft — Functional specification

> What DeployCraft does and how it does it. The code is written from this document.
> DeployCraft is an independent implementation: no code, icon, image or text from any other software.

## 1. Overall architecture

| Component | Role |
|---|---|
| **Console** (WPF) | Create packages, choose targets, launch and follow deployments |
| **Service** (Windows) | Runs in the background, exposes a local HTTP API, runs deployments and schedules |
| **Runner** | Small program temporarily copied to each target computer: it runs the steps locally, then is deleted |
| **Database** | Local SQLite: packages, deployments, schedules, credentials, history |

## 2. Concepts

### Package
- Name, version, description
- Ordered list of **steps**
- **Conditions** (see below)
- Options: file copy mode (push: the server copies; pull: the computer reads a share), run as (deployment
  account / Local System / logged-on user), maximum step timeout; each can stay "Default" (value from the
  Preferences)
- Offline: Wake-on-LAN, retries; or the Preferences settings

### Step types
| Step | Description |
|---|---|
| Install | Run an MSI / EXE / MSU with silent parameters, configurable success codes |
| Command | cmd command line |
| PowerShell | PowerShell script |
| File copy | Copy a file or folder to a folder on the computer (see § "File copy" step) |
| Restart | Restart (or shut down) the computer and wait for it to come back (see § "Restart" step) |
| Pause | Wait N seconds (1 s to 24 h) on the computer before the next step; nothing is run, no success code; the wait stops if the deployment is cancelled |
| Message | Show a message to logged-on users (see § "Message" step) |
| Log off *(coming)* | Log the user off |
| Nested package | Run the steps of another package (see § Nested package) |

### Conditions (0.10.0, per package and per step)
- Windows version (10, 11, Server, earlier; none ticked = all), architecture (32 / 64-bit), minimum PowerShell
  version (3, 4, 5, 7)
- User logged on or nobody logged on
- File or folder (exists / does not exist, environment variables accepted), registry key, with or without a value
  name (exists / does not exist)
- Checked by the runner on the computer, just before execution; all must be met.
- Step: condition not met → step "Skipped" with the reason, the package continues.
- Package: conditions not met → no step is run, all "Skipped", computer counted as **succeeded** (nothing to do),
  reason shown in the Detail column. A schedule set to "stop deploying once succeeded" therefore does not launch
  it again.
- Condition that cannot be checked (misspelled key…) → step fails with the message.

### Run as (per step)
- Step Options tab: **Package setting** (default), deployment account, Local System or logged-on user. "Package
  setting" follows the package, or the choice made in Deploy once / the schedule; a step with its own setting keeps
  it. With no saved credential, "deployment account" becomes Local System. In a nested package, sub-steps with no
  setting take that of the "nested package" step.
- On the computer, a runner service has only one identity: consecutive steps with the same service (deployment
  account, or System, which also serves for the logged-on user) form a **slice**. The server runs the slices one
  after the other in the same folder (copied files stay), each with its own service (`DeployCraftRunner-N-1`, `-2`…),
  the next one only after the previous one has been removed. The account password is only used to create the
  service: it is never written on the computer.
- A slice that stops the package (failure with "Stop on failure", stop on success, package conditions not met):
  the following slices are not launched, their steps are "Skipped" with the same reason. Runner interrupted by an
  error: no next slice either, reason "Previous slice interrupted: …". Cancellation (during a slice or between two):
  the current slice is stopped, no other is launched. While running, the computer's detail shows the slice identity
  ("Running (System)"). The `runner.log` journal accumulates the slices; step numbers there are those of the package.
- Pull mode: the share is read with the identity of the slice (System: the computer account `POSTE$`).
- Changing identity costs a few seconds (creating and stopping a service); a package with no per-step setting keeps
  a single service, as before.

### Error handling (per step)
- **Stop on failure** (default): the following steps are not run, the computer is failed. A package therefore
  stops dead at the first failed step (maintainer's choice: safer, and cleaner packages).
- **Continue**: the step is marked failed (and so is the computer), the following steps run.
- **Stop on success**: the following steps are not run and the computer is counted as **succeeded**. Useful when
  failure means "nothing to do here" (e.g. a test that exits with an error if the software is already up to date).
  The step is shown as "Stopped on success" with its return code; the following ones "Skipped", with the number of
  the step that stopped the package; the computer's Detail column gives the reason.

### Nested package
- A "Nested package" step designates another package (chosen from a list). **At launch**, it is replaced by the
  steps of that package, frozen in the history: modifying the nested package afterwards does not change a
  running deployment. Tracking, the counter and the start step count these steps one by one.
- Replaced steps are named "Step › Sub-step" (step title, otherwise the nested package name).
- The steps of the nested package take the place of the step, in their order.
- Conditions: each replaced step keeps its own and also receives those of the "nested package" step and those of
  the nested package. Not met: "Skipped", reason "Conditions of nested package "X" not met. …".
- Timeout and success codes: those of each sub-step (empty timeout: that of the nested package, otherwise that of
  the Preferences). Run as and copy mode: those of the deployment. Error behavior: that of each sub-step; those of
  the "nested package" step have no effect (greyed out).
- "Nested package" step disabled: all its steps are disabled. A disabled sub-step stays disabled.
- Refused on save: no package chosen, package not found, loop ("A contains B which contains A"), more than 5
  levels. A package used as a nested package cannot be deleted (the message names the packages that use it).
- "Redeploy" re-reads the current packages (as for any package): a nested package modified since then is picked up
  in its new version.

### Step enabled / disabled
- A step with "Step enabled" unticked stays in the package but is never run: "Skipped", reason "Step disabled.",
  and the package continues. No file is copied or checked for it.
- Shown as "(disabled)" and greyed out in the package window and the package page. Right-click › Enable / Disable.

### Targets
- Manual entry of computer names, pasting, text file
- Active Directory (OU tree)
- *(coming)* Reusable target lists, include / exclude filters

### Deployment
- A deployment = 1 package × N computers
- Overall status + **per-computer** status + **per-step** status
- Readable errors: name not found in DNS, access denied, computer unreachable, timeout, exit code outside the
  success codes, etc.

### "Install" step
- File (local or UNC path), parameters, whole folder if the installer has several files
- Types: .exe, .msi, .msp, .msu, .ps1, .bat / .cmd, .vbs, .reg. **Command line** shown, generated from the file,
  the parameters and the options (msi: `msiexec /i|/f|/x "file" ALLUSERS=1 /qn /norestart /log output.log …`;
  msp: `/p`; msu: `wusa`; ps1: PowerShell `-File`; vbs: `cscript //B`; reg: `reg import`). **Custom** box: the
  pre-filled line becomes editable and is run as is (through cmd, in the computer's working folder).
- Windows Installer options (.msi, .msp, .msu): operation install / repair / uninstall (no repair for an .msu, no
  operation for an .msp), restart never / always / ask / not set, silent.
- The Windows Installer log (`output.log`) is appended to the step result (its end if too long); common error
  codes (1603, 1618, 1638…) are explained.
- **Additional files** (also for Command and PowerShell): one path per line, copied next to the command before the
  step (by the server, or by the computer in pull mode for a UNC path). File not found: error.
- **Keep the file on the computer**: after the step, the file (and its additional files, or the whole folder) is
  copied to `%ProgramData%\DeployCraft\Installeurs\<package>`.
- **Default success codes: `0, 1641, 3010, 2359302`** (OK, OK + restart started, OK + restart required, MSU update
  already installed)

### "File copy" step
- **Source**: file or folder as seen from the server (local or UNC path). **Destination**: folder on the computer,
  created if missing; environment variables (`%ProgramData%`, `%PUBLIC%`…) take the values of the step's identity
  (those of the logged-on user if the step runs on their behalf).
- For a folder source: **patterns** separated by ";" (`*` by default), **subfolders** (ticked by default,
  recreated even if empty). A file source copies that single file.
- **Replace existing files** (ticked by default); unticked, a file already on the computer is kept.
- **Succeed even if some files cannot be replaced** (file in use…): unticked, a single file not copied makes the
  step fail. A source not found is always an error.
- Push: the server drops the selected files into `exec\copie-N` (with the deployment account if the source is on a
  share). Pull (source `\\serveur\…`): the computer reads the share itself. In both cases, the copy to the
  destination is done on the computer by `robocopy` (shipped with Windows), under the step's identity; its output
  appears in the step detail. A locked file is retried once.
- No success codes to enter: those of robocopy are used (0 to 7 = success; 8 to 15 = some files not copied,
  accepted only with "succeed even if…"; 16 = serious error).
- Refused on save: empty source or destination.

### "Restart" step
- **Delay** from 30 seconds to 1 hour (60 s by default) and optional **message** (500 characters at most): the
  logged-on user sees the countdown and the message. The delay also gives the runner time to return its result.
- The restart is scheduled on the computer by `shutdown.exe`, always by the service (never on behalf of the
  logged-on user). The step succeeds as soon as it is scheduled.
- A "Restart" step ends its slice (see "Run as"). The server waits for the computer to shut down (at most delay +
  5 min; otherwise error "restart cancelled on the computer?"), then for it to respond again, at most the **step's
  maximum timeout** (otherwise error). It then launches a service again for the following steps, retrying for 2
  minutes while Windows finishes starting. The slot stays on the computer's disk during the restart. Tracking:
  "Restarting computer", then "Waiting for computer to come back".
- **Shut down instead of restarting**: the following steps are "Skipped", reason "Computer shut down by step N
  "X"", and the computer counts as succeeded.
- Step skipped (conditions, disabled) or failed: no restart, no wait.
- Refused at launch on the DeployCraft server itself. Refused on save: delay outside 30 s to 1 h, message longer
  than 500 characters.

### "Message" step
- **Title** (100 characters, "Information" by default) and **text** (2,000 characters, required), shown in a
  window with an OK button, in **every open session** on the computer. The service (always as System for this
  step) launches a small window in each session through Windows PowerShell: each message has its own window, never
  queued behind another, and returns "OK clicked" or "closed at the end of display".
- **Display duration** in seconds (0 to 24 h): the window closes by itself at the end; 0 = it stays until clicked.
- **Wait for the user to click OK** (unticked by default):
  - unticked: the step succeeds as soon as the message is shown, the next step follows;
  - ticked with a duration: the step waits for the click or the end of display, and succeeds in both cases;
  - ticked without a duration: the step waits for the click, at most the step's maximum timeout; nobody clicked →
    failure.
  Several sessions: the step waits for the answer from each.
- Nobody logged on: the step succeeds without showing anything ("No user logged on" in the detail).
- The step detail says for whom the message was shown and the result (click, automatic close).
- Cancellation while waiting: the step fails "Cancelled." (the window may stay displayed).
- Refused on save: empty or too long text, title too long, duration outside 0 to 24 h.

## 3. How a deployment runs on a computer

1. The server checks that the computer responds (DNS, port 445).
2. It connects to the computer's `ADMIN$` share with the chosen credential and reserves a **slot**:
   ```
   C:\Windows\DeployCraft\
     slot-N.lock               ← slot lock (several deployments can run in parallel on a computer)
     slot-N\
       DeployCraftRunner-N.exe ← runner (.NET Framework 4.8, built into Windows 10 / 11)
       job.json                ← instructions (steps, success codes, timeouts)
       status.json             ← progress and results, written by the runner
       runner.log              ← runner log
       exec\                   ← package files = current directory of the steps
     last-runner.log           ← log of the last run (kept after cleanup)
   ```
3. It creates a **temporary Windows service** `DeployCraftRunner-N` and starts it (under the chosen credential or
   as Local System).
4. The runner runs the steps in order and writes `status.json` after each change; the server re-reads it every
   second for the live display (shared read, without blocking the runner). A failed save (file being read) is
   retried, then caught up at the next one: it never interrupts the steps.
5. End: the server compares each exit code to the success codes, then stops and deletes the service and the slot.
   Only `last-runner.log` remains.

Why it works this way: no permanent agent or WinRM, nothing to install on the computers, several deployments
possible at the same time on one computer.

### Deploy once (0.6.0)
- Several packages in the chosen order: one deployment per package. On a given computer, package N+1 waits for
  package N to finish, even if it failed or was cancelled.
- Start step **per computer** (on the first package). "Resume at failed step" opens a single deployment where
  each computer resumes at its own step.
- Run as (package option, changeable at launch):
  - **Deployment account**: the temporary service runs under the chosen credential. The server first grants the
    "Log on as a service" right on the computer (remote LSA). Logon failure (error 1069) → clear message. With no
    saved credential → Local System.
  - **Local System**.
  - **Logged-on user**: service as System, each step is launched in the active session (user's token,
    `winsta0\default` desktop, output collected through a pipe). Nobody logged on → step fails.
- Copy mode: **push** (the server copies) or **pull** (the runner copies itself from a UNC path, with its
  identity: chosen credential, or the computer account `POSTE$` as System). Local path → push.
- Priority: the deployment's computers go ahead of those waiting for a slot (8 in parallel).
- Remember targets: setting `package.{id}.targets`, offered the next time the window is opened.

### Light / dark theme (0.7.0)
- Preferences › Interface › Theme = **Light** (default), **Dark** or **Follow Windows** (follows the Windows app
  mode, even if it changes while the console is open). Applied live to all windows, title bar included.
- Setting specific to the console's Windows user (`%APPDATA%\DeployCraft\console.json`), not to the server.
- Technical: WPF Fluent theme (.NET 10) for standard controls, accent replaced by our orange; our colors in
  `Themes\Clair.xaml` / `Themes\Sombre.xaml` (same keys, always as `DynamicResource`).

### Schedules (0.8.0)
- **All schedules** page: grid (next / last run, last result) and, below it, the deployments of the selected
  schedule. Start now, edit, enable / disable, duplicate, delete.
- Tabbed window: **Triggers**, **Targets**, **Packages** (ordered), **Options**, **Target history**.
- Triggers: once, interval (with optional time range), daily, weekly, monthly (day N, last day, "the Nth / last
  [Monday…]"); several per schedule; start and optional end.
- Options: credential, run as, copy mode, **stop deploying to a computer once it has succeeded**, stop deploying
  after N failures, abandon computers still waiting after N minutes.
- Typical use: an interval trigger + "stop deploying once succeeded" retries an installation in a loop for a few
  days until it goes through everywhere.
- Service: scheduler that examines the triggers **every 15 s** (server local time). Several past triggers at once
  → a single launch. Missed trigger (service stopped): caught up only if it is less than 5 min old. Saving a
  schedule does not catch up past triggers.
- Interval: grid start + k × interval; with a time range, it jumps to the start of the next range (a range whose
  end precedes its start crosses midnight). Monthly: day 31 → last day of shorter months.
- At each launch, for each package, only the computers that need it are kept: not already succeeded (option,
  ticked by default), not at the maximum number of failures, and **not yet served by a previous launch of the
  same schedule** (waiting or running: it will be picked up at the next one). Packages remain chained per
  computer. Nothing to do → no deployment, "Last result" explains why.
- History per schedule × computer × package: succeeded (final) and number of failures. Cancellations and
  computers abandoned for lack of a slot do not count as failures. Deleting a row → the computer will be
  redeployed.
- Deployments keep their original schedule; "Launched by" = "Schedule "name"" (or the user who clicked
  "Start now").
- Coming: "computer back online" trigger, Offline tab, notifications.

### Credentials (0.9.0)
- Options › **Credentials…**: list "account — description (default)"; Add, Edit, Set as default, Delete. The first
  credential becomes the default; deleting the default designates another. Deletion refused if a schedule uses it.
- Add / edit: Domain (pre-filled with the computer's domain), User, Password (empty = unchanged), Description;
  **Test** checks a domain account against the directory. A local account cannot be tested: each computer checks
  it at deployment.
- **Local account** (empty domain or "." → `.\user`): presented to each computer as `POSTE\user` (ADMIN$ access and
  service manager), and the temporary service runs under `.\user`. Note: a local account other than the built-in
  Administrator is filtered by remote UAC, unless the LocalAccountTokenFilterPolicy setting is applied.
- Deploy once and schedule: **Credential** list (default preselected) + **Manage…**. The deployment keeps the
  credential used ("Account" sheet, with "access: …" when the package runs as Local System or logged-on user).
  Schedule with no credential (created before 0.9.0) = the default one.
- Active Directory: read with the default credential if it belongs to the domain, otherwise the first one of the
  domain.
- Security: passwords encrypted with DPAPI (machine scope), never sent back to the console; at startup, the
  service reserves its data folder to System and Administrators. Unreadable password (database from another
  server) → flagged in the list and the banner, to be re-entered.
- Update from 0.8: the old single account becomes the first credential, as default.
- Coming: LAPS (local administrator password of each computer read from AD), "show password" button.

### Console users (access to the service API)
- The console identifies itself to the service with the person's Windows session (Windows authentication, no
  password to enter). A request without an identity is refused.
- Access: the **local administrators of the server**, always (even if the console is opened without elevation),
  and the accounts or groups in the Options › **Console users…** list (Add: `DOMAINE\name`, verified and kept by
  its SID; Delete). Only the server's administrators edit the list.
- Refused account: message "Your account … is not allowed to use DeployCraft on SERVEUR-01" in the banner, status
  bar "Access denied".
- **Active sessions** in the same window: accounts that queried the service less than 2 minutes ago.
- "Launched by" and "Cancelled by" come from the account authenticated by the service, no longer from the console.
- Status bar: "you: DOMAINE\account".

### Package window (0.10.0)
- On the left: **Properties** tree then **Steps** (one line per step); on the right: the page of the selected
  node. Toolbar: save, new step ▾, move up, move down (Ctrl+Up / Ctrl+Down), cut, copy, paste, delete. A step
  **dragged and dropped** in the tree is placed before or after the target step, depending on the half hovered
  (insertion line; Esc cancels). Cut / copy / paste (Ctrl+X / C / V) go through the clipboard: you can also paste
  into another package.
- Properties in 4 tabs:
  - **Details**: name, version (shown next to the name in the package page), description.
  - **Conditions**: see § Conditions.
  - **Options**: run as, copy mode, maximum timeout; "Default (value)" = Preferences.
  - **Offline**: "Use the Preferences settings", otherwise the package's own settings.
- Step: title, **Step enabled** box, then 3 tabs: **Details**, **Conditions**, **Options** (maximum timeout, empty
  = that of the package; on error). The steps overview has a Conditions column.
- Settings inheritance: step → package → Preferences. At launch, the values actually used are frozen in the
  deployment history.
- The text of steps and conditions is sent as is: no `$(…)` variables (a PowerShell subexpression such as
  `$(Get-Date)` stays intact); `%VAR%` are those of Windows on the computer.

### Package export and import
- **File › Export package…** (also on right-click) or **Export all packages…**: a `.json` file containing the
  chosen packages **and the nested packages they use**, so that it is self-sufficient. Credentials (passwords) are
  never exported.
- **File › Import packages…**: a DeployCraft export (`.json`) or a **package XML file**. A window first shows a
  **preview** (nothing is created): for each package and each step, taken as is (green), taken with caveats
  (orange, with the explanation) or not taken (red, with the reason). "Import" creates the packages; nothing is
  lost without being flagged.
- Always **new** packages: a name already taken becomes "Name (2)", "Name (3)"… "Nested package" steps are linked
  to the packages imported at the same time. A nested package is created before the one that uses it; a loop of
  nested packages in the file is refused. Each package is checked as on save.
- Package XML file:
  - Steps taken: Install, Command, PowerShell, File copy, Pause, Restart, Nested package. Not yet taken
    (flagged): Message, Log off; never: inventory scan.
  - Install: Windows Installer options (install / repair / uninstall, restart, silent), custom command line and
    "keep the file on the computer" are taken. An operation impossible for the file type (repairing an .msu,
    operation on an .msp) is replaced by installation (flagged).
  - Nested package: linked to the package of the same file, otherwise to a package of the same name already in
    DeployCraft (flagged), otherwise the step is not taken.
  - "Before" and "after" steps: put one after the other, in execution order (flagged).
  - Conditions: Windows versions, architecture, PowerShell version, logged-on user, presence of a file or of a
    registry key / value. A condition with no exact equivalent (file version, compared registry value, Windows
    Server version by version, collection of an inventory software…) is flagged and the step is **imported
    disabled** (all steps if it is a package condition): it never runs on more computers than intended. You check
    it, then re-enable it.
  - Additional files of a step (Install, Command, PowerShell): taken, `$(Repository)` included.
  - `$(Repository)` is replaced by the repository folder entered in the import window; other variables stay as
    they are (flagged). Package folders: DeployCraft has none, packages go at the root. Schedules and computer
    lists in the file: flagged, not taken.

### Package library
- **Package library** entry in the tree: the whole public winget catalog (about 15,000 programs), with the
  already-downloaded packages first, then a selection of common programs. Columns: program, identifier, available
  version, version in DeployCraft, installer, status.
- **Search** across the whole catalog: name, identifier, publisher or keyword; every word must match.
- The service downloads the catalog index (a single small file) and re-reads it by itself once a day, or on
  **Refresh catalog**. The console loads the list once, then only follows the rows that change.
- **Download** (or double-click, several rows possible): DeployCraft reads the program's manifest and picks an
  installer **for the whole computer and without a window** (64-bit preferred, in French if the publisher offers
  one, .msi preferred). With no installer of this kind, the download is refused with the reason. Otherwise the
  status follows each stage as a percentage: download, file verification (catalog hash: a different file is
  rejected), copy into the library folder, package creation.
- The created package carries the program's name and version, with an "Install" step and the silent parameters of
  the installer type (.msi: `ALLUSERS=1`; others: parameters from the manifest, or the type's factory defaults).
- **Update available** when the catalog has a newer version than the package: **Update** replaces the file, the
  parameters and the version of the package, keeps the rest (conditions, added steps) and deletes the old version
  from the folder. A deleted package is no longer linked to the library.
- **Folder…**: where to store the installers (`<folder>\<program>\<version>\`). By default a folder on the server
  (push mode only); a share is recommended. It is written with the default credential.

### Preferences › Deployment and Offline (0.10.0)
- **Server** settings (common to all consoles), saved when the window is closed: run as, copy mode, maximum timeout
  per step (60 min by default), offline settings.

### Performance (Preferences › Deployment)
- **Computers in parallel**: from 1 to 100 (8 by default), all deployments and schedules combined. The other
  computers wait for a slot, priority computers first. The setting is applied at service startup, at each save of
  the Preferences and at each launch: an increase serves waiting computers immediately; a decrease lets running
  computers finish and only applies as they complete.
- **Copy limit** in Mbit/s (0 = unlimited, the default; up to 10,000): **total** throughput of copies from the
  server to the computers (runner, installation files, files of "file copy" steps), shared among all running
  copies, whatever the number of computers. Token bucket with one second of reserve; with a limit, the copy is done
  in 256 KB chunks (the file date is preserved). In pull mode, each computer reads the share itself: the limit
  does not apply.
- Out-of-range values are refused on save (console and service).

### History cleanup
- Preferences › Deployment: "Delete finished deployments after N days". **Server** setting, **disabled by
  default**, N from 1 to 3650 (30 suggested), refused by the service outside these limits.
- The service cleans up at startup, then once a day; the setting is re-read on each pass.
- Only **finished** deployments (succeeded or failed) whose end is more than N days ago are deleted, with their
  computers. A waiting or running deployment is never deleted, nor is the schedule history (the one used for "stop
  deploying once succeeded").
- The number of deployments deleted is written to the service log. An out-of-range value found in the database
  (edited by hand) is brought back within the limits: at most one day less history kept than expected.
- The database file does not shrink (SQLite reuses the freed space) but stops growing.

### Offline computers (0.10.0)
- The computer is always tested before the copy (DNS name, file share).
- **Wake-on-LAN**: computer that does not respond → wake packet (general broadcast and broadcast of its subnet,
  UDP ports 9 and 7), then wait for it to respond (3 min by default). The MAC address and the subnet broadcast are
  sent by the runner at each deployment: a computer never deployed to cannot be woken.
- **Retries**: offline computer → "Waiting", step "Offline: retry at HH:mm (attempt n of N)", slot released during
  the wait; failure after the last attempt. Cancellable; a service restart abandons pending retries. Only
  "offline" errors are retried.

### Tracking a computer: copy, runs, note
- **Copy progress** (push mode): the server first lists all the files to copy to the computer (runner,
  installers, additional files, files of "file copy" steps), then copies them in one go. The computer's current
  step shows "Copying files: 45% (12.3 MB/s)": bytes copied out of the total and throughput over the last 3
  seconds (1 MB = 1,024 KB), updated once per second (nothing is written to the database for each chunk). In pull
  mode, the computer reads the share itself: only "Copying files" is shown.
- **Runs** (detail of a computer's steps): number of times the step ran on this computer **in this deployment**,
  retries included (offline computer retried later). A skipped step counts 0. A redeployment is a new deployment,
  with its own count.
- **Note**: short message about the step result (first line, 160 characters at most): reason for a skipped step,
  error or code outside the success codes, package stopped on success, success that requires a restart (3010,
  1641) or update already installed (2359302). Other success codes (for example 1 for a file copy) have no note.
  Computed at display time, not stored.
- **Cancellation during the copy**: the copy stops between two chunks (or during the wait imposed by the
  throughput limit), then the slot is cleaned up along with the half-copied file.

## 4. Information to display

| Object | Information |
|---|---|
| Deployment | no., status (including partially failed, cancelled), created / started / finished, duration, package + version, console user who launched it, run-as account, originating schedule, counters (waiting, running, succeeded, failed) |
| Computer of a deployment | name, status, start / end, duration, current step, "n of m steps", error + failed step, resume at failed step, prioritization, output file |
| Step of a computer | no., title, status, return code, duration, error, run-as account, copy progress (%, throughput), note, output |
| Retry queue | computer, packages, attempts made / max / remaining, next attempt, last attempt, expiry |
| Schedule | name, targets, ordered packages, triggers, enabled, next / last run, stop after N failures, stop after success, max timeout, notification |
| Deployment settings | run-as account, per-computer timeout, history cleanup after N days, offline behavior |
| Reports / notifications | e-mail after deployment (recipients, subject, body, attachment, only if changed) |
