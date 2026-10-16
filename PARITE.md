# Fonctions — liste de travail

> **Objectif** : couvrir les fonctions qu'on attend d'un outil de déploiement
> (fonctionnement + organisation de l'interface), puis seulement à la fin retirer ce qui ne sert pas et
> ajouter nos propres fonctions. Les libellés et le code de DeployCraft sont entièrement les nôtres.

Légende : ✅ fait · 🟡 partiel · ⬜ à faire · ❌ écarté (décision du mainteneur) · ⛔ hors périmètre pour l'instant (à remplacer ou à écarter, décision du mainteneur)

Pour prendre une tâche : voir `CONTRIBUTING.md` (§ « Choisir une tâche »). Une ligne passe à ✅
dans la même PR que le code qui la réalise.

---

## 1. Fenêtre principale

| # | Fonction | État | Notes |
|---|---|---|---|
| 1.1 | Arbre de navigation à gauche : Paquets, Listes de cibles, Planifications, Tous les déploiements, File de réessai | ✅ | Paquets, déploiements, planifications (0.8.0). Listes de cibles et file de réessai écartées (choix du mainteneur) |
| 1.2 | Dossiers dans l'arbre (paquets et listes de cibles), sous-dossiers, glisser-déposer | ✅ | Dossiers et sous-dossiers de paquets (Ctrl+Maj+F), renommer, supprimer un dossier vide, glisser-déposer des paquets et des dossiers. Listes de cibles écartées (1.1) |
| 1.3 | Page d'un paquet : détails, étapes, déploiements du paquet, planifications attachées | 🟡 | Onglet Planifications en 0.8.0 |
| 1.4 | Page d'un dossier, page « plusieurs paquets sélectionnés » | ❌ | Écarté : choix du mainteneur |
| 1.5 | Renommer (F2), dupliquer, couper / copier / coller, supprimer dans l'arbre | ✅ | Paquets : couper, copier, coller (Ctrl+X / C / V) vers un dossier ; dossiers : couper / coller |
| 1.6 | Apparence d'un nœud de l'arbre (couleur / mise en forme) | ⬜ | Avec la reprise de l'apparence générale, après les fonctions (choix du mainteneur) |
| 1.7 | Menus Fichier / Édition / Affichage / Options / Aide + clic droit partout | ✅ | Affichage : accueil, déploiements, planifications, bibliothèque, paquets (Ctrl+0 à 4), rechercher un paquet (Ctrl+F), actualiser |
| 1.8 | Fenêtre des raccourcis clavier | ❌ | Écarté : choix du mainteneur (à revoir selon les retours d'usage) |
| 1.9 | Thèmes clair / sombre | ✅ | 0.7.0 : Clair, Sombre ou Selon Windows ; Préférences › Interface, appliqué à chaud |
| 1.10 | Page d'accueil | ✅ | Ouverte au démarrage : en ce moment, dernières 24 heures (échecs cliquables), prochaines planifications, bibliothèque, raccourcis |
| 1.11 | Import / export (paquets, dossiers, listes de cibles, planifications, réglages) | ✅ | Paquets : export / import JSON à nous, import de fichiers XML de paquets avec aperçu. Le reste écarté (choix du mainteneur) |
| 1.12 | Imprimer, aperçu avant impression, mise en page, enregistrer une grille en CSV | ❌ | Écarté : choix du mainteneur (le résultat dans la console suffit) |
| 1.13 | À propos | ✅ | Aide › À propos : versions console et service, compte connecté, licence, liens, copie des informations |

## 2. Paquets

| # | Fonction | État | Notes |
|---|---|---|---|
| 2.1 | Nom, description (texte riche), version, dossier | ✅ | Nom, version, description. Texte riche écarté (choix du mainteneur) ; dossiers : voir 1.2 |
| 2.2 | Fenêtre paquet à onglets : Détails, Conditions, Options, Hors ligne | ✅ | 0.10.0 ; l'étape a aussi Détails, Conditions, Options |
| 2.3 | Options du paquet : délai personnalisé, mode de copie, exécuter en tant que, réessai | ✅ | 0.10.0 : chaque option peut suivre les Préférences ; réessai dans l'onglet Hors ligne |
| 2.4 | Conditions du paquet : version d'OS + architecture, version PowerShell, utilisateur connecté, fichier, registre | ✅ | 0.10.0 : vérifiées par le runner ; non remplies = poste réussi sans rien exécuter |
| 2.5 | Sections d'étapes : avant / principales / après | ❌ | Écarté : choix du mainteneur, un échec arrête le paquet. Faites en 0.11.0, retirées ensuite |
| 2.6 | Par étape : titre, activée / désactivée, codes de succès | ✅ | Étape désactivée : « Ignoré », le paquet continue |
| 2.7 | Par étape : erreur → arrêter en échec / arrêter en succès / continuer | ✅ | Arrêt en succès : poste compté réussi, étapes suivantes « Ignoré » |
| 2.8 | Par étape : exécuter en tant que (compte de déploiement / compte de déploiement sur le bureau / système local / utilisateur connecté / hériter du paquet) | ✅ | Paramètre du paquet / compte de déploiement / Système local / utilisateur connecté, par « tranches » sur le poste. « Compte de déploiement sur le bureau » : écarté par le mainteneur (déploiements toujours silencieux, sans mot de passe sur le poste) |
| 2.9 | Par étape : mêmes conditions qu'au paquet | ✅ | 0.10.0 : non remplies = étape « Ignoré », le paquet continue |
| 2.10 | Copier / couper / coller / réordonner des étapes, y compris entre paquets | ✅ | 0.10.0 : presse-papiers (Ctrl+X / C / V), barre d'outils et clic droit |
| 2.11 | Avertissements : fichier introuvable, pas de paramètre silencieux, conflit avec le mode « pull » | ✅ | En haut de l'étape et rappelés à l'enregistrement ; fichiers vérifiés par le service avec l'identifiant par défaut |
| 2.12 | Dupliquer, exporter un paquet | ✅ | Export avec ses paquets imbriqués |
| 2.13 | Dossiers de paquets publics / privés | ❌ | Écarté : choix du mainteneur (une seule console) |

### Types d'étapes

| # | Étape | État | Détails à couvrir |
|---|---|---|---|
| 2.20 | Installer | ✅ | exe, bat/cmd, msi, msp, msu, ps1, vbs, reg ; MSI installer / réparer / désinstaller, redémarrage, silencieux ; ligne de commande générée ou personnalisée ; journal MSI ; fichiers additionnels ; garder le fichier |
| 2.21 | Commande | ✅ | Fichiers additionnels |
| 2.22 | PowerShell | ✅ | Fichiers additionnels. Écartés : ligne de commande personnalisée (choix du mainteneur) ; alerte `Write-Host` inutile (sa sortie est bien reprise dans le résultat) |
| 2.23 | Copie de fichiers | ✅ | Fichier ou dossier (push ou pull), motifs, sous-dossiers, écraser ou garder, réussir malgré les fichiers impossibles à remplacer ; copie sur le poste par robocopy |
| 2.24 | Redémarrer | ✅ | Message, délai (30 s à 1 h), arrêt au lieu du redémarrage ; attend le retour du poste (délai max de l'étape) avant l'étape suivante |
| 2.25 | Pause | ✅ | N secondes (1 s à 24 h), interrompue par l'annulation |
| 2.26 | Message | ✅ | Titre, texte, durée d'affichage (0 = jusqu'au clic), attendre le clic sur OK avant l'étape suivante ; affiché dans chaque session ouverte |
| 2.27 | Déconnexion de l'utilisateur | ❌ | Écarté : choix du mainteneur (outil de déploiement silencieux) |
| 2.28 | Paquet imbriqué | ✅ | Remplacé par ses étapes au lancement ; conditions combinées, boucles et plus de 5 niveaux refusés |
| 2.29 | Scan d'inventaire | ⛔ | Nécessite un inventaire des postes |

## 3. Déployer une fois

| # | Fonction | État | Notes |
|---|---|---|---|
| 3.1 | Plusieurs paquets, ordonnés | ✅ | 0.6.0 : un déploiement par paquet ; sur un poste, le suivant attend la fin du précédent (même en échec) |
| 3.2 | Cibles : saisie, collage, Active Directory | ✅ | Arbre AD dépliable (0.5.1) |
| 3.3 | Cibles : liste de cibles, fichier texte | ✅ | Fichier texte en 0.6.0. Listes de cibles écartées (choix du mainteneur) : les postes se choisissent dans l'AD |
| 3.4 | Résolution DNS des noms saisis (vérification en direct) | ✅ | Déployer une fois : chaque poste ajouté est cherché dans le DNS du serveur (sablier, coche ou croix ; nom complet en info-bulle) ; n'empêche pas de déployer |
| 3.5 | Démarrer à l'étape N, **par poste** | ✅ | 0.6.0 ; porte sur le premier paquet. « Reprendre à l'étape en échec » = un seul déploiement |
| 3.6 | Options : compte, mode de copie, exécuter en tant que | ✅ | 0.6.0 ; identifiant au choix en 0.9.0 ; « Paramètre du paquet » par défaut. Compte de déploiement / Système local / Utilisateur connecté ; push / pull (UNC) |
| 3.7 | Options : notifications, mémoriser les cibles, prioritaire | ✅ | Mémoriser + prioritaire en 0.6.0. Notifications écartées (choix du mainteneur) |
| 3.8 | Onglet Hors ligne : ping avant, Wake-on-LAN, file de réessai | ✅ | 0.10.0 : réglé dans le paquet (et les Préférences). Dans Déployer une fois : écarté (choix du mainteneur, les postes sont allumés avant ; le réveil existe) |
| 3.9 | Bouton « Planifier » (crée une planification à partir de la fenêtre) | ✅ | 0.8.0 : reprend paquets, cibles, options |
| 3.10 | Cibles depuis un inventaire des postes | ⛔ | |

## 4. Suivi des déploiements

| # | Fonction | État | Notes |
|---|---|---|---|
| 4.1 | Liste maître / détail : déploiements, postes, étapes, sortie | ✅ | |
| 4.2 | Statuts de poste détaillés : en attente, ping, réveil (WoL), connexion, copie, exécution, reconnexion, annulation, finalisation… | ✅ | Étape en cours affichée (attente de créneau, réveil, résolution du nom, copie, exécution, poste injoignable, redémarrage, nettoyage) ; statut « Annulé » |
| 4.3 | Compteurs par déploiement (en attente / en cours / réussis / échecs) + « partiellement échoué », « annulé » | ✅ | Colonnes En attente, En cours, Réussis, Échecs, Annulés ; statuts « Partiel » et « Annulé » |
| 4.4 | Progression de copie (% et débit), note par étape, compte d'exécution par étape | ⬜ | |
| 4.5 | Annuler, redéployer, redéployer les échecs, reprendre à l'étape en échec | ✅ | |
| 4.6 | Prioriser un poste en attente | ❌ | Écarté : choix du mainteneur |
| 4.7 | Créer une planification depuis un déploiement | ✅ | Clic droit sur un déploiement : paquet, postes et options repris, déclencheurs à choisir |
| 4.8 | Nettoyage automatique de l'historique après N jours | ✅ | Préférences › Déploiement : désactivé d'usine, de 1 à 3650 jours ; au démarrage du service puis une fois par jour ; seuls les déploiements terminés sont supprimés (jamais en attente ou en cours, ni l'historique des planifications) |
| 4.9 | Fichier de sortie complet téléchargeable par étape | ✅ | Détail des étapes : copier, ouvrir dans le Bloc-notes ou enregistrer la sortie (avec le journal Windows Installer) |

## 5. Planifications

| # | Fonction | État | Notes |
|---|---|---|---|
| 5.1 | Page « Toutes les planifications », fenêtre planification | ✅ | 0.8.0 : grille + déploiements de la planification ; fenêtre à onglets Déclencheurs / Cibles / Paquets / Options / Historique des cibles |
| 5.2 | Déclencheurs : une fois, quotidien, hebdomadaire, mensuel (jour N ou « le 1er lundi »), intervalle | ✅ | 0.8.0 : plusieurs par planification, plage horaire pour l'intervalle, fin facultative, « dernier jour / dernier lundi » |
| 5.3 | Déclencheur « heartbeat » (poste qui revient en ligne) | ⛔ | Nécessite un inventaire des postes ; un équivalent maison reste possible |
| 5.4 | Paquets ordonnés, cibles, options (compte, copie, exécuter en tant que, délai, notifications, hors ligne, réessai) | ✅ | 0.8.0 : paquets, cibles, copie, exécuter en tant que, délai d'attente ; identifiant en 0.9.0. Notifications et hors ligne écartés (choix du mainteneur) |
| 5.5 | Arrêter après N échecs, arrêter après un succès (par poste) | ✅ | 0.8.0 : historique des cibles par poste × paquet, effaçable |
| 5.6 | Activer / désactiver, prochaine et dernière exécution, dernière erreur | ✅ | 0.8.0 : « Dernier résultat » |
| 5.7 | Attacher / détacher à un paquet, dupliquer, déployer maintenant | ✅ | Dupliquer, démarrer maintenant ; clic droit sur un paquet › Ajouter à une planification…, et Retirer depuis l'onglet Planifications du paquet |

## 6. Listes de cibles et filtres

| # | Fonction | État | Notes |
|---|---|---|---|
| 6.1 | Listes de cibles réutilisables (Ctrl+Maj+T), dans des dossiers | ❌ | Écarté : choix du mainteneur (les postes se choisissent dans l'AD) |
| 6.2 | Sources : postes, Active Directory (OU, groupe, conteneur), autre liste de cibles | ❌ | Écarté : choix du mainteneur |
| 6.3 | Filtres globaux d'inclusion / exclusion (nom, IPv4, IPv6) | ❌ | Écarté : choix du mainteneur (à reconsidérer bien plus tard, selon les retours) |

## 7. File de réessai (hors ligne)

| # | Fonction | État | Notes |
|---|---|---|---|
| 7.1 | Ping avant déploiement, Wake-on-LAN | ✅ | 0.10.0 : test DNS + partage toujours fait ; réveil avec l'adresse MAC apprise au dernier déploiement |
| 7.2 | File de réessai : activée, nombre d'essais max, intervalle, expiration ; héritage préférences → paquet → déploiement | ✅ | Retirée en 0.5.0, remise en 0.10.0 comme **option désactivée par défaut** : nombre d'essais et intervalle, préférences → paquet ; perdue au redémarrage du service. Expiration écartée (choix du mainteneur) |
| 7.3 | Page de la file : poste × paquet, essais restants, prochain essai, « essayer maintenant », supprimer | ❌ | Écarté : choix du mainteneur (hors ligne non utilisé ; « Redéployer sur les postes en échec » existe) |

## 8. Préférences

| # | Section | État | Contenu |
|---|---|---|---|
| 8.1 | Comptes | 🟡 | 0.9.0 : Options › Identifiants, plusieurs comptes (domaine ou local `.\utilisateur`), description, compte par défaut, test auprès du domaine ; choisis dans Déployer une fois et les planifications. Reste : LAPS (mot de passe admin local de chaque poste lu dans l'AD, fonction Microsoft), après la v1 (#106) |
| 8.2 | Variables | ❌ | Écarté : choix du mainteneur (ajoutées en 0.12.0, retirées ensuite ; un `$(…)` reste tel quel) |
| 8.3 | Déploiement | ✅ | 0.10.0 : exécuter en tant que, délai par étape, comportement hors ligne par défaut ; nettoyage de l'historique (4.8) |
| 8.4 | Performance | ✅ | 0.10.0 : mode de copie par défaut. Postes en parallèle réglables (1 à 100, 8 par défaut) et limite de débit des copies vers les postes (Mbit/s, partagée par toutes les copies en cours) dans Préférences › Déploiement |
| 8.5 | Dépôt de fichiers (`$(Repository)`) | ❌ | Écarté : choix du mainteneur (les fichiers sont sur un partage du serveur) |
| 8.6 | Filtres de cibles | ❌ | Écarté : choix du mainteneur (cf. 6.3) |
| 8.7 | Serveur de messagerie + notifications | ❌ | Écarté : choix du mainteneur |
| 8.8 | Interface, impression | ✅ | 0.7.0 : page Interface (thème) ; fenêtre des Préférences en liste de pages. Impression écartée (choix du mainteneur) |
| 8.9 | Service en arrière-plan | ⛔ | Compte du service : en attente, décision du mainteneur (16/10) ; repris seulement si le besoin se présente. Aujourd'hui le service tourne en Système local et utilise le compte de déploiement pour les postes et les partages |
| 8.10 | Utilisateurs de la console, sessions actives | ✅ | Authentification Windows ; administrateurs du serveur + liste de comptes et groupes |
| 8.11 | Mode serveur central (local / serveur / client) | ❌ | Écarté : choix du mainteneur (console et service sur la même machine) |
| 8.12 | Service sur la cible | ❌ | Écarté : choix du mainteneur |
| 8.13 | Proxy, base de données, journalisation | ❌ | Écarté : choix du mainteneur |
| 8.14 | Mises à jour automatiques | 🟡 | La console cherche une nouvelle version stable dans nos Releases au démarrage (et par Aide) et prévient ; l'installation reste manuelle, en administrateur sur le serveur |

## 9. Rapports et notifications

| # | Fonction | État | Notes |
|---|---|---|---|
| 9.1 | Rapports : déploiements, paquets, planifications, listes de cibles | ❌ | Écarté : choix du mainteneur |
| 9.2 | Notification e-mail après déploiement ou planification : destinataires (à / cc / cci), objet, corps, pièce jointe, détails, seulement si changement, pas pour les réessais | ❌ | Écarté : choix du mainteneur |

## 10. Divers

| # | Fonction | État | Notes |
|---|---|---|---|
| 10.1 | Ligne de commande (déployer un paquet sur des cibles, lister…) | ❌ | Écarté : choix du mainteneur |
| 10.2 | Assistant de premier démarrage | ✅ | Ouvert tant que le serveur n'a ni identifiant ni paquet, et par Aide › Assistant de démarrage : identifiant, dossier de la bibliothèque, premier paquet, préférences |
| 10.3 | Aide intégrée | ✅ | Aide › Aide de DeployCraft (F1, ouverte sur le sujet de la vue affichée) : 9 sujets, recherche |
| 10.4 | Bibliothèque de paquets en ligne, mise à jour automatique des paquets | 🟡 | À partir du catalogue public winget : tout le catalogue avec recherche, téléchargement vérifié, paquet créé, mise à jour en un clic. Reste : mise à jour automatique (immédiate ou différée) |
| 10.5 | Intégration d'un inventaire des postes | ⛔ | |
| 10.6 | Licence, essai, statistiques d'usage | ⛔ | Sans objet |
