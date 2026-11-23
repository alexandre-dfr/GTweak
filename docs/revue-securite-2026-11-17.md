# Revue de sécurité du 17 novembre 2026

Revue complète du code actuel (branche `main` après la fusion de la PR 2), centrée sur les frontières de confiance : ce que l'outil, lancé en administrateur (interface ou routine hebdomadaire), lit, exécute ou supprime dans des emplacements qu'un utilisateur standard peut modifier.

## Failles critiques (corrigées)

### 1. Nettoyage : suppression détournable par une jonction (élévation de privilèges)

- **Où :** `Invoke-GTWalk` / `Remove-TweakPath` (`Assets/runtime/Common.ps1`), options `cln.temp`, `cln.browsers`, `cln.shaders`, `cln.crash`, `cln.launchers`, `cln.recent`, `cln.thumbs`, etc.
- **Problème :** le nettoyage tourne avec les droits administrateur (et chaque semaine sans surveillance via la routine), dans des dossiers où l'utilisateur peut écrire : son `Temp`, ses caches, `C:\Windows\Temp`. Les jonctions étaient contrôlées puis l'élément était supprimé **par son chemin** (`[IO.File]::Delete`). Entre le contrôle et la suppression, un processus sans droits pouvait remplacer un dossier parent par une jonction vers `\RPC Control` et y créer un lien du gestionnaire d'objets vers n'importe quel fichier. La suppression atteignait alors un fichier système. C'est une technique connue de suppression arbitraire de fichier, qui mène à SYSTEM.
- **Correctif :** suppression par poignée (`GTNative.SafeFs`).
  - Chaque élément est ouvert sans suivre un lien final (`FILE_FLAG_OPEN_REPARSE_POINT`).
  - Son chemin réel est lu sur la poignée (`GetFinalPathNameByHandle`) et doit rester sous le chemin réel de la racine.
  - L'élément est supprimé par cette même poignée (`FileDispositionInfo`).
  - Le chemin réel de la racine est relu sur une poignée et comparé au chemin attendu : un dossier parent redirigé est refusé.
  - Chaque dossier traversé est vérifié de la même façon avant la descente.

### 2. Routine : l'exe copié dans Program Files pouvait être remplacé (élévation persistante)

- **Où :** `Planification.InstallVerrouille` (`Core/Planification.cs`).
- **Problème :** l'exe portable est souvent dans un dossier où l'utilisateur peut écrire (Téléchargements). Pour la routine, il était relu par son chemin (`File.Copy(MainModule.FileName, …)`) puis copié dans `%ProgramFiles%\GamingTweaker\App`, d'où la tâche le lance chaque semaine avec les droits les plus élevés. Un exe en cours d'exécution ne peut pas être modifié, mais il peut être renommé : un processus sans droits pouvait renommer l'original, déposer un autre exe sous le même nom, et c'est ce dernier qui était copié puis exécuté chaque semaine en administrateur.
- **Correctif :** `ExeEnCours.CopierVers`.
  - L'exe est ouvert sans partage en écriture ni en suppression, ce qui rend tout renommage impossible.
  - L'appli vérifie que cette poignée désigne bien l'image chargée en mémoire, en comparant le nom de l'image mappée (`GetMappedFileName`) au chemin réel de la poignée.
  - La copie se fait depuis cette poignée. Si les deux ne correspondent pas, la routine n'est pas planifiée.

## Bugs du cycle de test dans Windows Sandbox (corrigés)

### 3. Option laissée appliquée après un problème

`CycleTest.Cycle` sortait sans annuler quand l'application échouait, quand l'état initial était indéterminé ou quand la détection ne confirmait pas l'application. L'option restait appliquée dans le bac à sable et faussait les options testées ensuite. L'annulation est désormais toujours tentée dès que le journal contient des entrées, et son résultat est ajouté à la raison de l'échec.

### 4. Faux échec « registre non restauré »

Le contrôle final comparait la valeur actuelle à la valeur « avant » de **chaque** entrée du journal. Pour une option qui écrit deux fois la même valeur, la deuxième entrée a pour « avant » la valeur intermédiaire : l'option était déclarée en échec alors que l'annulation était correcte. Seule la première entrée de chaque cible est désormais comparée (`CycleTest.VerifierRegistre`).

