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

## Dernière version : 0.13.0 — à valider par le mainteneur

Regroupe les PR #38, #39, #40, #42, #44, #31 et #46 : nettoyage automatique de l'historique (Préférences ›
Déploiement, contribution externe), installeur qui gère une console restée ouverte, retrait des variables $(Nom)
(choix du mainteneur), éléments cliquables plus visibles, « exécuter en tant que » par étape (2.8),
authentification Windows de l'API (Options › Utilisateurs de la console) et commandes grisées tant que le service
refuse l'accès ou ne répond pas. 120 tests automatiques. **Survols (boutons, lignes, arbres) pas vérifiés par
Prod** (curseur non pilotable en RDP) : à regarder par le mainteneur.

La 0.12.0 a été testée par le mainteneur (ses retours sont dans la 0.13.0) ; la 0.10.0 est validée.
**Pas encore testé** : le réveil d'un vrai poste éteint (il faut un poste de test que le mainteneur accepte
d'éteindre, avec le réveil réseau activé).

Gestion du dépôt (15/10) : `main` protégée (PR + CI obligatoires), un sujet = une issue (qui le prend), voir `CLAUDE.md`.

**Authentification de l'API du service (PARITE 8.10, issue #5, PR #31 fusionnée)** : la console s'identifie avec
la session Windows (Negotiate) ; accès aux administrateurs du serveur + Options › Utilisateurs de la console ;
« Lancé par » pris du compte authentifié. Testé sur le domaine de test par Prod et le mainteneur.
Suite (issue #45, PR #46 fusionnée) : commandes grisées tant que l'accès est refusé ou le service injoignable
(Actualiser / F5, Quitter et Aide restent actifs). Testé à l'écran par Prod. Petit reste : Préférences est aussi
grisé, donc le thème ne peut pas changer sans service.

## Point de reprise (Cloud, 15/10) : retours du mainteneur sur la 0.12.0

Glisser-déposer et remarque partielle : validés. Les trois points sont faits et publiés en 0.13.0 :

1. **Fait — PR #40 fusionnée** : variables $(Nom) retirées (choix du mainteneur), relu et testé par Prod (un
   `$(Poste)` / `$(Get-Date)` part tel quel). Table `variables` laissée en base, plus lue. PARITE 8.2 ❌.
2. **Fait — PR #44 fusionnée (2.8)** : testé par Prod sur un vrai poste, y compris le passage compte de
   déploiement → Système → compte de déploiement. « Bureau » écarté par le mainteneur (PARITE 2.8).
3. **Fait — PR #42 fusionnée** : boutons de barre d'outils (fond et bordure au repos, survol bordé d'accent),
   liens soulignés en orange, survol des grilles et des arbres. Vu par Prod au repos en sombre et en clair ;
   **survols pas vérifiés** (curseur non pilotable en RDP) : à regarder par le mainteneur dans la 0.13.0.

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
- **Retours du mainteneur sur la 0.11.0 (PR #33)** : fusionnée. Remarque aussi quand une partie seulement des
  étapes est ignorée (« 1 étape ignorée : « X » (raison) ») ; glisser-déposer des étapes dans l'arbre de la fenêtre
  paquet (`PackageWindow.DragDrop.cs`, déplacement libre), Ctrl+Haut / Ctrl+Bas. **Glisser-déposer pas encore
  testé pour de vrai** (session Windows verrouillée pendant les tests de Prod) : à vérifier dans la 0.12.0.
- **Thème sombre trop « plat » (PR #36)** : fusionnée. Onglets soulignés d'accent, boutons et champs bordés, clés
  `Control*` / `Primary*` dans les deux thèmes (contrastes WCAG). **0.12.0 publiée** par Prod.
- **Installeur (PR #39)** : fusionnée. Une mise à jour avec la console ouverte échouait (code 5) après avoir supprimé
  le service. Désormais : console fermée proprement (Restart Manager, filtre `DeployCraft.exe`), service seulement
  arrêté juste avant la copie, reconfiguré par `sc config`, et redémarré si l'installation échoue.
- **2.8 exécuter en tant que par étape (PR #44)** : fusionnée, publiée en 0.13.0. Tranches par identité (`Remote/RunAsPlan.cs`),
  un service par tranche, `halt` / `haltReason` dans status.json, `firstNumber` dans job.json. « Compte de
  déploiement sur le bureau » : écarté par le mainteneur (déploiements toujours silencieux, sans mot de passe sur
  le poste).
- Contributeur externe (chromozone21, par fork) : issues #22, #23, #24, à ne pas prendre.
  **#23 — 4.8 nettoyage de l'historique (PR #38)** : fusionnée. Réglage `DeploymentDefaults.History`
  (`HistorySettings` : `Cleanup`, `KeepDays` de 1 à 3650, désactivé d'usine), `Store.PurgeFinishedDeployments`,
  service `HistoryCleaner` (au démarrage puis chaque jour), case et champ dans Préférences › Déploiement. Le premier
  ménage a lieu au prochain démarrage du service ou dans les 24 h, pas à l'activation du réglage.
- **2.23 copie de fichiers (issue #48, branche `feat/copie-de-fichiers`)** : en relecture par Prod. Nouveau type
  d'étape `CopyFiles` : source fichier ou dossier, destination du poste (`%variables%` acceptées), motifs,
  sous-dossiers, remplacer ou garder, réussir malgré les fichiers non remplaçables. Push : le serveur dépose les
  fichiers retenus dans `exec\copie-N` (`TargetSession.StageCopy`) ; pull : le poste lit le partage. La copie finale
  est faite par `robocopy` via `cmd /c` (`Runner/FileCopy.cs`, lié aux tests), codes de succès 0–7 (0–15 si on
  ignore les fichiers non copiés). Tests : `CopyFilesTests`.
- Ensuite : 2.24, 2.26, 2.27, 2.20–2.22 (à tester sur un vrai poste).
- Import de paquets depuis un fichier XML (#14) : en attente, après ces sujets.

Autres sujets possibles : réglages hors ligne dans Déployer une fois et les planifications, LAPS (en option),
listes de cibles.

## Version actuelle : 0.13.0

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
- Étapes Pause et paquet imbriqué, étapes désactivables, arrêter en échec ou en succès (0.11.0)
- Glisser-déposer des étapes (0.12.0 ; les variables $(Nom) de la 0.12.0 sont retirées en 0.13.0)
- Exécuter en tant que par étape, authentification de l'API (Options › Utilisateurs de la console), nettoyage
  automatique de l'historique (0.13.0)
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
