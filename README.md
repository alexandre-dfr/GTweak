# GamingTweaker

Application Windows portable (un seul `GamingTweaker.exe`) qui règle un PC de jeu. Vous cochez des options dans cinq menus (Optimisation, Télémétrie, Bloatware, Nettoyage, Mises à jour) ; seules les options cochées sont exécutées. Interface en français. Windows 10 (1903 ou plus récent) et Windows 11, x64.

## Lancer

1. Copiez `GamingTweaker.exe` (et `GamingTweaker.exe.config`) où vous voulez, par exemple sur une clé USB.
2. Double-cliquez : Windows demande les droits administrateur (obligatoire pour modifier le système).
3. Cochez les options, cliquez sur **Aperçu** pour voir exactement ce qui sera fait, puis sur **LANCER**.

Rien n'est installé. Les capteurs matériels (menu Informations) ne sont chargés qu'à la demande.

## Sécurité

- Seules les options cochées s'exécutent ; chaque option est classée Sûr, Avancé ou Risque. « Recommandé » ne coche que des options Sûr, et jamais celles que le catalogue en exclut (plan d'alimentation sur portable).
- Le catalogue ne touche à aucune protection de sécurité (Defender, Spectre/Meltdown, VBS, BitLocker, Windows Update) et ne contient que des réglages à effet réel en jeu ou sur l'allègement de Windows ; les réglages placebo ou de pure apparence ont été retirés (voir `docs/revue-code-2026-11-11.md`).
- Un point de restauration Windows est créé avant les options réversibles (si Windows l'autorise ; sinon vous êtes prévenu).
- Aucun redémarrage automatique : si un redémarrage est nécessaire, le rapport l'indique.
- Les profils (`.json`) ne contiennent que des identifiants d'options, jamais de commande.
- L'exe n'est pas signé : SmartScreen ou un antivirus peut le signaler. Il modifie le système, c'est normal.

## Profils intégrés

Trois profils sont fournis en tête de la liste des profils, en lecture seule :

- **Gaming léger (intégré)** : options Sûr d'Optimisation, de Télémétrie et de Nettoyage.
- **Gaming max (intégré)** : options Sûr et Avancé d'Optimisation, plus les options Sûr de Télémétrie et de Nettoyage.
- **Confidentialité (intégré)** : toute la Télémétrie, Sûr et Avancé.

Aucun ne contient d'option Risqué, d'option à liste (services, démarrage : leur contenu se choisit sur votre PC) ni d'option que « Recommandé » ne coche pas. Pour en modifier un ou le planifier, enregistrez-le sous votre propre nom avec « Sauver ».

## Annuler

Chaque modification réversible est notée dans un journal. Un journal applicatif (`app.log`, dans le dossier de la machine sous le dossier de données protégé) garde la trace des sessions, de la routine et des erreurs internes pour le support ; il ne contient aucune donnée personnelle. L'historique permet d'annuler une session ou une option, et **Tout restaurer** revient à l'état d'avant. Si une valeur a changé entre-temps, l'application le signale avant de l'écraser. Les options non réversibles ou à annulation partielle sont indiquées dans l'aperçu.

## Routine planifiée

**Planifier** crée la tâche hebdomadaire `\GamingTweaker\Routine` qui applique le profil choisi sans interface (`GamingTweaker.exe --profile <fichier> --silent`). Codes de sortie : 0 = tout est appliqué ou ignoré, 1 = au moins un échec, 2 = exécution impossible. **Supprimer la routine** retire la tâche. Les données de la routine sont dans le dossier de données protégé de GamingTweaker, dossier protégé (propriétaire Administrateurs).

## Désinstaller

L'application est portable : supprimer `GamingTweaker.exe` suffit pour le programme. Pour retirer aussi la routine planifiée et le dossier de données protégé (`%ProgramFiles%\GamingTweaker` : sessions, journaux d'annulation, quarantaine), lancer en administrateur :

```bash
GamingTweaker.exe --uninstall
```

Sans `--confirm`, la commande affiche seulement ce qui serait supprimé. Les réglages appliqués ne sont pas annulés par la désinstallation : passer d'abord par **Tout restaurer** ou par l'annulation des sessions dans l'historique, car les journaux d'annulation disparaissent avec le dossier de données.

## Tests et vérification

- `dotnet test` : tests automatiques (aucune modification du système en dehors de `HKCU\Software\GamingTweakerTest` et du dossier temporaire).
- `tools/sandbox/cycle.wsb` : cycle détecter, appliquer, annuler dans Windows Sandbox (voir `tools/sandbox/start-cycle.ps1`) ; le résultat est écrit dans `tools/sandbox/resultats/cycle.json`. L'option `--cycle-test` est refusée hors du compte du bac à sable.
- `docs/checklist-premier-pc.md` : liste de contrôle à passer sur le premier PC réel.

## Licences

Les réglages repris de projets tiers sont réécrits ; les sources et licences sont dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
