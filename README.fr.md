<div align="center">

[English](README.md) · **Français**

# DeployCraft

**Déployez vos logiciels sur tout un parc Windows, sans agent, depuis une seule console.**

[![CI](https://github.com/vzeol/DeployCraft/actions/workflows/ci.yml/badge.svg)](https://github.com/vzeol/DeployCraft/actions/workflows/ci.yml)
[![Version](https://img.shields.io/github/v/release/vzeol/DeployCraft?include_prereleases&label=version)](https://github.com/vzeol/DeployCraft/releases)
[![Licence : GPL v3](https://img.shields.io/badge/licence-GPL%20v3-blue)](LICENSE)
![Windows](https://img.shields.io/badge/plateforme-Windows-0078D6)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

[Télécharger](https://github.com/vzeol/DeployCraft/releases) ·
[Signaler un problème](https://github.com/vzeol/DeployCraft/issues/new/choose) ·
[Fonctions](docs/FEATURES.md) ·
[Contribuer](CONTRIBUTING.md)

![Console DeployCraft : suivi des déploiements](docs/console.png)

</div>

DeployCraft est un outil **libre et gratuit** (GPL v3) pour les administrateurs systèmes : un service sur un serveur
du domaine, une console Windows, et un petit programme copié le temps d'un déploiement sur chaque poste, puis retiré.
Rien à installer à l'avance sur les postes.

Son organisation reprend les habitudes des outils de déploiement du marché ; c'est une réalisation
indépendante, écrite de zéro.

> La console est en **français** par défaut, et aussi en **anglais** : Préférences › Interface › Langue (prise en
> compte au prochain démarrage).

## Fonctions

**Paquets**
- Étapes : installeur (`.msi`, `.exe`, `.msu`, `.msp` avec options), commande, PowerShell, copie de fichiers, pause,
  redémarrage ou arrêt, message à l'utilisateur, paquet imbriqué
- Conditions par paquet et par étape : version et architecture de Windows, fichier, registre, utilisateur connecté…
- Compte d'exécution par étape (compte de déploiement, Système, utilisateur connecté), codes de succès, délai,
  comportement en cas d'erreur
- Bibliothèque de paquets prêts à l'emploi, téléchargement vérifié et **mise à jour automatique**
- Dossiers, glisser-déposer, couper / copier / coller, export et import

**Déploiement**
- Postes saisis, collés ou choisis dans l'**Active Directory**, noms vérifiés dans le DNS
- Suivi en direct poste par poste : étape en cours, progression des copies, sortie de chaque étape
- **Planifications** (réessai en boucle jusqu'à réussite), redéploiement des postes en échec, reprise à une étape
- Postes hors ligne : réveil **Wake-on-LAN** et nouveaux essais
- Nombre de postes en parallèle et débit des copies réglables

**Administration**
- Identifiants multiples, chiffrés sur le serveur ; accès à la console réservé aux comptes autorisés
- Nettoyage automatique de l'historique, assistant de premier démarrage, aide intégrée (F1)
- Nouvelle version de DeployCraft proposée au démarrage, installée en un clic
- Thème clair ou sombre

Liste complète et état de chaque fonction : [`docs/FEATURES.md`](docs/FEATURES.md).

## Prérequis

| | |
|---|---|
| **Serveur** | Windows 64 bits membre du domaine (un serveur ou une VM dédiée de préférence), compte administrateur |
| **Postes** | Windows 10 ou 11, partage `ADMIN$` et gestionnaire de services accessibles depuis le serveur (partage de fichiers, port 445) |
| **Compte de déploiement** | Administrateur des postes (par exemple un compte du domaine membre du groupe Administrateurs local) |

## Installation

1. Télécharger `DeployCraft-Setup-x.y.z.exe` dans les [**Releases**](https://github.com/vzeol/DeployCraft/releases).
2. Le lancer en administrateur sur le serveur. Il installe le service Windows **DeployCraft** (démarrage automatique,
   compte Système local) et la console. Rien d'autre à installer : .NET est embarqué.
3. Ouvrir la console **DeployCraft** : l'assistant de premier démarrage guide les premiers réglages (Aide ›
   Assistant de démarrage pour y revenir).

Seuls les administrateurs du serveur ouvrent la console par défaut. Pour donner l'accès à d'autres comptes ou
groupes : Options › **Utilisateurs de la console…**

## Premier déploiement

1. **Options › Identifiants…** : ajouter le compte de déploiement. Le mot de passe est saisi dans la console et
   chiffré sur le serveur.
2. **Nouveau paquet**, ou un paquet de la **Bibliothèque** : par exemple une étape Installer avec un `.msi`.
3. **Déployer une fois** : choisir un **poste de test**, puis suivre le déploiement en direct.
4. Une fois validé sur un poste de test, déployer sur les autres ou créer une **planification**.

## L'essayer

La **1.0.0** est disponible dans les [Releases](https://github.com/vzeol/DeployCraft/releases). Merci de
l'essayer !

- Installez-la sur un serveur ou une VM de test, et commencez par des **postes de test**.
- Un problème, une remarque ? [Ouvrez une issue](https://github.com/vzeol/DeployCraft/issues/new/choose) : le
  formulaire demande juste ce qu'il faut. Pensez à retirer les noms de vos serveurs, postes et domaine.
- Une faille de sécurité : en privé, voir [`SECURITY.md`](SECURITY.md).

## Configuration

Pas de fichier `.env` : le service se règle par options de ligne de commande (ou `appsettings.json`).

| Réglage | Par défaut | Rôle |
|---|---|---|
| `--Urls` | `http://localhost:5080` | Adresse de l'API locale du service |
| `--DataDir` | `C:\ProgramData\DeployCraft` | Base SQLite et données (réservé à Système + Administrateurs) |
| `DEPLOYCRAFT_URL` (variable de la console) | `http://localhost:5080` | Service auquel la console se connecte |

## Contribuer

Les contributions sont les bienvenues : signalements, idées, code. Avant le premier commit, lire
[`CONTRIBUTING.md`](CONTRIBUTING.md) (instance de test isolée, branches, PR, règles).

Prérequis : SDK .NET 10 et Inno Setup 6.

```powershell
dotnet build DeployCraft.slnx                    # compiler
dotnet test DeployCraft.slnx                     # tests automatiques
dotnet format DeployCraft.slnx                   # mise en forme
.\build.ps1 -Version 1.0.0                       # installeur → dist\
```

| Dossier / fichier | Rôle |
|---|---|
| `src/DeployCraft.Server` | Service Windows : API locale, base SQLite, moteur de déploiement, lecture AD |
| `src/DeployCraft.Console` | Console WPF (l'application fenêtrée) |
| `src/DeployCraft.Runner` | Runner .NET Framework 4.8 exécuté en service temporaire sur les postes |
| `tests/DeployCraft.Tests` | Tests automatiques |
| `installer/` | Script Inno Setup |
| [`docs/SPEC.md`](docs/SPEC.md) | Spécification fonctionnelle (en anglais) |
| [`docs/FEATURES.md`](docs/FEATURES.md) | Liste des fonctions et leur état : la liste des tâches (en anglais) |
| [`docs/STATUS.md`](docs/STATUS.md) | État du projet et point de reprise (en anglais) |

## Licence

Copyright (C) 2026 vzeol.

DeployCraft est un logiciel libre : vous pouvez le redistribuer et le modifier selon les termes de la
**GNU General Public License version 3** (voir [`LICENSE`](LICENSE)). Il est fourni **sans aucune garantie** :
testez-le d'abord sur des postes de test.
