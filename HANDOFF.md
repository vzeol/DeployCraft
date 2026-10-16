# Reprise du projet

> À lire en début de session (mainteneur, contributeur ou Claude) pour reprendre là où on s'est arrêté.
> Pour participer : `CONTRIBUTING.md`. Liste des tâches : `PARITE.md`. Règles : `CLAUDE.md`.

## Objectif

Un outil libre (GPL v3) de déploiement de logiciels sur un parc Windows, sans agent, qui couvre les fonctions
attendues d'un outil de ce type (`PARITE.md`), puis tri et ajouts à la fin.

## Méthode de travail (consigne du mainteneur)

**Un sujet à la fois.** On finit un sujet sur sa branche (commits, tests, HANDOFF, PR, installeur), puis fusion
et release par Claude (CI au vert) ; le mainteneur l'installe et teste sur son parc : **on ne passe au sujet
suivant qu'une fois qu'il a tout validé**.

Les informations propres à l'environnement du mainteneur (serveurs, postes de test, comptes) sont dans ses notes
**hors du dépôt** : ne jamais les recopier ici.

## Version actuelle : 1.0.0-rc.1 — en test chez le mainteneur

**Toute la liste `PARITE.md` est traitée** : 61 fonctions faites, 22 écartées par le mainteneur, 6 mises de côté.
Seule la 8.1 reste partielle : LAPS est prévu après la v1 (issue #106). La 1.0.0-rc.1 est une version candidate :
le mainteneur l'utilise quelques jours sur son parc. Si rien ne bloque, la même version est publiée en **1.0.0**
(stable), et la console installée la propose au démarrage (« Oui » → installation) : ce sera le premier vrai essai
de la mise à jour depuis la console. Un problème → correctif, puis 1.0.0-rc.2.

Nouveau depuis la 0.14.0 (PR #61 à #114) :
- Étape Installer complète : options MSI, ligne de commande, fichiers additionnels (2.20–2.22)
- Bibliothèque de paquets à partir du catalogue public winget, avec mise à jour automatique des paquets (10.4)
- Dossiers de paquets, couper / copier / coller dans l'arbre, onglet Détails du paquet, menu Affichage, page d'accueil
- Assistant de premier démarrage, aide intégrée (F1), fenêtre À propos
- Avertissements des étapes, sortie d'une étape (copier, ouvrir, enregistrer), statut « Annulé » et compteurs
- Planification depuis un déploiement, ajout / retrait d'un paquet d'une planification depuis l'arbre
- Vérification DNS des postes saisis dans Déployer une fois (3.4)
- Postes en parallèle réglables (1–100) et limite de débit des copies (8.4) ; progression de copie (% et débit),
  exécutions et note par étape (4.4)
- Nouvelle version signalée au démarrage, installation en un clic avec empreinte vérifiée (8.14)
- Suivi plus robuste : coupure réseau tolérée pendant l'exécution, verrou de slot repris, sortie UTF-16 lisible

Tests sur le domaine de test par Prod : déploiements réels (Installer, copies, annulation), parallélisme, limite de
débit, vérification DNS, installeur (`/RELAUNCH=1` rouvre la console sans élévation). À regarder par le mainteneur
au quotidien : l'ensemble de l'interface, la bibliothèque et la mise à jour automatique des paquets.

## Après la v1

- 8.1 LAPS (issue #106).
- Import de paquets depuis un fichier XML (issue #14, en attente).
- 8.9 compte du service : mis de côté, repris seulement si le besoin se présente.

Construire : `.\build.ps1 -Version x.y.z` → `dist\DeployCraft-Setup-x.y.z.exe` (prérequis : SDK .NET 10, Inno Setup 6).
Chaque version est publiée dans les **Releases** GitHub avec son installeur (tag `vx.y.z`) ; une version candidate
ou bêta est une pré-version (la console ne la propose pas).

## Notes techniques

- Sessions Claude Code dans le cloud (Linux) : .NET installé au démarrage, build / tests / format possibles
  (sauf tests propres à Windows), voir `CLAUDE.md` § « Session dans le cloud ».
- Tester sans toucher aux vraies données : `dotnet run --project src\DeployCraft.Server -- --Urls http://localhost:5091 --DataDir <dossier>` et lancer la console avec `DEPLOYCRAFT_URL=http://localhost:5091`.
- Ne fermer que ses propres fenêtres de test (par PID), jamais toutes les instances DeployCraft.
- Windows PowerShell 5.1 : enregistrer les scripts `.ps1` en UTF-8 **avec BOM** (sinon les accents cassent).
- Les conditions sont vérifiées par le runner (`src\DeployCraft.Runner\Conditions.cs`, testé via un lien dans le
  projet de tests) ; la lecture du vrai poste est dans `LocalFacts.cs`.
