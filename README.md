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

## Compteur en jeu (FPS, CPU, GPU, RAM)

Le bouton en forme de compteur, en haut à droite (à côté du thème), ouvre les réglages du compteur affiché par-dessus le jeu. Il est désactivé par défaut. **Ctrl+Maj+F11** l'affiche ou le masque pendant une partie.

- **Contenu**, chaque élément activable séparément :
  - images par seconde de la fenêtre au premier plan ;
  - temps d'image et « 1 % bas » (99e centile du temps d'image sur 10 s) ;
  - courbe des FPS sur 10, 30 ou 60 s ;
  - charge du processeur, de la carte graphique et de la mémoire en pourcentage.
- **Position** : un coin de l'écran principal, ou **n'importe où** : tant que la fenêtre des réglages est ouverte, le compteur est affiché avec un cadre pointillé et se déplace à la souris, sur n'importe quel écran ; tirez le coin ◢ du cadre (ou Ctrl+molette) pour l'agrandir ou le réduire (60 à 300 %). Il garde cette place et cette taille.
- **Apparence** : taille, couleur (7 couleurs proposées ou une couleur libre `#RRVVBB`), opacité du fond. Largeur fixe et police à chasse fixe : le compteur ne bouge pas quand les chiffres changent ; il est rafraîchi deux fois par seconde.
- **Fonctionnement** : les FPS viennent des événements publiés par Windows (ETW, comme PresentMon) : « Present » de DirectX 9 à 12 en priorité, sinon le noyau graphique (DxgKrnl), qui voit aussi les images des jeux Vulkan et OpenGL. La source est indiquée sous les FPS ; rien n'est injecté dans le jeu, le compteur est donc compatible avec les anti-triche. Processeur, mémoire et carte graphique sont mesurés comme dans le Gestionnaire des tâches. La surimpression laisse passer les clics et ne prend jamais le clavier. Rien ne tourne quand le compteur est masqué.
- **Limites** : la mesure Vulkan/OpenGL par le noyau graphique n'a pas pu être vérifiée par la CI (pas de carte graphique) ; si un jeu Vulkan ou OpenGL affiche « — », signalez-le. Le compteur s'affiche en plein écran sans bordure et en fenêtre, pas en plein écran exclusif. Comme toute surimpression, il peut empêcher Windows d'utiliser l'affichage direct (« independent flip ») d'un jeu en plein écran sans bordure. La latence d'entrée (« input lag ») n'est pas affichée : elle ne se mesure correctement qu'avec du matériel dédié.

Les réglages sont dans `compteur.json`, dans le dossier de la machine.

## Partager un profil

Le bouton **Partager** (à côté de « Recharger ») propose :

- **Exporter la sélection…** : enregistre ce qui est coché dans un fichier `.json` de votre choix (par défaut sur le Bureau).
- **Importer un profil…** : sur l'autre PC, choisissez ce fichier. Il est vérifié comme un profil enregistré (version, identifiants d'options, noms d'éléments), puis réécrit dans le dossier des profils et chargé. Les options Risqué demandent la même confirmation qu'au chargement. Si un profil du même nom existe, l'appli demande avant de le remplacer. Un nom de profil intégré reçoit le suffixe « (importé) ».

Comme tout profil, le fichier ne contient que des identifiants d'options et des noms d'éléments, jamais de commande. Les options incompatibles avec l'autre PC sont ignorées et signalées.

## Nouvelle version

