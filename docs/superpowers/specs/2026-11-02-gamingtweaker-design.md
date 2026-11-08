# GamingTweaker : spécification de conception

Date : 2026-11-02. Statut : à relire. « GamingTweaker » est un nom de travail.

## 1. Objectif

Un exe portable pour Windows, utilisé sur plusieurs PC fixes principalement dédiés au gaming. Il sert à :

- alléger Windows (services, tâches de fond, télémétrie, bloatware) pour réduire les sources de latence ;
- maintenir le PC à jour (Windows, applications, pilote graphique) ;
- nettoyer le PC en profondeur ;
- afficher les informations matérielles et les capteurs.

Principe d'usage : on coche dans les menus, un seul bouton lance uniquement ce qui est coché, et la sélection se sauvegarde en profil pour être relancée en routine.

### Critères de réussite

1. Un exe unique se copie sur un PC fixe et démarre sans rien installer.
2. Chaque option affiche son état réel sur le PC et son niveau de risque.
3. Seules les options cochées sont exécutées, et l'aperçu montre les commandes exactes avant le lancement.
4. Toute option réversible s'annule individuellement ou par session, et l'état initial est retrouvé.
5. Un profil créé sur un PC fonctionne sur les autres : ce qui est incompatible est ignoré et signalé.
6. Une routine planifiée s'exécute sans interface et produit un rapport.

### Hypothèses

- Usage personnel, sans distribution publique. L'exe n'est pas signé.
- Interface en français uniquement.
- Windows 10 version 1903 ou plus récente et Windows 11, en x64. Windows 11 est la cible principale.
- CPU Intel ou AMD. GPU de toute marque, avec mise à jour automatique du pilote pour NVIDIA seulement.

## 2. Architecture

### Technologie

- C#, .NET Framework 4.8, interface WPF, compilé pour x64.
- Compilation avec le SDK .NET et le paquet `Microsoft.NETFramework.ReferenceAssemblies`.
- Manifeste exigeant les droits administrateur.
- Actions écrites en PowerShell 5.1, présent sur toutes les machines cibles.
- `LibreHardwareMonitorLib` et ses dépendances sont intégrées à l'exe en ressources, puis chargées uniquement à l'activation des capteurs.

### Fichiers à l'exécution

```text
GamingTweaker.exe
Profils\                        profils JSON
Machines\<NomDuPC>-<id court>\  journaux d'annulation, rapports, mesures, état du Mode Jeu
```

