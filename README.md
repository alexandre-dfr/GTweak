# GamingTweaker

Application Windows portable (un seul `GamingTweaker.exe`) qui règle un PC de jeu. Vous cochez des options dans cinq menus (Optimisation, Télémétrie, Bloatware, Nettoyage, Mises à jour) ; seules les options cochées sont exécutées. Interface en français. Windows 10 (1903 ou plus récent) et Windows 11, x64.

## Lancer

GamingTweaker est uniquement portable : il n'a pas d'installateur et n'ajoute aucun programme au PC. Téléchargez la **dernière version stable** : [Releases > Latest](https://github.com/alexandre-dfr/GTweak/releases/latest).

Les pré-versions `test-build` et `test-<branche>` sont des builds de développement, recompilés à chaque modification : chacun est un nouveau fichier inconnu de Windows, que SmartScreen signale à coup sûr. Une version stable reste le même fichier pour tout le monde : sa réputation SmartScreen se construit au fil des téléchargements et l'avertissement finit par disparaître.

Ensuite :

1. Copiez `GamingTweaker.exe` où vous voulez, par exemple sur une clé USB. C'est le seul fichier : aucune dépendance, aucun fichier de configuration (.NET Framework 4.8 est déjà dans Windows 10 1903+ et Windows 11).
2. Double-cliquez : Windows demande les droits administrateur (obligatoire pour modifier le système).
3. Cochez les options, cliquez sur **Aperçu** pour voir exactement ce qui sera fait, puis sur **LANCER**. Au premier lancement, un encadré de l'accueil résume ces étapes (bouton « Compris » pour le masquer, « Réafficher le guide » dans Paramètres).

Rien n'est installé. Les capteurs matériels (menu Informations) ne sont chargés qu'à la demande.

### Avertissement SmartScreen

L'exe n'est pas signé : au premier lancement d'un exe téléchargé par le navigateur, Windows affiche « Windows a protégé votre ordinateur ». La mise à jour intégrée l'évite. Pour un exe téléchargé à la main :

- **Avant le premier lancement** : ouvrez PowerShell et collez cette commande (elle retire la marque « téléchargé depuis Internet », comme le bouton « Débloquer » des propriétés du fichier) :

```powershell
Unblock-File "$env:USERPROFILE\Downloads\GamingTweaker.exe"
```

- **Sinon** : « Informations complémentaires » puis « Exécuter quand même », une seule fois. GamingTweaker retire alors lui-même cette marque de son exe : l'avertissement ne revient plus aux lancements suivants (une nouvelle version téléchargée le provoque une fois de plus).

La fenêtre d'autorisation administrateur affiche « Éditeur : inconnu » pour la même raison ; seule une signature de l'exe change ce texte.

Seules les options utiles à ce PC sont affichées : une option réservée à une autre version de Windows (Recall et Click To Do demandent Windows 11 24H2), à une autre carte graphique, ou à un logiciel absent (Office, Brave, Chrome, Firefox, Steam, Epic Games) n'apparaît pas.

### Quand l'utiliser

Le meilleur moment est juste après l'installation de Windows, dans cet ordre : pilotes (chipset, carte graphique, réseau, son), Windows Update jusqu'à ce qu'il n'y ait plus rien (mises à jour facultatives comprises), mise à jour des applis du Microsoft Store, installation de vos jeux et logiciels, puis GamingTweaker. Dans un autre ordre, une mise à jour de Windows ou d'un pilote peut rétablir certains réglages ; dans ce cas, relancez simplement votre profil.

## Sécurité

