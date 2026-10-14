# DeployCraft — consignes pour Claude Code

Réponds en français. Le mainteneur du projet (compte GitHub `vzeol`) ne code pas : sois autonome
(concevoir, coder, tester), explique simplement ce que tu fais, et demande avant toute action risquée
sur un vrai parc. Ces règles valent pour **tout le monde** : mainteneur, contributeurs, et toutes les
sessions Claude.

Au début de chaque session, vérifie qui tu es (`gh api user --jq .login`) et lis :
1. `HANDOFF.md` : état actuel et point de reprise
2. `PARITE.md` : liste des fonctions à couvrir (= liste des tâches)
3. `CONTRIBUTING.md` : réalisation indépendante, test isolé, branches / PR, conventions, publication

## Autorité sur le dépôt

- **Les sessions Claude du mainteneur** (compte `vzeol`) **gèrent le dépôt de A à Z**, sans attendre son
  accord : branches, PR, relecture (y compris des PR des contributeurs), fusion, tags, releases, réglages.
- Elles appliquent les bonnes pratiques de ce fichier : tout passe par une branche et une PR (jamais de commit
  direct sur `main`), fusion seulement quand la PR est relue et la CI au vert, release après la fusion.
- Restent à demander au mainteneur : installer une version sur son serveur, déployer sur un vrai poste.
  Et au moindre doute ou question, lui demander plutôt que supposer.
- **Tout autre compte** (contributeur, et ses sessions Claude) : fork ou branche + PR seulement. Ne fusionne
  aucune PR, ne crée ni tag ni release, ne modifie pas les réglages du dépôt. Le mainteneur décide.

## Sécurité (priorité absolue)

- Le dépôt est **public** : tout ce qui est commité est visible de tous et le reste dans l'historique.
- **Aucune information interne** dans le dépôt (code, doc, tests, captures, messages de commit, issues) :
  noms de serveurs, de postes, de domaines, de comptes, adresses IP, noms d'organisation, adresses e-mail
  personnelles. Utiliser des exemples : `SERVEUR-01`, `PC-TEST-01`, `DOMAINE.LOCAL`, `admin`.
  Les notes propres à l'environnement du mainteneur restent **hors du dépôt**.
- Commits signés avec l'adresse GitHub masquée (`…@users.noreply.github.com`), jamais une adresse personnelle
  ou professionnelle.
- Ne jamais demander, lire ni écrire un mot de passe : l'utilisateur le saisit lui-même dans la console.
- Ne jamais commiter de secret : `.env`, clé, token, mot de passe, fichier `.pem` / `.key`, base de données
  (`deploycraft.db`). Ne jamais afficher le contenu d'un `.env` (code, logs, commits, messages).
- `.gitignore` : ne jamais le supprimer ni l'affaiblir.
- Secret ou information interne commité par erreur : prévenir le mainteneur tout de suite, recommander de
  révoquer / changer ce qui doit l'être ; le retirer du dernier commit ne suffit pas (il reste dans l'historique).
- Ne jamais commiter de dépendances installées ni de fichiers générés (`bin/`, `obj/`, `publish/`, `dist/`).
- Pas de `.env` dans ce projet : la configuration passe par `appsettings.json` et les options
  `--DataDir` / `--Urls` (documentées dans le README).

## Réalisation indépendante

DeployCraft est écrit de zéro. Jamais de code, d'icône, d'image, de texte ni de fichier provenant d'un autre
logiciel ; les libellés sont écrits avec nos propres mots.

On peut **observer** d'autres outils de déploiement en fonctionnement (ce qu'ils permettent de faire, comment
l'interface est organisée) et **citer leurs fonctions** pour dresser la liste des tâches (`PARITE.md`).
On ne décrit jamais leur fonctionnement interne et on ne reproduit jamais leurs textes ni leurs visuels :
`SPEC.md` décrit uniquement ce que fait DeployCraft, avec nos mots.

## Sessions du mainteneur

Le mainteneur fait travailler plusieurs sessions Claude, chacune avec un accès différent (par exemple : l'une
voit la version la plus récente d'un outil de référence mais n'a pas de domaine, l'autre a un domaine de test).
Toutes développent et ont la même autorité sur le dépôt. Pour ne jamais travailler à deux sur la même chose :
- avant de commencer un sujet, ouvrir (ou reprendre) son **issue**, se l'assigner et écrire dedans **quelle
  session** le prend (toutes utilisent le compte `vzeol` : l'assignation seule ne les distingue pas) ; un sujet
  déjà pris n'est pas touché ;
- une session qui a besoin de ce qu'une autre peut faire (observer un comportement, tester sur un domaine)
  le demande dans l'issue ou la PR concernée ;
- `main` est protégée : PR obligatoire et CI au vert pour fusionner, même pour les administrateurs.

## Tests sur un parc

Tester sur une instance isolée (`--DataDir`, port 5091). Pour un déploiement sur un vrai poste, demander
d'abord lequel au mainteneur.

## Commits

- Format **Conventional Commits**, suite en français, au présent :
  `feat: identifiants multiples`, `fix: case d'heure tronquée`, `docs: …`, `refactor: …`, `test: …`,
  `chore: …`, `style: …`, `ci: …`. Interdit : messages vagues (`update`, `fix`, `wip`).
- Un commit = un changement logique et cohérent.
- Pas de `git add -A` / `git add .` : lancer `git status` et `git diff`, puis ajouter les fichiers **par leur nom**.
- Jamais de `git push --force` sur `main` (sur sa propre branche : `--force-with-lease` seulement).

## Workflow

- Une branche par sujet (`feat/…`, `fix/…`, `docs/…`, `chore/…`), jamais de travail direct sur `main`.
- PR petites, faciles à relire, avec : quoi, pourquoi, comment tester (modèle `.github/pull_request_template.md`).
  Issues et PR polies, précises, professionnelles.

## Qualité

- Ajouter ou mettre à jour des **tests** (`tests/DeployCraft.Tests`) pour tout changement de comportement,
  même basiques. Ce qui touche un vrai poste reste testé à la main : la PR dit comment.
- Formatter : `dotnet format` (règles dans `.editorconfig`).
- Dépendances figées par `packages.lock.json` (toujours commité) ; pas de dépendance ajoutée sans raison.
- La CI GitHub Actions (`.github/workflows/ci.yml`) compile, teste et vérifie la mise en forme de chaque PR.
- Pas de code mort, pas de gros blocs commentés, pas de `Console.WriteLine` / `Debug.WriteLine` de mise au point oubliés.

## Avant chaque commit ou push, vérifier

1. Aucun secret, aucune information interne ni fichier sensible dans `git diff --staged`.
2. `.gitignore` intact.
3. `dotnet build DeployCraft.slnx`, `dotnet test DeployCraft.slnx` et
   `dotnet format DeployCraft.slnx --verify-no-changes` passent.
4. Message de commit conforme aux règles ci-dessus.

## Fin de session

Mettre à jour `HANDOFF.md` (sur la branche du sujet en cours, jamais directement sur `main`).
