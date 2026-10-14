# Contribuer à DeployCraft

Marche à suivre pour toute personne qui participe au développement, humaine ou IA (Claude Code).
À lire en entier avant le premier commit. Les règles complètes sont aussi dans `CLAUDE.md`.

## 1. Ce qu'on construit

DeployCraft est un outil libre (GPL v3) de déploiement de logiciels sur un parc Windows, sans agent :
un service Windows sur un serveur du domaine, une console WPF, et un petit runner copié
temporairement sur chaque poste. Livrable : **un seul installeur**.

Objectif actuel : couvrir les fonctions attendues d'un outil de déploiement de ce type (`PARITE.md`),
puis trier à la fin.

Documents du dépôt :

| Fichier | Contenu |
|---|---|
| `HANDOFF.md` | État du projet, ce qui reste à tester, point de reprise |
| `PARITE.md` | Liste des fonctions, avec leur état : **c'est la liste des tâches** |
| `SPEC.md` | Spécification : ce que fait DeployCraft et comment |
| `CONTRIBUTING.md` | Ce guide |
| `CLAUDE.md` | Consignes chargées automatiquement par Claude Code (valables pour tous) |

## 2. Réalisation indépendante (non négociable)

DeployCraft est écrit de zéro. On ne reprend **jamais** le code, les icônes, le logo, les images, les
textes ou les fichiers d'un autre logiciel. Les libellés sont écrits en français, avec nos propres mots.
Démarche : décrire le comportement voulu dans `SPEC.md` → coder **depuis la spec**.
Notre identité visuelle : accent orange, icônes Windows (Segoe Fluent / MDL2).

**Aucune information interne** dans le dépôt public : noms de serveurs, de postes, de domaines, de comptes,
adresses IP, noms d'organisation, e-mails personnels — y compris dans les captures d'écran et les messages
de commit. Utiliser des exemples (`SERVEUR-01`, `PC-TEST-01`, `DOMAINE.LOCAL`).

## 3. Accès et outils

1. Installer : Git, **SDK .NET 10**, **Inno Setup 6** (`winget install JRSoftware.InnoSetup`),
   GitHub CLI (`gh auth login`, puis `gh auth setup-git`).
2. Contributeur extérieur : **forker** le dépôt, travailler sur une branche de son fork, ouvrir une PR.
   Collaborateur invité par le mainteneur : `gh repo clone vzeol/DeployCraft`, branche + PR.
3. Régler Git pour signer avec son adresse GitHub masquée (`git config user.email "…@users.noreply.github.com"`).

Structure :

| Dossier | Rôle |
|---|---|
| `src/DeployCraft.Server` | Service Windows : API HTTP locale (port 5080), base SQLite, moteur de déploiement, lecture AD |
| `src/DeployCraft.Console` | Console WPF ; réutilise les modèles de `Server/Data` |
| `src/DeployCraft.Runner` | Runner **.NET Framework 4.8** (présent sur tous les Windows), lancé en service temporaire sur les postes |
| `installer/` | Script Inno Setup |

## 4. Compiler et tester sans risque

```powershell
dotnet build DeployCraft.slnx
```

**Ne jamais tester sur les vraies données.** Lancer une instance isolée :

```powershell
# Serveur de test (base dans un dossier jetable, port 5091)
dotnet run --project src\DeployCraft.Server -- --Urls http://localhost:5091 --DataDir C:\Temp\dc-test

# Console branchée sur ce serveur (autre terminal)
$env:DEPLOYCRAFT_URL = "http://localhost:5091"
.\src\DeployCraft.Console\bin\Debug\net10.0-windows\DeployCraft.exe
```

- Le serveur doit tourner **en administrateur** pour déployer sur la machine locale.
- Les identifiants (comptes de déploiement) se saisissent dans la console (Options › Identifiants).
  Ne jamais écrire un mot de passe dans le code, un commit, une issue ou une conversation avec une IA.
- Fermer seulement **ses propres** fenêtres de test (par PID), jamais toutes les instances DeployCraft.
- Tests en réel sur un domaine : seulement sur des postes de test convenus avec le mainteneur
  (poste de test, machine hors production). Commencer par une étape sans effet (`hostname`).

Tests automatiques : `dotnet test DeployCraft.slnx` (projet `tests\DeployCraft.Tests`). Tout changement de
comportement ajoute ou met à jour un test, même basique. Ce qui touche un vrai poste (réseau, service
temporaire) se teste à la main sur l'instance isolée : la PR dit comment.

## 5. Choisir une tâche

1. Prendre une ou plusieurs lignes ⬜ / 🟡 de `PARITE.md`, de préférence dans le même bloc
   (ex. « 5. Planifications »).
2. Ouvrir une **issue GitHub** intitulée avec les numéros (`[5.1–5.3] Planifications : page + déclencheurs`)
   et se l'assigner : c'est ce qui évite de travailler à deux sur la même chose.
3. Si le comportement voulu n'est pas encore dans `SPEC.md` : le décrire d'abord
   (dans la même PR).

## 6. Branches, commits, PR

En résumé : chacun travaille sur **sa branche** (sa copie du projet) et propose ses changements
par une **pull request** (PR). Rien n'entre dans `main` sans relecture : personne ne casse le
travail des autres, et deux personnes (ou une personne et Claude) peuvent avancer en même temps.