Au démarrage de l'interface, l'appli demande à GitHub la dernière release publiée (`api.github.com`, sans identifiant de la machine ni de l'utilisateur ; les pré-versions de test sont ignorées). Si elle est plus récente, un bandeau propose « Voir la nouvelle version », qui ouvre la page de la release dans le navigateur. **Rien n'est téléchargé ni installé automatiquement.** Hors ligne, ou si GitHub ne répond pas en 10 s, rien ne s'affiche. La routine planifiée et le mode `--silent` ne font pas cette vérification.

## Réglages du pilote NVIDIA

Avec une carte NVIDIA, le menu Optimisation affiche un groupe « Pilote NVIDIA » : mode de faible latence Ultra, gestion de l'alimentation en performances maximales, cache des shaders illimité, filtrage des textures en hautes performances, synchronisation verticale désactivée. Chaque option se coche séparément (niveau Avancé, jamais cochée par « Recommandé » ni par un profil intégré). Elles modifient les paramètres globaux du pilote par son interface `nvapi64.dll`, installée avec le pilote : rien n'est téléchargé. La valeur d'avant est notée dans le journal, et chaque option s'annule seule.

## Diagnostic jeu

Le menu « Diagnostic » (lecture seule) repère ce qui bride réellement les performances : écran utilisé sous sa fréquence maximale, mémoire sous sa vitesse nominale (XMP/EXPO) ou en simple canal, Mode Jeu de Windows et HAGS désactivés, plan d'alimentation, batterie, disque système presque plein, pilote graphique ancien, enregistrement en arrière-plan, programmes au démarrage, écran HDR non activé, écran à fréquence variable (FreeSync, G-SYNC). Chaque point propose la page des Paramètres ou l'option qui le corrige.

## Mesures avant / après

Historique > Mesures : un relevé de 30 s (temps DPC et d'interruptions, processeur au repos, services, processus, mémoire disponible, programmes au démarrage). Faites-en un avant d'optimiser et un après, puis « Avant / après » : chaque mesure est marquée « mieux », « moins bien » ou « stable ».

## Mode Jeu

Le menu Mode Jeu se lit de haut en bas :

- **État** : actif ou inactif, ce que le Mode Jeu va faire en une phrase, et le bouton **Activer maintenant** (ou **Rétablir** quand il est actif).
- **1. Ce que fait le Mode Jeu** : plan d'alimentation Performances élevées, libération de la mémoire, profil du pilote NVIDIA, services Windows à arrêter, programmes à fermer (listes repliées, avec le nombre de cases cochées). Ces choix sont **enregistrés** : ils sont recochés au prochain lancement, et ce sont les mêmes pour le Mode Jeu manuel et automatique. Un service ou programme choisi qui ne tourne pas en ce moment reste affiché, coché.
- **2. Mode automatique (facultatif)** :
  - **Vos jeux** : ajoutez des jeux depuis Steam, Epic Games ou un `.exe`. Pour un jeu Steam ou Epic, tout programme lancé depuis son dossier compte (les lanceurs, outils de plantage et anti-triche sont ignorés).
  - **Fonctionnement** : quand un jeu de la liste démarre, le Mode Jeu s'active ; quand vous quittez le dernier, tout est rétabli. Une notification le signale à chaque fois. Un Mode Jeu activé à la main n'est jamais rétabli automatiquement, et un Mode Jeu rétabli à la main pendant une partie n'est pas réactivé avant la partie suivante.
  - **Démarrer GamingTweaker avec Windows** : crée la tâche `\GamingTweaker\Demarrage`, lancée à l'ouverture de session avec les droits administrateur (sans invite). Elle démarre `GamingTweaker.exe --tray` depuis une copie placée dans `%ProgramFiles%\GamingTweaker\Demarrage`, jamais depuis l'emplacement de l'exe portable. `--uninstall` la retire aussi. Sans cette case, le mode automatique ne fonctionne que tant que GamingTweaker est ouvert : un rappel s'affiche.
  - **Zone de notification** : tant que le mode automatique ou le démarrage avec Windows est actif, fermer la fenêtre la réduit en icône près de l'horloge. Clic droit sur l'icône pour rouvrir ou quitter. En quittant, un Mode Jeu activé automatiquement est rétabli.

## En cas d'erreur

Une erreur inattendue n'est jamais silencieuse : un message l'indique et un rapport est enregistré dans `%LOCALAPPDATA%\GamingTweaker\erreurs.log` (lisible sans droits administrateur), avec le détail technique et les dernières étapes du journal. Envoyez ce fichier pour faire corriger le problème. Une erreur au démarrage ferme l'application après le message ; une erreur plus tard la laisse ouverte.

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

## Publication (CI)

Le workflow `.github/workflows/build.yml` compile l'exe sur Windows, lance les tests puis le cycle réel des options sur la machine jetable de GitHub. **L'exe n'est publié que si tout est vert** :

| Déclencheur | Publication (onglet Releases) |
|---|---|
| push sur `main` | pré-version `test-build` |
| push sur une autre branche | pré-version `test-<branche>`, supprimée automatiquement avec la branche |
| tag `vX.Y.Z` | release `GamingTweaker X.Y.Z` (c'est elle que l'appli propose en mise à jour) |

Un build en échec ne remplace jamais l'exe déjà publié ; le rapport du cycle réel, les captures et le résultat des tests vont dans une pré-version `<canal>-echec`, sans exe. Elle est supprimée au build vert suivant.

Pour publier une version : mettre `<Version>` à jour dans `src/GamingTweaker/GamingTweaker.csproj` (avec `AssemblyVersion` et `FileVersion`), fusionner dans `main`, puis :

```bash
git tag v0.9.2
```

```bash
git push origin v0.9.2
```

Le workflow refuse un tag qui ne correspond pas à `<Version>`.

### Signature de l'exe

Un exe non signé qui demande les droits administrateur est bloqué par SmartScreen et souvent signalé par les antivirus. Le workflow signe l'exe (Authenticode SHA-256, horodaté) si deux secrets existent dans le dépôt (Settings > Secrets and variables > Actions) :

- `SIGN_PFX_BASE64` : le certificat de signature de code `.pfx`, encodé en base64 (`[Convert]::ToBase64String([IO.File]::ReadAllBytes('cert.pfx'))` dans PowerShell) ;
- `SIGN_PFX_PASSWORD` : son mot de passe.

Sans ces secrets, l'exe est publié non signé et le résumé du build le signale. Où obtenir un certificat : un certificat de signature de code « open source » (par exemple Certum) est la voie la moins chère pour un projet AGPL ; un certificat OV classique fonctionne aussi. Les certificats récents sont souvent livrés sur clé matérielle ou dans un service cloud, sans fichier `.pfx` exportable : dans ce cas, l'étape « Signer l'exe » est à adapter à l'outil du fournisseur (par exemple Azure Trusted Signing). Un certificat neuf n'efface pas tout de suite l'avertissement SmartScreen : la réputation se construit avec les téléchargements.

## Licence

GamingTweaker est distribué sous licence **GNU AGPL-3.0** (fichier [LICENSE](LICENSE)) : vous pouvez l'utiliser, l'étudier, le modifier et le redistribuer, à condition de fournir le code source sous la même licence. Ce choix découle des réglages repris de privacy.sexy (AGPL-3.0) ; ceux de WinUtil (MIT) sont compatibles.

## Licences tierces

Les réglages repris de projets tiers sont réécrits ; les sources et licences sont dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
