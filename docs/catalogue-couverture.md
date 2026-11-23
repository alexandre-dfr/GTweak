# Couverture des sources du catalogue

Ce document fixe, pour chaque entrée de WinUtil et chaque sous-catégorie de privacy.sexy, l'option du catalogue qui la reprend ou la raison de son exclusion (spec, section 4.9). Les Tasks 8 à 11 (et 12 pour `upd.*`) ajoutent les options listées dans le « Récapitulatif par menu », en plus de leurs tableaux minimaux.

## Sources figées

| Source | Fichier | Commit | Licence |
|---|---|---|---|
| WinUtil (Chris Titus Tech) | `config/tweaks.json` | `a94bf5d7008ddf87731a476f148c1334cca10ed2` | MIT |
| privacy.sexy (undergroundwires) | `src/application/collections/windows.yaml` | `b0c9b103089fb209139de4dee30c6c9965c19f9f` | AGPL-3.0 |

Copies dans `tools/inventaire/sources/`, hors exe. L'inventaire lisible par machine est produit par `python -I tools/inventaire/inventaire.py` (fichier `tools/inventaire/inventaire.json`). Voir `THIRD-PARTY-NOTICES.md`.

## Règles appliquées

- **Granularité :** une ligne par entrée WinUtil ; une ligne par sous-catégorie « feuille » de privacy.sexy (catégorie dont les enfants directs comprennent des scripts). Le chemin des catégories parentes précède le nom.
- **Niveau d'origine :** pour WinUtil, la catégorie (`Essential`, `Advanced`, `Customize Preferences`, `Performance Plans`). Pour privacy.sexy, la recommandation la plus fréquente parmi les scripts de la sous-catégorie (`standard`, `strict`, `aucune`), la plus prudente en cas d'égalité.
- **Niveau GamingTweaker :** `standard` → Sûr, `strict` → Avancé, `aucune` → Risqué ou exclu ; WinUtil `Essential` → Sûr ou Avancé, `Advanced` → Avancé ou Risqué (section 4.9). Les préférences de goût du groupe « Confort Windows » sont classées Avancé pour que « Recommandé » ne les coche pas.
- **Exclusions :** section 5, « Écartés » de la section 4.9, et tout réglage qui désactive une protection de sécurité au-delà de la section 4.10, casse la maintenance de Windows Update, ne concerne que les portables, ou supprime des données personnelles.
- **Recentrage (revue du 11 novembre 2026) :** les options sans gain réel en jeu ni allègement de Windows, ou qui affaiblissent la sécurité (Defender, Spectre/Meltdown, VBS, BitLocker, IPv6, hosts, Edge, Prefetch, registre, mises à jour automatiques, préférences d'apparence), sont retirées du catalogue ; les lignes correspondantes passent en « exclu » avec la raison.
- **Plancher de risque :** une ligne ne vise jamais une option plus sûre que son niveau d'origine ne le permet (`standard` → Sûr ou plus, `strict` et `Advanced` → Avancé ou plus, `aucune` → Risqué) ; pour les listes `blt.apps` et `blt.apps-tierces`, la règle s'applique appli par appli. Quand l'option correspondante est plus sûre, la ligne est exclue et la raison renvoie à l'option qui couvre déjà le besoin.
- Plusieurs lignes peuvent viser le même identifiant. Une ligne qui vise un identifiant ne reprend que les scripts indiqués dans sa raison ; les autres scripts de la sous-catégorie sont écartés pour la raison indiquée.
- Toutes les reprises sont réécrites avec les fonctions `Set-Tweak*` de `Common.ps1` ; aucun script source n'est exécuté ni intégré tel quel.

## Tableau de couverture

| source | entrée | niveau d'origine | décision | raison |
|---|---|---|---|---|
| WinUtil | Activity History - Disable (`WPFTweaksActivity`) | Essential | tel.activity-off | Correspond au tableau minimal ; l'historique du presse-papiers reste intact comme chez WinUtil |
| WinUtil | Hibernation - Disable (`WPFTweaksHiber`) | Essential | opt.hibernate-off | Correspond au tableau minimal (Avancé) |
| WinUtil | Widgets - Remove (`WPFTweaksWidget`) | Essential | opt.widgets-off | Correspond au tableau minimal (stratégie, sans retirer le paquet) |
| WinUtil | Start Menu Previous Layout - Enable (`WPFTweaksRevertStartMenu`) | Essential | exclu | Bascule `FeatureManagement\Overrides` non documentée, inopérante sur les versions récentes selon WinUtil lui-même |
| WinUtil | Microsoft Store Recommended Search Results - Disable (`WPFTweaksDisableStoreSearch`) | Essential | exclu | Repose sur un refus d'ACL sur `store.db` du Store : type de modification non journalisable (section 2, Annulation) et peut casser la recherche du Store ; les suggestions du Store sont couvertes par `tel.suggestions-off` |
| WinUtil | Location Tracking - Disable (`WPFTweaksLocation`) | Essential | tel.location-off | Nouvelle option ; même domaine que la sous-catégorie privacy.sexy « Disable location access » |
| WinUtil | Services - Set to Manual (`WPFTweaksServices`) | Essential | opt.services | Ajoute `CscService` et `SharedAccess` à la liste, en manuel et non en désactivé (section 4.4) ; `Spooler` et `StorSvc` retirés le 11 novembre 2026 (impression et stockage cassés en manuel) ; `MapsBroker` y est déjà ; `DiagTrack` relève de `tel.diagtrack-off` |
| WinUtil | svchost.exe Splitting - Disable (`WPFTweaksSvcHostSplitting`) | Advanced | exclu | Retiré : gain de mémoire négligeable au-delà de 8 Go, aucun effet sur les FPS, gêne possible des services Xbox. |
| WinUtil | Brave Browser - Debloat (`WPFTweaksBraveDebloat`) | Advanced | tel.brave-debloat | Advanced → Avancé : stratégies Brave seulement, sans effet si Brave est absent |
| WinUtil | RDP Unsigned File Warnings - Disable (`WPFTweaksDisableWarningForUnsignedRdp`) | Advanced | exclu | Avertissements RDP : sans rapport avec l'usage visé (section 4.9, Écartés) |
| WinUtil | Microsoft Edge - Debloat (`WPFTweaksEdgeDebloat`) | Advanced | tel.edge-debloat | Advanced → Avancé ; la télémétrie d'Edge seule est dans `tel.edge-telemetry-off` (Sûr) |
| WinUtil | ConsumerFeatures - Disable (`WPFTweaksConsumerFeatures`) | Essential | tel.suggestions-off | Enrichit l'option : `CloudContent\DisableWindowsConsumerFeatures`=1 |
| WinUtil | Telemetry - Disable (`WPFTweaksTelemetry`) | Essential | tel.level-min | Enrichit l'option (`AllowTelemetry`, expériences personnalisées, Siuf) ; identifiant publicitaire → `tel.adid-off`, vocal et saisie → `tel.misc-off`, `WerSvc` → `tel.wer-off`, PowerShell 7 → `tel.apps-telemetry-off` ; `SubmitSamplesConsent` de Defender non repris (affaiblit la protection cloud, hors 4.10) |
| WinUtil | Delivery Optimization - Disable (`WPFTweaksDeliveryOptimization`) | Essential | opt.delivery-opt-off | Nouvelle option : `DODownloadMode`=0 ; le service DoSvc n'est pas désactivé (Windows Update en dépend) |
| WinUtil | Microsoft Edge - Remove (`WPFTweaksRemoveEdge`) | Advanced | exclu | Retiré : aucun gain en jeu ; casse Widgets, Outlook et les liens du Store ; Windows Update le réinstalle. |
| WinUtil | BitLocker - Disable (`WPFTweaksDisableBitLocker`) | Essential | exclu | Retiré : déchiffrement définitif du disque sans gain mesurable en jeu. |
| WinUtil | Date & Time - Set Time to UTC (`WPFTweaksUTC`) | Advanced | exclu | Retiré : hors sujet pour un PC de jeu (double démarrage Linux), heure décalée sinon. |
| WinUtil | Microsoft OneDrive - Remove (`WPFTweaksRemoveOneDrive`) | Advanced | blt.apps | Entrée OneDrive de la liste (Avancé, section 4.5), désinstalleur officiel, dossier de l'utilisateur jamais touché |
| WinUtil | File Explorer Home and Gallery - Disable (`WPFTweaksRemoveHomeAndGallery`) | Advanced | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Visual Effects - Set to Best Performance (`WPFTweaksDisplay`) | Advanced | exclu | Advanced : ne peut viser `opt.visualfx` (Sûr) ; les effets visuels y sont déjà couverts par le tableau minimal (`VisualFXSetting`=2), les réglages fins de WinUtil (`MenuShowDelay`, `KeyboardDelay`, `UserPreferencesMask`) ne sont pas repris |
| WinUtil | Disable Reserved Storage (`WPFTweaksReservedStorage`) | Advanced | exclu | Peut faire échouer les mises à jour de fonctionnalités (WinUtil le signale) : casse la maintenance de Windows Update |
| WinUtil | Restore Point - Create (`WPFTweaksRestorePoint`) | Essential | exclu | L'appli crée déjà un point de restauration avant chaque lancement d'options réversibles (section 2, Flux) |
| WinUtil | End Task With Right Click - Enable (`WPFTweaksEndTaskOnTaskbar`) | Essential | opt.endtask-taskbar | Essential → Avancé : préférence d'usage, non cochée par « Recommandé » (Confort Windows, Windows 11) |
| WinUtil | Storage Sense - Disable (`WPFTweaksStorage`) | Advanced | opt.storagesense-off | Advanced → Avancé |
| WinUtil | Windows AI - Disable And Remove (`WPFTweaksWindowsAI`) | Advanced | tel.ai-off | Advanced → Risqué : retrait de paquets IA via `EndOfLife`, annulation seulement par réinstallation ; Copilot → `tel.copilot-off`, Recall → `tel.recall-off`, Microsoft 365 → `blt.apps` |
| WinUtil | Windows Platform Binary Table (WPBT) - Disable (`WPFTweaksWPBT`) | Essential | blt.wpbt-off | Essential → Avancé : certains constructeurs installent ainsi des pilotes utiles |
| WinUtil | Prevent Device Companion Apps (`WPFTweaksPreventDeviceMetadataFromNetwork`) | Essential | blt.device-metadata-off | Essential → Sûr |
| WinUtil | Razer Software Auto-Install - Disable (`WPFTweaksRazerBlock`) | Advanced | blt.vendor-autoinstall-off | Advanced → Avancé : `DisableCoInstallers`=1 bloque les co-installeurs de tous les constructeurs, pour toute la machine ; le refus d'ACL sur le dossier Razer n'est pas journalisable (Task 11 : le remplacer ou le marquer non réversible) ; `SearchOrderConfig`=0 relève de `upd.wu-drivers-off` |
| WinUtil | Logitech Download Assistant Auto-Install - Disable (`WPFTweaksLogiBlock`) | Advanced | blt.vendor-autoinstall-off | Advanced → Avancé : même option (co-installeurs de tous les constructeurs) ; le refus d'ACL sur `LogiDownloadAssistant` n'est pas journalisable (Task 11) |
| WinUtil | System Tray Notifications & Calendar - Disable (`WPFTweaksDisableNotifications`) | Advanced | opt.notifications-off | Advanced → Avancé (Confort Windows) |
| WinUtil | Adobe URL Block List - Enable (`WPFTweaksBlockAdobeNet`) | Advanced | exclu | Section 4.9, Écartés : liste de blocage Adobe téléchargée à l'exécution, donc non contrôlée |
| WinUtil | Right-Click Menu Previous Layout - Enable (`WPFTweaksRightClickMenu`) | Advanced | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Disk Cleanup - Run (`WPFTweaksDiskCleanup`) | Essential | cln.resetbase | Le DISM `/ResetBase` est repris ; `cleanmgr /VERYLOWDISK` non repris (supprime hors de la liste de dossiers autorisés, section 4.3) |
| WinUtil | Temporary Files - Remove (`WPFTweaksDeleteTempFiles`) | Essential | cln.temp | Correspond au tableau minimal |
| WinUtil | IPv6 - Set IPv4 as Preferred (`WPFTweaksIPv46`) | Advanced | opt.ipv4-prefer | Advanced → Avancé : `DisabledComponents`=0x20, IPv6 reste actif |
| WinUtil | Teredo - Disable (`WPFTweaksTeredo`) | Advanced | exclu | Retiré : aucun gain en jeu, casse le réseau Xbox Live derrière certains NAT. |
| WinUtil | IPv6 - Disable (`WPFTweaksDisableIPv6`) | Advanced | exclu | Retiré : Microsoft déconseille DisabledComponents=0xFF (connectivité, +5 s au démarrage) ; aucun gain en jeu. `opt.ipv4-prefer` couvre le besoin. |
| WinUtil | Background Apps - Disable (`WPFTweaksDisableBGapps`) | Advanced | exclu | Advanced : ne peut viser `opt.bgapps-off` (Sûr) ; la stratégie `LetAppsRunInBackground` y couvre déjà le besoin, `GlobalUserDisabled` n'est pas repris |
| WinUtil | File Explorer Automatic Folder Discovery - Disable (`WPFTweaksDisableExplorerAutoDiscovery`) | Essential | exclu | Retiré : supprime toutes les vues de dossiers mémorisées pour un gain marginal dans l'Explorateur. |
| WinUtil | BSoD Verbose Mode (`WPFToggleDetailedBSoD`) | Customize Preferences | opt.bsod-detailed | Préférence sans effet de bord (Confort Windows) |
| WinUtil | System Tray Battery Percentage (`WPFToggleBatteryPercentage`) | Customize Preferences | exclu | Section 4.9, Écartés : pourcentage de batterie, sans intérêt sur un PC fixe |
| WinUtil | Dark Theme for Windows (`WPFToggleDarkMode`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | File Explorer File Extensions (`WPFToggleShowExt`) | Customize Preferences | opt.show-extensions | Confort Windows, cité en 4.9 ; Sûr (aide aussi à repérer les exécutables déguisés) |
| WinUtil | File Explorer Hidden Files (`WPFToggleHiddenFiles`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Logon Verbose Mode (`WPFToggleVerboseLogon`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Microsoft Outlook New Version (`WPFToggleNewOutlook`) | Customize Preferences | exclu | Section 4.9, Écartés : basculement Outlook, sans rapport avec l'usage visé |
| WinUtil | Scrollbars Always Visible (`WPFToggleScrollbars`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Multiplane Overlay (`WPFMultiplaneOverlay`) | Customize Preferences | opt.mpo-off | Correspond au tableau minimal (`OverlayTestMode`=5) |
| WinUtil | Mouse Acceleration (`WPFToggleMouseAcceleration`) | Customize Preferences | opt.mouse.precision-off | Correspond au tableau minimal |
| WinUtil | Num Lock on Startup (`WPFToggleNumLock`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Window Snapping (`WPFToggleWindowSnapping`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | S0 Sleep Network Connectivity (`WPFToggleStandbyFix`) | Customize Preferences | exclu | Section 4.9, Écartés : réglage de veille S0, sans intérêt sur un PC fixe |
| WinUtil | S3 Sleep (`WPFToggleS3Sleep`) | Customize Preferences | exclu | Section 4.9, Écartés : réglage de veille S3, sans intérêt sur un PC fixe |
| WinUtil | Settings Home Page (`WPFToggleHideSettingsHome`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Start Menu Bing Search (`WPFToggleBingSearch`) | Customize Preferences | tel.bing-off | Enrichit l'option : `HKCU\...\Search` `BingSearchEnabled`=0 |
| WinUtil | Logon Screen Acrylic Blur (`WPFToggleLoginBlur`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Lock Screen - Disable (`WPFToggleDisableLockscreen`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Start Menu Recommendations (`WPFToggleStartMenuRecommendations`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Sticky Keys (`WPFToggleStickyKeys`) | Customize Preferences | opt.stickykeys-off | Correspond au tableau minimal |
| WinUtil | Taskbar Centered Icons (`WPFToggleTaskbarAlignment`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Taskbar Search Icon (`WPFToggleTaskbarSearch`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Taskbar Task View Icon (`WPFToggleTaskView`) | Customize Preferences | exclu | Retiré : préférence d'apparence ou d'ergonomie sans gain FPS ni allègement de Windows (catalogue recentré). |
| WinUtil | Game Mode (`WPFToggleGameMode`) | Customize Preferences | opt.gamemode-on | Correspond au tableau minimal |
| WinUtil | Enable Long Paths (`WPFToggleLongPaths`) | Customize Preferences | opt.longpaths | `LongPathsEnabled`=1, sans effet de bord (Confort Windows) |
| WinUtil | O&O ShutUp10++ - Run (`WPFOOSUbutton`) | Advanced | exclu | Télécharge et lance un exe tiers à chaque clic ; la Task 8 prévoit seulement un lien vers la page officielle d'O&O ShutUp10++ |
| WinUtil | DNS - Set to: (`WPFchangedns`) | Advanced | exclu | Choix du fournisseur DNS : préférence réseau personnelle hors des quatre menus, modification par carte réseau non couverte par le journal |
| WinUtil | Ultimate Performance Profile - Enable (`WPFAddUltPerf`) | Performance Plans - NOT FOR LAPTOPS | opt.plan-ultimate | Correspond au tableau minimal ; WinUtil le marque « NOT FOR LAPTOPS », sans objet sur un PC fixe |
| WinUtil | Ultimate Performance Profile - Disable (`WPFRemoveUltPerf`) | Performance Plans - NOT FOR LAPTOPS | opt.plan-ultimate | Annulation de `opt.plan-ultimate` par le journal |
| privacy.sexy | Privacy cleanup | aucune | exclu | Dominante sans recommandation, contenu destructif (identifiants, clé produit, utilisateur `default0`, clichés instantanés, associations, SRUM) ; Corbeille, ResetBase et installations précédentes déjà couverts par `cln.recycle`, `cln.resetbase` et `cln.windows-old` (tableaux minimaux) |
| privacy.sexy | Privacy cleanup > Clear recent activity | standard | cln.recent | Nouvelle option Sûr (standard dominant) |
| privacy.sexy | Privacy cleanup > Clear recent activity > Clear Quick Access lists | aucune | exclu | Égalité standard / sans recommandation, donc niveau « aucune » ; les éléments épinglés sont des données personnelles (section 4.3) |
| privacy.sexy | Privacy cleanup > Clear recent activity > Clear Windows Registry recent activity | strict | exclu | Niveau strict (égalité) ; les favoris de Regedit sont des données personnelles (section 4.3), et la dernière clé consultée seule n'apporte rien |
| privacy.sexy | Privacy cleanup > Clear third-party application data | standard | cln.apps-traces | Nouvelle option Sûr (standard dominant) ; index Listary non repris (sans recommandation) |
| privacy.sexy | Privacy cleanup > Clear third-party application data > Clear privacy.sexy data | aucune | exclu | Données propres à privacy.sexy, sans objet dans GamingTweaker |
| privacy.sexy | Privacy cleanup > Clear third-party application data > Clear Steam data | standard | cln.launchers | Enrichit l'option : dumps et caches de Steam |
| privacy.sexy | Privacy cleanup > Clear third-party application data > Clear Visual Studio usage data > Clear Visual Studio telemetry and feedback data | standard | cln.apps-traces | Données de télémétrie de Visual Studio |
| privacy.sexy | Privacy cleanup > Clear third-party application data > Clear Visual Studio usage data > Clear Visual Studio licenses | aucune | exclu | Efface les licences de Visual Studio : destructif, sans gain |
| privacy.sexy | Privacy cleanup > Clear browser history | aucune | exclu | Données personnelles du navigateur (historique, cookies, profils) : jamais touchées (section 4.3) |
| privacy.sexy | Privacy cleanup > Clear browser history > Clear Internet Explorer history | standard | cln.browsers | Seuls les caches (Temporary Internet Files) sont repris ; cookies, DOMStore et URL tapées exclus (section 4.3) |
| privacy.sexy | Privacy cleanup > Clear browser history > Clear Chrome history | standard | cln.browsers | Rapports de plantage (Crashpad) repris ; « Chrome user data » exclu (section 4.3) |
| privacy.sexy | Privacy cleanup > Clear browser history > Clear Firefox history | aucune | exclu | Données personnelles du navigateur (historique, cookies, profils) : jamais touchées (section 4.3) |
| privacy.sexy | Privacy cleanup > Clear browser history > Clear Safari history | standard | exclu | Safari pour Windows n'existe plus ; cookies et données de profil exclus (section 4.3) |
| privacy.sexy | Privacy cleanup > Clear temporary Windows files | standard | cln.temp | Dossiers temporaires système et utilisateur ; le dossier Prefetch relève de `cln.prefetch` (Risqué, section 4.10) |
| privacy.sexy | Privacy cleanup > Clear Windows log and caches | aucune | exclu | Dominante sans recommandation ; miniatures et journaux d'événements déjà couverts par `cln.thumbs` et `cln.logs` (tableaux minimaux) ; l'historique d'analyse de Defender n'est pas effacé |
| privacy.sexy | Privacy cleanup > Clear Windows log and caches > Clear Windows system log files | standard | cln.logs | Enrichit la liste des journaux autorisés |
| privacy.sexy | Privacy cleanup > Clear Windows log and caches > Clear Windows system log files > Clear Windows Update system logs | standard | cln.logs | Journaux seulement ; la base de Windows Update n'est pas touchée |
| privacy.sexy | Privacy cleanup > Clear Outlook data | aucune | exclu | Données Outlook : hors usage visé, et « Outlook user data » supprime des données personnelles |
| privacy.sexy | Disable OS data collection | standard | tel.misc-off | Nouvelle option Sûr (standard dominant) ; Recall → `tel.recall-off`, enregistrement d'écran des jeux → `opt.gamedvr-off`, flux d'activité → `tel.activity-off` |
| privacy.sexy | Disable OS data collection > Disable app access to personal information | standard | tel.app-access-off | Nouvelle option Sûr ; caméra, micro et activité en arrière-plan (sans recommandation) non inclus, car ils cassent le chat vocal des applis Store et Xbox et les notifications |
| privacy.sexy | Disable OS data collection > Disable app access to personal information > Disable app access to your files | standard | tel.app-access-off | Documents, Images, Vidéos, Musique |
| privacy.sexy | Disable OS data collection > Disable app access to personal information > Disable app access to phone | standard | tel.app-access-off | Historique d'appels, appels et messages |
| privacy.sexy | Disable OS data collection > Disable app access to personal information > Disable app access to Bluetooth devices | standard | tel.app-access-off | Applis Store seulement ; les manettes et casques restent utilisables par les pilotes |
| privacy.sexy | Disable OS data collection > Disable app access to personal information > Disable app voice activation | standard | tel.app-access-off | Activation vocale des applis |
| privacy.sexy | Disable OS data collection > Disable Customer Experience Improvement Program | standard | tel.tasks-off | Enrichit l'option : stratégies CEIP en plus des tâches |
| privacy.sexy | Disable OS data collection > Disable Customer Experience Improvement Program > Disable background customer experience data collection | standard | tel.tasks-off | Enrichit l'option : SQM, CEIP noyau, Bluetooth, USB, diagnostic de disque |
| privacy.sexy | Disable OS data collection > Disable Customer Experience Improvement Program > Disable background customer experience data collection > Disable server-specific customer experience data collection | standard | exclu | Propre à Windows Server, hors des versions visées |
| privacy.sexy | Disable OS data collection > Disable Application Experience data collection > Disable automatic system compatibility checks (Microsoft Compatibility Appraiser) | standard | tel.tasks-off | Correspond au tableau minimal (Microsoft Compatibility Appraiser) |
| privacy.sexy | Disable OS data collection > Disable Application Experience data collection > Disable background application compatibility checks (Application Experience scheduled tasks) | strict | tel.compat-off | Nouvelle option Avancé (strict dominant) |
| privacy.sexy | Disable OS data collection > Disable Application Compatibility Framework | standard | tel.compat-off | Rehaussé en Avancé : l'assistant de compatibilité peut servir aux anciens jeux ; le moteur de compatibilité (`DisableEngine`) n'est pas repris, car il applique les correctifs des anciens jeux |
| privacy.sexy | Disable OS data collection > Disable Application Compatibility Framework > Disable Program Compatibility Assistant (PCA) | standard | tel.compat-off | Service PCA en manuel, stratégie `DisablePCA` |
| privacy.sexy | Disable OS data collection > Disable Windows telemetry and data collection | standard | tel.level-min | Enrichit l'option : télémétrie de licence, configuration cloud ; rapports d'erreurs → `tel.wer-off` |
| privacy.sexy | Disable OS data collection > Disable Windows telemetry and data collection > Disable diagnostics telemetry services | aucune | exclu | Égalité standard / sans recommandation, donc niveau « aucune » : Diagnostics Hub et `diagsvc` servent aux outils de diagnostic ; `DiagTrack` et `dmwappushservice` sont déjà dans `tel.diagtrack-off` (tableau minimal) |
| privacy.sexy | Disable OS data collection > Disable Windows telemetry and data collection > Disable census data collection | standard | tel.tasks-off | Enrichit l'option : tâches Device Information |
| privacy.sexy | Disable OS data collection > Disable Windows telemetry and data collection > Disable enterprise/business focused data collection | standard | tel.level-min | Enrichit l'option : `AllowCommercialDataPipeline`=0 |
| privacy.sexy | Disable OS data collection > Disable Windows telemetry and data collection > Disable enterprise/business focused data collection > Disable Desktop Analytics telemetry | strict | exclu | Stratégies de Desktop Analytics, service retiré par Microsoft et sans effet sur un PC personnel non géré |
| privacy.sexy | Disable OS data collection > Disable connectivity checks | aucune | exclu | Casse l'indicateur de connexion : le Store, les launchers et des jeux concluent à l'absence d'Internet |
| privacy.sexy | Disable OS data collection > Disable update and app sharing | aucune | exclu | Égalité avec la désactivation de DoSvc (sans recommandation), qui casse Windows Update ; le pair à pair est couvert par `opt.delivery-opt-off` via la ligne WinUtil |
| privacy.sexy | Disable OS data collection > Disable location access | standard | tel.location-off | Nouvelle option Sûr |
| privacy.sexy | Disable OS data collection > Disable Windows search data collection | standard | tel.search-off | Nouvelle option Sûr |
| privacy.sexy | Disable OS data collection > Disable Windows search data collection > Disable Cortana data collection | standard | tel.cortana-off | Nouvelle option Sûr ; sans effet si Cortana est absente |
| privacy.sexy | Disable OS data collection > Disable Windows search data collection > Disable Cortana data collection > Disable Cortana activity history | standard | tel.cortana-off | Historique de Cortana |
| privacy.sexy | Disable OS data collection > Disable Windows search data collection > Disable Cortana data collection > Disable Cortana voice listening | standard | tel.cortana-off | Écoute vocale de Cortana |
| privacy.sexy | Disable OS data collection > Disable Windows search data collection > Disable privacy-invasive indexing | standard | tel.search-off | Indexation des éléments chiffrés, accès distant à l'index ; l'indexation elle-même relève de `opt.wsearch` |
| privacy.sexy | Disable OS data collection > Disable Windows search data collection > Disable search suggestions | standard | tel.bing-off | Enrichit l'option : recherche web et points forts de la recherche |
| privacy.sexy | Disable OS data collection > Disable targeted advertisements and marketing | standard | tel.adid-off | Identifiant publicitaire ; contenu suggéré des Paramètres → `tel.suggestions-off` |
| privacy.sexy | Disable OS data collection > Disable targeted advertisements and marketing > Disable cloud-based advertising and tips | standard | tel.suggestions-off | Enrichit l'option : astuces, expériences consommateur ; Windows à la une (strict) inclus en sous-réglage facultatif |
| privacy.sexy | Disable OS data collection > Disable biometrics (breaks fingerprinting/facial login) | strict | exclu | Casse Windows Hello (empreinte, visage) : désactive une fonction de sécurité, hors section 4.10 |
| privacy.sexy | Disable OS data collection > Disable Windows Insider Program | standard | tel.insider-off | Nouvelle option Sûr |
| privacy.sexy | Disable OS data collection > Disable cloud sync | standard | tel.sync-off | Nouvelle option Sûr |
| privacy.sexy | Configure programs | standard | tel.apps-telemetry-off | Nouvelle option Sûr (standard dominant) : .NET CLI, PowerShell ; services Razer Game Scanner et Logitech Gaming Registry en manuel ; winget et Accessibility Insights non repris (réglages dans des fichiers JSON, non journalisables) ; CCleaner (sans recommandation) non repris |
| privacy.sexy | Configure programs > Disable Visual Studio data collection | standard | tel.apps-telemetry-off | Visual Studio, IntelliCode, collecteur standard en manuel |
| privacy.sexy | Configure programs > Disable Nvidia telemetry | standard | tel.nvidia-off | Nouvelle option Sûr ; la suppression de fichiers du pilote n'est pas reprise (non journalisable, réinstallée avec le pilote), le service passe en manuel |
| privacy.sexy | Configure programs > Disable Nvidia telemetry > Disable Nvidia telemetry scheduled tasks | standard | tel.nvidia-off | Tâches NvTm* |
| privacy.sexy | Configure programs > Disable Visual Studio Code data collection | aucune | exclu | Dominante sans recommandation (mises à jour, extensions, dépôts de VS Code) : outil de développement hors usage visé |
| privacy.sexy | Configure programs > Disable Microsoft Office telemetry | standard | tel.office-off | Nouvelle option Sûr ; sans effet si Office est absent |
| privacy.sexy | Configure programs > Configure browsers > Configure Edge | strict | tel.edge-debloat | Nouvelle option Avancé (strict dominant) |
| privacy.sexy | Configure programs > Configure browsers > Configure Edge > Disable Edge telemetry | standard | tel.edge-telemetry-off | Nouvelle option Sûr |
| privacy.sexy | Configure programs > Configure browsers > Configure Edge > Disable Edge and WebView2 automatic updates | strict | exclu | Bloque les correctifs de sécurité d'Edge et de WebView2 ; WebView2 est conservé et doit rester à jour (section 4.10) |
| privacy.sexy | Configure programs > Configure browsers > Configure Edge > Disable Edge and WebView2 automatic updates > Disable automatic installation of Edge and WebView | standard | exclu | Retiré : aucun gain en jeu ; casse Widgets, Outlook et les liens du Store ; Windows Update le réinstalle. |
| privacy.sexy | Configure programs > Configure browsers > Configure Edge > Disable Copilot in Edge | aucune | exclu | Dominante sans recommandation ; barre latérale d'Edge, préférence du navigateur ; Copilot de Windows → `tel.copilot-off` |
| privacy.sexy | Configure programs > Configure browsers > Configure Edge > Disable Edge ads | standard | tel.edge-telemetry-off | Publicités et pages promotionnelles d'Edge |
| privacy.sexy | Configure programs > Configure browsers > Configure Edge > Harden Edge privacy | strict | tel.edge-debloat | Prévention du suivi, cookies tiers ; peut gêner certains sites |
| privacy.sexy | Configure programs > Configure browsers > Configure Edge (Legacy) | standard | exclu | Edge hérité retiré de Windows depuis 2021, sans objet |
| privacy.sexy | Configure programs > Configure browsers > Configure Internet Explorer | standard | exclu | Internet Explorer est retiré ou désactivé sur les versions visées |
| privacy.sexy | Configure programs > Configure browsers > Configure Chrome | standard | tel.browsers-telemetry-off | Nouvelle option Sûr ; sans effet si Chrome est absent |
| privacy.sexy | Configure programs > Configure browsers > Configure Chrome > Configure Chrome cleanup | standard | tel.browsers-telemetry-off | Analyses et partage du nettoyage Chrome |
| privacy.sexy | Configure programs > Configure browsers > Configure Firefox | standard | tel.browsers-telemetry-off | Télémétrie et agent du navigateur par défaut de Firefox |
| privacy.sexy | Configure programs > Disable Google background automatic updates | strict | exclu | Bloque les mises à jour de sécurité d'un logiciel tiers (Chrome) |
| privacy.sexy | Configure programs > Disable Adobe background automatic updates | standard | exclu | Bloque les mises à jour de sécurité d'un logiciel tiers (Acrobat Reader) |
| privacy.sexy | Configure programs > Disable Dropbox background automatic updates | standard | exclu | Bloque les mises à jour de sécurité d'un logiciel tiers (Dropbox) |
| privacy.sexy | Configure programs > Disable Media Player data collection | standard | tel.apps-telemetry-off | Statistiques et métadonnées du Lecteur Windows Media ; service de partage réseau en manuel |
| privacy.sexy | Security improvements | standard | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux (DEP forcé et SEHOP peuvent faire planter d'anciens jeux) |
| privacy.sexy | Security improvements > Improve network security > Enable strong secret key requirements | strict | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux (casse Hyper-V selon privacy.sexy) |
| privacy.sexy | Security improvements > Improve network security > Disable insecure connections | strict | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux |
| privacy.sexy | Security improvements > Improve network security > Disable insecure connections > Disable insecure ciphers | strict | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux |
| privacy.sexy | Security improvements > Improve network security > Disable insecure connections > Disable insecure hashes | strict | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux |
| privacy.sexy | Security improvements > Improve network security > Disable insecure connections > Disable insecure protocols | standard | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux (SMBv1 et TLS 1.0 utilisés par d'anciens NAS et serveurs de jeu) |
| privacy.sexy | Security improvements > Improve network security > Enable secure connections | standard | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux |
| privacy.sexy | Security improvements > Improve network security > Disable insecure remote administration access | standard | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux |
| privacy.sexy | Security improvements > Improve network security > Disable non-essential network components | aucune | exclu | Durcissement de sécurité sans rapport avec la télémétrie, l'optimisation, le nettoyage ou le bloatware : aucun menu de la spec ne l'accueille, et certains réglages cassent d'anciens jeux, serveurs ou réseaux locaux |
| privacy.sexy | Security improvements > Disable clipboard data collection | strict | tel.clipboard-off | Nouvelle option Avancé (strict dominant) ; casse la synchronisation et l'historique du presse-papiers |
| privacy.sexy | Security improvements > Enable protection against Meltdown and Spectre | aucune | exclu | Les protections Spectre et Meltdown sont actives par défaut et l'outil ne les touche plus : rien à activer |
| privacy.sexy | Block tracking hosts | strict | exclu | Retiré : Microsoft Defender détecte ce blocage comme SettingsModifier:Win32/HostsFileHijack et restaure le fichier ; les stratégies `tel.level-min`, `tel.wer-off` et `tel.diagtrack-off` couvrent les mêmes flux. |
| privacy.sexy | Block tracking hosts > Block third-party app hosts | aucune | exclu | Hôtes Dropbox et Spotify : cassent ces applis, hors télémétrie de Windows |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender data collection | strict | exclu | Rapports et envoi d'échantillons alimentent la protection cloud de Defender : l'affaiblir dépasse la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender data collection > Disable Defender Antivirus cloud protection | strict | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable system modification restrictions | aucune | exclu | Exige de contourner la protection anti-falsification de Defender (section 5) ; VBS → `opt.vbs-off` (Risqué) |
| privacy.sexy | Privacy over security > Disable Defender > Disable system modification restrictions > Disable Defender System Guard | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable network inspection | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable real-time protection | aucune | exclu | Retiré : coupe l'antivirus sans gain mesurable en jeu ; motif AMSI dans chaque script généré. |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable real-time protection > Disable Defender monitoring of behavior | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable real-time protection > Disable monitoring of downloads and attachments in Defender | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable real-time protection > Disable Defender monitoring of file and program activity | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender remediation | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender scans > Disable scan actions | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender scans > Disable catch-up scans | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender scans > Disable Defender scan options | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender scans > Disable Defender scan options > Disable intensive CPU usage during Defender scans | aucune | opt.defender-scan-cpu | Sans recommandation → Risqué : limite la charge processeur des analyses sans les désactiver ; échoue si la protection anti-falsification bloque la stratégie |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender scans > Minimize scanned areas | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender scans > Minimize scanned areas > Disable scanning archive files | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender scans > Disable auto-scans | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender scans > Disable Antimalware Scan Interface (AMSI) | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender Antivirus management | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender Antivirus management > Disable Defender Antivirus remote management | strict | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender updates | aucune | exclu | Retarde ou bloque les mises à jour de Defender : affaiblit la protection, hors section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender updates > Disable Defender Security Intelligence (signature) updates | aucune | exclu | Retarde ou bloque les mises à jour de Defender : affaiblit la protection, hors section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender updates > Disable Defender Security Intelligence (signature) updates > Disable alternate definition updates | aucune | exclu | Retarde ou bloque les mises à jour de Defender : affaiblit la protection, hors section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender reporting | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender services and drivers | aucune | exclu | Exige de contourner la protection anti-falsification de Defender (section 5) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender services and drivers > Disable Defender Antivirus service | aucune | exclu | Exige de contourner la protection anti-falsification de Defender (section 5) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender Antivirus scheduled tasks | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Antivirus > Disable Defender Antivirus file activity monitoring | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Firewall | aucune | exclu | Pare-feu : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Firewall > Disable Defender Firewall services and drivers | aucune | exclu | Pare-feu : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender for Endpoint | aucune | exclu | Defender for Endpoint et DLP : produits d'entreprise absents d'un PC personnel, et protection de sécurité hors section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable SmartScreen | aucune | exclu | SmartScreen est une protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable SmartScreen > Disable SmartScreen for apps and files | aucune | exclu | SmartScreen est une protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable SmartScreen > Disable SmartScreen for apps and files > Disable SmartScreen for Store apps | aucune | exclu | SmartScreen est une protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable SmartScreen > Disable SmartScreen in Microsoft browsers | aucune | exclu | SmartScreen est une protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable SmartScreen > Disable SmartScreen system components | aucune | exclu | SmartScreen est une protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable SmartScreen > Disable SmartScreen Enhanced Phishing Protection | aucune | exclu | SmartScreen est une protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Windows Security interface | aucune | exclu | Masque les alertes et l'interface de sécurité : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Windows Security interface > Disable Defender interface background services | aucune | exclu | Masque les alertes et l'interface de sécurité : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Windows Security interface > Disable sections in "Windows Security" | aucune | exclu | Masque les alertes et l'interface de sécurité : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Windows Security interface > Disable sections in "Windows Security" > Disable device security sections | aucune | exclu | Masque les alertes et l'interface de sécurité : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Windows Security interface > Disable security notifications > Disable Security and Maintenance notifications | aucune | exclu | Masque les alertes et l'interface de sécurité : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Windows Security interface > Disable security notifications > Disable Windows Security notifications | aucune | exclu | Masque les alertes et l'interface de sécurité : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Windows Security interface > Disable security notifications > Disable Defender Antivirus notifications | aucune | exclu | Masque les alertes et l'interface de sécurité : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Windows Security interface > Disable Security and Maintenance | aucune | exclu | Masque les alertes et l'interface de sécurité : protection de sécurité absente de la section 4.10 |
| privacy.sexy | Privacy over security > Disable Defender > Disable Defender Exploit Guard | aucune | exclu | Désactive une protection de Defender au-delà de la section 4.10 (seule la protection en temps réel est reprise, `opt.defender-rt-off`) |
| privacy.sexy | Privacy over security > Disable Defender > Disable outdated Defender Application Guard | strict | exclu | Application Guard est retiré de Windows, et c'est une protection de sécurité hors section 4.10 |
| privacy.sexy | Privacy over security > Disable automatic updates | aucune | exclu | Retiré : prive le PC des correctifs de sécurité sans gain en jeu ; la pause de Windows Update dans les Paramètres suffit. |
| privacy.sexy | Privacy over security > Disable automatic updates > Disable Windows Update automatic driver updates | strict | upd.wu-drivers-off | Nouvelle option Avancé (strict dominant) : évite que Windows Update remplace le pilote graphique ; les mises à jour logicielles continuent |
| privacy.sexy | Privacy over security > Disable automatic updates > Disable Windows update services | aucune | exclu | Casse la maintenance de Windows Update ; le blocage des installations automatiques n'est plus proposé (option retirée) (section 4.10) |
| privacy.sexy | Privacy over security > Disable automatic updates > Disable Windows update scheduled tasks | aucune | exclu | Casse la maintenance de Windows Update ; le blocage des installations automatiques n'est plus proposé (option retirée) (section 4.10) |
| privacy.sexy | Privacy over security > Disable automatic updates > Maximize auto-update duration | aucune | exclu | Empêche la reprise des mises à jour (privacy.sexy l'indique) : casse la maintenance de Windows Update ; le blocage des installations automatiques n'est plus proposé (option retirée) (section 4.10) |
| privacy.sexy | Privacy over security > Configure how downloaded files are handled | aucune | exclu | Supprime la marque « fichier téléchargé » (Mark of the Web) et l'avertissement à l'antivirus : protection de sécurité hors section 4.10 |
| privacy.sexy | UI for privacy | standard | tel.ui-privacy | Nouvelle option Sûr (standard dominant) ; hibernation → `opt.hibernate-off`, notifications des fournisseurs de synchronisation → `tel.suggestions-off`, fichiers récents de l'Accès rapide → `tel.recent-docs-off` |
| privacy.sexy | UI for privacy > Disable online content in File Explorer | standard | tel.explorer-online-off | Nouvelle option Sûr |
| privacy.sexy | UI for privacy > Secure recent document lists | strict | tel.recent-docs-off | Nouvelle option Avancé (strict dominant) : les listes de documents récents disparaissent |
| privacy.sexy | UI for privacy > Remove folders from This PC in File Explorer | aucune | exclu | Changement d'affichage sans effet sur la vie privée ni sur les performances ; les dossiers restent accessibles |
| privacy.sexy | Remove bloatware | strict | exclu | « Réunion maintenant » a été retiré de Windows par Microsoft, sans objet |
| privacy.sexy | Remove bloatware > Remove Windows apps | aucune | blt.apps | Ajouts à `bloatware-list.json`, chaque appli au niveau de son script : standard → Sûr (Cortana, Hub de commentaires, Cartes, Network Speed Test), strict → Avancé (App Connector, Courrier et Calendrier), sans recommandation → Risqué (Messaging, Portail de réalité mixte, Paint 3D, Contacts, Pay, Mobile Plans, Skype, GroupMe, Films et TV, To Do, Obtenir de l'aide) ; Astuces et Solitaire restent Sûr car la section 4.5 les nomme ; jamais proposées : App Installer (`winget`), Calculatrice, Photos, Caméra, Alarmes, Pense-bêtes, Lecteur multimédia, Enregistreur vocal, Bureau à distance, Minecraft |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove 3D modeling apps | aucune | blt.apps | Print 3D → Sûr (standard) ; 3D Builder et Visionneuse 3D → Risqué (sans recommandation) |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove MSN (Bing) apps | standard | blt.apps | Météo, Sports, Actualités, Finance → Sûr (section 4.5) |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove extension apps | aucune | exclu | Codecs HEIF, HEVC, VP9, WebP, RAW utilisés par les jeux, les navigateurs et les applis multimédias, sans gain |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove Microsoft Office apps | aucune | blt.apps | Microsoft 365 (Office Hub) → Sûr (standard, section 4.5) ; OneNote et Sway → Risqué (sans recommandation, peuvent contenir des notes) |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove Microsoft Store apps | aucune | exclu | Microsoft Store jamais proposé (section 4.5) |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove Phone apps | strict | blt.apps | Phone Companion et Microsoft Phone → Sûr (standard) ; Lien avec Windows (`Microsoft.YourPhone`), Call et Cross Device Experience Host → Avancé (strict) |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove third-party apps | aucune | blt.apps-tierces | Sans recommandation → Risqué, option distincte de la liste Sûr : Shazam, Flipboard, Twitter, iHeart, Duolingo, Photoshop Express, Pandora, Eclipse Manager, Code Writer, Spotify, que l'utilisateur peut avoir installés lui-même ; seules les applis présentes sont listées |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove third-party apps > Remove Candy Crush apps | aucune | blt.apps-tierces | Sans recommandation → Risqué : Candy Crush Saga et Soda Saga |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove system apps | aucune | exclu | Composants système (Explorateur, sélecteur de fichiers, broker de comptes, écran de verrouillage, Windows Hello, Recherche) : leur retrait casse Windows |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove system apps > Remove Edge (Legacy) | strict | exclu | Edge hérité retiré de Windows depuis 2021, sans objet |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove system apps > Remove Edge (Legacy) > Remove Edge (Legacy) associations | strict | exclu | Edge hérité retiré de Windows depuis 2021, sans objet |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove system apps > Remove Out-of-Box Experience (OOBE) apps | aucune | exclu | Casse l'installation, la réinitialisation et l'ajout de comptes |
| privacy.sexy | Remove bloatware > Remove Windows apps > Remove system apps > Remove printing user interface | aucune | exclu | Casse l'impression |
| privacy.sexy | Remove bloatware > Remove OneDrive | strict | blt.apps | Entrée OneDrive de la liste (Avancé, section 4.5) ; « Remove OneDrive user data and synced folders » jamais repris (données personnelles) |
| privacy.sexy | Remove bloatware > Remove Edge | aucune | exclu | Retiré : aucun gain en jeu ; casse Widgets, Outlook et les liens du Store ; Windows Update le réinstalle. |
| privacy.sexy | Remove bloatware > Remove Edge > Remove Edge associations | aucune | exclu | Les associations sont reprises par Windows après la désinstallation ; modifier `UserChoice` est protégé par un hachage et casse les choix par défaut |
| privacy.sexy | Remove bloatware > Remove Xbox | standard | blt.apps | Appli Xbox (`Microsoft.GamingApp`, `Microsoft.XboxApp`) → Avancé (section 4.5) ; services Xbox Live → `opt.xbox-services` (Risqué) ; Game Bar, Identity Provider, Game Callable UI et overlays jamais retirés, car ils cassent le Game Pass et les jeux Xbox Live |
| privacy.sexy | Remove bloatware > Disable built-in Windows features | aucune | blt.features | Sans recommandation → Risqué (DISM, redémarrage, réactivation parfois sans source) : Fax et numérisation, Scan Management, Internet Explorer ; DirectPlay, Legacy Components, Media Features et Lecteur Windows Media jamais (anciens jeux, codecs) ; Windows Search relève de `opt.wsearch` |
| privacy.sexy | Remove bloatware > Disable built-in Windows features > Disable Hyper-V virtualization features | aucune | exclu | Hyper-V n'est pas activé par défaut ; la virtualisation relève de `opt.vbs-off`, et WSL2 ou des émulateurs en dépendent |
| privacy.sexy | Remove bloatware > Disable built-in Windows features > Disable printing features | aucune | blt.features | Work Folders seulement ; « Print and Document Services » conservé (impression) |
| privacy.sexy | Remove bloatware > Disable built-in Windows features > Disable printing features > Disable printer networking | aucune | blt.features | Client d'impression Internet, LPD, LPR ; « Microsoft Print to PDF » conservé |
| privacy.sexy | Remove bloatware > Disable built-in Windows features > Disable printing features > Disable XPS support features | aucune | blt.features | Imprimante XPS et Visionneuse XPS |
| privacy.sexy | Remove bloatware > Remove on-demand capabilities and features > Remove preinstalled features on demand | aucune | blt.capabilities | Sans recommandation → Risqué : IE 11, Math Recognizer, PowerShell ISE, Print Management, Assistance rapide, Enregistreur d'actions, Fax ; DirectX Configuration Database (jeux), OpenSSH et OneSync jamais |
| privacy.sexy | Remove bloatware > Remove on-demand capabilities and features > Remove not preinstalled features on demand | aucune | exclu | Fonctionnalités non préinstallées : rien à retirer sur une installation standard ; .NET Framework et outils requis par des jeux ou des pilotes |
| privacy.sexy | Remove bloatware > Remove on-demand capabilities and features > Remove not preinstalled features on demand > Remove printing capabilities | aucune | exclu | Fonctionnalités non préinstallées : rien à retirer sur une installation standard ; .NET Framework et outils requis par des jeux ou des pilotes |
| privacy.sexy | Remove bloatware > Remove on-demand capabilities and features > Remove not preinstalled features on demand > Remove Remote Server Administration Tools (RSAT) | aucune | exclu | Fonctionnalités non préinstallées : rien à retirer sur une installation standard ; .NET Framework et outils requis par des jeux ou des pilotes |
| privacy.sexy | Remove bloatware > Remove on-demand capabilities and features > Remove not preinstalled features on demand > Remove storage capabilities | aucune | exclu | Fonctionnalités non préinstallées : rien à retirer sur une installation standard ; .NET Framework et outils requis par des jeux ou des pilotes |
| privacy.sexy | Remove bloatware > Remove Widgets | strict | exclu | Niveau strict : le retrait du Windows Web Experience Pack n'est pas repris ; la stratégie est déjà dans `opt.widgets-off` (tableau minimal, Sûr) |
| privacy.sexy | Remove bloatware > Remove Windows Copilot | strict | exclu | Niveau strict : ne peut viser `tel.copilot-off` (Sûr), dont la stratégie `TurnOffWindowsCopilot` couvre déjà le besoin ; icône et lancement automatique non repris |
| privacy.sexy | Remove bloatware > Disable non-essential services | strict | opt.services | `MapsBroker` et `RetailDemo` sont déjà dans la liste, en manuel ; `wlidsvc` (Store, compte Microsoft, Xbox), `WpnService` (notifications) et `VSS` (points de restauration) jamais ; services par utilisateur (UserDataSvc, MessagingService) écartés |
| privacy.sexy | Remove bloatware > Disable non-essential services > Disable synchronization of mail, contacts, calendar, and user data | strict | exclu | Services par utilisateur (UnistoreSvc, OneSyncSvc) : casse Courrier, Calendrier et Contacts, sans gain mesurable |
| privacy.sexy | Remove bloatware > Remove Snipping Tool | aucune | exclu | Outil de capture utile, sans gain ; le raccourci Impr. écran resterait modifié |
| privacy.sexy | Remove bloatware > Remove Outlook | strict | blt.apps | « Outlook pour Windows » → Avancé (strict) et blocage de sa réinstallation automatique ; le basculement vers le nouvel Outlook reste écarté (section 4.9) |
| privacy.sexy | Advanced settings | aucune | exclu | Serveur NTP : sans rapport avec les menus ; stockage réservé : peut faire échouer les mises à jour de fonctionnalités |

## Récapitulatif par menu

Nouvelles options à créer, en plus des tableaux minimaux du plan. Le titre est le texte d'interface proposé.

### Télémétrie

| id | groupe | titre | risque | réversible | lignes sources |
|---|---|---|---|---|---|
| `tel.misc-off` | Collecte Windows | Collectes diverses désactivées (reconnaissance vocale en ligne, saisie, commentaires, capteurs, Wi-Fi Sense) | Sûr | oui | 1 |
| `tel.compat-off` | Collecte Windows | Télémétrie de compatibilité des applications désactivée (AIT, PCA, enregistreur d'actions, inventaire) | Avancé | oui | 3 |
| `tel.location-off` | Collecte Windows | Localisation désactivée | Avancé | oui | 2 |
| `tel.insider-off` | Collecte Windows | Programme Windows Insider et essais de fonctionnalités désactivés | Sûr | oui | 1 |
| `tel.app-access-off` | Autorisations des applis | Accès des applis aux données personnelles bloqué (hors caméra et micro) | Avancé | oui | 5 |
| `tel.search-off` | Recherche | Collecte de la recherche Windows désactivée (données partagées, contenu cloud, indexation sensible) | Sûr | oui | 2 |
| `tel.cortana-off` | Recherche | Cortana et sa collecte désactivées | Sûr | oui | 3 |
| `tel.sync-off` | Synchronisation | Synchronisation des paramètres avec le compte Microsoft désactivée | Sûr | oui | 1 |
| `tel.clipboard-off` | Synchronisation | Presse-papiers cloud et historique du presse-papiers désactivés | Avancé | oui | 1 |
| `tel.ui-privacy` | Interface | Suivi des applis récentes et notifications de l'écran de verrouillage désactivés | Sûr | oui | 1 |
| `tel.start-reco-off` | Interface | Section « Recommandé » du menu Démarrer masquée | Avancé | oui | 0 (optimizerDuck) |
| `tel.explorer-online-off` | Interface | Contenu en ligne de l'Explorateur désactivé (astuces, publication web, commandes de tirages) | Sûr | oui | 1 |
| `tel.recent-docs-off` | Interface | Historique des documents récents désactivé | Avancé | oui | 1 |
| `tel.apps-telemetry-off` | Applications tierces | Télémétrie des outils tiers désactivée (.NET, PowerShell, Visual Studio, Lecteur Windows Media, services Razer et Logitech) | Sûr | oui | 3 |
| `tel.nvidia-off` | Applications tierces | Télémétrie NVIDIA désactivée (tâches, service, participation) | Sûr | oui | 2 |
| `tel.office-off` | Applications tierces | Télémétrie de Microsoft Office désactivée | Sûr | oui | 1 |
| `tel.edge-telemetry-off` | Navigateurs | Télémétrie et publicités d'Edge désactivées | Sûr | oui | 2 |
| `tel.edge-debloat` | Navigateurs | Fonctions promotionnelles d'Edge désactivées (Shopping, Rewards, Copilot, barre de recherche) | Avancé | oui | 3 |
| `tel.browsers-telemetry-off` | Navigateurs | Télémétrie de Chrome et de Firefox désactivée | Sûr | oui | 3 |
| `tel.brave-debloat` | Navigateurs | Brave allégé (Rewards, Leo, Wallet, VPN) | Avancé | oui | 1 |
| `tel.ai-off` | IA | Fonctions IA de Windows retirées (paquets IA, IA du Bloc-notes, page IA des Paramètres) | Risqué | partiel : paquets IA retirés : retour par réinstallation seulement | 1 |
| `tel.clicktodo-off` | IA | Désactiver Click To Do (analyse de l'écran par l'IA) | Sûr | oui | 0 (optimizerDuck) |

### Optimisation

| id | groupe | titre | risque | réversible | lignes sources |
|---|---|---|---|---|---|
| `opt.delivery-opt-off` | Réseau et planificateur | Partage pair à pair des mises à jour désactivé | Sûr | oui | 1 |
| `opt.ipv4-prefer` | Réseau et planificateur | IPv4 préféré à IPv6 | Avancé | oui | 1 |
| `opt.storagesense-off` | Arrière-plan | Assistant de stockage désactivé | Avancé | oui | 1 |
| `opt.defender-scan-cpu` | Arrière-plan | Charge processeur des analyses Defender limitée | Risqué | oui | 1 |
| `opt.bsod-detailed` | Confort Windows | Écran bleu détaillé | Sûr | oui | 1 |
| `opt.show-extensions` | Confort Windows | Extensions de fichiers affichées | Sûr | oui | 1 |
| `opt.longpaths` | Confort Windows | Chemins de plus de 260 caractères autorisés | Sûr | oui | 1 |
| `opt.endtask-taskbar` | Confort Windows | « Fin de tâche » dans le menu de la barre des tâches | Avancé | oui | 1 |
| `opt.notifications-off` | Confort Windows | Notifications et calendrier de la zone de notification désactivés | Avancé | oui | 1 |
| `opt.tasks-tierces` | Arrière-plan | Désactiver des tâches planifiées d'applications | Avancé | oui | 0 (optimizerDuck) |

### Nettoyage

| id | groupe | titre | risque | réversible | lignes sources |
|---|---|---|---|---|---|
| `cln.recent` | Traces | Historique d'activité récente effacé (fichiers récents, Exécuter, recherches, Accès rapide) | Sûr | non : suppression définitive | 1 |
| `cln.apps-traces` | Traces | Traces d'applications tierces effacées (Java, .NET CLI, Visual Studio) | Sûr | non : suppression définitive | 2 |

### Bloatware

| id | groupe | titre | risque | réversible | lignes sources |
|---|---|---|---|---|---|
| `blt.apps` | Applications | Applications préinstallées (liste, niveau par appli) | Sûr | partiel : réinstallation depuis le Store, sans garantie du même état | 9 |
| `blt.features` | Fonctionnalités Windows | Fonctionnalités facultatives inutilisées désactivées (Fax et numérisation, Internet Explorer, XPS, impression Internet) | Risqué | partiel : DISM : réactivation parfois impossible sans source | 4 |
| `blt.capabilities` | Fonctionnalités Windows | Fonctionnalités à la demande retirées (IE 11, Math Recognizer, PowerShell ISE, Assistance rapide, Enregistreur d'actions, Fax) | Risqué | partiel : DISM : réinstallation par Windows Update | 1 |
| `blt.wpbt-off` | Logiciels constructeur | Programmes du micrologiciel (WPBT) bloqués au démarrage | Avancé | oui | 1 |
| `blt.device-metadata-off` | Logiciels constructeur | Applis compagnons des périphériques non téléchargées | Sûr | oui | 1 |
| `blt.vendor-autoinstall-off` | Logiciels constructeur | Co-installeurs de tous les constructeurs bloqués pour toute la machine (logiciels Razer, Logitech et autres non installés avec les pilotes) | Avancé | partiel : registre journalisé ; refus d'ACL non journalisable, retiré par `revert` | 2 |
| `blt.apps-tierces` | Applications | Applications tierces promues retirées (liste : Spotify, Candy Crush, Duolingo…) | Risqué | partiel : réinstallation depuis le Store, sans garantie du même état | 2 |

### Mises à jour

| id | groupe | titre | risque | réversible | lignes sources |
|---|---|---|---|---|---|
| `upd.wu-drivers-off` | Windows Update | Pilotes non installés par Windows Update | Avancé | oui | 1 |
| `upd.pause-max` | Windows Update | Pause de Windows Update possible jusqu'à un an | Avancé | oui | 0 (optimizerDuck) |

`blt.apps` est l'option à liste du fournisseur `bloatware` (Task 11) : chaque appli de `bloatware-list.json` porte son propre niveau, indiqué dans la colonne raison ci-dessus et jamais plus sûr que la recommandation de son script, sauf Astuces, Solitaire, OneDrive et l'appli Xbox, dont la section 4.5 fixe le niveau. `blt.apps-tierces` est une liste distincte, entièrement Risqué. Colonne « réversible » : oui = annulation par le journal ; partiel ou non = mécanisme non journalisable, précisé dans la note.

### Reprises d'optimizerDuck

Options ajoutées le 20 novembre 2026 d'après [optimizerDuck](https://github.com/itsfatduck/optimizerDuck) (GPL-3.0, compatible AGPL-3.0), réécrites avec les fonctions de `Common.ps1` ; la colonne « lignes sources » vaut 0 car elles ne viennent ni de WinUtil ni de privacy.sexy :

- `tel.clicktodo-off`, `tel.start-reco-off` (stratégie Explorer seule, sans `IsEducationEnvironment`), `upd.pause-max` (allonge la pause, n'en active aucune) ;
- `tel.suggestions-off` enrichie : `OemPreInstalledAppsEnabled`, `PreInstalledAppsEnabled`, `PreInstalledAppsEverEnabled`, `DisableConsumerAccountStateContent` ;
- `opt.tasks-tierces` : tâches planifiées des applications tierces (liste détectée sur le PC) ;
- écartés : `SvcHostSplitThresholdInKB`, `Win32PrioritySeparation`, `NetworkThrottlingIndex`/`SystemResponsiveness`, file du clavier, réglages GPU par le registre, effets visuels, plan d'alimentation `.pow` (placebo ou déjà retirés par la revue du 11 novembre 2026) et `PowerThrottlingOff` (ne concerne en pratique que les portables).

### Options existantes enrichies

Options des tableaux minimaux que des lignes sources complètent (voir la colonne raison) :

- `tel.activity-off` : 1 ligne(s)
- `opt.hibernate-off` : 1 ligne(s)
- `opt.widgets-off` : 1 ligne(s)
- `opt.services` : 2 ligne(s)
- `tel.suggestions-off` : 2 ligne(s)
- `tel.level-min` : 3 ligne(s)
- `cln.resetbase` : 1 ligne(s)
- `cln.temp` : 2 ligne(s)
- `opt.mpo-off` : 1 ligne(s)
- `opt.mouse.precision-off` : 1 ligne(s)
- `tel.bing-off` : 2 ligne(s)
- `opt.stickykeys-off` : 1 ligne(s)
- `opt.gamemode-on` : 1 ligne(s)
- `opt.plan-ultimate` : 2 ligne(s)
- `cln.launchers` : 1 ligne(s)
- `cln.browsers` : 2 ligne(s)
- `cln.logs` : 2 ligne(s)
- `tel.tasks-off` : 4 ligne(s)
- `tel.adid-off` : 1 ligne(s)

## O&O ShutUp10++ : domaines couverts

O&O ShutUp10++ est un logiciel gratuit propriétaire : aucun code ni aucune liste n'en est repris. Les domaines ci-dessous viennent des réglages documentés par Microsoft (stratégies de groupe, CSP Policy et page « Gérer les connexions des composants du système d'exploitation Windows aux services Microsoft »). **Aucune correspondance point par point avec ShutUp10++ n'est garantie**, car sa liste complète n'est pas publiée sur ses pages de présentation. Son principe recommandé / limité / déconseillé correspond à Sûr / Avancé / Risqué. L'appli ne propose plus de lien vers ce logiciel (retiré le 20 novembre 2026 pour alléger l'interface).

| Domaine (documentation Microsoft) | Options GamingTweaker |
|---|---|
| Télémétrie et données de diagnostic (`AllowTelemetry`, DiagTrack, CEIP, tâches de collecte) | `tel.level-min`, `tel.diagtrack-off`, `tel.tasks-off`, `tel.compat-off` |
| Rapports d'erreurs Windows | `tel.wer-off` |
| Identifiant publicitaire et expériences personnalisées | `tel.adid-off`, `tel.level-min` |
| Suggestions, astuces, applis promues, Windows à la une | `tel.suggestions-off` |
| Historique d'activité et chronologie | `tel.activity-off`, `tel.ui-privacy` |
| Reconnaissance vocale en ligne, saisie manuscrite et frappe, commentaires | `tel.misc-off` |
| Autorisations des applis (position, contacts, calendrier, fichiers, téléphone…) | `tel.app-access-off` (caméra et micro laissés à l'utilisateur) |
| Localisation | `tel.location-off` |
| Recherche Windows, Bing et Cortana | `tel.bing-off`, `tel.search-off`, `tel.cortana-off` |
| Synchronisation des paramètres et presse-papiers cloud | `tel.sync-off`, `tel.clipboard-off` |
| Copilot, Recall et fonctions IA | `tel.copilot-off`, `tel.recall-off`, `tel.ai-off` |
| Microsoft Edge (télémétrie, publicités, fonctions promotionnelles) | `tel.edge-telemetry-off`, `tel.edge-debloat` |
| Windows Update (pair à pair, pilotes) | `opt.delivery-opt-off`, `upd.wu-drivers-off` ; les installations automatiques ne sont plus bloquées (option retirée) |
| Programme Insider et essais de fonctionnalités | `tel.insider-off` |
| Applis en arrière-plan et Widgets | `opt.bgapps-off`, `opt.widgets-off` |
| Écran de verrouillage et interface (notifications, documents récents) | `tel.ui-privacy`, `tel.recent-docs-off` |
| Microsoft Defender et SmartScreen | Exclu : aucune protection n'est désactivée (seule la charge processeur des analyses est réglable par `opt.defender-scan-cpu`) |

## Comptes

| Source | Lignes | Reprises | Exclues |
|---|---|---|---|
| WinUtil | 68 | 32 | 36 |
| privacy.sexy | 180 | 70 | 110 |

Identifiants : 87 au total après le recentrage du 11 novembre 2026 (36 options retirées, voir docs/revue-code-2026-11-11.md), 91 après les reprises d'optimizerDuck du 20 novembre 2026.
