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

## Dernier sujet : identifiants multiples (0.9.0)

Options › Identifiants (ajouter, modifier, par défaut, supprimer, tester), choix de l'identifiant dans Déployer une
fois et les planifications, comptes locaux, dossier de données réservé aux administrateurs. Détail : `SPEC.md`
§ « Identifiants (0.9.0) ». Testé avec des comptes fictifs (30 tests automatiques + instance isolée) ; validation
avec de vrais comptes en cours chez le mainteneur.

Prochains sujets possibles : fenêtre paquet à onglets (`SPEC.md` § « Fenêtre paquet »), LAPS, listes de cibles,
onglet Hors ligne de Déployer une fois. À traiter aussi : l'API du service (`localhost:5080`) n'a pas
d'authentification ; un utilisateur ouvert sur le serveur pourrait lancer un déploiement (PARITE 8.10).

## Version actuelle : 0.9.0

- Paquets multi-étapes (Installer / Commande / PowerShell), codes de succès, délai, comportement en cas d'erreur,
  exécuter en tant que et mode de copie par paquet
- Déployer une fois : plusieurs paquets, étape de départ par poste, choix AD en arbre OU › OU › poste
- Suivi maître / détail (déploiements, postes, étapes avec sortie), colonne Prioritaire
- Annuler, Redéployer, Redéployer sur les postes en échec, Reprendre à l'étape en échec
- Menus Fichier / Édition / Options / Aide avec raccourcis clavier, clic droit partout
- Identifiants multiples (0.9.0)
- Thème clair / sombre / selon Windows (Préférences › Interface)
- Planifications : déclencheurs, historique des cibles, réessai en boucle jusqu'à réussite (0.8.0)
- Pas encore de file de réessai automatique ; le réessai passe par une planification à intervalle
- Pas encore testé : abandon des postes en attente après N minutes (il faut plus de 8 postes en même temps)

Construire : `.\build.ps1 -Version x.y.z` → `dist\DeployCraft-Setup-x.y.z.exe` (prérequis : SDK .NET 10, Inno Setup 6).
Chaque version est publiée dans les **Releases** GitHub avec son installeur (tag `vx.y.z`).

## Notes techniques

- Tester sans toucher aux vraies données : `dotnet run --project src\DeployCraft.Server -- --Urls http://localhost:5091 --DataDir <dossier>` et lancer la console avec `DEPLOYCRAFT_URL=http://localhost:5091`.
- Ne fermer que ses propres fenêtres de test (par PID), jamais toutes les instances DeployCraft.
- Windows PowerShell 5.1 : enregistrer les scripts `.ps1` en UTF-8 **avec BOM** (sinon les accents cassent).
- Aucun poste AD n'apparaît « désactivé » : à vérifier (domaine sans compte désactivé, ou détection fausse).
