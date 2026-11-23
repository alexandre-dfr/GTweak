# Confidentialité

GamingTweaker ne collecte rien : aucune télémétrie, aucune statistique d'utilisation, aucun compte.

## Connexions réseau

L'appli ne se connecte à Internet que dans ces cas, tous visibles :

| Quand | Vers | Ce qui est envoyé |
|---|---|---|
| Au démarrage de l'interface (pas en mode `--silent` ni par la routine planifiée) | `api.github.com` | Une requête anonyme pour connaître la dernière release. Aucun identifiant de la machine ni de l'utilisateur. |
| Clic sur « Mettre à jour » | `github.com` (fichiers des releases) | Le téléchargement de `GamingTweaker.exe` et de sa signature, rien d'autre. |
| Options du menu Mises à jour que vous cochez, et ouverture de leur liste | Windows Update, `winget` (sources de vos applis), site de NVIDIA (recherche du dernier pilote pour votre carte) | Ce que ces outils envoient eux-mêmes ; pour NVIDIA, le modèle de la carte graphique, nécessaire à la recherche du pilote. |
| Bloatware : réinstallation d'une appli lors d'une annulation | Microsoft Store | Ce que le Store envoie pour installer l'appli. |
| « Activer les capteurs » (menu Informations), si vous acceptez l'installation de PawnIO | `winget` (ou la page officielle de PawnIO dans le navigateur) | Ce que `winget` envoie pour télécharger le pilote. |

## Données enregistrées sur le PC

Tout reste sur le PC : profils, journaux d'annulation, rapports de session et `app.log` dans le dossier de données protégé
(`%ProgramFiles%\GamingTweaker`) ; `erreurs.log` et `demarrage.log` dans `%LOCALAPPDATA%\GamingTweaker` ; réglages du compteur dans `compteur.json`.
Ces fichiers ne contiennent pas de donnée personnelle (pas de mot de passe, pas de contenu de fichiers). Ils ne sont envoyés à personne :
c'est vous qui choisissez de les joindre à une demande d'aide.

`GamingTweaker.exe --uninstall --confirm` supprime le dossier de données et la routine planifiée.
