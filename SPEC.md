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
| Copie de fichiers *(à venir)* | Copier des fichiers / dossiers vers la cible |
| Redémarrer *(à venir)* | Redémarrer et attendre le retour du poste |
| Pause *(à venir)* | Attendre N secondes |
| Message *(à venir)* | Afficher un message à l'utilisateur connecté |
| Déconnexion *(à venir)* | Déconnecter l'utilisateur |
| Paquet imbriqué *(à venir)* | Exécuter un autre paquet (détection des boucles) |

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

### Gestion d'erreur (par étape)
- Arrêter le déploiement en échec / continuer

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
   chaque seconde pour l'affichage en direct.
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

### Fenêtre paquet (0.10.0)
- À gauche : arbre **Propriétés** puis **Étapes** (une ligne par étape) ; à droite : la page du nœud choisi.
  Barre d'outils : enregistrer, nouvelle étape ▾, monter, descendre, couper, copier, coller, supprimer.
  Couper / copier / coller (Ctrl+X / C / V) passent par le presse-papiers : on colle aussi dans un autre paquet.
- Propriétés en 4 onglets :
  - **Détails** : nom, version (affichée à côté du nom dans la page du paquet), description.
  - **Conditions** : voir § Conditions.
  - **Options** : exécuter en tant que, mode de copie, délai max ; « Par défaut (valeur) » = Préférences.
  - **Hors ligne** : « Utiliser les réglages des Préférences », sinon réglages propres au paquet.
- Étape en 3 onglets : **Détails**, **Conditions**, **Options** (délai max, vide = celui du paquet ; en cas
  d'erreur). La vue d'ensemble des étapes a une colonne Conditions.
- Héritage des réglages : étape → paquet → Préférences. Au lancement, les valeurs réellement utilisées sont
  figées dans l'historique du déploiement.

### Préférences › Déploiement et Hors ligne (0.10.0)
- Réglages du **serveur** (communs à toutes les consoles), enregistrés à la fermeture de la fenêtre : exécuter en
  tant que, mode de copie, délai max par étape (60 min d'usine), réglages hors ligne.

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