## Durcissement

- **Signature du pilote NVIDIA :** le contrôle acceptait tout certificat dont le sujet *contenait* « NVIDIA Corporation » (par exemple `CN=Not NVIDIA Corporation`). Le nom commun doit maintenant être exactement `NVIDIA Corporation`.

## Points relevés, non corrigés (risque faible)

- `Grant-GTOwnership` (Windows.old uniquement) contrôle le lien puis appelle `takeown`/`icacls` par chemin. Une course reste théoriquement possible, mais elle ne donne aux Administrateurs que des droits qu'ils peuvent déjà obtenir.
- `Profils.Save` écrit à côté de l'exe s'il est inscriptible. Un dossier `Profils` remplacé par une jonction redirigerait l'écriture d'un fichier `.json` choisi par l'utilisateur ailleurs. Le contenu reste un profil (aucune commande) et le nom vient de l'administrateur.
- `--cycle-test` n'est autorisé que sous le compte `WDAGUtilityAccount`. Ce compte existe, désactivé, sur un vrai Windows : seul un administrateur pourrait l'activer.

## Suite du 23 novembre 2026 : mise à jour intégrée, copies protégées, CI

### 5. Mise à jour intégrée : fichier échangeable entre la vérification et le lancement (corrigé)

- **Où :** `MiseAJour.InstallerAsync` (`Core/MiseAJour.cs`).
- **Problème :** l'exe téléchargé (`GamingTweaker.exe.nouveau`, dans le dossier souvent modifiable de l'exe portable) était haché, refermé, déplacé par son chemin puis relancé par un processus administrateur, donc sans invite UAC. Entre le contrôle et le lancement, un processus sans droits pouvait le remplacer : son exe démarrait en administrateur. Même famille que le point 2.
- **Correctif :** le fichier est ouvert une seule fois, en lecture et suppression, partagé en lecture seulement (aucune écriture, aucun renommage ni suppression par un autre processus). Lien, dossier, lien physique ou chemin réel différent du chemin attendu : refus. L'empreinte et la signature sont vérifiées sur cette poignée, le fichier est renommé par elle (`FileRenameInfo`) et elle reste ouverte jusqu'au lancement de la nouvelle version (`MiseAJourPrete`).

### 6. Empreinte SHA-256 publiée avec l'exe : intégrité sans authenticité (corrigé)

- **Problème :** l'empreinte venait de la même release que l'exe. Un compte GitHub, un jeton ou une CI compromis pouvait publier un exe et son empreinte, installés ensuite en administrateur par tous les utilisateurs.
- **Correctif :** signature RSA 3072 bits faite hors de GitHub par le mainteneur (`tools/signer-release.ps1`, clé privée chiffrée DPAPI sur son PC). L'exe ne contient que la clé publique (`Assets/cles-release.txt`) et vérifie « GamingTweaker.exe|version|SHA-256 » : un ancien exe signé ne peut pas être republié sous un tag plus récent. Sans signature valide, rien n'est remplacé ; avec une clé configurée, une release non signée n'est pas annoncée. Le script d'installation `installer.ps1` est retiré : GamingTweaker est uniquement portable.

### 7. Copie de la routine jamais mise à jour (corrigé)

`Planification.InstallVerrouille` copiait l'exe dans `%ProgramFiles%\GamingTweaker\App` une seule fois : après une mise à jour, la routine hebdomadaire gardait l'ancien catalogue. Au démarrage de l'interface, `CopiesProtegees.ActualiserSiAnciennes` remplace désormais les copies de la routine et du démarrage avec Windows quand elles sont d'une version plus ancienne (jamais par une plus ancienne), par `ExeEnCours.CopierVers`, après le contrôle du dossier protégé.

### 8. CI

Actions épinglées par SHA de commit, permissions en lecture seule par défaut (écriture limitée aux jobs qui publient), jeton retiré de `.git/config` pendant la compilation et les tests (`persist-credentials: false`). Le job de build garde l'écriture : le quota d'artefacts du compte est épuisé, l'exe ne peut pas passer à un job de publication séparé.