- Seules les options cochées s'exécutent ; chaque option est classée Sûr, Avancé ou Risque. « Recommandé » ne coche que des options Sûr, et jamais celles que le catalogue en exclut (plan d'alimentation sur portable).
- Une faille de sécurité se signale en privé (voir [.github/SECURITY.md](.github/SECURITY.md)), jamais dans une issue publique.
- Le catalogue ne touche à aucune protection de sécurité (Defender, Spectre/Meltdown, VBS, BitLocker, Windows Update) et ne contient que des réglages à effet réel en jeu ou sur l'allègement de Windows ; les réglages placebo ou de pure apparence ont été retirés (voir `docs/revue-code-2026-11-11.md`).
- Un point de restauration Windows est créé avant les options réversibles, la mise à jour du pilote NVIDIA et les mises à jour de Windows (si Windows l'autorise ; sinon vous êtes prévenu).
- Aucun redémarrage automatique : si un redémarrage est nécessaire, le rapport l'indique.
- Les profils (`.json`) ne contiennent que des identifiants d'options, jamais de commande.
- L'exe n'est pas signé : SmartScreen ou un antivirus peut le signaler. Il modifie le système, c'est normal.

## Profils intégrés

Trois profils sont fournis en tête de la liste des profils, en lecture seule :

- **Gaming léger (intégré)** : options Sûr d'Optimisation, de Télémétrie et de Nettoyage.
- **Gaming max (intégré)** : options Sûr et Avancé d'Optimisation, plus les options Sûr de Télémétrie et de Nettoyage.
- **Confidentialité (intégré)** : toute la Télémétrie, Sûr et Avancé.

Aucun ne contient d'option Risqué, d'option à liste (services, démarrage : leur contenu se choisit sur votre PC) ni d'option que « Recommandé » ne coche pas. Pour en modifier un ou le planifier, enregistrez-le sous votre propre nom avec **Profil ▾ > Enregistrer la sélection sous…**.

## Accueil et Paramètres

- **Accueil** : le matériel de ce PC (processeur, carte graphique et sa mémoire vidéo, mémoire vive, disque système avec son type et son espace libre), les profils intégrés en un clic, l'état des réglages par menu, le diagnostic et le Mode Jeu.
  Si un réglage appliqué par GamingTweaker n'est plus en place (rétabli par une mise à jour de Windows ou d'un pilote, ou à la main), un bandeau le signale avec le bouton **Recocher ces réglages** : ils sont cochés, rien ne change avant « Aperçu » puis « LANCER ». Une option annulée depuis l'historique n'est plus signalée.
- **Paramètres** (dernier menu) : thème (comme Windows, clair ou sombre), démarrage avec Windows, guide de l'accueil, version, et tous les réglages du compteur en jeu. Les deux boutons en haut à droite ne font que basculer : le compteur (affiché / masqué) et le thème (clair / sombre).

## Compteur en jeu (FPS, CPU, GPU, RAM)

Le bouton en forme de compteur, en haut à droite (à côté du thème), affiche ou masque le compteur par-dessus le jeu ; ses réglages sont dans le menu **Paramètres**. Il est toujours masqué au lancement de l'appli (il ne s'affiche que si vous le demandez, pour la session en cours). **Ctrl+Maj+F11** l'affiche ou le masque pendant une partie ; la combinaison se change dans Paramètres (« Changer… », puis appuyez sur la nouvelle combinaison : elle doit contenir Ctrl ou Alt).

- **Contenu**, chaque élément activable séparément :
  - images par seconde de la fenêtre au premier plan ;
  - temps d'image et « 1 % bas » (99e centile du temps d'image sur 10 s) ;
  - courbe des FPS sur 10, 30 ou 60 s ;
  - charge du processeur, de la carte graphique et de la mémoire en pourcentage ;
  - températures de la carte graphique (lue par le pilote NVIDIA ou AMD, rien à installer) et du processeur (demande le pilote PawnIO, proposé dans le menu Informations). Pendant qu'un anti-triche à pilote noyau tourne (Vanguard, FACEIT, Easy Anti-Cheat, BattlEye, EA Javelin), la température du processeur n'est pas lue et affiche « AC » : le compteur ne parle alors à aucun pilote tiers, seulement à Windows (FPS, charges) et au pilote graphique (température GPU), comme l'overlay de NVIDIA. Désactivées par défaut.
