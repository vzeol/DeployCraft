# Sécurité

DeployCraft tourne avec des droits élevés sur un serveur et sur les postes d'un parc : une faille peut avoir des
conséquences sérieuses. Merci de la signaler **en privé**.

## Signaler une faille

Utilisez le signalement privé de GitHub : onglet **Security** › **Report a vulnerability**
([lien direct](https://github.com/vzeol/DeployCraft/security/advisories/new)).
N'ouvrez pas d'issue publique.

Indiquez si possible : la version concernée, ce qu'un attaquant peut obtenir, et comment le reproduire.
Retirez toute information propre à votre environnement (noms de domaine, de serveurs, de comptes).

Vous recevrez une réponse dès que possible. Le correctif est publié dans une nouvelle version, puis la faille est
décrite publiquement, avec votre nom si vous le souhaitez.

## Versions suivies

Seule la dernière version publiée reçoit les correctifs de sécurité.

## Comment DeployCraft protège votre parc

- **Accès à la console** : authentification Windows ; réservé aux administrateurs du serveur et aux comptes ou groupes
  autorisés dans Options › Utilisateurs de la console.
- **Identifiants de déploiement** : saisis dans la console, chiffrés sur le serveur (protection des données de Windows,
  portée machine) ; jamais copiés sur les postes. Le dossier de données est réservé à Système et aux administrateurs.
- **Sur les postes** : le runner est copié dans le partage `ADMIN$`, lancé comme service temporaire, puis supprimé.
  Rien ne reste installé.
- **Mises à jour** : l'installeur d'une nouvelle version n'est lancé qu'après vérification de son empreinte SHA-256.
