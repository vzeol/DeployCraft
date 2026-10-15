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

## Dernière version : 0.11.0 — à valider par le mainteneur

Regroupe les PR #16, #18, #19, #21, #26 et #27 (détail ci-dessous) : étape activée / désactivée, arrêter en échec
ou en succès, étape Pause, sections avant / principales / après, paquet imbriqué, correctif de `status.json`
(étapes restantes perdues si l'écriture tombait pendant une lecture du serveur), étape annulée terminée en échec,
« REMARQUE » au lieu de « ERREUR » pour un poste réussi, colonnes élargies, boîtes de message aux couleurs du thème.
Chaque PR a été testée sous Windows sur une instance isolée et un poste de labo. 98 tests automatiques.

La 0.10.0 (fenêtre paquet à onglets, conditions, options par défaut, hors ligne) a été validée par le mainteneur.
**Pas encore testé** : le réveil d'un vrai poste éteint (il faut un poste de test que le mainteneur accepte
d'éteindre, avec le réveil réseau activé).

Gestion du dépôt (15/10) : `main` protégée (PR + CI obligatoires), un sujet = une issue (qui le prend), voir `CLAUDE.md`.

**Sujet en cours ailleurs : authentification de l'API du service (PARITE 8.10, issue #5).** Aujourd'hui l'API
(`localhost:5080`) n'a pas d'authentification : un utilisateur ouvert sur le serveur pourrait lancer un déploiement.

## En cours : options et étapes manquantes (DeployCraft Cloud, relu par DeployCraft Prod)

Fonctionnement (validé par le mainteneur) : la session Cloud code et ouvre les PR ; la session Windows
« DeployCraft Prod » (même compte) pilote, relit et teste sur un poste de labo, et répond par message entre
sessions. La session Cloud fusionne (squash) **seulement après son feu vert** ; versions, tags et releases sont
faits par Prod avec l'accord du mainteneur. Le mainteneur est prévenu sur son téléphone pour ce qui demande une
décision.

Programme, une PR par sujet, chacune relue et testée sous Windows par DeployCraft Prod avant fusion (pas de
version ni de release côté Cloud) :
- **A — 2.6 étape activée / désactivée + 2.7 arrêter en succès (#15, PR #16)** : fusionnée. Enchaînement des
  étapes du runner sorti dans `Runner/StepSequence.cs`, testé sous Linux (état `stopped` = échec avec arrêt en
  succès, poste réussi). Pas vu par Prod : le rendu visuel (grisé, thèmes), à regarder à l'installation.
- **B — 2.25 Pause (#17, PR #18)** : fusionnée. Type `pause` + champ `seconds` ; le runner attend sans lancer
  de processus (`StepSequence.Wait`).
- **Correctif trouvé par Prod pendant les tests de B (PR #19)** : fusionné. `status.json` remplacé par le runner
  pendant que le serveur le lit → l'écriture échouait et coupait les étapes restantes. Désormais : réessais
  d'écriture (runner) et de lecture partagée (serveur), et un enregistrement raté n'arrête plus les étapes.
  Annulation : l'étape interrompue passe en échec « Annulé. » au lieu de rester « en cours ».
- **C — 2.5 sections avant / principales / après (PR #21)** : fusionnée en 0.11.0, puis **retirée** à la demande du
  mainteneur (un échec doit arrêter net le paquet ; branche `refactor/retirer-sections`). Les paquets enregistrés
  en 0.11.0 gardent leur ordre, le champ « section » est ignoré à la lecture.
- **D — 2.28 paquet imbriqué (PR #26)** : fusionné. `Package.Flatten` remplace les étapes « paquet imbriqué »
  au lancement (dans `Resolve`) ; conditions héritées dans `ExtraConditions` / `extraConditions`. « Arrêter le
  déploiement » renommé « Arrêter en échec ».
- **Retours du mainteneur sur la 0.10.0 (PR #27)** : fusionnée. « REMARQUE » (couleur atténuée) au lieu de
  « ERREUR » rouge pour un poste réussi, « Raison : » pour une étape ignorée, colonnes élargies, boîte
  `MessageDialog` aux couleurs du thème (plus aucun `MessageBox.Show`). **Version 0.11.0 publiée** par Prod.
- **E — 8.2 variables (PR #30)** : fusionnée. Serveur, runner, API
  `/variables`, `Store.Variables.cs` (Store en `partial`), `Data/VariableText.cs` (partagé avec la console),
  `VariablesPage` (onglet Préférences › Variables ; à la fermeture des Préférences, « enregistrer ? » Oui / Non /
  Annuler via `MessageDialog`) et 15 tests.
  Règles validées par Prod : seuls les noms connus sont remplacés (un `$(…)` inconnu reste tel quel, à cause de
  PowerShell) ; noms en lettres / chiffres / « _ » ; pas de remplacement récursif ; `$(Poste)` dans `Prepare`.
  Le message de boucle des paquets imbriqués donne maintenant le chemin (« B › C › B »), demandé par Prod.
- Contributeur externe (chromozone21, par fork) : issues #22, #23, #24, à ne pas prendre.
- Ensuite : 2.8, 2.23, 2.24, 2.26, 2.27, 2.20–2.22 (à tester sur un vrai poste).
- Import des paquets PDQ Deploy (#14) : en attente, après ces sujets.

Autres sujets possibles : réglages hors ligne dans Déployer une fois et les planifications, LAPS (en option),
listes de cibles.

## Version actuelle : 0.11.0

- Paquets multi-étapes (Installer / Commande / PowerShell), codes de succès, délai, comportement en cas d'erreur,
  exécuter en tant que et mode de copie par paquet
- Déployer une fois : plusieurs paquets, étape de départ par poste, choix AD en arbre OU › OU › poste
- Suivi maître / détail (déploiements, postes, étapes avec sortie), colonne Prioritaire
- Annuler, Redéployer, Redéployer sur les postes en échec, Reprendre à l'étape en échec
- Menus Fichier / Édition / Options / Aide avec raccourcis clavier, clic droit partout
- Identifiants multiples (0.9.0), comptes locaux « .\ » et test sur un poste (0.9.1)
- Thème clair / sombre / selon Windows (Préférences › Interface)
- Planifications : déclencheurs, historique des cibles, réessai en boucle jusqu'à réussite (0.8.0)
- Conditions par paquet et par étape, version du paquet, options « par défaut » des Préférences, couper / copier /
  coller d'étapes (0.10.0)
- Hors ligne (0.10.0, désactivé par défaut) : Wake-on-LAN, nouveaux essais (perdus si le service redémarre)
- Étapes Pause et paquet imbriqué, sections avant / principales / après, étapes désactivables, arrêter en échec ou
  en succès (0.11.0)
- Pas encore testé : abandon des postes en attente après N minutes (il faut plus de 8 postes en même temps)

Construire : `.\build.ps1 -Version x.y.z` → `dist\DeployCraft-Setup-x.y.z.exe` (prérequis : SDK .NET 10, Inno Setup 6).
Chaque version est publiée dans les **Releases** GitHub avec son installeur (tag `vx.y.z`).

## Notes techniques

- Sessions Claude Code dans le cloud (Linux) : .NET installé au démarrage, build / tests / format possibles
  (sauf tests propres à Windows), voir `CLAUDE.md` § « Session dans le cloud ».
- Tester sans toucher aux vraies données : `dotnet run --project src\DeployCraft.Server -- --Urls http://localhost:5091 --DataDir <dossier>` et lancer la console avec `DEPLOYCRAFT_URL=http://localhost:5091`.
- Ne fermer que ses propres fenêtres de test (par PID), jamais toutes les instances DeployCraft.
- Windows PowerShell 5.1 : enregistrer les scripts `.ps1` en UTF-8 **avec BOM** (sinon les accents cassent).
- Les conditions sont vérifiées par le runner (`src\DeployCraft.Runner\Conditions.cs`, testé via un lien dans le
  projet de tests) ; la lecture du vrai poste est dans `LocalFacts.cs`.
