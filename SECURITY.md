# Security

DeployCraft runs with high privileges on a server and on the computers of a fleet: a vulnerability can have serious
consequences. Please report it **privately**.

## Reporting a vulnerability

Use GitHub private reporting: **Security** tab › **Report a vulnerability**
([direct link](https://github.com/vzeol/DeployCraft/security/advisories/new)). Do not open a public issue.

If possible, include: the affected version, what an attacker can obtain, and how to reproduce it. Remove anything
specific to your environment (domain, server and account names).

You will get an answer as soon as possible. The fix is released in a new version, then the vulnerability is described
publicly, with your name if you wish.

## Supported versions

Only the latest stable release receives security fixes; older versions do not have a separate maintenance branch.
Pre-releases are for testing upcoming changes. Report vulnerabilities in any version privately using the channel
above. For usage questions and release guidance, see [SUPPORT.md](SUPPORT.md).

## How DeployCraft protects your fleet

- **Console access**: Windows authentication; limited to the server's administrators and to the accounts or groups
  allowed in Options › Utilisateurs de la console (*Console users*).
- **Deployment credentials**: typed in the console, encrypted on the server (Windows data protection, machine scope);
  never copied to the computers. The data folder is restricted to System and administrators.
- **On the computers**: the runner is copied to the `ADMIN$` share, started as a temporary service, then deleted.
  Nothing stays installed.
- **Updates**: the installer of a new version is only started after its SHA-256 hash has been checked.
