# GamingTweaker

Application Windows portable (un seul `GamingTweaker.exe`) qui règle un PC de jeu. Vous cochez des options dans cinq menus (Optimisation, Télémétrie, Bloatware, Nettoyage, Mises à jour) ; seules les options cochées sont exécutées. Interface en français. Windows 10 (1903 ou plus récent) et Windows 11, x64.

## Lancer

1. Copiez `GamingTweaker.exe` (et `GamingTweaker.exe.config`) où vous voulez, par exemple sur une clé USB.
2. Double-cliquez : Windows demande les droits administrateur (obligatoire pour modifier le système).
3. Cochez les options, cliquez sur **Aperçu** pour voir exactement ce qui sera fait, puis sur **LANCER**.

Rien n'est installé. Les capteurs matériels (menu Informations) ne sont chargés qu'à la demande.

## Sécurité

- Seules les options cochées s'exécutent ; chaque option est classée Sûr, Avancé ou Risque.
- Un point de restauration Windows est créé avant les options réversibles (si Windows l'autorise ; sinon vous êtes prévenu).
- Aucun redémarrage automatique : si un redémarrage est nécessaire, le rapport l'indique.
- Les profils (`.json`) ne contiennent que des identifiants d'options, jamais de commande.
- L'exe n'est pas signé : SmartScreen ou un antivirus peut le signaler. Il modifie le système, c'est normal.

## Annuler

Chaque modification réversible est notée dans un journal. L'historique permet d'annuler une session ou une option, et **Tout restaurer** revient à l'état d'avant. Si une valeur a changé entre-temps, l'application le signale avant de l'écraser. Les options non réversibles ou à annulation partielle sont indiquées dans l'aperçu.

## Routine planifiée

**Planifier** crée la tâche hebdomadaire `\GamingTweaker\Routine` qui applique le profil choisi sans interface (`GamingTweaker.exe --profile <fichier> --silent`). Codes de sortie : 0 = tout est appliqué ou ignoré, 1 = au moins un échec, 2 = exécution impossible. **Supprimer la routine** retire la tâche. Les données de la routine sont dans le dossier de données protégé de GamingTweaker, dossier protégé (propriétaire Administrateurs).

## Tests et vérification

- `dotnet test` : tests automatiques (aucune modification du système en dehors de `HKCU\Software\GamingTweakerTest` et du dossier temporaire).
- `tools/sandbox/cycle.wsb` : cycle détecter, appliquer, annuler dans Windows Sandbox (voir `tools/sandbox/start-cycle.ps1`) ; le résultat est écrit dans `tools/sandbox/resultats/cycle.json`. L'option `--cycle-test` est refusée hors du compte du bac à sable.
- `docs/checklist-premier-pc.md` : liste de contrôle à passer sur le premier PC réel.

## Licences

Les réglages repris de projets tiers sont réécrits ; les sources et licences sont dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
