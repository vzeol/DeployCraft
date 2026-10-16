# Sécurité

[Français](#sécurité) · [English](#security-english)

DeployCraft tourne avec des droits élevés sur un serveur et sur les postes d'un parc : une faille peut avoir des
conséquences sérieuses. Merci de la signaler **en privé**.

## Signaler une faille

Utilisez le signalement privé de GitHub : onglet **Security** › **Report a vulnerability**
([lien direct](https://github.com/vzeol/DeployCraft/security/advisories/new)).
N'ouvrez pas d'issue publique.

Indiquez si possible : la version concernée, ce qu'un attaquant peut obtenir, et comment le reproduire.
Retirez toute information propre à votre environnement (noms de domaine, de serveurs, de comptes).

Vous recevrez une réponse dès que possible. Le correctif est publié dans une nouvelle version, puis la faille est
décrite publiquement, avec votre nom si vous le souhaitez.

## Versions suivies

Seule la dernière version publiée reçoit les correctifs de sécurité.

## Comment DeployCraft protège votre parc

- **Accès à la console** : authentification Windows ; réservé aux administrateurs du serveur et aux comptes ou groupes
  autorisés dans Options › Utilisateurs de la console.
- **Identifiants de déploiement** : saisis dans la console, chiffrés sur le serveur (protection des données de Windows,
  portée machine) ; jamais copiés sur les postes. Le dossier de données est réservé à Système et aux administrateurs.
- **Sur les postes** : le runner est copié dans le partage `ADMIN$`, lancé comme service temporaire, puis supprimé.
  Rien ne reste installé.
- **Mises à jour** : l'installeur d'une nouvelle version n'est lancé qu'après vérification de son empreinte SHA-256.

---

# Security (English)

DeployCraft runs with high privileges on a server and on the computers of a fleet: a vulnerability can have serious
consequences. Please report it **privately**.

## Reporting a vulnerability

Use GitHub private reporting: **Security** tab › **Report a vulnerability**
([direct link](https://github.com/vzeol/DeployCraft/security/advisories/new)). Do not open a public issue.

If possible, include: the affected version, what an attacker can obtain, and how to reproduce it. Remove anything
specific to your environment (domain, server and account names). French or English are both fine.

You will get an answer as soon as possible. The fix is released in a new version, then the vulnerability is described
publicly, with your name if you wish.

## Supported versions

Only the latest released version receives security fixes.

## How DeployCraft protects your fleet

- **Console access**: Windows authentication; limited to the server's administrators and to the accounts or groups
  allowed in Options › Utilisateurs de la console (*Console users*).
- **Deployment credentials**: typed in the console, encrypted on the server (Windows data protection, machine scope);
  never copied to the computers. The data folder is restricted to System and administrators.
- **On the computers**: the runner is copied to the `ADMIN$` share, started as a temporary service, then deleted.
  Nothing stays installed.
- **Updates**: the installer of a new version is only started after its SHA-256 hash has been checked.
