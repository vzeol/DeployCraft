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

## Dernier sujet : identifiants multiples (0.9.0, 0.9.1) — validé par le mainteneur

Options › Identifiants (ajouter, modifier, par défaut, supprimer, tester), choix de l'identifiant dans Déployer une
fois et les planifications, comptes locaux, dossier de données réservé aux administrateurs. Détail : `SPEC.md`
§ « Identifiants (0.9.0) ». 0.9.1 : « .\ » reconnu comme compte local, Tester un compte local sur un poste choisi.
Validé par le mainteneur avec ses vrais comptes ; 38 tests automatiques.

Gestion du dépôt (15/10) : `main` protégée (PR + CI obligatoires), un sujet = une issue (qui le prend), voir `CLAUDE.md`.

**Sujet en cours : authentification de l'API du service (PARITE 8.10, issue #5).** Aujourd'hui l'API
(`localhost:5080`) n'a pas d'authentification : un utilisateur ouvert sur le serveur pourrait lancer un déploiement.

Autres sujets possibles : fenêtre paquet à onglets (`SPEC.md` § « Fenêtre paquet »), LAPS, listes de cibles,
onglet Hors ligne de Déployer une fois.

## Version actuelle : 0.9.1

- Paquets multi-étapes (Installer / Commande / PowerShell), codes de succès, délai, comportement en cas d'erreur,
  exécuter en tant que et mode de copie par paquet
- Déployer une fois : plusieurs paquets, étape de départ par poste, choix AD en arbre OU › OU › poste
- Suivi maître / détail (déploiements, postes, étapes avec sortie), colonne Prioritaire
- Annuler, Redéployer, Redéployer sur les postes en échec, Reprendre à l'étape en échec
- Menus Fichier / Édition / Options / Aide avec raccourcis clavier, clic droit partout
- Identifiants multiples (0.9.0), comptes locaux « .\ » et test sur un poste (0.9.1)
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