```powershell
git switch main ; git pull                 # partir de la dernière version
git switch -c feat/planifications          # créer sa branche
# … coder, tester …
git status ; git diff                      # relire avant d'ajouter
git add src\DeployCraft.Server\Data\Models.cs …   # fichiers nommés un par un, jamais `git add -A`
git commit -m "feat: planifications, page et déclencheurs"
git push -u origin feat/planifications
gh pr create --fill                        # ouvre la PR (le modèle se remplit)
```

Si `main` a avancé pendant ce temps : `git switch main ; git pull ; git switch feat/… ; git merge main`,
régler les conflits éventuels, re-tester, pousser.

- `main` doit **toujours compiler** et produire un installeur qui marche.
- Une branche par tâche : `feat/planifications`, `fix/ad-arbre`, `docs/spec-reboot`…
- Commits au format Conventional Commits, suite en français, au présent (`feat: déclencheur hebdomadaire`,
  `fix: …`, `docs: …`, `test: …`, `chore: …`). Un commit = un changement logique. Jamais de message vague.
- PR vers `main` avec : lignes de `PARITE.md` concernées, ce qui a été testé, captures d'écran pour l'interface.
  Mettre à jour `PARITE.md` (et `SPEC.md` si besoin) **dans la PR**.
- **Qui décide** : **rien n'entre dans `main` sans la demande explicite du mainteneur**
  (compte GitHub `vzeol`), ni fusion, ni push, ni commit direct, même pour une correction de doc.
  Ses sessions Claude relisent les PR et demandent des corrections, puis attendent son accord pour
  fusionner et publier. Tout autre contributeur, humain ou IA, propose par PR. Fusion en « squash ».
- Jamais de `git push --force` sur `main` ; sur sa propre branche, `--force-with-lease` seulement.
- Avant chaque commit : aucun secret dans `git diff --staged`, `.gitignore` intact, build + tests + format OK.

## 7. Conventions de code

- Suivre le style du code autour : C# moderne, noms de code en anglais, **interface, messages et
  commentaires en français**. Peu de commentaires, et seulement pour expliquer *pourquoi*.
- Messages d'erreur : lisibles par un technicien, avec la cause probable
  (« Accès refusé au partage ADMIN$ de PC01 : le compte de déploiement est-il admin de ce poste ? »).
- Base SQLite : nouvelles colonnes via `AddColumnIfMissing` dans `Store.Migrate()`. Une base
  existante ne doit jamais perdre de données. Structures imbriquées rangées en JSON.
- API : routes dans `src/DeployCraft.Server/Program.cs`, client dans `src/DeployCraft.Console/ApiClient.cs`.
- Runner : .NET Framework 4.8 **sans dépendance externe**. L'échange avec le serveur passe par
  `job.json` / `status.json` : modifier en même temps `Runner/Job.cs` et `Server/Remote/RunnerContract.cs`.
- Console : jamais de couleur en dur. Ajouter la clé dans `Themes/Clair.xaml` **et** `Themes/Sombre.xaml`,
  l'utiliser en `{DynamicResource …}` (sinon elle ne suit pas le changement de thème), et vérifier la
  fenêtre dans les deux thèmes. Un style implicite d'un contrôle standard s'appuie sur celui de Fluent
  (`BasedOn="{StaticResource {x:Type TextBox}}"`, voir `Themes/Styles.xaml`).
- Pas de nouvelle dépendance NuGet sans raison valable (expliquée dans la PR). Versions figées par
  `packages.lock.json` (commité ; régénéré par `dotnet restore` quand une dépendance change).
- Format : `dotnet format DeployCraft.slnx` avant de commiter (règles dans `.editorconfig`, vérifié par la CI).
- Scripts `.ps1` enregistrés en **UTF-8 avec BOM** (sinon Windows PowerShell 5.1 casse les accents).

## 8. Version et publication

Numéro `MAJEUR.MINEUR.CORRECTIF` : nouvelle fonction → mineur, correction → correctif.

```powershell
.\build.ps1 -Version 0.7.0                  # → dist\DeployCraft-Setup-0.7.0.exe
git tag v0.7.0 ; git push origin v0.7.0
gh release create v0.7.0 dist\DeployCraft-Setup-0.7.0.exe --title "DeployCraft 0.7.0" --notes "…"
```

La release est faite après la fusion dans `main`, **quand le mainteneur le demande**.
Puis mettre à jour `HANDOFF.md` (version actuelle, à tester, suite).

## 9. Travailler avec Claude Code

Claude Code lit `CLAUDE.md` tout seul en ouvrant le dépôt. En début de session, lui demander
de lire `HANDOFF.md`, `PARITE.md` et ce guide. Les mêmes règles s'appliquent à lui :
réalisation indépendante, aucune information interne, une branche par tâche, ne jamais manipuler
de mot de passe. Le Claude d'un contributeur a les mêmes droits que lui : il propose par PR,
il ne fusionne pas. Les règles complètes sont dans `CLAUDE.md`.

En fin de session : mettre à jour `HANDOFF.md` (où on en est, ce qui reste) pour que la
personne suivante, humaine ou IA, reprenne sans rien deviner.