- **Position** : un coin de l'écran principal, ou **n'importe où** : avec « Placer le compteur à l'écran » (Paramètres), le compteur est affiché avec un cadre pointillé jusqu'à ce que vous quittiez les Paramètres et se déplace à la souris, sur n'importe quel écran ; tirez le coin ◢ du cadre (ou Ctrl+molette) pour l'agrandir ou le réduire (60 à 300 %). Il garde cette place et cette taille.
- **Apparence** : taille, opacité du fond et **couleurs** : choisissez l'élément (FPS, temps d'image et 1 % bas, courbe, CPU, GPU, RAM) puis sa couleur, parmi 7 proposées ou une couleur libre `#RRVVBB` ; « Tout le compteur » remet la même couleur partout. Largeur fixe et police à chasse fixe : le compteur ne bouge pas quand les chiffres changent ; il est rafraîchi deux fois par seconde.
- **Fonctionnement** : les FPS viennent des événements publiés par Windows (ETW, comme PresentMon) : « Present » de DirectX 9 à 12 en priorité, sinon le noyau graphique (DxgKrnl), qui voit aussi les images des jeux Vulkan et OpenGL. La source est indiquée sous les FPS ; rien n'est injecté dans le jeu, le compteur est donc compatible avec les anti-triche. Processeur, mémoire et carte graphique sont mesurés comme dans le Gestionnaire des tâches. La surimpression laisse passer les clics et ne prend jamais le clavier. Rien ne tourne quand le compteur est masqué.
- **Limites** : la mesure Vulkan/OpenGL par le noyau graphique n'a pas pu être vérifiée par la CI (pas de carte graphique) ; si un jeu Vulkan ou OpenGL affiche « — », signalez-le. Le compteur s'affiche en plein écran sans bordure et en fenêtre, pas en plein écran exclusif. Comme toute surimpression, il peut empêcher Windows d'utiliser l'affichage direct (« independent flip ») d'un jeu en plein écran sans bordure. La latence d'entrée (« input lag ») n'est pas affichée : elle ne se mesure correctement qu'avec du matériel dédié.

Les réglages sont dans `compteur.json`, dans le dossier de la machine.

## Partager un profil

Le menu **Profil ▾** de la barre du bas (qui regroupe aussi Enregistrer, Recharger et Planifier) propose :

- **Exporter la sélection…** : enregistre ce qui est coché dans un fichier `.json` de votre choix (par défaut sur le Bureau).
- **Importer un profil…** : sur l'autre PC, choisissez ce fichier. Il est vérifié comme un profil enregistré (version, identifiants d'options, noms d'éléments), puis réécrit dans le dossier des profils et chargé. Les options Risqué demandent la même confirmation qu'au chargement. Si un profil du même nom existe, l'appli demande avant de le remplacer. Un nom de profil intégré reçoit le suffixe « (importé) ».

Comme tout profil, le fichier ne contient que des identifiants d'options et des noms d'éléments, jamais de commande. Les options incompatibles avec l'autre PC sont ignorées et signalées.

## Nouvelle version

