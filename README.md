# DeployCraft

Outil **libre** (GPL v3) de déploiement de logiciels sur un parc Windows, sans agent : un service sur un
serveur du domaine, une console Windows, et un petit runner copié temporairement sur chaque poste.
Son organisation s'inspire des outils de déploiement du marché comme PDQ Deploy ; c'est une réalisation
indépendante, écrite de zéro.

![Console DeployCraft : suivi des déploiements](docs/console.png)

## Fonctions

Paquets multi-étapes (installeur, commande, PowerShell) avec conditions (version de Windows, fichier,
registre…), « Déployer une fois » sur des postes saisis ou choisis dans l'Active Directory, suivi en direct
poste par poste, planifications (réessai en boucle jusqu'à réussite), réveil Wake-on-LAN et nouveaux essais des
postes hors ligne, identifiants multiples, thème clair / sombre. Liste complète et état : `PARITE.md`.

## Installation (sur un serveur du domaine)

1. Récupérer `DeployCraft-Setup-x.y.z.exe` dans les [**Releases**](https://github.com/vzeol/DeployCraft/releases).
2. Le lancer en administrateur : il installe le service Windows **DeployCraft** (démarrage automatique,
   compte Système local) et la console. Rien d'autre à installer (.NET est embarqué).
3. Ouvrir la console **DeployCraft**, puis Options › **Identifiants…** pour ajouter le compte
   administrateur des postes (le mot de passe est saisi dans la console, chiffré sur le serveur).

## Lancement

- Le service démarre tout seul avec Windows ; la console se lance depuis le menu Démarrer.
- Les postes doivent accepter le partage `ADMIN$` et le gestionnaire de services à distance
  (pare-feu : partage de fichiers, port 445).

## Configuration

Pas de fichier `.env` : le service se règle par options de ligne de commande (ou `appsettings.json`).

| Réglage | Par défaut | Rôle |
|---|---|---|
| `--Urls` | `http://localhost:5080` | Adresse de l'API locale du service |
| `--DataDir` | `C:\ProgramData\DeployCraft` | Base SQLite et données (réservé à Système + Administrateurs) |
| `DEPLOYCRAFT_URL` (variable de la console) | `http://localhost:5080` | Service auquel la console se connecte |

## Développer

Prérequis : SDK .NET 10 et Inno Setup 6.

```powershell
dotnet build DeployCraft.slnx                    # compiler
dotnet test DeployCraft.slnx                     # tests automatiques
dotnet format DeployCraft.slnx                   # mise en forme
.\build.ps1 -Version 0.9.0                       # installeur → dist\
```

Instance de test isolée, branches, PR et règles : **`CONTRIBUTING.md`** (à lire avant le premier commit)
et `CLAUDE.md`.

| Dossier / fichier | Rôle |
|---|---|
| `src/DeployCraft.Server` | Service Windows : API locale, base SQLite, moteur de déploiement, lecture AD |
| `src/DeployCraft.Console` | Console WPF (l'application fenêtrée) |
| `src/DeployCraft.Runner` | Runner .NET Framework 4.8 exécuté en service temporaire sur les postes |
| `tests/DeployCraft.Tests` | Tests automatiques |
| `installer/` | Script Inno Setup |
| `SPEC.md` | Spécification fonctionnelle |
| `PARITE.md` | Liste des fonctions et leur état : la liste des tâches |
| `HANDOFF.md` | État du projet et point de reprise |

## Licence

Copyright (C) 2026 vzeol.

DeployCraft est un logiciel libre : vous pouvez le redistribuer et le modifier selon les termes de la
**GNU General Public License version 3** (voir `LICENSE`). Il est fourni **sans aucune garantie** : testez-le
d'abord sur des postes de test.

PDQ Deploy est une marque de PDQ.com Corporation. DeployCraft n'a aucun lien avec PDQ.com et n'est ni
approuvé ni soutenu par cette société.
