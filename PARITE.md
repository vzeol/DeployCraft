# Fonctions — liste de travail

> **Objectif** : couvrir les fonctions qu'on attend d'un outil de déploiement comme PDQ Deploy
> (fonctionnement + organisation de l'interface), puis seulement à la fin retirer ce qui ne sert pas et
> ajouter nos propres fonctions. Les libellés et le code de DeployCraft sont entièrement les nôtres.

Légende : ✅ fait · 🟡 partiel · ⬜ à faire · ❌ écarté (décision du mainteneur) · ⛔ dépend d'un service PDQ ou tiers (à remplacer ou à écarter, décision du mainteneur)

Pour prendre une tâche : voir `CONTRIBUTING.md` (§ « Choisir une tâche »). Une ligne passe à ✅
dans la même PR que le code qui la réalise.

---

## 1. Fenêtre principale

| # | Fonction | État | Notes |
|---|---|---|---|
| 1.1 | Arbre de navigation à gauche : Paquets, Listes de cibles, Planifications, Tous les déploiements, File de réessai | 🟡 | Paquets, déploiements, planifications (0.8.0) |
| 1.2 | Dossiers dans l'arbre (paquets et listes de cibles), sous-dossiers, glisser-déposer | ⬜ | Ctrl+Maj+F |
| 1.3 | Page d'un paquet : détails, étapes, déploiements du paquet, planifications attachées | 🟡 | Onglet Planifications en 0.8.0 |
| 1.4 | Page d'un dossier, page « plusieurs paquets sélectionnés » | ⬜ | |
| 1.5 | Renommer (F2), dupliquer, couper / copier / coller, supprimer dans l'arbre | 🟡 | |
| 1.6 | Apparence d'un nœud de l'arbre (couleur / mise en forme) | ⬜ | |
| 1.7 | Menus Fichier / Édition / Affichage / Options / Aide + clic droit partout | 🟡 | 0.5.0 : Fichier, Édition, Options, Aide |
| 1.8 | Fenêtre des raccourcis clavier | ⬜ | |
| 1.9 | Thèmes clair / sombre | ✅ | 0.7.0 : Clair, Sombre ou Selon Windows ; Préférences › Interface, appliqué à chaud |
| 1.10 | Page d'accueil | ⬜ | |
| 1.11 | Import / export (paquets, dossiers, listes de cibles, planifications, réglages) | ⬜ | Format XML ou JSON à nous |
| 1.12 | Imprimer, aperçu avant impression, mise en page, enregistrer une grille en CSV | ⬜ | |
| 1.13 | À propos | 🟡 | |

## 2. Paquets

| # | Fonction | État | Notes |
|---|---|---|---|
| 2.1 | Nom, description (texte riche), version, dossier | 🟡 | Version en 0.10.0. Pas de texte riche ni de dossier |
| 2.2 | Fenêtre paquet à onglets : Détails, Conditions, Options, Hors ligne | ✅ | 0.10.0 ; l'étape a aussi Détails, Conditions, Options |
| 2.3 | Options du paquet : délai personnalisé, mode de copie, exécuter en tant que, réessai | ✅ | 0.10.0 : chaque option peut suivre les Préférences ; réessai dans l'onglet Hors ligne |
| 2.4 | Conditions du paquet : version d'OS + architecture, version PowerShell, utilisateur connecté, fichier, registre | ✅ | 0.10.0 : vérifiées par le runner ; non remplies = poste réussi sans rien exécuter |
| 2.5 | Sections d'étapes : avant / principales / après | ❌ | Écarté : choix du mainteneur, un échec arrête le paquet. Faites en 0.11.0, retirées ensuite |
| 2.6 | Par étape : titre, activée / désactivée, codes de succès | ✅ | Étape désactivée : « Ignoré », le paquet continue |
| 2.7 | Par étape : erreur → arrêter en échec / arrêter en succès / continuer | ✅ | Arrêt en succès : poste compté réussi, étapes suivantes « Ignoré » |
| 2.8 | Par étape : exécuter en tant que (compte de déploiement / compte de déploiement sur le bureau / système local / utilisateur connecté / hériter du paquet) | ⬜ | Aujourd'hui : toujours système local |
| 2.9 | Par étape : mêmes conditions qu'au paquet | ✅ | 0.10.0 : non remplies = étape « Ignoré », le paquet continue |
| 2.10 | Copier / couper / coller / réordonner des étapes, y compris entre paquets | ✅ | 0.10.0 : presse-papiers (Ctrl+X / C / V), barre d'outils et clic droit |
| 2.11 | Avertissements : fichier introuvable, pas de paramètre silencieux, conflit avec le mode « pull » | 🟡 | |
| 2.12 | Dupliquer, exporter un paquet | ⬜ | |
| 2.13 | Dossiers de paquets publics / privés | ⬜ | |

### Types d'étapes

| # | Étape | État | Détails à couvrir |
|---|---|---|---|
| 2.20 | Installer | 🟡 | Types exe, cmd, msi, msu, msp, vbs, ps1, reg ; MSI : installer / désinstaller / réparer, silencieux, redémarrage (jamais / demander / toujours) ; ligne de commande personnalisée ; dossier entier ; fichiers additionnels ; garder le fichier sur le poste. Manquent : vbs, reg, opérations MSI, fichiers additionnels, garder le fichier |
| 2.21 | Commande | 🟡 | Fichiers additionnels manquants |
| 2.22 | PowerShell | 🟡 | Ligne de commande personnalisée, fichiers additionnels, alerte `Write-Host` |
| 2.23 | Copie de fichiers | ⬜ | Fichier ou dossier, motifs, sous-dossiers, écraser, ignorer les erreurs d'écrasement |
| 2.24 | Redémarrer | ⬜ | Message, délai, arrêt seulement ; attendre le retour du poste |
| 2.25 | Pause | ✅ | N secondes (1 s à 24 h), interrompue par l'annulation |
| 2.26 | Message | ⬜ | Durée d'affichage, attendre que l'utilisateur clique |
| 2.27 | Déconnexion de l'utilisateur | ⬜ | Message, délai |
| 2.28 | Paquet imbriqué | ✅ | Remplacé par ses étapes au lancement ; conditions combinées, boucles et plus de 5 niveaux refusés |
| 2.29 | Scan d'inventaire | ⛔ | Dépend de PDQ Inventory |

## 3. Déployer une fois

| # | Fonction | État | Notes |
|---|---|---|---|
| 3.1 | Plusieurs paquets, ordonnés | ✅ | 0.6.0 : un déploiement par paquet ; sur un poste, le suivant attend la fin du précédent (même en échec) |
| 3.2 | Cibles : saisie, collage, Active Directory | ✅ | Arbre AD dépliable (0.5.1) |
| 3.3 | Cibles : liste de cibles, fichier texte | 🟡 | Fichier texte en 0.6.0 ; listes de cibles avec §6 |
| 3.4 | Résolution DNS des noms saisis (vérification en direct) | ⬜ | |
| 3.5 | Démarrer à l'étape N, **par poste** | ✅ | 0.6.0 ; porte sur le premier paquet. « Reprendre à l'étape en échec » = un seul déploiement |
| 3.6 | Options : compte, mode de copie, exécuter en tant que | ✅ | 0.6.0 ; identifiant au choix en 0.9.0 ; « Paramètre du paquet » par défaut. Compte de déploiement / Système local / Utilisateur connecté ; push / pull (UNC) |
| 3.7 | Options : notifications, mémoriser les cibles, prioritaire | 🟡 | Mémoriser + prioritaire en 0.6.0 ; notifications avec §9 |
| 3.8 | Onglet Hors ligne : ping avant, Wake-on-LAN, file de réessai | 🟡 | 0.10.0 : réglé dans le paquet (et les Préférences), pas encore dans Déployer une fois. Test DNS + partage de fichiers toujours fait avant la copie |
| 3.9 | Bouton « Planifier » (crée une planification à partir de la fenêtre) | ✅ | 0.8.0 : reprend paquets, cibles, options |
| 3.10 | Cibles depuis PDQ Inventory / Spiceworks | ⛔ | |

## 4. Suivi des déploiements

| # | Fonction | État | Notes |
|---|---|---|---|
| 4.1 | Liste maître / détail : déploiements, postes, étapes, sortie | ✅ | |
| 4.2 | Statuts de poste détaillés : en attente, ping, réveil (WoL), connexion, copie, exécution, reconnexion, annulation, finalisation… | 🟡 | |
| 4.3 | Compteurs par déploiement (en attente / en cours / réussis / échecs) + « partiellement échoué », « annulé » | 🟡 | |
| 4.4 | Progression de copie (% et débit), note par étape, compte d'exécution par étape | ⬜ | |
| 4.5 | Annuler, redéployer, redéployer les échecs, reprendre à l'étape en échec | ✅ | |
| 4.6 | Prioriser un poste en attente | ⬜ | |
| 4.7 | Créer une planification depuis un déploiement | ⬜ | |
| 4.8 | Nettoyage automatique de l'historique après N jours | ✅ | Préférences › Déploiement : désactivé d'usine, de 1 à 3650 jours ; au démarrage du service puis une fois par jour ; seuls les déploiements terminés sont supprimés (jamais en attente ou en cours, ni l'historique des planifications) |
| 4.9 | Fichier de sortie complet téléchargeable par étape | ⬜ | |

## 5. Planifications

| # | Fonction | État | Notes |
|---|---|---|---|
| 5.1 | Page « Toutes les planifications », fenêtre planification | ✅ | 0.8.0 : grille + déploiements de la planification ; fenêtre à onglets Déclencheurs / Cibles / Paquets / Options / Historique des cibles |
| 5.2 | Déclencheurs : une fois, quotidien, hebdomadaire, mensuel (jour N ou « le 1er lundi »), intervalle | ✅ | 0.8.0 : plusieurs par planification, plage horaire pour l'intervalle, fin facultative, « dernier jour / dernier lundi » |
| 5.3 | Déclencheur « heartbeat » (poste qui revient en ligne) | ⛔ | Dépend de PDQ Inventory ; un équivalent maison reste possible |
| 5.4 | Paquets ordonnés, cibles, options (compte, copie, exécuter en tant que, délai, notifications, hors ligne, réessai) | 🟡 | 0.8.0 : paquets, cibles, copie, exécuter en tant que, délai d'attente ; identifiant en 0.9.0 ; manquent notifications et hors ligne |
| 5.5 | Arrêter après N échecs, arrêter après un succès (par poste) | ✅ | 0.8.0 : historique des cibles par poste × paquet, effaçable |
| 5.6 | Activer / désactiver, prochaine et dernière exécution, dernière erreur | ✅ | 0.8.0 : « Dernier résultat » |
| 5.7 | Attacher / détacher à un paquet, dupliquer, déployer maintenant | 🟡 | 0.8.0 : dupliquer, démarrer maintenant, nouvelle planification depuis un paquet ; pas d'« attacher » depuis l'arbre |

## 6. Listes de cibles et filtres

| # | Fonction | État | Notes |
|---|---|---|---|
| 6.1 | Listes de cibles réutilisables (Ctrl+Maj+T), dans des dossiers | ⬜ | |
| 6.2 | Sources : postes, Active Directory (OU, groupe, conteneur), autre liste de cibles | ⬜ | |
| 6.3 | Filtres globaux d'inclusion / exclusion (nom, IPv4, IPv6) | ⬜ | Préférences |

## 7. File de réessai (hors ligne)

| # | Fonction | État | Notes |
|---|---|---|---|
| 7.1 | Ping avant déploiement, Wake-on-LAN | ✅ | 0.10.0 : test DNS + partage toujours fait ; réveil avec l'adresse MAC apprise au dernier déploiement |
| 7.2 | File de réessai : activée, nombre d'essais max, intervalle, expiration ; héritage préférences → paquet → déploiement | 🟡 | Retirée en 0.5.0, remise en 0.10.0 comme **option désactivée par défaut** : nombre d'essais et intervalle, préférences → paquet. Pas de page « File de réessai » ni d'expiration ; perdue au redémarrage du service |
| 7.3 | Page de la file : poste × paquet, essais restants, prochain essai, « essayer maintenant », supprimer | ⬜ | |

## 8. Préférences

| # | Section | État | Contenu |
|---|---|---|---|
| 8.1 | Comptes | 🟡 | 0.9.0 : Options › Identifiants, plusieurs comptes (domaine ou local `.\utilisateur`), description, compte par défaut, test auprès du domaine ; choisis dans Déployer une fois et les planifications. Reste : LAPS (mot de passe admin local de chaque poste lu dans l'AD, fonction Microsoft) |
| 8.2 | Variables | ✅ | $(Nom) : personnalisées (Préférences › Variables) + système $(Date), $(Heure), $(Serveur), $(Poste) ; chemins, paramètres, commandes, conditions |
| 8.3 | Déploiement | ✅ | 0.10.0 : exécuter en tant que, délai par étape, comportement hors ligne par défaut ; nettoyage de l'historique (4.8) |
| 8.4 | Performance | 🟡 | 0.10.0 : mode de copie par défaut. Reste : limite de bande passante, postes en parallèle (aujourd'hui fixé à 8) |
| 8.5 | Dépôt de fichiers (`$(Repository)`) | ⬜ | Chemin, fichiers inutilisés, exclusions |
| 8.6 | Filtres de cibles | ⬜ | cf. 6.3 |
| 8.7 | Serveur de messagerie + notifications | ⬜ | |
| 8.8 | Interface, impression | 🟡 | 0.7.0 : page Interface (thème) ; fenêtre des Préférences en liste de pages. Reste : écran de démarrage (on n'en a pas), impression |
| 8.9 | Service en arrière-plan | ⬜ | Compte du service |
| 8.10 | Utilisateurs de la console, sessions actives | ⬜ | Qui a le droit de se connecter au service |
| 8.11 | Mode serveur central (local / serveur / client) | ⬜ | Notre console parle déjà en HTTP au service |
| 8.12 | Service sur la cible | ⬜ | Nom d'hôte utilisé (FQDN / court / IP), réglages du service temporaire |
| 8.13 | Proxy, base de données, journalisation | ⬜ | |
| 8.14 | Mises à jour automatiques | ⬜ | À brancher sur nos Releases GitHub |

## 9. Rapports et notifications

| # | Fonction | État | Notes |
|---|---|---|---|
| 9.1 | Rapports : déploiements, paquets, planifications, listes de cibles | ⬜ | |
| 9.2 | Notification e-mail après déploiement ou planification : destinataires (à / cc / cci), objet, corps, pièce jointe, détails, seulement si changement, pas pour les réessais | ⬜ | |

## 10. Divers

| # | Fonction | État | Notes |
|---|---|---|---|
| 10.1 | Ligne de commande (déployer un paquet sur des cibles, lister…) | ⬜ | |
| 10.2 | Assistant de premier démarrage | ⬜ | |
| 10.3 | Aide intégrée | ⬜ | |
| 10.4 | Bibliothèque de paquets PDQ, Auto Deploy | ⛔ | Catalogue en ligne de PDQ : impossible à reprendre. Possible : bibliothèque locale à nous |
| 10.5 | Intégration PDQ Inventory, Spiceworks | ⛔ | |
| 10.6 | Licence, essai, statistiques d'usage | ⛔ | Sans objet |