Au démarrage de l'interface, l'appli demande à GitHub la dernière release publiée (`api.github.com`, sans identifiant de la machine ni de l'utilisateur ; les pré-versions de test sont ignorées). Si elle est plus récente, un bandeau propose :

- **Mettre à jour** : l'appli télécharge `GamingTweaker.exe` et sa signature `GamingTweaker.exe.sig` depuis la release, vérifie la signature avec la clé publique intégrée à l'exe (et l'empreinte SHA-256 calculée par GitHub), met le nouvel exe à la place de l'exe en cours, au même endroit, puis redémarre. Vos réglages, profils et l'historique sont conservés. Si la signature ne correspond pas, rien n'est remplacé. Un fichier téléchargé par l'appli ne déclenche pas SmartScreen.
- **Nouveautés** : ouvre la page de la release dans le navigateur.

Rien n'est téléchargé sans ce clic. Hors ligne, ou si GitHub ne répond pas en 10 s, aucun bandeau ne s'affiche. La routine planifiée et le mode `--silent` ne font pas cette vérification. Toutes les connexions de l'appli sont listées dans [CONFIDENTIALITE.md](CONFIDENTIALITE.md) : aucune télémétrie.

La mise à jour intégrée ne voit que les releases publiées par un tag `vX.Y.Z` (voir « Publication (CI) ») : les pré-versions `test-*` sont ignorées. Une release n'est annoncée qu'une fois signée par le mainteneur (voir « Signer une version »). Sans clé intégrée à l'exe, le bandeau propose seulement la page de téléchargement.

La routine planifiée et le démarrage avec Windows lancent une copie de l'exe placée dans le dossier protégé. Au démarrage de l'interface, une copie plus ancienne que l'exe ouvert est remplacée par celui-ci (jamais par une version plus ancienne) : après une mise à jour, la routine applique donc le nouveau catalogue.

## Réglages du pilote NVIDIA

Avec une carte NVIDIA, le menu Optimisation affiche un groupe « Pilote NVIDIA » : mode de faible latence Ultra, gestion de l'alimentation en performances maximales, cache des shaders illimité, filtrage des textures en hautes performances, synchronisation verticale désactivée. Chaque option se coche séparément (niveau Avancé, jamais cochée par « Recommandé » ni par un profil intégré). Elles modifient les paramètres globaux du pilote par son interface `nvapi64.dll`, installée avec le pilote : rien n'est téléchargé. La valeur d'avant est notée dans le journal, et chaque option s'annule seule.

## Diagnostic jeu

Le menu « Diagnostic » (lecture seule) repère ce qui bride réellement les performances : écran utilisé sous sa fréquence maximale, mémoire sous sa vitesse nominale (XMP/EXPO) ou en simple canal, Mode Jeu de Windows et HAGS désactivés, plan d'alimentation, batterie, disque système presque plein, pilote graphique ancien, enregistrement en arrière-plan, programmes au démarrage, écran HDR non activé, écran à fréquence variable (FreeSync, G-SYNC). Chaque point propose la page des Paramètres ou l'option qui le corrige.

## Mesures avant / après

Historique > Mesures : un relevé de 30 s (temps DPC et d'interruptions, processeur au repos, services, processus, mémoire disponible, programmes au démarrage). Faites-en un avant d'optimiser et un après, puis « Avant / après » (les deux relevés les plus récents, ou les deux que vous avez cochés) : chaque mesure est marquée « mieux », « moins bien » ou « stable ».

**En jeu** (même écran) : lancez votre jeu, cliquez sur **Mesurer une partie (60 s)** et revenez dans le jeu. La mesure commence 10 s plus tard et relève pendant 60 s les FPS moyens, le « 1 % bas » (fluidité), le pire « 1 % bas » (saccades) et la charge du processeur et de la carte graphique de la fenêtre au premier plan, par la même source que le compteur en jeu (événements de Windows, rien d'injecté). Faites-en une avant d'optimiser et une après, sur la même scène et avec les mêmes réglages graphiques, puis **Comparer les parties** : la dernière partie est comparée à la précédente du même jeu ; un écart de moins de 3 % est jugé stable (bruit d'une partie à l'autre). Une mesure où le jeu n'a pas été au premier plan au moins 80 % du temps est signalée incomplète.

## Mode Jeu

Le menu Mode Jeu se lit de haut en bas :

- **État** : actif ou inactif, ce que le Mode Jeu va faire en une phrase, et le bouton **Activer maintenant** (ou **Rétablir** quand il est actif).
- **1. Ce que fait le Mode Jeu** : plan d'alimentation Performances élevées, libération de la mémoire, profil du pilote NVIDIA, services Windows à arrêter, programmes à fermer (listes repliées, avec le nombre de cases cochées). Ces choix sont **enregistrés** : ils sont recochés au prochain lancement, et ce sont les mêmes pour le Mode Jeu manuel et automatique. Un service ou programme choisi qui ne tourne pas en ce moment reste affiché, coché.
- **2. Mode automatique (facultatif)** :
  - **Vos jeux** : ajoutez des jeux depuis Steam, Epic Games ou un `.exe`. Pour un jeu Steam ou Epic, tout programme lancé depuis son dossier compte (les lanceurs, outils de plantage et anti-triche sont ignorés).
  - **Fonctionnement** : quand un jeu de la liste démarre, le Mode Jeu s'active (environ une seconde et demie après le lancement : GamingTweaker est prévenu par Windows de chaque programme qui démarre, et vérifie aussi toutes les 5 s) ; quand vous quittez le dernier, tout est rétabli. Une notification le signale à chaque fois. Un Mode Jeu activé à la main n'est jamais rétabli automatiquement, et un Mode Jeu rétabli à la main pendant une partie n'est pas réactivé avant la partie suivante.
  - **Démarrer GamingTweaker avec Windows** : crée la tâche `\GamingTweaker\Demarrage`, lancée à l'ouverture de session avec les droits administrateur (sans invite). Elle démarre `GamingTweaker.exe --tray` depuis une copie placée dans `%ProgramFiles%\GamingTweaker\Demarrage`, jamais depuis l'emplacement de l'exe portable. `--uninstall` la retire aussi. Sans cette case, le mode automatique ne fonctionne que tant que GamingTweaker est ouvert : un rappel s'affiche.
  - **Zone de notification** : tant que le mode automatique ou le démarrage avec Windows est actif, fermer la fenêtre la réduit en icône près de l'horloge. Clic droit sur l'icône pour rouvrir ou quitter. En quittant, un Mode Jeu activé automatiquement est rétabli.

## En cas d'erreur

Une erreur inattendue n'est jamais silencieuse : un message l'indique et un rapport est enregistré dans `%LOCALAPPDATA%\GamingTweaker\erreurs.log` (lisible sans droits administrateur), avec le détail technique et les dernières étapes du journal. Envoyez ce fichier pour faire corriger le problème. Une erreur au démarrage ferme l'application après le message ; une erreur plus tard la laisse ouverte.

Chaque étape du démarrage est aussi notée dans `%LOCALAPPDATA%\GamingTweaker\demarrage.log`. Si l'application se ferme **sans aucun message** (un antivirus peut arrêter un exe non signé : voir Sécurité Windows > Historique de protection), le lancement suivant l'indique en haut de la fenêtre, avec la dernière étape atteinte.

La version exacte (par exemple `0.9.1 (build 58)`, le numéro de build de la CI) est affichée en bas de l'accueil, dans Paramètres et en tête de chaque rapport.

## Annuler

Chaque modification réversible est notée dans un journal. Un journal applicatif (`app.log`, dans le dossier de la machine sous le dossier de données protégé) garde la trace des sessions, de la routine et des erreurs internes pour le support ; il ne contient aucune donnée personnelle. L'historique permet d'annuler une session ou une option, et **Tout restaurer** revient à l'état d'avant. Si une valeur a changé entre-temps, l'application le signale avant de l'écraser. Les options non réversibles ou à annulation partielle sont indiquées dans l'aperçu.

## Routine planifiée

**Profil ▾ > Planifier une routine hebdomadaire…** crée la tâche hebdomadaire `\GamingTweaker\Routine` qui applique le profil choisi sans interface (`GamingTweaker.exe --profile <fichier> --silent`). Codes de sortie : 0 = tout est appliqué ou ignoré, 1 = au moins un échec, 2 = exécution impossible. **Supprimer la routine** retire la tâche. Les données de la routine sont dans le dossier de données protégé de GamingTweaker, dossier protégé (propriétaire Administrateurs).

## Désinstaller

Le plus simple : **Historique > Désinstaller GamingTweaker…**. Si des réglages ont été appliqués, l'appli propose d'abord **Tout restaurer** (ils ne peuvent plus être annulés une fois les journaux supprimés), puis retire la routine planifiée, le démarrage avec Windows et le dossier de données protégé, et se ferme ; il reste à supprimer `GamingTweaker.exe`.

L'application est portable : supprimer `GamingTweaker.exe` suffit pour le programme. Pour retirer aussi la routine planifiée et le dossier de données protégé (`%ProgramFiles%\GamingTweaker` : sessions, journaux d'annulation, quarantaine), lancer en administrateur :

```bash
GamingTweaker.exe --uninstall
```

Sans `--confirm`, la commande affiche seulement ce qui serait supprimé. Les réglages appliqués ne sont pas annulés par la désinstallation : passer d'abord par **Tout restaurer** ou par l'annulation des sessions dans l'historique, car les journaux d'annulation disparaissent avec le dossier de données.

## Tests et vérification

- `dotnet test` : tests automatiques (aucune modification du système en dehors de `HKCU\Software\GamingTweakerTest` et du dossier temporaire).
- `python -I tools/style.py` : règles de base des fichiers (UTF-8 sans BOM, pas d'espace en fin de ligne, saut de ligne final, JSON valide), vérifiées aussi par la CI ; `--fix` corrige.
- `tools/sandbox/cycle.wsb` : cycle détecter, appliquer, annuler dans Windows Sandbox (voir `tools/sandbox/start-cycle.ps1`) ; le résultat est écrit dans `tools/sandbox/resultats/cycle.json`. L'option `--cycle-test` est refusée hors du compte du bac à sable.
- `docs/checklist-premier-pc.md` : liste de contrôle à passer sur le premier PC réel.

## Publication (CI)

Le workflow `.github/workflows/build.yml` compile l'exe sur Windows, lance les tests puis le cycle réel des options sur la machine jetable de GitHub. **L'exe n'est publié que si tout est vert** :

| Déclencheur | Publication (onglet Releases) |
|---|---|
| push sur `main` | pré-version `test-build` ; si la `<Version>` du `.csproj` n'a pas encore de tag `vX.Y.Z` : release `GamingTweaker X.Y.Z` (tag créé automatiquement) |
| push sur une autre branche | pré-version `test-<branche>`, supprimée automatiquement avec la branche |
| tag `vX.Y.Z` | release `GamingTweaker X.Y.Z` (c'est elle que l'appli propose en mise à jour) |

Une publication contient **uniquement `GamingTweaker.exe`**. Un build en échec ne remplace jamais l'exe déjà publié ; le rapport du cycle réel, les captures et le résultat des tests vont dans une pré-version `<canal>-echec`, sans exe. Elle est supprimée au build vert suivant.

Pour publier une version stable :

1. Mettre `<Version>` à jour dans `src/GamingTweaker/GamingTweaker.csproj` (avec `AssemblyVersion` et `FileVersion`).
2. Fusionner dans `main` et attendre que le build `test-build` soit vert.
Le premier build vert de `main` avec cette nouvelle version publie la release stable et crée le tag `vX.Y.Z` lui-même. Pousser le tag à la main reste possible :

3. (Facultatif) Créer et pousser le tag depuis `main` :

```bash
git tag v0.9.3
```

```bash
git push origin v0.9.3
```

4. Si une clé de signature existe (voir « Signer une version »), signer la release.

Le workflow refuse un tag qui ne correspond pas à `<Version>`. Espacez les versions stables : chaque nouvel exe repart de zéro côté réputation SmartScreen, et c'est la release « Latest » que les utilisateurs téléchargent.

### Signer une version

La mise à jour intégrée remplace l'exe et le relance en administrateur : elle n'installe qu'un exe signé par **votre** clé, pas seulement un exe publié sur GitHub. L'empreinte SHA-256 de la release prouve que le téléchargement est intact, mais pas qui l'a publié : un compte GitHub, un jeton ou une CI compromis publierait l'exe et l'empreinte qui va avec. La clé privée reste sur votre PC, chiffrée pour votre compte Windows ; l'exe ne contient que la clé publique (`src/GamingTweaker/Assets/cles-release.txt`).

Une seule fois, dans PowerShell, depuis le clone du dépôt :

```powershell
powershell -ExecutionPolicy Bypass -File tools\signer-release.ps1 -Generer
```

La clé publique est ajoutée à `cles-release.txt` : commitez ce fichier, il entre dans la prochaine version. Sauvegardez la clé privée hors ligne (le script indique comment, avec `-Exporter`) : si elle est perdue, les versions suivantes ne pourront plus être installées par la mise à jour intégrée et chaque utilisateur devra télécharger une fois la nouvelle version à la main.

À chaque release, une fois le build du tag vert :

```powershell
powershell -ExecutionPolicy Bypass -File tools\signer-release.ps1 -Tag v0.9.3
```

Le script télécharge l'exe publié, contrôle son empreinte et sa version, affiche le commit d'origine, demande confirmation, signe « GamingTweaker.exe|version|SHA-256 » puis joint `GamingTweaker.exe.sig` à la release (avec `gh` s'il est installé, sinon il indique le fichier à déposer sur la page de la release). Tant que la signature n'est pas jointe, les utilisateurs ne voient pas la nouvelle version.

Changer de clé : ajoutez la nouvelle clé publique à `cles-release.txt`, publiez une version signée avec l'ancienne clé, puis retirez l'ancienne ligne dans la version suivante.

### SmartScreen et signature de l'exe

**Pourquoi l'avertissement SmartScreen ?** Windows avertit pour tout exe téléchargé qui n'est pas signé par un certificat de signature de code ayant acquis une réputation. La licence n'y change rien : GamingTweaker est déjà sous licence libre (AGPL-3.0), et une licence MIT ne supprimerait pas l'avertissement. Seule une signature le fait disparaître, et pour un certificat neuf il s'efface progressivement, au fil des téléchargements.

**La voie gratuite** : [SignPath Foundation](https://signpath.org) signe gratuitement les projets open source. Conditions principales : licence libre reconnue par l'OSI (l'AGPL l'est), **dépôt public**, binaires produits par la CI (GitHub Actions est pris en charge). Il faut en faire la demande ; une fois le projet accepté, l'étape de signature du workflow est remplacée par la leur.

**Avec un certificat acheté** (Certum « open source », certificat OV, etc.), le workflow signe déjà l'exe (Authenticode SHA-256, horodaté) si deux secrets existent dans le dépôt (Settings > Secrets and variables > Actions) :

- `SIGN_PFX_BASE64` : le certificat `.pfx`, encodé en base64 (`[Convert]::ToBase64String([IO.File]::ReadAllBytes('cert.pfx'))` dans PowerShell) ;
- `SIGN_PFX_PASSWORD` : son mot de passe.

Les certificats récents sont souvent livrés sur clé matérielle ou dans un service cloud, sans `.pfx` exportable : l'étape « Signer l'exe » est alors à adapter à l'outil du fournisseur (par exemple Azure Trusted Signing). Sans signature, l'exe est publié non signé et le résumé du build le signale.

**En attendant**, sur votre PC : SmartScreen ne se déclenche que pour un fichier téléchargé par un navigateur. « Informations complémentaires » puis « Exécuter quand même » suffit, une fois par version.

## Licence

GamingTweaker est distribué sous licence **GNU AGPL-3.0** (fichier [LICENSE](LICENSE)) : vous pouvez l'utiliser, l'étudier, le modifier et le redistribuer, à condition de fournir le code source sous la même licence. Ce choix découle des réglages repris de privacy.sexy (AGPL-3.0) ; ceux de WinUtil (MIT) sont compatibles.

## Licences tierces

Les réglages repris de projets tiers sont réécrits ; les sources et licences sont dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
