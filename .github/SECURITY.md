# Sécurité

GamingTweaker s'exécute en administrateur et modifie Windows : une faille peut avoir des conséquences sérieuses.

## Signaler une faille

**Ne publiez pas de faille dans une issue.** Utilisez le signalement privé de GitHub : onglet **Security** du dépôt, puis **Report a vulnerability**
(<https://github.com/alexandre-dfr/GTweak/security/advisories/new>).

Indiquez la version (bas de l'accueil ou menu Paramètres), la version de Windows, les étapes pour reproduire et l'impact (élévation de privilèges, exécution
de commande depuis un profil, suppression de fichiers hors des dossiers prévus…). Une réponse est donnée sous 7 jours.

## Versions suivies

Seule la dernière release publiée (onglet Releases, tag `vX.Y.Z`) reçoit les correctifs. Les pré-versions `test-*` servent aux essais.

## Garde-fous existants

- Les profils (`.json`) ne contiennent que des identifiants d'options et des noms d'éléments, validés au chargement, jamais de commande.
- Les données de l'appli sont dans un dossier protégé (propriétaire Administrateurs) ; les suppressions du Nettoyage sont limitées à des dossiers autorisés.
- Aucune option ne désactive une protection de sécurité (Defender, Spectre/Meltdown, VBS, BitLocker, mises à jour de sécurité) : un test le vérifie.
- Revue de sécurité : `docs/revue-securite-2026-11-17.md`.
