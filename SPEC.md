# DeployCraft — Spécification fonctionnelle

> Ce que fait DeployCraft et comment il le fait. Le code est écrit à partir de ce document.
> DeployCraft est une réalisation indépendante : aucun code, icône, image ni texte d'un autre logiciel.

## 1. Architecture générale

| Brique | Rôle |
|---|---|
| **Console** (WPF) | Créer les paquets, choisir les cibles, lancer et suivre les déploiements |
| **Service** (Windows) | Tourne en fond, expose une API HTTP locale, exécute les déploiements et les planifications |
| **Runner** | Petit programme copié temporairement sur chaque poste cible : il exécute les étapes localement puis est supprimé |
| **Base de données** | SQLite locale : paquets, déploiements, planifications, identifiants, historique |

## 2. Concepts

### Paquet
- Nom, version, description
- Liste ordonnée d'**étapes**
- **Conditions** (voir plus bas)
- Options : mode de copie des fichiers (push : le serveur copie ; pull : le poste lit un partage), exécuter en
  tant que (compte de déploiement / Système local / utilisateur connecté), délai max des étapes ; chacune peut
  rester « Par défaut » (valeur des Préférences)
- Hors ligne : réveil Wake-on-LAN, nouveaux essais ; ou réglages des Préférences

### Types d'étapes
| Étape | Description |
|---|---|
| Installer | Lancer un MSI / EXE / MSU avec paramètres silencieux, codes de succès configurables |
| Commande | Ligne de commande cmd |
| PowerShell | Script PowerShell |
| Copie de fichiers | Copier un fichier ou un dossier vers un dossier du poste (voir § Étape « Copie de fichiers ») |
| Redémarrer | Redémarrer (ou éteindre) le poste et attendre son retour (voir § Étape « Redémarrer ») |
| Pause | Attendre N secondes (1 s à 24 h) sur le poste avant l'étape suivante ; rien n'est lancé, pas de code de succès ; l'attente s'arrête si le déploiement est annulé |
| Message | Afficher un message aux utilisateurs connectés (voir § Étape « Message ») |
| Déconnexion *(à venir)* | Déconnecter l'utilisateur |
| Paquet imbriqué | Exécuter les étapes d'un autre paquet (voir § Paquet imbriqué) |

### Conditions (0.10.0, par paquet et par étape)
- Version de Windows (10, 11, Server, antérieures ; aucune cochée = toutes), architecture (32 / 64 bits),
  version minimale de PowerShell (3, 4, 5, 7)
