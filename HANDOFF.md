# Reprise du projet

> Ã€ lire en dÃ©but de session (mainteneur, contributeur ou Claude) pour reprendre lÃ  oÃ¹ on s'est arrÃªtÃ©.
> Pour participer : `CONTRIBUTING.md`. Liste des tÃ¢ches : `PARITE.md`. RÃ¨gles : `CLAUDE.md`.

## Objectif

Un outil libre (GPL v3) de dÃ©ploiement de logiciels sur un parc Windows, sans agent, qui couvre les fonctions
attendues d'un outil de ce type (`PARITE.md`), puis tri et ajouts Ã  la fin.

## MÃ©thode de travail (consigne du mainteneur)

**Un sujet Ã  la fois.** On finit un sujet sur sa branche (commits, tests, HANDOFF, PR, installeur), puis fusion
et release par Claude (CI au vert) ; le mainteneur l'installe et teste sur son parc : **on ne passe au sujet
suivant qu'une fois qu'il a tout validÃ©**.

Les informations propres Ã  l'environnement du mainteneur (serveurs, postes de test, comptes) sont dans ses notes
**hors du dÃ©pÃ´t** : ne jamais les recopier ici.

## Dernier sujet : identifiants multiples (0.9.0, 0.9.1) â€” validÃ© par le mainteneur

Options â€º Identifiants (ajouter, modifier, par dÃ©faut, supprimer, tester), choix de l'identifiant dans DÃ©ployer une
fois et les planifications, comptes locaux, dossier de donnÃ©es rÃ©servÃ© aux administrateurs. DÃ©tail : `SPEC.md`
Â§ Â« Identifiants (0.9.0) Â». 0.9.1 : Â« .\ Â» reconnu comme compte local, Tester un compte local sur un poste choisi.
ValidÃ© par le mainteneur avec ses vrais comptes ; 38 tests automatiques.

Gestion du dÃ©pÃ´t (15/10) : `main` protÃ©gÃ©e (PR + CI obligatoires), rÃ¨gles des sessions du mainteneur dans `CLAUDE.md`.

**Sujet en cours : authentification de l'API du service (PARITE 8.10, issue #5).** Aujourd'hui l'API
(`localhost:5080`) n'a pas d'authentification : un utilisateur ouvert sur le serveur pourrait lancer un dÃ©ploiement.

Autres sujets possibles : fenÃªtre paquet Ã  onglets (`SPEC.md` Â§ Â« FenÃªtre paquet Â»), LAPS, listes de cibles,
onglet Hors ligne de DÃ©ployer une fois.

## Version actuelle : 0.9.1

- Paquets multi-Ã©tapes (Installer / Commande / PowerShell), codes de succÃ¨s, dÃ©lai, comportement en cas d'erreur,
  exÃ©cuter en tant que et mode de copie par paquet
- DÃ©ployer une fois : plusieurs paquets, Ã©tape de dÃ©part par poste, choix AD en arbre OU â€º OU â€º poste
- Suivi maÃ®tre / dÃ©tail (dÃ©ploiements, postes, Ã©tapes avec sortie), colonne Prioritaire
- Annuler, RedÃ©ployer, RedÃ©ployer sur les postes en Ã©chec, Reprendre Ã  l'Ã©tape en Ã©chec
- Menus Fichier / Ã‰dition / Options / Aide avec raccourcis clavier, clic droit partout
- Identifiants multiples (0.9.0), comptes locaux « .\ » et test sur un poste (0.9.1)
- ThÃ¨me clair / sombre / selon Windows (PrÃ©fÃ©rences â€º Interface)
- Planifications : dÃ©clencheurs, historique des cibles, rÃ©essai en boucle jusqu'Ã  rÃ©ussite (0.8.0)
- Pas encore de file de rÃ©essai automatique ; le rÃ©essai passe par une planification Ã  intervalle
- Pas encore testÃ© : abandon des postes en attente aprÃ¨s N minutes (il faut plus de 8 postes en mÃªme temps)

Construire : `.\build.ps1 -Version x.y.z` â†’ `dist\DeployCraft-Setup-x.y.z.exe` (prÃ©requis : SDK .NET 10, Inno Setup 6).
Chaque version est publiÃ©e dans les **Releases** GitHub avec son installeur (tag `vx.y.z`).

## Notes techniques

- Tester sans toucher aux vraies donnÃ©es : `dotnet run --project src\DeployCraft.Server -- --Urls http://localhost:5091 --DataDir <dossier>` et lancer la console avec `DEPLOYCRAFT_URL=http://localhost:5091`.
- Ne fermer que ses propres fenÃªtres de test (par PID), jamais toutes les instances DeployCraft.
- Windows PowerShell 5.1 : enregistrer les scripts `.ps1` en UTF-8 **avec BOM** (sinon les accents cassent).
- Aucun poste AD n'apparaÃ®t Â« dÃ©sactivÃ© Â» : Ã  vÃ©rifier (domaine sans compte dÃ©sactivÃ©, ou dÃ©tection fausse).