- L'identifiant court est dérivé de `MachineGuid` (registre `HKLM\SOFTWARE\Microsoft\Cryptography`).
- Si le dossier de l'exe n'est pas accessible en écriture, `Profils` et `Machines` sont placés dans `%ProgramFiles%\GamingTweaker\` (racine protégée : un utilisateur standard ne peut rien y créer ; créée avec une DACL protégée, refus de démarrer si elle existe sans appartenir à SYSTEM, aux Administrateurs ou à TrustedInstaller, ou si c'est un lien). L'ancien `C:\ProgramData\GamingTweaker\` n'est plus utilisé.
- La quarantaine est toujours dans `%ProgramFiles%\GamingTweaker\Quarantaine\`, pour ne pas remplir une clé USB.

### Composants

| Composant | Rôle | Dépend de |
|---|---|---|
| `Catalogue` | Charge et valide les options intégrées à l'exe | Aucun |
| `SystemInfo` | Détecte Windows, édition, CPU, GPU, type de machine | WMI, registre |
| `Compatibilite` | Filtre les options selon `SystemInfo` | `Catalogue`, `SystemInfo` |
| `Detection` | Exécute les scripts de détection en arrière-plan | `Catalogue`, `Runner` |
| `ScriptBuilder` | Assemble le script d'un lancement à partir de la sélection | `Catalogue` |
| `Runner` | Exécute PowerShell, diffuse le journal, renvoie le résultat par option | PowerShell 5.1 |
| `Journal` | Écrit et relit les sauvegardes d'annulation | Système de fichiers |
| `Profils` | Lit, écrit et valide les profils | `Catalogue` |
| `Planification` | Crée et supprime la tâche planifiée | Planificateur de tâches |
| `Capteurs` | Lit les capteurs, charge LibreHardwareMonitor à la demande | LibreHardwareMonitorLib, PawnIO |
| `Mesures` | Prend et compare des relevés de charge du système | Compteurs de performance |
| `Ui` | Fenêtre principale et menus | Tous les composants ci-dessus |

`Ui` ne contient aucune logique système, et `--silent` utilise les mêmes composants sans `Ui`.

### Format d'une option

Une entrée JSON du catalogue, avec ses scripts `.ps1` intégrés en ressources :

```json
{
  "id": "opt.mouse.precision-off",
  "menu": "Optimisation",
  "groupe": "Souris et clavier",
  "titre": "Désactiver la précision du pointeur",
  "description": "Le déplacement du curseur devient proportionnel au mouvement de la souris.",
  "risque": "Sur",
  "reversible": true,
  "redemarrage": false,
  "compatibilite": { "windows": ["10", "11"], "editions": [], "cpu": [], "gpu": [] },
  "scripts": { "detect": "...", "apply": "...", "revert": "..." }
}
```

- `risque` vaut `Sur`, `Avance` ou `Risque`.
- `detect` renvoie `Applied`, `NotApplied` ou `Unknown`.
- `apply` utilise des fonctions communes (`Set-TweakRegistry`, `Set-TweakService`, etc.) qui enregistrent la valeur précédente dans le journal avant toute modification.
- `revert` remet la valeur par défaut de Windows. Il sert uniquement quand aucun journal n'existe pour cette option.
- Pour une option `reversible: false` (nettoyage), `revert` est absent et l'aperçu indique que l'action est définitive.

### Flux d'un lancement

1. L'utilisateur coche des options et ouvre l'aperçu : options, commandes exactes, espace estimé, redémarrage éventuel. L'aperçu peut s'exporter en `.ps1`.
2. L'appli crée un point de restauration, si des options réversibles sont sélectionnées.
3. `ScriptBuilder` assemble un script unique. Chaque option y est isolée pour qu'une erreur n'arrête pas la suivante.
4. `Runner` lance PowerShell avec `-NoProfile -ExecutionPolicy Bypass` pour ce seul processus, et affiche le journal en direct.
5. L'appli écrit le rapport : réussi, échoué avec la raison, ignoré, redémarrage requis. L'état des options est ensuite redétecté.

### Annulation

- Une session a un journal JSON, écrit entrée par entrée avant chaque modification.
- Types d'entrées : valeur de registre, service, tâche planifiée, plan d'alimentation, valeur `bcdedit`, ligne `hosts`, appli supprimée, fichier en quarantaine.
- Historique permet d'annuler une option ou toute une session, dans l'ordre inverse.
- Si une valeur a changé depuis la modification, l'annulation le signale et demande confirmation.
- Une appli supprimée s'annule par réinstallation, sans garantie d'obtenir exactement le même état. L'aperçu le signale.

### Profils

```json
{
  "version": 1,
  "nom": "gaming",
  "options": ["opt.mouse.precision-off", "tel.diagtrack-off"],
  "listes": {
    "winget": { "mode": "tout-sauf", "exclusions": ["Vendor.Package"] },
    "windowsUpdate": { "exclusionsKb": [] },
    "bloatware": ["Microsoft.BingNews"],
    "services": ["Fax"],
    "pilote": { "branche": "GameReady" }
  }
}
```

- Un profil ne contient que des identifiants, jamais de commandes.
- Un identifiant inconnu ou incompatible est ignoré et signalé.
- Enregistrer un profil contenant une option `Risque` demande une confirmation explicite.

### Mode routine

- Ligne de commande : `GamingTweaker.exe --profile <fichier> --silent`.
- Codes de sortie : `0` tout a réussi, `1` échec partiel, `2` impossible de démarrer (profil invalide, droits insuffisants).
- Jamais de redémarrage automatique, et jamais de chargement du pilote des capteurs.
- La planification copie l'exe et le profil dans `%ProgramFiles%\GamingTweaker\App\` et crée une tâche avec droits élevés, sous le compte de l'utilisateur et seulement quand il est connecté, car `winget` dépend du contexte utilisateur.
- La suppression de la routine retire la tâche et cette copie.
- Chaque routine compare aussi l'état des options du profil, ce qui réapplique celles qu'une mise à jour Windows a rétablies. Le rapport les liste.

## 3. Interface

```text
┌──────────────┬───────────────────────────────────────────────┐
│ Mises à jour │ Optimisation                     [Recommandé] │
│ Télémétrie   │ ───────────────────────────────────────────── │
│ Nettoyage    │ [x] Désactiver la précision du pointeur       │
│ Optimisation │     Sûr · Déjà appliqué                       │
│ Bloatware    │ [ ] Économie d'énergie USB désactivée         │
│ Informations │     Sûr · Non appliqué                        │
│ ──────────── │ [ ] Protections Spectre désactivées           │
│ Mode Jeu     │     Risqué · Non appliqué                     │
│ Historique   │                                               │
├──────────────┴───────────────────────────────────────────────┤
│ Profil : [gaming ▾] [Sauver]  12 cochés  [Aperçu]  [LANCER]  │
└──────────────────────────────────────────────────────────────┘
```

- Thème sombre et sobre.
- La barre du bas est toujours visible et regroupe la sélection de tous les menus.
- Les options sont rangées en groupes repliables, avec un champ de recherche, car le catalogue reprend un grand nombre de réglages (section 4.9).
- La fenêtre s'affiche sans attendre la détection : les états passent de « Détection… » à leur valeur finale.
- **Recommandé** ne coche que les options `Sur` du menu affiché.
- Options de planification à côté du profil : jour et heure, activation, suppression.
- Détection des listes dynamiques (mises à jour, `winget`, applis, périphériques) au premier affichage du menu concerné.

## 4. Contenu des menus

### 4.1 Mises à jour

- **Windows Update :** recherche et installation via l'API COM `Microsoft.Update.Session`. Les mises à jour logicielles s'affichent en liste à cocher, sans les pilotes par défaut.
- **Applications :** `winget upgrade` affiché en liste à cocher, installation silencieuse avec acceptation des conditions de `winget`. Si `winget` est absent, le menu l'indique.
- **Pilote NVIDIA :**
  - identification du GPU et de la version installée ;
  - choix Game Ready ou Studio ;
  - recherche de la dernière version via un accès NVIDIA non documenté ;
  - téléchargement, vérification de la signature Authenticode de NVIDIA, installation silencieuse sans redémarrage.
  - Si la recherche échoue, l'appli l'indique et ouvre la page officielle. Les options d'installation silencieuse sont à valider sur un vrai PC.
- **Pilote AMD ou Intel :** affichage de la version et lien vers la page officielle.

### 4.2 Télémétrie

Chaque option s'active ou se désactive, et « Tout restaurer » revient à l'état initial.

- **Sûr :** service DiagTrack, niveau de télémétrie minimal selon l'édition, tâches planifiées de collecte, identifiant publicitaire, suggestions et publicités Windows, historique d'activité, rapports d'erreurs, recherche Bing du menu Démarrer, Copilot et Recall si présents.
- **Avancé :** blocage de domaines de télémétrie par le fichier `hosts`.

L'interface précise que le niveau 0 n'est pleinement honoré que sur Enterprise et Education.

### 4.3 Nettoyage

L'aperçu affiche la taille à libérer par option. Les suppressions sont limitées à une liste de dossiers autorisés et ne suivent ni les liens symboliques ni les jonctions. Le dossier Téléchargements et les données personnelles ne sont jamais touchés.

- **Sûr :** fichiers temporaires, Corbeille, cache des miniatures, cache de Windows Update et de l'optimisation de la distribution, rapports de plantage, journaux Windows, caches de navigateurs (sans mots de passe ni historique), caches Steam et Epic, DISM `/StartComponentCleanup`.
- **Avancé :**
  - cache de shaders ;
  - `Windows.old` ;
  - anciens pilotes via `pnputil`, en conservant ceux utilisés ;
  - fichiers `.msi` et `.msp` orphelins ;
  - DISM `/ResetBase`.

**Orphelins :** la liste des fichiers utilisés vient de l'API `WindowsInstaller.Installer`. Si cette lecture échoue ou ne renvoie rien, l'option s'arrête sans rien déplacer. Les fichiers restent en quarantaine jusqu'à une purge manuelle depuis Historique, qui permet aussi de les restaurer.

### 4.4 Optimisation

| Domaine | Sûr | Avancé | Risqué |
|---|---|---|---|
| Souris et clavier | Précision du pointeur désactivée, raccourcis des touches rémanentes désactivés | | |
| Périphériques | Économie d'énergie USB et réseau désactivée, suspension sélective USB désactivée | Économie d'énergie PCIe désactivée | |
| Alimentation | Plan hautes performances | Plan Performances optimales, stationnement des cœurs désactivé | |
| Jeu et affichage | Mode Jeu activé, Xbox Game DVR désactivé | HAGS, optimisations plein écran, MPO, mode MSI du GPU | VBS et intégrité de la mémoire désactivées |
| Arrière-plan | Applis d'arrière-plan, Widgets, effets visuels | SysMain, indexation de la recherche, hibernation, programmes au démarrage | |
| Services | | Fax, registre à distance, cartes, téléphone, et autres services non essentiels | Services Xbox |
| Réseau et planificateur | | Algorithme de Nagle, limitation réseau, `Win32PrioritySeparation` | HPET et horloge via `bcdedit` |
| Sécurité processeur | | | Protections Spectre et Meltdown désactivées |

- Les services passent en démarrage manuel, pas en désactivé.
- Le texte des services Xbox indique le risque pour le Game Pass et certains jeux.

### 4.5 Bloatware

- La liste vient des applis réellement présentes, croisées avec une liste connue intégrée.
- Suppression pour l'utilisateur et de l'image provisionnée.
- **Sûr :** Bing News, Météo, Solitaire, Clipchamp, Astuces, Teams personnel et équivalents.
- **Avancé :** OneDrive, appli Xbox.
- **Risqué :** Edge (section 4.10).
- Microsoft Store n'est pas proposé.

### 4.6 Informations

- **Sans pilote :** Windows, CPU, carte mère et BIOS, RAM (barrettes et fréquence), GPU, disques et état SMART, réseau, écrans et fréquence de rafraîchissement, pilotes. Température et usage du GPU NVIDIA via `nvidia-smi`.
- **Capteurs :** le bouton « Activer les capteurs » propose d'installer PawnIO si absent, avec accord explicite, puis affiche températures, fréquences, tensions et ventilateurs via LibreHardwareMonitor.
- **WinRing0 :** jamais utilisé.
- **Sans PawnIO :** tout le reste du menu reste utilisable.
- **Export :** fichier texte.

### 4.7 Mode Jeu

- L'utilisateur choisit les services et processus à couper.
- Le premier clic enregistre l'état, les arrête et active le plan hautes performances. Le second clic rétablit tout.
- Après un plantage ou un redémarrage, le prochain lancement de l'appli détecte l'état enregistré et propose le rétablissement.

### 4.8 Historique

- Sessions avec rapports, annulation par option ou par session.
- Quarantaine : taille, restauration, purge.
- Mesures : nombre de services et de processus, RAM au repos, temps CPU en DPC et interruptions, sur un relevé de 30 secondes, comparables entre deux relevés.

L'interface précise que ces mesures indiquent la charge du système et ne mesurent pas l'input lag.

### 4.9 Couverture de WinUtil, privacy.sexy et O&O ShutUp10++

Les menus Télémétrie, Optimisation, Nettoyage et Bloatware reprennent les capacités de ces trois outils. Les listes des sections 4.2 à 4.5 sont donc un minimum. Toutes les reprises sont réécrites avec les fonctions communes, pour que le journal d'annulation fonctionne.

| Source | Licence | Reprise | Niveau de risque |
|---|---|---|---|
| **WinUtil** (`config/tweaks.json`, 68 entrées) | MIT | Scripts adaptables, mention dans `THIRD-PARTY-NOTICES.md` | Essential : Sûr ou Avancé selon l'effet. Advanced : Avancé ou Risqué |
| **privacy.sexy** (`windows.yaml`, environ 4 Mo) | AGPL-3.0 | Une option par sous-catégorie, et non par script | `standard` : Sûr. `strict` : Avancé. Sans recommandation : Risqué ou écarté |
| **O&O ShutUp10++** | Logiciel gratuit propriétaire | Aucun code ni liste copiés. Mêmes domaines de réglages, à partir des réglages documentés de Windows, et lien vers le logiciel officiel | Principe repris : recommandé, limité, déconseillé |

**Conditions de licence :** tant que l'exe reste privé, l'AGPL n'impose aucune publication. En cas de diffusion à des tiers, le code source de l'application devrait être fourni sous AGPL-3.0, ou les reprises de privacy.sexy retirées.

**Preuve de couverture :** `docs/catalogue-couverture.md` indique, pour chaque entrée WinUtil et chaque sous-catégorie privacy.sexy, l'option correspondante ou la raison de son exclusion. Pour ShutUp10++, dont la liste complète n'est pas publiée sur ses pages de présentation, la couverture n'est pas garantie point par point.

**Écartés de WinUtil (en plus de la section 5) :**
- pourcentage de batterie et réglages de veille S0 et S3, sans intérêt sur un PC fixe ;
- liste de blocage Adobe, téléchargée à l'exécution et donc non contrôlée ;
- basculement Outlook et avertissements RDP, sans rapport avec l'usage visé ;
- désactivation de BitLocker, classée Risqué et jamais cochée par une sélection recommandée.

**Ajout à l'Optimisation :** un groupe « Confort Windows » reprend les préférences utiles de WinUtil, comme les extensions de fichiers, le thème sombre et la barre des tâches.

### 4.10 Options Risqué ajoutées à la demande

Ces options sont classées `Risque`. Aucune sélection recommandée ne les coche. La première fois qu'on en coche une, une fenêtre explique la conséquence et demande de confirmer, et la confirmation de profil s'applique aussi.

| Option | Menu | Comportement | Limite |
|---|---|---|---|
| Protection en temps réel de Defender désactivée | Optimisation | `Set-MpPreference -DisableRealtimeMonitoring $true` | Si la protection anti-falsification est active, l'option échoue et indique comment la couper dans Sécurité Windows. Windows peut réactiver Defender |
| Mises à jour automatiques de Windows bloquées | Mises à jour | Stratégie `NoAutoUpdate`=1. Les services restent actifs | La mise à jour manuelle et la routine continuent de fonctionner. Sans elles, le PC ne reçoit plus de correctifs de sécurité |
| Edge désinstallé | Bloatware | Méthode de WinUtil, **sans** `--delete-profile`, et WebView2 conservé | Windows peut le réinstaller. L'annulation passe par `winget install Microsoft.Edge` |
| Dossier Prefetch vidé | Nettoyage | Fichiers `.pf` de `C:\Windows\Prefetch` | Définitif. Ralentit les lancements suivants, le temps que Windows reconstruise le cache |
| Nettoyage limité du registre | Nettoyage | Entrées de désinstallation dont le programme a disparu et cache `MuiCache`, après export `.reg` dans le dossier de la session | Restauration par ce fichier. Aucun autre nettoyage du registre, car le gain attendu est nul |

## 5. Exclusions

Les éléments suivants ne seront pas inclus dans cette version :

- réglages du panneau NVIDIA ;
- contournement de la protection anti-falsification de Defender ;
- mise à jour automatique des pilotes AMD et Intel ;
- signature de code et distribution publique ;
- traduction de l'interface.

## 6. Erreurs

| Situation | Comportement |
|---|---|
| Une option échoue | Les suivantes s'exécutent, le rapport donne la raison |
| Coupure ou plantage en cours de lancement | Au lancement suivant, proposition d'annuler la session interrompue |
| Point de restauration impossible | Avec interface : avertissement et choix de continuer ou non. En routine : poursuite avec le journal, mention dans le rapport |
| Pas d'Internet ou `winget` absent | Options concernées marquées « ignorées » |
| Redémarrage nécessaire | Signalé dans le rapport, jamais automatique |
| Option incompatible dans un profil | Ignorée et signalée |
| Dossier de l'exe en lecture seule | Données dans `%ProgramFiles%\GamingTweaker\` |
| Signature de l'installateur NVIDIA invalide | Fichier supprimé, installation refusée, échec au rapport |
| Option sans fin | Délai maximal propre à l'option, puis échec au rapport |

## 7. Tests

| Niveau | Contenu |
|---|---|
| Automatiques (compilation) | Catalogue : identifiants uniques, champs requis, syntaxe PowerShell validée par le parseur. Filtre de compatibilité, profils, journal, assemblage du script |
| Windows Sandbox | Pour chaque option testable : détecter, appliquer, détecter, annuler, puis comparer à l'état initial |
| PC de développement | Lecture seule : démarrage, interface, détection, Informations, aperçus, routine à vide |
| Premier PC fixe | Liste de contrôle fournie : pilote NVIDIA, Windows Update, `winget`, capteurs et PawnIO, réglages matériels, annulation réelle |

### Limites connues

Avant le premier PC fixe, ne seront pas vérifiés :

- les réglages liés au vrai matériel ;
- l'installation du pilote NVIDIA ;
- Windows 10, sans machine ou image de test ;
- l'effet réel sur les FPS et l'input lag.

Le bac à sable n'a ni vrai GPU, ni `winget` par défaut, et les options qu'il ne peut pas couvrir sont listées dans le rapport de test. Le lancement de Windows Sandbox reste à confirmer sur le PC de développement.

## 8. Risques

- **Accès NVIDIA non documenté :** peut changer sans préavis. Le repli vers la page officielle couvre ce cas.
- **Antivirus et SmartScreen :** l'exe non signé modifie le système et peut être signalé. Aucune technique de contournement n'est prévue.
- **Nécessité de PawnIO :** l'installation système réduit la portabilité des capteurs. Le fonctionnement exact avec LibreHardwareMonitorLib est à vérifier au début du développement.
- **Taille du catalogue :** reprendre trois outils multiplie les options à écrire et à tester. Le regroupement par sous-catégorie et le cycle en Sandbox limitent ce risque, sans couvrir le matériel réel.
- **Mises à jour Windows :** elles peuvent rétablir des réglages ou rendre certains obsolètes. La détection et le contrôle en routine les rendent visibles.