- Utilisateur connecté ou personne de connecté
- Fichier ou dossier (existe / n'existe pas, variables d'environnement acceptées), clé de registre, avec ou sans
  nom de valeur (existe / n'existe pas)
- Vérifiées par le runner sur le poste, juste avant l'exécution ; toutes doivent être remplies.
- Étape : condition non remplie → étape « Ignoré » avec la raison, le paquet continue.
- Paquet : conditions non remplies → aucune étape exécutée, toutes « Ignoré », poste compté **réussi** (rien à
  faire), raison affichée dans la colonne Détail. Une planification « ne plus déployer une fois réussi » ne
  le relance donc pas.
- Condition impossible à vérifier (clé mal écrite…) → étape en échec avec le message.

### Exécuter en tant que (par étape)
- Onglet Options de l'étape : **Paramètre du paquet** (par défaut), compte de déploiement, Système local ou
  utilisateur connecté. « Paramètre du paquet » suit le paquet, ou le choix fait dans Déployer une fois / la
  planification ; une étape avec son propre réglage le garde. Sans identifiant enregistré, « compte de
  déploiement » devient Système local. Dans un paquet imbriqué, les sous-étapes sans réglage prennent celui de
  l'étape « paquet imbriqué ».
- Sur le poste, un service runner n'a qu'une identité : les étapes consécutives de même service (compte de
  déploiement, ou Système, qui sert aussi pour l'utilisateur connecté) forment une **tranche**. Le serveur lance les
  tranches l'une après l'autre dans le même dossier (les fichiers copiés restent), chacune avec son propre service
  (`DeployCraftRunner-N-1`, `-2`…), la suivante seulement après la suppression du précédent. Le mot de passe du
  compte ne sert qu'à créer le service : il n'est jamais écrit sur le poste.
- Une tranche qui arrête le paquet (échec en « Arrêter en échec », arrêt en succès, conditions du paquet non
  remplies) : les tranches suivantes ne sont pas lancées, leurs étapes sont « Ignoré » avec la même raison.
  Runner interrompu par une erreur : pas de tranche suivante non plus, raison « Tranche précédente interrompue : … ».
  Annulation (pendant une tranche ou entre deux) : la tranche en cours est arrêtée, aucune autre n'est lancée.
  Pendant l'exécution, le détail du poste indique l'identité de la tranche (« Exécution (Système) »). Le journal `runner.log` cumule les
  tranches ; les numéros d'étape y sont ceux du paquet.
- Mode pull : le partage est lu avec l'identité de la tranche (Système : le compte ordinateur `POSTE$`).
- Changer d'identité coûte quelques secondes (création et arrêt d'un service) ; un paquet sans réglage par étape
  garde un seul service, comme avant.

### Gestion d'erreur (par étape)
- **Arrêter en échec** (par défaut) : les étapes suivantes ne s'exécutent pas, le poste est en échec. Un paquet
  s'arrête donc net à la première étape en échec (choix du mainteneur : plus prudent, et des paquets plus propres).
- **Continuer** : l'étape est notée en échec (le poste aussi), les étapes suivantes s'exécutent.
- **Arrêter en succès** : les étapes suivantes ne s'exécutent pas et le poste est compté **réussi**. Utile quand
  l'échec veut dire « rien à faire ici » (ex. un test qui sort en erreur si le logiciel est déjà à jour). L'étape
  s'affiche « Arrêt en succès » avec son code retour ; les suivantes « Ignoré », avec le numéro de l'étape qui a
  arrêté le paquet ; la colonne Détail du poste en donne la raison.

### Paquet imbriqué
- Une étape « Paquet imbriqué » désigne un autre paquet (choisi dans une liste). **Au lancement**, elle est
  remplacée par les étapes de ce paquet, figées dans l'historique : modifier le paquet imbriqué ensuite ne change
  pas un déploiement en cours. Le suivi, le compteur et l'étape de départ comptent ces étapes une à une.
- Étapes remplacées nommées « Étape › Sous-étape » (titre de l'étape, sinon nom du paquet imbriqué).
- Les étapes du paquet imbriqué prennent la place de l'étape, dans leur ordre.
- Conditions : chaque étape remplacée garde les siennes et reçoit en plus celles de l'étape « paquet imbriqué »
  et celles du paquet imbriqué. Non remplies : « Ignoré », raison « Conditions du paquet imbriqué « X » non
  remplies. … ».
- Délai et codes de succès : ceux de chaque sous-étape (délai vide : celui du paquet imbriqué, sinon celui des
  Préférences). Exécuter en tant que et mode de copie : ceux du déploiement. Comportement en cas d'erreur : celui
  de chaque sous-étape ; ceux de l'étape « paquet imbriqué » sont sans effet (grisés).
- Étape « paquet imbriqué » désactivée : toutes ses étapes sont désactivées. Une sous-étape désactivée le reste.
- Refusé à l'enregistrement : aucun paquet choisi, paquet introuvable, boucle (« A contient B qui contient A »),
  plus de 5 niveaux. Un paquet utilisé comme paquet imbriqué ne peut pas être supprimé (le message cite les
  paquets qui l'utilisent).
- « Redéployer » relit les paquets actuels (comme pour tout paquet) : un paquet imbriqué modifié depuis est pris
  dans sa nouvelle version.

### Étape activée / désactivée
- Une étape décochée « Étape activée » reste dans le paquet mais n'est jamais exécutée : « Ignoré », raison
  « Étape désactivée. », et le paquet continue. Aucun fichier n'est copié ni vérifié pour elle.
- Signalée « (désactivée) » et grisée dans la fenêtre paquet et la page du paquet. Clic droit › Activer /
  Désactiver.

### Cibles
- Saisie manuelle de noms de postes, collage, fichier texte
- Active Directory (arbre des OU)
- *(à venir)* Listes de cibles réutilisables, filtres d'inclusion / exclusion

### Déploiement
- Un déploiement = 1 paquet × N postes
- Statut global + statut **par poste** + statut **par étape**
- Erreurs lisibles : nom introuvable dans le DNS, accès refusé, poste injoignable, délai dépassé, code de sortie
  hors des codes de succès, etc.

### Étape « Installer »
- Fichier (chemin local ou UNC), paramètres, dossier entier si l'installeur a plusieurs fichiers
- **Codes de succès par défaut : `0, 1641, 3010, 2359302`** (OK, OK + redémarrage lancé, OK + redémarrage
  requis, mise à jour MSU déjà installée)

### Étape « Copie de fichiers »
- **Source** : fichier ou dossier vu depuis le serveur (chemin local ou UNC). **Destination** : dossier du poste,
  créé s'il manque ; les variables d'environnement (`%ProgramData%`, `%PUBLIC%`…) prennent les valeurs de
  l'identité de l'étape (celles de l'utilisateur connecté si l'étape s'exécute en son nom).
- Pour une source dossier : **motifs** séparés par « ; » (`*` par défaut), **sous-dossiers** (cochés par défaut,
  recréés même vides). Une source fichier copie ce seul fichier.
- **Remplacer les fichiers déjà présents** (coché par défaut) ; décoché, un fichier présent sur le poste est gardé.
- **Réussir même si des fichiers ne peuvent pas être remplacés** (fichier en cours d'utilisation…) : décoché, un
  seul fichier non copié fait échouer l'étape. Une source introuvable reste toujours une erreur.
- Push : le serveur dépose les fichiers retenus dans `exec\copie-N` (avec le compte de déploiement si la source
  est sur un partage). Pull (source `\\serveur\…`) : le poste lit lui-même le partage. Dans les deux cas, la copie
  vers la destination est faite sur le poste par `robocopy` (fourni avec Windows), sous l'identité de l'étape ; sa
  sortie apparaît dans le détail de l'étape. Un fichier bloqué est réessayé une fois.
- Pas de codes de succès à saisir : ceux de robocopy sont utilisés (0 à 7 = réussite ; 8 à 15 = des fichiers non
  copiés, acceptés seulement avec « réussir même si… » ; 16 = erreur grave).
- Refusé à l'enregistrement : source ou destination vide.

### Étape « Redémarrer »
- **Délai** de 30 secondes à 1 heure (60 s par défaut) et **message** facultatif (500 caractères au plus) : l'utilisateur
  connecté voit le compte à rebours et le message. Le délai laisse aussi au runner le temps de rendre son résultat.
- Le redémarrage est programmé sur le poste par `shutdown.exe`, toujours par le service (jamais au nom de
  l'utilisateur connecté). L'étape réussit dès qu'il est programmé.
- Une étape « Redémarrer » termine sa tranche (voir « Exécuter en tant que »). Le serveur attend que le poste
  s'arrête (au plus délai + 5 min ; sinon erreur « redémarrage annulé sur le poste ? »), puis qu'il réponde de
  nouveau, au plus le **délai max de l'étape** (sinon erreur). Il relance ensuite un service pour les étapes
  suivantes, en réessayant pendant 2 minutes le temps que Windows finisse de démarrer. Le slot reste sur le disque du
  poste pendant le redémarrage. Suivi : « Redémarrage du poste », puis « Attente du retour du poste ».
- **Éteindre au lieu de redémarrer** : les étapes suivantes sont « Ignoré », raison « Poste éteint par l'étape N
  « X » », et le poste compte comme réussi.
- Étape ignorée (conditions, désactivée) ou en échec : aucun redémarrage, aucune attente.
- Refusé au lancement sur le serveur DeployCraft lui-même. Refusé à l'enregistrement : délai hors de 30 s à 1 h,
  message de plus de 500 caractères.

### Étape « Message »
- **Titre** (100 caractères, « Information » par défaut) et **texte** (2 000 caractères, obligatoire), affichés dans
  une fenêtre avec un bouton OK, dans **chaque session ouverte** du poste. Le service (toujours en Système pour cette
  étape) lance dans chaque session une petite fenêtre par Windows PowerShell : chaque message a sa propre fenêtre,
  jamais mise en attente derrière une autre, et rend « OK cliqué » ou « fermé à la fin de l'affichage ».
- **Durée d'affichage** en secondes (0 à 24 h) : la fenêtre se ferme seule à la fin ; 0 = elle reste jusqu'au clic.
- **Attendre que l'utilisateur clique sur OK** (décoché par défaut) :
  - décoché : l'étape réussit dès l'affichage, la suite enchaîne ;
  - coché avec une durée : l'étape attend le clic ou la fin de l'affichage, et réussit dans les deux cas ;
  - coché sans durée : l'étape attend le clic, au plus le délai max de l'étape ; personne n'a cliqué → échec.
  Plusieurs sessions : l'étape attend la réponse de chacune.
- Personne de connecté : l'étape réussit sans rien afficher (« Aucun utilisateur connecté » dans le détail).
- Le détail de l'étape indique pour qui le message a été affiché et le résultat (clic, fermeture automatique).
- Annulation pendant l'attente : l'étape échoue « Annulé. » (la fenêtre peut rester affichée).
- Refusé à l'enregistrement : texte vide ou trop long, titre trop long, durée hors de 0 à 24 h.

## 3. Déroulé d'un déploiement sur un poste

1. Le serveur vérifie que le poste répond (DNS, port 445).
2. Il se connecte au partage `ADMIN$` du poste avec l'identifiant choisi et réserve un **slot** :
   ```
   C:\Windows\DeployCraft\
     slot-N.lock               ← verrou du slot (plusieurs déploiements possibles en parallèle sur un poste)
     slot-N\
       DeployCraftRunner-N.exe ← runner (.NET Framework 4.8, présent d'origine sur Windows 10 / 11)
       job.json                ← consignes (étapes, codes de succès, délais)
       status.json             ← avancement et résultats, écrits par le runner
       runner.log              ← journal du runner
       exec\                   ← fichiers du paquet = répertoire courant des étapes
     last-runner.log           ← journal du dernier passage (conservé après nettoyage)
   ```
3. Il crée un **service Windows temporaire** `DeployCraftRunner-N` et le démarre (sous l'identifiant choisi ou en
   Système local).
4. Le runner exécute les étapes dans l'ordre, écrit `status.json` après chaque changement ; le serveur le relit
   chaque seconde pour l'affichage en direct (lecture partagée, sans bloquer le runner). Un enregistrement raté
   (fichier en cours de lecture) est réessayé puis rattrapé au suivant : il n'interrompt jamais les étapes.
5. Fin : le serveur compare chaque code de sortie aux codes de succès, puis arrête et supprime le service et le
   slot. Seul `last-runner.log` reste.

Pourquoi ce fonctionnement : pas d'agent permanent ni de WinRM, rien à installer sur les postes, plusieurs
déploiements possibles en même temps sur un poste.

### Déployer une fois (0.6.0)
- Plusieurs paquets dans l'ordre choisi : un déploiement par paquet. Sur un même poste, le paquet N+1
  attend la fin du paquet N, même s'il a échoué ou a été annulé.
- Étape de départ **par poste** (sur le premier paquet). « Reprendre à l'étape en échec » ouvre un seul
  déploiement où chaque poste reprend à sa propre étape.
- Exécuter en tant que (option du paquet, modifiable au lancement) :
  - **Compte de déploiement** : le service temporaire tourne sous l'identifiant choisi. Le serveur accorde d'abord
    le droit « Ouvrir une session en tant que service » sur le poste (LSA distante). Échec de connexion
    (erreur 1069) → message clair. Sans identifiant enregistré → Système local.
  - **Système local**.
  - **Utilisateur connecté** : service en Système, chaque étape est lancée dans la session active
    (jeton de l'utilisateur, bureau `winsta0\default`, sortie récupérée par tube). Personne de connecté → étape en échec.
- Mode de copie : **push** (le serveur copie) ou **pull** (le runner copie lui-même depuis un chemin UNC,
  avec son identité : identifiant choisi, ou compte ordinateur `POSTE$` en Système). Chemin local → push.
- Prioritaire : les postes du déploiement passent devant ceux qui attendent un créneau (8 en parallèle).
- Mémoriser les cibles : réglage `package.{id}.targets`, proposé à la prochaine ouverture de la fenêtre.

### Thème clair / sombre (0.7.0)
- Préférences › Interface › Thème = **Clair** (par défaut), **Sombre** ou **Selon Windows**
  (suit le mode des applications de Windows, même s'il change pendant que la console est ouverte).
  Appliqué à chaud à toutes les fenêtres, barre de titre comprise.
- Réglage propre à l'utilisateur Windows de la console (`%APPDATA%\DeployCraft\console.json`), pas au serveur.
- Technique : thème Fluent de WPF (.NET 10) pour les contrôles standard, accent remplacé par notre orange ;
  nos couleurs dans `Themes\Clair.xaml` / `Themes\Sombre.xaml` (mêmes clés, toujours en `DynamicResource`).

### Planifications (0.8.0)
- Page **Toutes les planifications** : grille (prochaine / dernière exécution, dernier résultat) et, en dessous,
  les déploiements de la planification choisie. Démarrer maintenant, modifier, activer / désactiver, dupliquer,
  supprimer.
- Fenêtre à onglets : **Déclencheurs**, **Cibles**, **Paquets** (ordonnés), **Options**, **Historique des cibles**.
- Déclencheurs : une fois, intervalle (avec plage horaire facultative), quotidien, hebdomadaire, mensuel (jour N,
  dernier jour, « le N-ième / dernier [lundi…] ») ; plusieurs par planification ; début et fin facultative.
- Options : identifiant, exécuter en tant que, mode de copie, **ne plus déployer sur un poste une fois qu'il a
  réussi**, ne plus déployer après N échecs, abandonner les postes encore en attente après N minutes.
- Usage type : un déclencheur à intervalle + « ne plus déployer une fois réussi » réessaie une installation en
  boucle pendant quelques jours jusqu'à ce qu'elle passe partout.
- Service : planificateur qui examine les déclencheurs **toutes les 15 s** (heure locale du serveur). Plusieurs
  déclenchements passés d'un coup → un seul lancement. Déclenchement manqué (service arrêté) : rattrapé seulement
  s'il date de moins de 5 min. Enregistrer une planification ne rattrape pas les déclenchements passés.
- Intervalle : grille début + k × intervalle ; avec une plage horaire, on saute au début de la plage suivante
  (une plage dont la fin précède le début passe minuit). Mensuel : jour 31 → dernier jour des mois plus courts.
- À chaque lancement, pour chaque paquet, on ne garde que les postes qui en ont besoin : pas déjà réussi
  (option, cochée par défaut), pas au maximum d'échecs, et **pas encore servi par un lancement précédent de
  la même planification** (en attente ou en cours : il sera repris au suivant).
  Les paquets restent enchaînés par poste. Rien à faire → pas de déploiement, « Dernier résultat » l'explique.
- Historique par planification × poste × paquet : réussi (définitif) et nombre d'échecs. Les annulations et
  les postes abandonnés faute de créneau ne comptent pas comme échecs. Effacer une ligne → le poste sera redéployé.
- Les déploiements gardent la planification d'origine ; « Lancé par » = « Planification « nom » »
  (ou l'utilisateur qui a cliqué « Démarrer maintenant »).
- À venir : déclencheur « poste revenu en ligne », onglet Hors ligne, notifications.

### Identifiants (0.9.0)
- Options › **Identifiants…** : liste « compte — description (par défaut) » ; Ajouter, Modifier,
  Définir par défaut, Supprimer. Le premier identifiant devient celui par défaut ; supprimer celui par défaut
  en désigne un autre. Suppression refusée si une planification l'utilise.
- Ajout / modification : Domaine (pré-rempli avec le domaine du poste), Utilisateur, Mot de passe (vide =
  inchangé), Description ; **Tester** vérifie un compte du domaine auprès de l'annuaire. Un compte local ne se
  teste pas : chaque poste le vérifie au déploiement.
- **Compte local** (domaine vide ou « . » → `.\utilisateur`) : présenté à chaque poste sous `POSTE\utilisateur`
  (accès ADMIN$ et gestionnaire de services), et le service temporaire tourne sous `.\utilisateur`. À savoir :
  un compte local autre que l'Administrateur intégré est filtré par l'UAC à distance, sauf réglage
  LocalAccountTokenFilterPolicy.
- Déployer une fois et planification : liste **Identifiant** (celui par défaut présélectionné) + **Gérer…**.
  Le déploiement garde l'identifiant utilisé (fiche « Compte », avec « accès : … » quand le paquet tourne en
  Système local ou utilisateur connecté). Planification sans identifiant (créée avant 0.9.0) = celui par défaut.
- Active Directory : lu avec l'identifiant par défaut s'il est du domaine, sinon le premier du domaine.
- Sécurité : mots de passe chiffrés DPAPI (portée machine), jamais renvoyés à la console ; au démarrage, le
  service réserve son dossier de données à Système et Administrateurs. Mot de passe illisible (base venue d'un
  autre serveur) → signalé dans la liste et le bandeau, à ressaisir.
- Mise à jour depuis 0.8 : l'ancien compte unique devient le premier identifiant, par défaut.
- À venir : LAPS (mot de passe administrateur local de chaque poste lu dans l'AD), bouton « afficher le mot de passe ».

### Utilisateurs de la console (accès à l'API du service)
- La console s'identifie auprès du service avec la session Windows de la personne (authentification Windows,
  aucun mot de passe à saisir). Une demande sans identité est refusée.
- Ont accès : les **administrateurs locaux du serveur**, toujours (même console ouverte sans élévation), et les
  comptes ou groupes de la liste Options › **Utilisateurs de la console…** (Ajouter : `DOMAINE\nom`, vérifié
  et retenu par son SID ; Supprimer). Seuls les administrateurs du serveur modifient la liste.
- Compte refusé : message « Votre compte … n'est pas autorisé à utiliser DeployCraft sur SERVEUR-01 » dans le
  bandeau, barre d'état « Accès refusé ».
- **Sessions actives** dans la même fenêtre : comptes ayant interrogé le service depuis moins de 2 minutes.
- « Lancé par » et « Annulé par » viennent du compte authentifié par le service, plus de la console.
- Barre d'état : « vous : DOMAINE\compte ».

### Fenêtre paquet (0.10.0)
- À gauche : arbre **Propriétés** puis **Étapes** (une ligne par étape) ; à droite : la page du nœud choisi.
  Barre d'outils : enregistrer, nouvelle étape ▾, monter, descendre (Ctrl+Haut / Ctrl+Bas), couper, copier, coller,
  supprimer. Une étape **glissée-déposée** dans l'arbre se place avant ou après l'étape visée, selon la moitié
  survolée (trait d'insertion ; Échap annule).
  Couper / copier / coller (Ctrl+X / C / V) passent par le presse-papiers : on colle aussi dans un autre paquet.
- Propriétés en 4 onglets :
  - **Détails** : nom, version (affichée à côté du nom dans la page du paquet), description.
  - **Conditions** : voir § Conditions.
  - **Options** : exécuter en tant que, mode de copie, délai max ; « Par défaut (valeur) » = Préférences.
  - **Hors ligne** : « Utiliser les réglages des Préférences », sinon réglages propres au paquet.
- Étape : titre, case **Étape activée**, puis 3 onglets : **Détails**, **Conditions**, **Options** (délai max,
  vide = celui du paquet ; en cas d'erreur). La vue d'ensemble des étapes a une colonne Conditions.
- Héritage des réglages : étape → paquet → Préférences. Au lancement, les valeurs réellement utilisées sont
  figées dans l'historique du déploiement.
- Le texte des étapes et des conditions est envoyé tel quel : pas de variables `$(…)` (une sous-expression
  PowerShell comme `$(Get-Date)` reste intacte) ; les `%VAR%` sont celles de Windows sur le poste.

### Export et import de paquets
- **Fichier › Exporter le paquet…** (aussi au clic droit) ou **Exporter tous les paquets…** : un fichier `.json`
  qui contient les paquets choisis **et les paquets imbriqués qu'ils utilisent**, pour qu'il se suffise à lui-même.
  Les identifiants (mots de passe) ne sont jamais exportés.
- **Fichier › Importer des paquets…** : un export DeployCraft (`.json`) ou un **fichier XML de paquets**.
  Une fenêtre montre d'abord un **aperçu** (rien n'est créé) : pour chaque paquet et chaque étape, repris tel quel
  (vert), repris avec des réserves (orange, avec l'explication) ou non repris (rouge, avec la raison).
  « Importer » crée les paquets ; rien n'est perdu sans être signalé.
- Toujours de **nouveaux** paquets : un nom déjà pris devient « Nom (2) », « Nom (3) »… Les étapes « paquet
  imbriqué » sont reliées aux paquets importés en même temps. Un paquet imbriqué est créé avant celui qui l'utilise ;
  une boucle de paquets imbriqués dans le fichier est refusée. Chaque paquet est vérifié comme à l'enregistrement.
- Fichier XML de paquets :
  - Étapes reprises : Installer, Commande, PowerShell, Copie de fichiers, Pause, Redémarrer, Paquet imbriqué.
    Pas encore reprises (signalées) : Message, Déconnexion, désinstallation / réparation MSI,
    ligne de commande personnalisée d'une installation ; jamais : scan d'inventaire.
  - Paquet imbriqué : relié au paquet du même fichier, sinon à un paquet du même nom déjà dans DeployCraft
    (signalé), sinon l'étape n'est pas reprise.
  - Étapes « avant » et « après » : mises à la suite, dans l'ordre d'exécution (signalé).
  - Conditions : versions de Windows, architecture, version de PowerShell, utilisateur connecté, présence d'un
    fichier ou d'une clé / valeur de registre. Une condition sans équivalent exact (version de fichier, valeur de
    registre comparée, Windows Server version par version, collection d'un logiciel d'inventaire…) est signalée et l'étape est
    **importée désactivée** (toutes les étapes si c'est une condition du paquet) : elle ne s'exécute jamais sur plus
    de postes que prévu. On la vérifie, puis on la réactive.
  - Fichiers additionnels d'une étape : pas encore copiés par DeployCraft, l'étape est importée désactivée.
  - `$(Repository)` est remplacé par le dossier du dépôt saisi dans la fenêtre d'import ; les autres variables
    restent telles quelles (signalées). Dossiers de paquets : DeployCraft n'en a pas, les paquets vont à la racine.
    Planifications et listes de postes du fichier : signalées, non reprises.

### Bibliothèque de paquets
- Entrée **Bibliothèque de paquets** de l'arbre : tout le catalogue public winget (environ 15 000
  logiciels), avec en tête les paquets déjà téléchargés puis une sélection de logiciels courants. Colonnes :
  logiciel, identifiant, version disponible, version dans DeployCraft, installeur, état.
- **Recherche** dans tout le catalogue : nom, identifiant, éditeur ou mot-clé ; chaque mot doit correspondre.
- Le service télécharge l'index du catalogue (un seul petit fichier) et le relit de lui-même une fois par jour,
  ou sur **Actualiser le catalogue**. La console charge la liste une fois, puis ne suit que les lignes qui changent.
- **Télécharger** (ou double-clic, plusieurs lignes possibles) : DeployCraft lit la fiche du logiciel et choisit un
  installeur **pour tout le poste et sans fenêtre** (64 bits de préférence, en français si l'éditeur en propose un,
  .msi de préférence). Sans installeur de ce genre, le téléchargement est refusé avec la raison. Sinon l'état suit
  chaque étape en pourcentage : téléchargement, vérification du fichier (empreinte du catalogue : un fichier
  différent est rejeté), copie dans le dossier de la bibliothèque, création du paquet.
- Le paquet créé porte le nom du logiciel et sa version, avec une étape « Installer » et les paramètres
  silencieux du type d'installeur (.msi : `ALLUSERS=1` ; autres : paramètres de la fiche, ou ceux d'usine du type).
- **Mise à jour disponible** quand le catalogue a une version plus récente que le paquet : **Mettre à jour**
  remplace le fichier, les paramètres et la version du paquet, garde le reste (conditions, étapes ajoutées) et
  supprime l'ancienne version du dossier. Un paquet supprimé n'est plus relié à la bibliothèque.
- **Dossier…** : où ranger les installeurs (`<dossier>\<logiciel>\<version>\`). Par défaut un dossier du serveur
  (mode push seulement) ; un partage est conseillé. Il est écrit avec l'identifiant par défaut.

### Préférences › Déploiement et Hors ligne (0.10.0)
- Réglages du **serveur** (communs à toutes les consoles), enregistrés à la fermeture de la fenêtre : exécuter en
  tant que, mode de copie, délai max par étape (60 min d'usine), réglages hors ligne.

### Nettoyage de l'historique
- Préférences › Déploiement : « Supprimer les déploiements terminés après N jours ». Réglage du **serveur**,
  **désactivé d'usine**, N de 1 à 3650 (30 proposé), refusé par le service hors de ces limites.
- Le service fait le ménage au démarrage, puis une fois par jour ; le réglage est relu à chaque passage.
- Seuls les déploiements **terminés** (réussis ou en échec) dont la fin remonte à plus de N jours sont supprimés,
  avec leurs postes. Un déploiement en attente ou en cours n'est jamais supprimé, ni l'historique des planifications
  (celui qui sert à « ne plus déployer une fois réussi »).
- Le nombre de déploiements supprimés est écrit dans le journal du service. Une valeur hors limites trouvée en base
  (modifiée à la main) est ramenée dans les limites : au plus un jour d'historique conservé en moins que prévu.
- Le fichier de base ne rétrécit pas (SQLite réutilise la place libérée) mais cesse de grossir.

### Postes hors ligne (0.10.0)
- Le poste est toujours testé avant la copie (nom DNS, partage de fichiers).
- **Wake-on-LAN** : poste qui ne répond pas → paquet de réveil (diffusion générale et diffusion de son
  sous-réseau, ports UDP 9 et 7), puis attente qu'il réponde (3 min d'usine). L'adresse MAC et la diffusion du
  sous-réseau sont envoyées par le runner à chaque déploiement : un poste jamais déployé ne peut pas être réveillé.
- **Nouveaux essais** : poste hors ligne → « En attente », étape « Hors ligne : nouvel essai à HH:mm (essai n sur
  N) », créneau rendu pendant l'attente ; échec après le dernier essai. Annulable ; un redémarrage du service
  abandonne les essais en attente. Seules les erreurs « hors ligne » sont réessayées.

## 4. Informations à afficher

| Objet | Informations |
|---|---|
| Déploiement | n°, statut (dont partiellement échoué, annulé), créé / démarré / terminé, durée, paquet + version, utilisateur de la console qui l'a lancé, compte d'exécution, planification d'origine, compteurs (en attente, en cours, réussis, échecs) |
| Poste d'un déploiement | nom, statut, début / fin, durée, étape en cours, « n sur m étapes », erreur + étape en échec, reprise à l'étape en échec, priorisation, fichier de sortie |
| Étape d'un poste | n°, titre, statut, code retour, durée, erreur, compte d'exécution, progression de copie (%, débit), note, sortie |
| File de réessai | poste, paquets, essais effectués / max / restants, prochain essai, dernier essai, expiration |
| Planification | nom, cibles, paquets ordonnés, déclencheurs, activée, prochaine / dernière exécution, arrêt après N échecs, arrêt après succès, délai max, notification |
| Réglages de déploiement | compte d'exécution, délai par poste, nettoyage de l'historique après N jours, comportement hors ligne |
| Rapports / notifications | e-mail après déploiement (destinataires, objet, corps, pièce jointe, seulement si changement) |
