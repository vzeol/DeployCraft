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

## Dernière version : 0.14.0 — à valider par le mainteneur

Regroupe les PR #49, #51, #55, #57 et #59 : étapes Copie de fichiers (2.23), Redémarrer / Éteindre (2.24) et
Message (2.26), export et import de paquets (1.11, 2.12), et correctif du slot laissé sur le poste quand la
préparation échoue. 162 tests automatiques. Chaque étape a été testée par Prod sur un vrai poste (redémarrages,
arrêt puis réveil, messages avec le mainteneur devant l'écran). **Fenêtre d'import pas capturée par Prod** (menus
non pilotables à distance) : à regarder par le mainteneur. Issue #60 (échec intermittent
« runner arrêté sans résultat » alors que les étapes ont réussi) : corrigée (PR #68, testée par Prod : 10 sur 10).
Suite (issue #69, branche `fix/suivi-robuste`) : coupure réseau tolérée 10 min pendant l'exécution, verrou de slot
laissé par un serveur arrêté repris au lancement suivant (PR #71 fusionnée ; vraie coupure réseau : issue #72).
**Étape Installer (PARITE 2.20 à 2.22, PR #61, branche `feat/installer-options`)** : rebasée sur `main` le 16/10 ;
fenêtre et import XML à voir par le mainteneur en local, déploiements réels à tester par Prod.

La 0.13.0 est validée par le mainteneur. Le réveil Wake-on-LAN d'un vrai poste éteint a été testé (0.14.0).

Gestion du dépôt (15/10) : `main` protégée (PR + CI obligatoires), un sujet = une issue (qui le prend), voir `CLAUDE.md`.

**Authentification de l'API du service (PARITE 8.10, issue #5, PR #31 fusionnée)** : la console s'identifie avec
la session Windows (Negotiate) ; accès aux administrateurs du serveur + Options › Utilisateurs de la console ;
« Lancé par » pris du compte authentifié. Testé sur le domaine de test par Prod et le mainteneur.
Suite (issue #45, PR #46 fusionnée) : commandes grisées tant que l'accès est refusé ou le service injoignable
(Actualiser / F5, Quitter et Aide restent actifs). Testé à l'écran par Prod. Petit reste : Préférences est aussi
grisé, donc le thème ne peut pas changer sans service.

**Fenêtre À propos (PARITE 1.13, branche `feat/a-propos`)** : versions de la console et du service, compte connecté, licence GPL v3, liens vers le projet et la licence, bouton « Copier les informations ». Vue par le mainteneur sur une instance de test ; ne dépend pas du domaine.

## Point de reprise (White Room, 16/10 midi)

Les 9 PR de la bêta (#82 à #98) sont fusionnées après validation du mainteneur, avec ses 4 corrections (menu
Affichage unique, assistant non modal, Entrée ne ferme plus l'assistant, dossier de bibliothèque choisi dans une
fenêtre Windows). Ouvertes : #100 (Accueil dans le menu Affichage) et #101 (vérification DNS des postes, 3.4),
à montrer au mainteneur. Suite : sujets qui dépendent du domaine, avec Claude Prod. Pas de 0.15.0 sans son accord.

## Point de reprise (White Room, nuit du 16/10)

Fusionnées sur `main` depuis la 0.14.0 : bibliothèque de paquets (#66), À propos (#67), correctifs #60 (#68), #69 (#71)
et #77 (#80), étape Installer complète (#61), planification depuis un déploiement (#74), statut « Annulé » et
compteurs (#76), dossiers de paquets (#79). **Pas de 0.15.0** : le mainteneur veut ajouter d'autres fonctions avant.

**Pré-version `v0.15.0-beta.2`** (branche `beta/0.15.0`, installeur dans les Releases) = `main` + les PR ouvertes,
toutes « sans domaine », à valider par le mainteneur avant fusion : #82 avertissements des étapes, #84 sortie d'une
étape, #86 page d'accueil, #88 assistant de démarrage, #90 aide intégrée (F1), #92 nouvelle version disponible,
#94 couper / copier / coller dans l'arbre, #96 menu Affichage, #98 ajouter un paquet à une planification.
À la fusion : conflits attendus dans `DeployCraft.Console.csproj`, `MainWindow.xaml(.cs)` et `PARITE.md` (garder les
deux côtés) ; ajouter alors « Accueil » au menu Affichage. `v0.15.0-beta.1` est remplacée par la bêta 2.

Reste dans PARITE : 1.6 (apparence, plus tard), 3.4 et 1.12 (contributeur), 4.4 progression de copie, 8.1, 8.4
(bande passante, postes en parallèle), 8.9 (compte du service), 10.4 (mise à jour automatique des paquets) ;
ceux qui touchent les déploiements réels se testent sur le domaine (Prod). Vraie coupure réseau : issue #72.

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
- **2.23 copie de fichiers (issue #48, PR #49)** : fusionnée après tests de Prod sur un vrai poste. Nouveau type
  d'étape `CopyFiles` : source fichier ou dossier, destination du poste (`%variables%` acceptées), motifs,
  sous-dossiers, remplacer ou garder, réussir malgré les fichiers non remplaçables. Push : le serveur dépose les
  fichiers retenus dans `exec\copie-N` (`TargetSession.StageCopy`) ; pull : le poste lit le partage. La copie finale
  est faite par `robocopy` via `cmd /c` (`Runner/FileCopy.cs`, lié aux tests), codes de succès 0–7 (0–15 si on
  ignore les fichiers non copiés). Tests : `CopyFilesTests`.
- **2.24 redémarrer (issue #50, PR #51)** : relue et testée par Prod sur un vrai poste (redémarrage en Système et en
  compte de déploiement, arrêt puis rallumage par Wake-on-LAN, annulation sur le poste). Type `Reboot` : délai (30 s à
  1 h), message, éteindre au lieu de redémarrer. Le runner programme `shutdown.exe` (`Runner/Shutdown.cs`) ; l'étape
  termine sa tranche (`RunAsPlan.Split(modes, cutAfter)`) ; `TargetSession.WaitForRestartAsync` attend l'arrêt puis le
  retour (délai max de l'étape), `StartAfterRestartAsync` réessaie la création du service. Arrêt : `RunAsPlan.Finish`
  ignore les étapes suivantes. Tests : `RebootTests`. Retouches visuelles de la copie de fichiers dans la même PR.
- **Correctif slot (issue #54, PR #55)** : fusionné. Une préparation ratée (source introuvable…) ne laisse plus
  `slot-N` ni `slot-N.lock` sur le poste.
- **2.26 message (issue #56, PR #57)** : fusionnée après tests de Prod sur un vrai poste (sans attente, fin de
  durée, délai max, clic, annulation, compte de déploiement, texte spécial, aucune console). Type `Message` : titre,
  texte, durée d'affichage (0 = jusqu'au clic), attendre le clic. Le runner (tranche en Système) lance dans chaque session ouverte une fenêtre
  par Windows PowerShell (`UserProcess.Start(…, session)`, script encodé et codes de sortie dans
  `Runner/MessageOutcome.cs`, lié aux tests). Première version (message de session Windows) écartée après les tests
  de Prod : fermeture par délai vue comme un clic, messages mis en file par session.
  « & » affiché tel quel (`UseMnemonic`). Tests : `MessageTests`.
- Ensuite : 2.27, 2.20–2.22 (à tester sur un vrai poste).
- **10.4 bibliothèque de paquets (branche `feat/bibliotheque`, Claude White Room)** : testée en local par le
  mainteneur (Acrobat Reader déployé sur son poste). `Library/CatalogIndex` (index du catalogue public winget, base
  SQLite téléchargée une fois par jour : liste complète, recherche, versions), `CatalogManifest` (lecture YAML des
  fiches, choix de l'installeur pour tout le poste et sans fenêtre, paramètres silencieux par type),
  `PackageLibrary` (état dans le réglage `library`, téléchargement en arrière-plan avec étapes et pourcentages,
  création / mise à jour du paquet), `LibraryCatalog.Suggested` (logiciels courants en tête). Routes
  `GET /library`, `GET /library/status`, `POST /library/refresh`, `POST /library/download`, `PUT /library/folder`.
  Tests : `LibraryTests`. Reste : mise à jour automatique (mode immédiat ou différé), à décider avec le mainteneur.
- **1.11 / 2.12 export et import de paquets (PR #59, Claude White Room)** : fusionné. Fichier › Exporter
  le paquet / tous les paquets (`.json`, paquets imbriqués inclus) et Importer des paquets (`.json` ou `.xml`), avec
  une fenêtre d'aperçu (repris / avec réserves / non repris). `PackageExchange` (export, lecture, `Complete` :
  noms uniques, liens des paquets imbriqués, validation, enregistrement), `XmlPackageImport` (fichiers XML),
  types partagés `ExchangeModels.cs`, routes `POST /packages/export` et `/packages/import`. Une condition sans
  équivalent exact désactive l'étape importée. Tests : `PackageExchangeTests`. Aucune mention d'un autre logiciel
  dans DeployCraft (règle du mainteneur).

Autres sujets possibles : réglages hors ligne dans Déployer une fois et les planifications, LAPS (en option),
listes de cibles.

## Version actuelle : 0.14.0

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
- Étapes Copie de fichiers, Redémarrer / Éteindre et Message ; export et import de paquets (0.14.0)
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
