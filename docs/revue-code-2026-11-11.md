# Revue de code GamingTweaker (GTweak) — 11 novembre 2026

Stack réelle (vérifiée dans `src/GamingTweaker/GamingTweaker.csproj`) : C# / .NET Framework 4.8 / WPF, x64, exe portable unique, DLL embarquées en ressources (Newtonsoft.Json 13.0.3, LibreHardwareMonitorLib 0.9.6), actions en PowerShell 5.1 généré à la volée. Manifeste `requireAdministrator` en Release. 123 options (61 optimisation, 31 télémétrie, 8 bloatware, 18 nettoyage, 5 mises à jour), 365 scripts `.ps1`, 587 tests xUnit. Pas de CI, pas de signature, pas de métadonnées de version.

> **Suite donnée (même jour, branche `claude/great-euler-uvk3iv`) :** 36 options retirées (C-1, E-3, S-1 et préférences d'apparence), C-2 réglé par retrait de `opt.gpu-msi`, E-1 (`Get-GTUserTemp`), E-2 (`$GT_WingetPath` injecté par `ScriptBuilder.Header`), E-4 (fonction Defender hors du runtime), E-6 (reclassements, Spooler/StorSvc retirés, champ `recommande`), M-3 (AppData de Windows.old exclu), M-4 (anti-triche protégés dans Mode Jeu), M-12 (version, DPI PerMonitorV2, `DebugType none`, version et build Windows dans `report.txt`), `Catalogue.Validate` au démarrage, et correction du nom des manifestes (`app.manifest.xml` ≠ `app.manifest` attendu par le csproj : le build Release ne pouvait pas produire un exe `requireAdministrator`). Restent ouverts : E-5, M-1, M-2, M-5 à M-11.

Légende des constats : **[V]** fait vérifié dans le code · **[H]** hypothèse raisonnée · **[T]** à tester sur machine.

---

## 1. Résumé exécutif

**Verdict global : architecture nettement au-dessus de la moyenne des « gaming tweakers » (journal d'annulation avant chaque modification, dossier de données protégé, environnement PowerShell nettoyé, scripts verrouillés, listes blanches partout), mais le catalogue contient des options qui n'ont rien à faire dans un outil destiné à des joueurs non techniques, et deux bugs fonctionnels font échouer des options « Sûr » à coup sûr.**

Niveau de risque pour l'utilisateur final : **moyen** tant que les options Risqué restent cochables (Spectre/Meltdown, Defender, BitLocker, IPv6, svchost) ; **faible** sur le reste grâce au journal et au point de restauration.

Top 3 des problèmes :

1. **Catalogue dangereux pour la cible** : `opt.spectre-off`, `opt.defender-rt-off`, `opt.bitlocker-off` (déchiffrement irréversible du disque), `opt.vbs-off`, `opt.ipv6-off` exposent un joueur à un risque réel pour un gain FPS nul ou non mesurable sur matériel récent. Le marquage « Risqué » et la confirmation ne suffisent pas : ces options doivent sortir du catalogue ou passer derrière un mode « expert » explicite.
2. **`opt.gpu-msi` peut rendre la machine non bootable (écran noir)** : la clé `MessageSignaledInterruptProperties` est créée si elle n'existe pas (`Set-GTRegRaw` fait `New-Item -Force`), donc MSI est forcé sur un périphérique dont le pilote ne le déclare pas. C'est précisément le scénario « Code 10 / écran noir » documenté par la communauté.
3. **Deux options « Sûr » échouent à chaque exécution en production** : `cln.temp` et `cln.apps-traces` utilisent `$env:TEMP`, or `PowerShellLanceur.EnvironnementPropre` force `TEMP` sur le dossier `Run` protégé, hors liste blanche → `Remove-TweakPath` lève « Chemin refusé ». Et `winget` n'est jamais trouvé depuis les scripts (`Get-Command winget.exe` avec un PATH réduit à System32), donc `upd.apps` est toujours ignorée et l'annulation des applications retirées se termine toujours en « réinstallation manuelle ».

---

## 2. Tableau des tweaks

Colonnes : mécanisme · réversible (J = par journal, R = script revert valeurs par défaut, — = non) · efficacité réelle · risque · verdict.

### Optimisation : alimentation, matériel, jeu

| Tweak | Mécanisme | Rév. | Efficacité réelle | Risque | Verdict |
|---|---|---|---|---|---|
| opt.mouse.precision-off | HKCU MouseSpeed/Threshold = 0 + SPI_SETMOUSE | J+R | Réelle (comportement souris), pas un gain FPS | Nul | Garder |
| opt.stickykeys-off | HKCU Accessibility Flags | J+R | Réelle (confort) | Nul | Garder |
| opt.usb-power-off | WMI MSPower_DeviceEnable sur USB\* | J+R | Réelle sur déconnexions USB ; FPS : nulle | Faible (conso) | Garder |
| opt.nic-power-off | MSPower_DeviceEnable sur NIC physiques | J+R | Réelle sur réveil réseau | Faible | Garder |
| opt.usb-suspend-off | powercfg USB selective suspend = 0 | J+R | Marginale | Faible | Garder (Avancé plutôt que Sûr sur portable) |
| opt.plan-high | powercfg -duplicatescheme + setactive | J+R | Réelle mais faible sur CPU modernes (Win11 : le curseur « Mode d'alimentation » est le mécanisme officiel) | Portable : batterie, chauffe ; le curseur disparaît hors plan Balanced [T] | Modifier : détecter portable (Win32_Battery / ChassisTypes) et basculer en Avancé |
| opt.plan-ultimate | idem, GUID e9a42b02 | J+R | Marginale vs High (surtout latence idle) | idem + modern standby [T] | Garder en Avancé, même détection portable |
| opt.pcie-aspm-off | powercfg PCIe ASPM = 0 | J+R | Réelle sur micro-latences NVMe/GPU idle, invisible en jeu | Faible | Garder. Revert `2 2` faux pour AC (défaut Balanced AC = Moderate) → journal seulement |
| opt.coreparking-off | powercfg CPMINCORES = 100 | J+R | Placebo sur Win10/11 desktop (parking déjà quasi inactif) ; ne touche pas CPMINCORES1 (E-cores) | Faible | Garder en Avancé, texte honnête. Revert `4 4` arbitraire |
| opt.hibernate-off | powercfg /h off | J+R | Réelle (disque), désactive Fast Startup | Portable : perte de l'hibernation sur batterie critique | Garder |
| opt.gamemode-on | HKCU GameBar Auto/AllowAutoGameMode | J+R | Faible mais réelle (c'est le défaut de Windows) | Nul | Garder |
| opt.gamedvr-off | HKCU GameDVR_Enabled = 0 | J+R | Réelle (capture arrière-plan) | Nul | Garder |
| opt.hags | HwSchMode = 2 (build ≥ 19041) | J+R | Variable, documentée Microsoft | Faible | Garder |
| opt.fso-off | GameDVR_FSEBehaviorMode = 2 | J+R | Variable ; peut gêner overlays, Alt-Tab | Faible | Garder en Avancé |
| opt.mpo-off | Dwm OverlayTestMode = 5 | J+R | Réelle sur bugs MPO, sinon coût (flip windowed, vidéo) ; clé de debug non documentée | Moyen | Garder en Avancé, limiter aux symptômes |
| opt.gpu-msi | MSISupported = 1 sur chaque GPU PCI | J (revert refusé) | Placebo sur NVIDIA/AMD récents (déjà en MSI) | **Élevé : écran noir si clé créée sur un pilote sans MSI** | **Modifier** (ne jamais créer la clé) ou retirer |
| opt.visualfx | VisualFXSetting = 2 | J+R | Nulle en jeu (DWM composite toujours) | Nul | Garder en Avancé (change l'apparence) |
| opt.hpet | bcdedit /deletevalue useplatformclock, disabledynamictick yes | J+R partiel | Placebo sur matériel récent ; `useplatformclock` est déjà absent par défaut | BCD : clé validée par BitLocker ? [T] ; conso | Retirer ou Avancé mesuré |
| opt.vbs-off | DeviceGuard EnableVBS = 0, HVCI Enabled = 0 | J+R | Réelle (2-10 % sur CPU anciens) mais protection noyau perdue | **Élevé** ; verrou UEFI non géré (valeur écrite, VBS toujours actif, détection dit « Appliqué ») | Retirer du catalogue grand public |
| opt.spectre-off | FeatureSettingsOverride/Mask = 3 | J+R | Nulle sur CPU ≥ Zen 2 / Intel 9e gén. (mitigations matérielles) | **Critique** : vulnérabilité navigateur/JS, aucune contrepartie | **Retirer** |
| opt.defender-rt-off | Set-MpPreference -DisableRealtimeMonitoring | J+R | Nulle en FPS ; Windows réactive seul | **Critique** ; signature AMSI dans tous les scripts générés | **Retirer** |
| opt.defender-scan-cpu | Policy AvgCPULoadFactor = 5 | J+R | Réelle sur analyses planifiées | Faible (analyses très lentes) | Garder en Avancé |
| opt.bitlocker-off | Disable-BitLocker C: | — | Nulle en jeu (overhead FPS non mesurable) | **Critique** : déchiffrement irréversible, données exposées | **Retirer** |

### Optimisation : arrière-plan, services, réseau, confort

| Tweak | Mécanisme | Rév. | Efficacité | Risque | Verdict |
|---|---|---|---|---|---|
| opt.bgapps-off | Policy LetAppsRunInBackground = 2 | J+R | Faible ; casse notifications/arrière-plan des apps Store (Xbox, Phone Link) | Faible | Avancé plutôt que Sûr |
| opt.widgets-off | Policy AllowNewsAndInterests = 0 | J+R | Réelle (processus Widgets) | Nul | Garder |
| opt.sysmain / opt.wsearch | sc config manual + Stop-Service | J+R | Réelle sur HDD, nulle sur SSD | Faible | Garder en Avancé |
| opt.services | Liste blanche de 11 services → manual | J+R | Faible | **Spooler** en manuel casse l'impression (pas de trigger start) ; **StorSvc** en manuel casse le Store/USB [T] | Modifier : retirer Spooler et StorSvc de la liste |
| opt.xbox-services | 4 services Xbox → manual | J+R | Faible | Game Pass, sauvegardes cloud | Garder en Risqué (texte OK) |
| opt.startup | StartupApproved binaire (méthode Gestionnaire des tâches) | J | Réelle | Faible | Garder |
| opt.nagle-off | TcpAckFrequency/TCPNoDelay par interface | J+R | `TcpAckFrequency` documenté ; `TCPNoDelay` sous Interfaces n'est pas une clé TCP/IP documentée (MSMQ seulement) ; la plupart des jeux sont en UDP | Nul | Garder en Avancé, retirer TCPNoDelay |
| opt.netthrottle-off | NetworkThrottlingIndex = 0xFFFFFFFF | J+R | Placebo (n'agit que pendant lecture MMCSS, seuil 10 paquets/ms) | Nul | Garder ou retirer, texte honnête |
| opt.prioritysep | Win32PrioritySeparation = 0x26 | J+R | Placebo (défaut 0x2 déjà court/variable/boost) | Nul | Retirer ou mesurer |
| opt.svchost-split-off | SvcHostSplitThresholdInKB = RAM | J+R | ~100-200 Mo RAM, rien en FPS sur ≥ 16 Go | Moyen (services Xbox, stabilité) | Retirer |
| opt.delivery-opt-off | Policy DODownloadMode = 0 | J+R | Réelle (upload) | Nul | Garder |
| opt.ipv4-prefer | DisabledComponents bit 0x20 | J+R | Réelle sur FAI IPv6 défaillant | Faible | Garder |
| opt.teredo-off | Policy Teredo_State = Disabled | J+R | Nulle | Xbox Live / NAT : chat, matchmaking | Garder en Risqué ou retirer |
| opt.ipv6-off | DisabledComponents = 0xFF | J+R | Nulle (Microsoft déconseille, +5 s au boot) | Moyen | Retirer |
| opt.storagesense-off | HKCU StoragePolicy\01 = 0 | J+R | Faible | Nul | Garder |
| opt.utc-time | RealTimeIsUniversal (QWord) | J+R | Réelle (dual boot) | Heure décalée sinon | Garder en Risqué |
| opt.longpaths | LongPathsEnabled = 1 | J+R | Réelle (mods) | Nul | Garder |
| opt.bsod-detailed | CrashControl DisplayParameters/DisableEmoticon | J+R | Diagnostic | Nul | Garder |
| opt.verbose-logon | VerboseStatus = 1 | J+R | Diagnostic | Nul | Garder |
| opt.start-reco-off | PolicyManager HideRecommendedSection + **IsEducationEnvironment = 1** | J+R | Réelle, mais hack qui déclare la machine « Éducation » dans le magasin MDM | Effets de bord (Spotlight, « géré par votre organisation ») | Modifier : garder seulement la stratégie Explorer sur 23H2+, retirer IsEducationEnvironment |
| opt.classic-context-menu, taskbar-*, explorer-home-off, notifications-off, settings-home-off, lockscreen-off, login-blur-off, dark-mode, show-*, numlock-on, snap-off, scrollbars-visible, endtask-taskbar | HKCU/HKLM documentés | J+R | Confort | Nul | Garder |
| opt.explorer-autodiscovery-off | Suppression Bags/BagMRU (export .reg) + FolderType = NotSpecified | J+R partiel | Réelle sur gros dossiers | Perte des vues personnalisées (annoncé) | Garder en Avancé |

### Télémétrie

| Tweak | Mécanisme | Rév. | Efficacité | Risque | Verdict |
|---|---|---|---|---|---|
| tel.diagtrack-off | DiagTrack + dmwappushservice Disabled | J+R | Réelle (CPU/disque idle faible) | Faible | Garder |
| tel.level-min | Policies DataCollection AllowTelemetry = 0 etc. | J+R | Réelle (niveau 0 = Enterprise seulement, annoncé) | Nul | Garder |
| tel.tasks-off, wer-off, activity-off, misc-off, compat-off, insider-off, adid-off, suggestions-off, bing-off, search-off, cortana-off, sync-off, clipboard-off, ui-privacy, explorer-online-off, recent-docs-off, apps-telemetry-off, nvidia-off, office-off, edge-*, browsers-telemetry-off, brave-debloat, copilot-off, recall-off | Stratégies documentées / tâches / services | J+R | Vie privée, pas FPS | Nul à faible | Garder |
| tel.location-off | Policies + ConsentStore location Deny + lfsvc Disabled | J+R | Vie privée | Casse fuseau horaire auto, Localiser mon appareil, Météo | Avancé plutôt que Sûr |
| tel.app-access-off | 17 ConsentStore = Deny dont **broadFileSystemAccess** | J+R | Vie privée | Casse apps Store qui lisent des fichiers (Xbox app, Phone Link, Courrier) [T] | Avancé plutôt que Sûr |
| tel.ai-off | Policy Notepad + WSAIFabricSvc + Remove-AppxPackage Copilot | J partiel | Réelle | Réinstallation Store seulement | Garder en Risqué (OK) |
| tel.hosts | 36 lignes `0.0.0.0` dans hosts | J+R | Doublon des stratégies ; **Defender le détecte comme SettingsModifier:Win32/HostsFileHijack et restaure le fichier** [H fort, comportement public depuis 2020] | Alerte « Menace détectée » chez l'utilisateur, option annulée silencieusement | **Retirer** |

### Bloatware

| Tweak | Mécanisme | Rév. | Efficacité | Risque | Verdict |
|---|---|---|---|---|---|
| blt.apps / blt.apps-tierces | Remove-AppxPackage -AllUsers + provisioned, liste blanche fixe | J (winget msstore, jamais trouvé : voir C-3) | Réelle (disque, tâches de fond) | Faible ; OneDrive via OneDriveSetup /uninstall | Garder après correction winget |
| blt.edge-remove | Fichier factice SystemApps + EdgeUpdateDev\AllowUninstall + setup --uninstall | J (winget) | Nulle en jeu | Widgets, Outlook, liens Store cassés ; WU réinstalle | Garder en Risqué ou retirer |
| blt.features | DISM /Disable-Feature (IE, XPS, Fax, LPD/LPR, WorkFolders) | J | Faible | Réactivation parfois sans source | Garder en Avancé |
| blt.capabilities | DISM /Remove-Capability (IE11, ISE, QuickAssist, Steps Recorder…) | J | Faible | Réinstallation via WU | Garder en Avancé |
| blt.wpbt-off | DisableWpbtExecution = 1 | J+R | Réelle (anti-bloat constructeur) | Pilotes WPBT constructeur | Garder |
| blt.vendor-autoinstall-off | DisableCoInstallers = 1 | J+R | Réelle | Logiciels compagnons non installés | Garder |
| blt.device-metadata-off | PreventDeviceMetadataFromNetwork = 1 | J+R | Faible | Nul | Garder |

### Nettoyage

| Tweak | Mécanisme | Rév. | Efficacité | Risque | Verdict |
|---|---|---|---|---|---|
| cln.temp | Remove-TweakPath `$env:TEMP` + Windows\Temp | — | **Échoue toujours** (voir C-2) | — | Corriger |
| cln.apps-traces | idem, dossiers VS sous `$env:TEMP` | — | **Échoue toujours** | — | Corriger |
| cln.recycle | Clear-RecycleBin | — | Disque | **Données utilisateur**, classé Sûr/Recommandé | Avancé, jamais dans « Recommandé » |
| cln.logs | Logs CBS/DISM/WU + `wevtutil cl` tous journaux sauf Security | — | Disque faible | Perte du diagnostic (plantages jeu/pilote), classé Sûr | Avancé |
| cln.thumbs, cln.wu-cache, cln.crash, cln.browsers, cln.launchers, cln.recent, cln.shaders | Remove-TweakPath sur racines autorisées | — | Disque | Faible, bien bornés | Garder |
| cln.dism / cln.resetbase | DISM StartComponentCleanup [/ResetBase] | — | Disque | ResetBase : MAJ non désinstallables (annoncé) | Garder |
| cln.windows-old | takeown/icacls par élément + suppression | — | Disque | Dossiers personnels protégés mais **AppData de l'ancien profil supprimé** (sauvegardes de jeux) | Modifier : exclure `Windows.old\Users\*\AppData` |
| cln.old-drivers | pnputil /delete-driver sans /force | — | Disque | Faible (refus si utilisé) | Garder |
| cln.orphans | WindowsInstaller.Installer API + quarantaine | J (quarantaine) | Disque | Faible, bien conçu | Garder |
| cln.prefetch | Suppression *.pf | — | **Négative** (lancements plus lents) | Faible | Retirer |
| cln.registry | Uninstall orphelins + MuiCache, export .reg | J | Nulle (annoncé) | Faible | Garder ou retirer |

### Mises à jour

| Tweak | Mécanisme | Rév. | Efficacité | Risque | Verdict |
|---|---|---|---|---|---|
| upd.windows | COM Microsoft.Update.Session, pilotes/upgrades exclus | — | Réelle | Faible | Garder |
| upd.apps | winget upgrade | — | **Toujours ignorée** (C-3) | — | Corriger |
| upd.nvidia | API NVIDIA non documentée → téléchargement https nvidia.com → Authenticode « NVIDIA Corporation » → `-s -noreboot` | — | Réelle | Faible (bien verrouillé) | Garder |
| upd.wu-auto-off | Policy NoAutoUpdate = 1 | J+R | Nulle en jeu | Plus de correctifs de sécurité | Retirer ou Risqué avec « Pause » officielle à la place |
| upd.wu-drivers-off | SearchOrderConfig = 0 + ExcludeWUDriversInQualityUpdate | J+R | Réelle (évite l'écrasement du pilote GPU) | Faible | Garder |

---

## 3. Constats classés par sévérité

### Critique

**C-1. Options de sécurité désactivables par un joueur non technique** — [V]
`src/GamingTweaker/Assets/scripts/opt.spectre-off.apply.ps1:1-2`, `opt.defender-rt-off.apply.ps1:5`, `opt.bitlocker-off.apply.ps1:5`, `opt.vbs-off.apply.ps1:1-2`.
Scénario : un utilisateur charge un profil « agressif » trouvé sur un forum, confirme la boîte « Risqué » (une seule confirmation par option, puis `Confirme = true` dans `Ui/OptionViewModel.cs:80-90`) → Spectre/Meltdown désactivés sur un CPU où ils ne coûtent rien, Defender coupé, disque déchiffré. Aucun gain mesurable en jeu, exposition réelle.
Correctif : retirer `opt.spectre-off`, `opt.defender-rt-off`, `opt.bitlocker-off` du catalogue embarqué ; déplacer `opt.vbs-off` et `opt.ipv6-off` derrière un drapeau de build « expert ». Si vous tenez à les garder, exiger une saisie texte (« je comprends ») et refuser leur présence dans un profil planifié :

```csharp
// Core/Planification.cs, InstallVerrouille(), après Profils.Load
var interdites = new[] { "opt.spectre-off", "opt.defender-rt-off", "opt.bitlocker-off", "opt.vbs-off" };
var presentes = load.Profil.Options.Intersect(interdites, StringComparer.Ordinal).ToList();
if (presentes.Count > 0)
    throw new InvalidOperationException("Routine refusée : options de sécurité non planifiables : " + string.Join(", ", presentes));
```

**C-2. `opt.gpu-msi` crée la clé MSI sur des périphériques qui ne la déclarent pas** — [V]
`Assets/runtime/Common.ps1:1054-1066` (`Get-GTGpuMsiKeys` renvoie le chemin sans vérifier l'existence), `Common.ps1:119-122` (`Set-GTRegRaw` : `New-Item -Path $Path -Force`), `opt.gpu-msi.apply.ps1:3`.
Scénario : GPU ou iGPU dont le pilote n'a pas créé `Interrupt Management\MessageSignaledInterruptProperties` → la clé est créée avec `MSISupported = 1` → au redémarrage le pilote échoue (Code 10) ou écran noir ; l'annulation nécessite le mode sans échec (le script revert refuse, `opt.gpu-msi.revert.ps1:1`).
Correctif :

```powershell
# Common.ps1, Get-GTGpuMsiKeys : ne renvoyer que les clés existantes
$msi = (ConvertTo-GTHklmPath $inst.Name) + '\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties'
if (Test-Path -LiteralPath $msi) { $res += $msi }
```

```powershell
# opt.gpu-msi.apply.ps1
$keys = @(Get-GTGpuMsiKeys)
if ($keys.Count -eq 0) { Skip-GTItem 'Aucune carte graphique ne déclare la prise en charge MSI : rien n''est modifié (clé jamais créée).' }
foreach ($k in $keys) { Set-TweakRegistryIfNeeded -Path $k -Name 'MSISupported' -Type DWord -Value 1 }
```

Ajouter dans `Journal`/`Restore-GTEntry` un garde : une entrée `registry` avec `existed=false` sur un chemin `Enum\PCI\...` doit supprimer la valeur, jamais la clé entière (déjà le cas), et le `revert` doit proposer la commande mode sans échec dans son message.

### Élevé

**E-1. `cln.temp` et `cln.apps-traces` échouent systématiquement en production** — [V]
`Core/PowerShellLanceur.cs:277-278` (`TEMP`/`TMP` = dossier Run), `Common.ps1:1152-1160` (`$env:TEMP` n'est accepté que si la feuille ou le parent s'appelle Temp/Tmp), `Common.ps1:1291-1293` (`Assert-GTCleanPaths` lève « Chemin refusé »), `cln.temp.apply.ps1:2`, `cln.apps-traces.apply.ps1:11`.
Scénario : exe élevé → `$env:TEMP = C:\Program Files\GamingTweaker\Run` → hors racines → exception → option « ÉCHEC : Chemin refusé ». La variable `GT_USERTEMP` (`PowerShellLanceur.cs:294`) est préparée mais jamais lue. Les tests ne couvrent pas ce chemin (`tests/.../NettoyageTests.cs:797` vérifie seulement le drapeau `hkcu`).
Correctif :

```powershell
# Common.ps1, nouvelle aide
function Get-GTUserTemp {
    if ($env:GT_USERTEMP) { return [string]$env:GT_USERTEMP }
    if ($env:LOCALAPPDATA) { return (Join-Path $env:LOCALAPPDATA 'Temp') }
    return $null
}
# Get-GTDefaultAllowedRoots : remplacer le bloc $tmp = [string]$env:TEMP par
$tmp = Get-GTUserTemp
```

```powershell
# cln.temp.apply.ps1
$gtT = Get-GTUserTemp
if (-not $gtT) { Skip-GTItem 'Dossier temporaire de l''utilisateur introuvable.' }
Remove-TweakPath -Path @($gtT, (Join-Path $env:SystemRoot 'Temp')) | Out-Null
# cln.apps-traces.apply.ps1 : Join-Path (Get-GTUserTemp) $gtT
```

Ajouter un test qui exécute `cln.temp` via `Runner` avec un faux profil (`GT_USERTEMP` pointant sur un dossier temporaire).

**E-2. `winget` jamais trouvé depuis les scripts** — [V]
`Common.ps1:1607-1612` (`Get-Command winget.exe`), `PowerShellLanceur.cs:274` (PATH = System32, Windows, Wbem, WindowsPowerShell). `winget.exe` est un alias d'exécution sous `%LOCALAPPDATA%\Microsoft\WindowsApps`, absent de ce PATH.
Conséquences : `upd.apps` → « winget est introuvable » à chaque fois ; `Restore-GTItem` pour `blt.apps`, `blt.apps-tierces`, `blt.edge-remove`, OneDrive → « réinstallation manuelle » ; l'annulation annoncée dans l'aperçu est fausse. Le côté C# (`Core/SystemExe.cs:292-302`) résout correctement winget via `Get-AppxPackage`.
Correctif : transmettre le chemin résolu côté C# au script, comme `GT_DownloadRoot` :

```csharp
// Core/ScriptBuilder.cs, Header()
var winget = SystemExe.Winget();
sb.Append("$GT_WingetPath = ").Append(winget == null ? "$null" : Quote(winget)).Append("\r\n");
```

`Get-GTWingetPath` lit déjà `$GT_WingetPath` en priorité ; supprimer le repli `Get-Command` (il ne trouvera jamais rien avec le PATH nettoyé et pourrait exécuter un `winget.exe` planté dans System32 par un autre admin).

**E-3. `tel.hosts` sera annulé par Defender et déclenche une alerte** — [H fort] [T]
`tel.hosts.apply.ps1:1-37`. Depuis mi-2020, Defender classe l'ajout d'entrées `0.0.0.0` vers des domaines Microsoft (`vortex`, `watson`, `settings-win`…) en `SettingsModifier:Win32/HostsFileHijack` et restaure le fichier. L'option passe « Appliquée », puis la détection repasse « Non appliquée » et l'utilisateur voit une menace. L'outil refuse à juste titre de toucher aux exclusions Defender.
Correctif : retirer l'option (les stratégies `tel.level-min`, `tel.wer-off`, `tel.diagtrack-off` couvrent les mêmes flux) ou, au minimum, détecter Defender actif et ignorer :

```powershell
# tel.hosts.apply.ps1, en tête
try { if ((Get-MpComputerStatus -ErrorAction Stop).RealTimeProtectionEnabled) {
    Skip-GTItem 'Microsoft Defender détecte ce blocage comme une modification malveillante du fichier hosts et le défait : option ignorée.' } } catch { }
```

**E-4. `Set-TweakDefenderRealtime` est présent dans chaque script généré (détection comprise)** — [V] [T]
`Common.ps1:218-224` injecté par `ScriptBuilder.Header()` (`Core/ScriptBuilder.cs:63`) dans détection, estimation, exécution et annulation. AMSI voit donc `Set-MpPreference -DisableRealtimeMonitoring` à chaque lancement. Si une signature comportementale réagit (fréquent sur des scripts non signés avec ce motif), la détection au démarrage est tuée et toutes les options passent « Inconnu » sans explication.
Correctif : retirer la fonction du runtime commun (voir C-1) ; de manière générale, scinder `Common.ps1` en un noyau (journal, registre, services, tâches) et des modules chargés seulement par les options qui en ont besoin (`Defender`, `BCD`, `DISM`, `winget`, `NVIDIA`).

**E-5. Désactivation VBS sans gestion du verrou UEFI ni vérification de l'état réel** — [V]
`opt.vbs-off.apply.ps1`, `opt.vbs-off.detect.ps1`. La détection lit le registre, pas l'état du système. Avec `RequirePlatformSecurityFeatures` + verrou UEFI (courant sur portables OEM et machines gérées), les valeurs sont écrites mais VBS reste actif ; le rapport dit « Appliquée » et la redétection confirme à tort.
Correctif :

```powershell
# opt.vbs-off.detect.ps1
try {
    $dg = Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' -ClassName Win32_DeviceGuard -ErrorAction Stop
    if ($dg.VirtualizationBasedSecurityStatus -eq 0 -and -not ($dg.SecurityServicesRunning -contains 2)) { 'Applied' } else { 'NotApplied' }
} catch { 'Unknown' }
```

Et dans `apply`, après écriture, lire `HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Locked` (ou `Win32_DeviceGuard.RequiredSecurityProperties`) et terminer par `Set-GTItemNote 'Redémarrage requis ; si VBS reste actif, un verrou UEFI impose une confirmation au démarrage.'`.

**E-6. Recommandé / Sûr inclut des actions destructrices ou qui cassent des usages courants** — [V]
`Ui/MainViewModel.cs:203-206` (`ApplyRecommended` coche toute option `Sur` du menu), catalogue : `cln.recycle`, `cln.logs` (Sûr), `tel.location-off`, `tel.app-access-off` (Sûr), `opt.plan-high`, `opt.bgapps-off` (Sûr), `opt.services` propose `Spooler` et `StorSvc`.
Scénario : « Recommandé » dans Nettoyage vide la Corbeille (fichiers supprimés par erreur la veille) et efface les journaux d'événements que le support demandera après un plantage ; dans Télémétrie, bloque l'accès fichiers des apps Store et la localisation (heure/fuseau). `Spooler` en manuel → « Le spouleur d'impression ne s'exécute pas ».
Correctif : reclasser en Avancé (voir tableau), retirer `Spooler` et `StorSvc` de `ServicesProvider.Services` (`Lists/ServicesProvider.cs:76`) et de `opt.services.apply.ps1:1`, et faire de « Recommandé » une liste explicite par menu plutôt qu'un filtre sur le niveau :

```csharp
public static readonly IReadOnlyDictionary<string, string[]> Recommandes = new Dictionary<string, string[]> {
    { "Optimisation", new[] { "opt.gamedvr-off", "opt.gamemode-on", "opt.usb-power-off", "opt.nic-power-off", "opt.delivery-opt-off", "opt.longpaths" } },
    { "Nettoyage",    new[] { "cln.thumbs", "cln.wu-cache", "cln.crash", "cln.browsers", "cln.launchers", "cln.dism" } },
    // ...
};
public void ApplyRecommended(string menu) {
    string[] ids; if (!Recommandes.TryGetValue(menu, out ids)) return;
    foreach (var o in Options.Where(o => o.Menu == menu && ids.Contains(o.Id))) o.IsChecked = true;
}
```

### Moyen

**M-1. Verrou d'instance détournable par un utilisateur standard (déni de service local)** — [V]
`Core/SingleInstance.cs:172-175` : mutex `Global\GamingTweaker.Run` sans DACL ; une `UnauthorizedAccessException` est traitée comme « déjà en cours ». N'importe quel processus non élevé peut créer ce mutex et bloquer l'interface (« Une autre exécution est en cours ») et la routine hebdomadaire (code 2) indéfiniment.
Correctif :

```csharp
public static IDisposable TryAcquire(string nom = null)
{
    var name = nom ?? NomParDefaut;
    var sec = new MutexSecurity();
    sec.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), MutexRights.FullControl, AccessControlType.Allow));
    sec.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), MutexRights.FullControl, AccessControlType.Allow));
    Mutex m; bool created;
    try { m = new Mutex(false, name, out created, sec); }
    catch (UnauthorizedAccessException)
    {
        // Mutex créé par un autre compte (non administrateur) : nom usurpé, on utilise un nom dérivé réservé aux administrateurs.
        try { m = new Mutex(false, name + ".admin", out created, sec); } catch (UnauthorizedAccessException) { return null; }
    }
    if (!created) { m.Dispose(); return null; }
    return m;
}
```

**M-2. Démarrage lent : toutes les détections dans un seul processus, sans cache** — [V] [T]
`Core/Detection.cs:15-63` (120 s max, puis tout « Inconnu »), scripts de détection : ~25 appels `Get-ScheduledTask` (0,2-0,5 s chacun), 2 `bcdedit /enum`, 4 `powercfg /qh`, `Get-MpComputerStatus`, `Get-BitLockerVolume`, `Get-AppxPackage -AllUsers` (`tel.ai-off.detect.ps1:7`), 2 énumérations WMI `MSPower_DeviceEnable`. Estimation : 15-40 s avant que les états s'affichent, et à nouveau après chaque exécution (`Ui/MainWindow.xaml.cs:93`).
Correctif : mettre en cache dans le script de détection (même modèle que `GT_CapabilityCache`) :

```powershell
function Get-GTTaskEnabled([string]$Path) {
    if ($null -eq $script:GT_TaskCache) {
        $script:GT_TaskCache = @{}
        foreach ($t in @(Get-ScheduledTask -ErrorAction SilentlyContinue)) { $script:GT_TaskCache[($t.TaskPath + $t.TaskName)] = [bool]($t.State -ne 'Disabled') }
    }
    if ($script:GT_TaskCache.ContainsKey($Path)) { return $script:GT_TaskCache[$Path] }
    return $null
}
```

Même chose pour `Get-GTPowerDevices` et `bcdedit /enum {current}` (une lecture par processus). Invalider le cache après une écriture (`Set-TweakTask`).

**M-3. `cln.windows-old` supprime `AppData` de l'ancien profil** — [V]
`Common.ps1:1092-1093` (noms protégés : Documents, Desktop, Pictures… mais pas AppData), `cln.windows-old.apply.ps1:3`. Beaucoup de jeux stockent les sauvegardes dans `%LOCALAPPDATA%` ou `%APPDATA%` ; la description promet que « les dossiers personnels sont conservés ».
Correctif : exclure explicitement les AppData de l'ancienne installation :

```powershell
$gtOld = $env:SystemDrive + '\Windows.old'
$gtExcl = @(Get-ChildItem -LiteralPath (Join-Path $gtOld 'Users') -Directory -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'AppData' })
Remove-TweakPath -Path $gtOld -RemoveRoot -RemoveLinks -TakeOwnership -ExcludePath $gtExcl | Out-Null
```

Et corriger la description (« les dossiers personnels et AppData de l'ancien profil sont conservés ; Windows.old ne sera donc pas vide »).

**M-4. Mode Jeu peut arrêter les services et processus anti-triche** — [V]
`Core/ModeJeu.cs:80-85` (liste des services protégés) et `:73-78` (processus) ne contiennent aucun anti-triche. `ModeJeuListes.ListerServices` propose donc `vgc` (Vanguard), `EasyAntiCheat`, `EasyAntiCheat_EOS`, `BEService`, `FACEIT`, `ESEADriver2`, `PnkBstrA/B` ; `ListerProcessus` propose `vgtray`, `FACEITService`… Arrêter `vgc` oblige à redémarrer pour jouer à Valorant ; arrêter un service anti-triche pendant une partie déconnecte le joueur et, pour certains, déclenche une infraction.
Correctif : étendre les listes protégées :

```csharp
public static readonly string[] ServicesProteges = { /* existants */,
    "vgc", "vgk", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEDaisy", "FACEIT", "ESEADriver2", "PnkBstrA", "PnkBstrB", "nProtect", "XignCode", "GamingServices", "GamingServicesNet" };
public static readonly string[] ProcessusProteges = { /* existants */,
    "vgtray", "vgc", "FACEITService", "FACEIT", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "PnkBstrA", "PnkBstrB" };
```

**M-5. Revert hardcodés qui ne correspondent pas aux valeurs d'origine** — [V]
`opt.pcie-aspm-off.revert.ps1` (`2 2` : Balanced AC = 1 Moderate), `opt.coreparking-off.revert.ps1` (`4 4` : varie selon OEM), `opt.mouse.precision-off.revert.ps1` (`1/6/10` : standard), `opt.services.revert.ps1` (`MapsBroker AutomaticDelayed`, `StorSvc AutomaticDelayed` : varient selon version). Ces scripts ne servent qu'en l'absence de journal (`Core/Restauration.cs:110-121`), mais « Tout restaurer » les exécute dès qu'une option est détectée « Appliquée » sans journal (`Ui/MainViewModel.cs:398`), par exemple si l'utilisateur avait appliqué le réglage lui-même avant d'installer l'outil.
Correctif : pour les plans d'alimentation, utiliser la valeur par défaut du plan lue par `powercfg /qh` (`Default AC/DC Setting Index` est affiché par `/qh`) plutôt qu'une constante ; sinon `Skip-GTItem 'Valeur d''origine inconnue sans journal'` comme le fait déjà `opt.gpu-msi.revert.ps1`.

**M-6. `opt.start-reco-off` déclare la machine « environnement Éducation » dans le magasin MDM** — [V]
`opt.start-reco-off.apply.ps1:2` écrit `HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Education\IsEducationEnvironment = 1`. Ce n'est pas une stratégie destinée à être écrite à la main : effets de bord sur Spotlight (annoncé), sur des fonctions Store/Éducation, et le magasin `PolicyManager\current` est régénéré par le moteur MDM (la valeur peut disparaître ou générer des erreurs d'enrôlement). Sur 23H2+/24H2, `HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer\HideRecommendedSection = 1` suffit.
Correctif : ne garder que la stratégie Explorer, gérer la compat `windows: ["11"]` + build ≥ 22631.

**M-7. Pas de désinstallation / nettoyage de l'empreinte** — [V]
`%ProgramFiles%\GamingTweaker\{Machines,Quarantaine,Run,Telechargements,App}`, tâche `\GamingTweaker\Routine` élevée, dossier de tâches `\GamingTweaker` non supprimé (`Core/Planification.cs:384`). L'utilisateur qui supprime l'exe laisse une tâche élevée hebdomadaire qui continue d'appliquer un profil, et des Go en quarantaine.
Correctif : ajouter `GamingTweaker.exe --uninstall` (sous verrou) : `Planification.Remove()`, suppression du dossier `\GamingTweaker` via `ITaskFolder.DeleteFolder`, proposition de « Tout restaurer » puis suppression de la racine protégée ; exposer un bouton dans Historique.

**M-8. Point de restauration : logique inversée et absent en routine** — [V]
`Core/RunPipeline.cs:89-98` : créé seulement si une option réversible est cochée ; en mode silencieux, l'échec est ignoré (`continuer = req.Silent`). Un profil de routine « bloatware + DISM + Windows.old » tourne sans aucun filet (le point de restauration n'aide pas pour les fichiers, mais il aide pour Appx/DISM/services). La limite 24 h de `Checkpoint-Computer` n'est pas contournée (`SystemRestorePointCreationFrequency`).
Correctif : créer le point dès qu'une option touche HKLM, un service, DISM ou Appx (ajouter un champ `systeme: true` au catalogue), et poser temporairement `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore\SystemRestorePointCreationFrequency = 0` avant `Checkpoint-Computer`, puis le retirer.

**M-9. Détection « Inconnu » pour toutes les options à liste** — [V]
`opt.services.detect.ps1`, `opt.xbox-services`, `opt.startup`, `blt.apps`, `blt.features`, `blt.capabilities`, `upd.*` renvoient `'Unknown'` ; `cln.*` renvoient `'NotApplied'`. Conséquence dans `RunPipeline.ExecuteLocked` : jamais « déjà appliquée », jamais dans `Retablies`, et la redétection après exécution (`RunPipeline.cs:164-171`) ne vérifie rien pour elles. La promesse « vérification de l'effet réel » ne tient que pour les options à valeur fixe.
Correctif : pour les listes, détecter sur la sélection courante (passer `$Selection` au script de détection comme à l'application) ; `Get-GTServiceManualState $Selection` existe déjà.

**M-10. Pas de détection portable / machine gérée (domaine, Intune)** — [V]
`Core/SystemInfo.cs` ne lit ni `Win32_Battery`/`ChassisTypes`, ni l'état domaine/MDM (`dsregcmd`, `HKLM\SOFTWARE\Microsoft\Enrollments`). Sur une machine gérée, les stratégies écrites sous `HKLM\SOFTWARE\Policies` sont écrasées au prochain rafraîchissement GPO (le journal contient alors une valeur « after » que le conflit `UndoService.FindConflicts` signalera… à l'annulation seulement).
Correctif : ajouter `EstPortable` et `EstGere` à `MachineFacts`, afficher un bandeau et reclasser les options d'alimentation ; refuser la routine sur machine gérée.

**M-11. Localisation : `schtasks` lu en page de code OEM, messages de `reg.exe`/`sc.exe` concaténés en clair** — [V]
`Core/Planification.cs:417-421` (`OEMCodePage` : correct), `Planification.SuppressionReussie` (`Planification.cs:205-210`) ne reconnaît que FR/EN (« introuvable », « cannot find ») : sur un Windows DE/ES la suppression d'une tâche déjà absente est signalée comme un échec. `powercfg`, `bcdedit` sont parsés de façon indépendante de la langue (bien). `Get-GTActiveScheme` suppose un GUID dans la sortie (OK).
Correctif : utiliser l'API COM `Schedule.Service` pour la suppression comme pour la création (`ITaskFolder.DeleteTask`), et tester `HRESULT 0x80070002` plutôt que du texte.

**M-12. Pas de métadonnées de version, pas de DPI awareness, pas de CI** — [V]
`GamingTweaker.csproj` : aucun `Version`/`FileVersion`/`ApplicationIcon`/`Deterministic`, `app.manifest.xml` sans `<dpiAware>`/`<dpiAwareness>` (WPF 4.8 = System DPI → flou sur écran secondaire 4K), pas de `.github/workflows`. Le rapport `report.txt` ne contient pas la version de l'outil ni le build Windows : impossible de corréler un bug de support.
Correctif :

```xml
<PropertyGroup>
  <Version>0.9.0</Version>
  <AssemblyVersion>0.9.0.0</AssemblyVersion>
  <FileVersion>0.9.0.0</FileVersion>
  <Deterministic>true</Deterministic>
  <ApplicationIcon>Assets\app.ico</ApplicationIcon>
</PropertyGroup>
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
</PropertyGroup>
```

```xml
<!-- app.manifest.xml -->
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
```

Et dans `RunPipeline.WriteReports` : `sb.AppendLine("GamingTweaker " + typeof(RunPipeline).Assembly.GetName().Version + " — Windows build " + build)`.

### Faible

**F-1. Lignes `catch { }` vides** — [V] 47 occurrences (`grep`), dont `AssemblyLoader`, `DemarrageSur.RestreindreRechercheDll`, `ElevationCheck`, `Planification.RemoveVerrouille`. Acceptable pour les appels « meilleur effort », mais aucune trace n'est écrite : un `Journal.Trace()` fichier (`%ProgramFiles%\GamingTweaker\Machines\<id>\app.log`, rotation 1 Mo) permettrait au support de voir pourquoi `SameUserAsDesktop` renvoie faux ou pourquoi `SetDefaultDllDirectories` échoue.

**F-2. `opt.nagle-off` écrit `TCPNoDelay` sous `Tcpip\Parameters\Interfaces`** — [V] clé non documentée pour la pile TCP (documentée pour MSMQ seulement). Garder `TcpAckFrequency` seul, décrire l'option comme « jeux TCP uniquement ».

**F-3. Profils modifiables par un utilisateur standard** — [V] `Core/DataPaths.cs:113` place `Profils\` à côté de l'exe s'il est inscriptible (Téléchargements). Un programme tournant sous le compte standard peut y ajouter `cln.windows-old` ou `blt.features` (Avancé, pas de confirmation) ; l'utilisateur verra l'aperçu mais un non-technicien clique « LANCER ». La routine est protégée (copie dans `App\`). Suggestion : afficher en rouge dans l'aperçu les options Avancé absentes de la sélection précédente du même profil (diff), ou stocker un hachage du profil dans `last-applied.json`.

**F-4. `RestreindreRechercheDll` arrive après le chargement du CLR et de WPF** — [H] `Program.cs:27` : les DLL natives chargées avant `Main` (CLR, `PresentationNative`, `wpfgfx`, `d3dcompiler_47.dll`) suivent l'ordre de recherche standard ; le contrôle `DllsEnConflit` ne couvre que les DLL managées embarquées. Mitigation réaliste : documenter « copiez l'exe dans un dossier non inscriptible » ou proposer à la première exécution de se copier dans `%ProgramFiles%\GamingTweaker\App` et de se relancer de là (le code de `Planification.InstallVerrouille` fait déjà l'essentiel). À vérifier avec Process Monitor sur `NAME NOT FOUND` dans le dossier de l'exe.

**F-5. Faux positifs antivirus probables** — [H] exe non signé, non versionné, `requireAdministrator`, DLL embarquées chargées via `Assembly.Load(byte[])` (`Core/AssemblyLoader.cs:136`), PowerShell `-ExecutionPolicy Bypass`, `Set-MpPreference -DisableRealtimeMonitoring` dans chaque script, `taskkill /T /F`, écriture dans `hosts`, création d'une tâche élevée. Chaque élément est légitime ; ensemble, c'est le profil d'un « HackTool ». Retirer C-1/E-3/E-4 réduit nettement la surface ; la signature Authenticode (certificat OV ~300 €/an) règle SmartScreen à terme.

**F-6. `Journal.Read` lève sur une ligne corrompue au milieu, mais `HistoriqueViewModel.LireSession` l'affiche seulement comme « journal illisible »** — [V] correct, mais aucun outil de réparation : proposer « Ouvrir le journal » pour le support.

**F-7. `Mesures` et `Capteurs` : `DispatcherTimer` 2 s pendant que le menu est visible** — [V] `Ui/InfoViewModel.cs:198-199` : `LibreHardwareMonitor.Update()` toutes les 2 s lit SMBus/EC via PawnIO ; arrêté hors du menu (bien). Aucun processus résident en dehors de la tâche hebdomadaire : conforme à l'attente.

**F-8. Nomenclature et dette** — [V] `Common.ps1` fait 1 945 lignes monolithiques ; `MainViewModel` 560 lignes ; mélange FR/EN dans les identifiants ; `Catalogue.Validate` n'est pas appelé à l'exécution (seulement en test). Appeler `Validate` dans `LoadEmbedded` et échouer au démarrage sur erreur.

### Suggestions

- **S-1** Mesurer avant de garder : `opt.prioritysep`, `opt.netthrottle-off`, `opt.coreparking-off`, `opt.hpet`, `opt.svchost-split-off` reposent sur des croyances de forums. Le module `Mesures` (DPC, interruptions) ne mesure pas les FPS : ajouter un relevé PresentMon/`CapFrameX` optionnel ou retirer ces options.
- **S-2** `tel.level-min` : sur Home/Pro, proposer `AllowTelemetry = 1` (Required) avec un texte honnête plutôt que 0 (ignoré).
- **S-3** `blt.edge-remove` : hors EEE, Windows Update réinstalle Edge ; prévenir et n'afficher l'option que si `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\International\Geo\Nation` est dans l'EEE.
- **S-4** `upd.nvidia` : conserver l'installateur vérifié (hash SHA-256 dans le rapport) et proposer « installation propre » (`-clean`) en option Avancé.
- **S-5** Ajouter `Testable=false` → exécution refusée dans la routine (aujourd'hui seulement exclu du cycle Sandbox).

---

## 4. Ce qui est bien fait

- **Journal avant modification, forcé sur disque** (`Common.ps1:48-71`), marqueurs `undone` ciblés, troncature du fragment final, conflits détectés avant annulation (`Core/UndoService.cs:58-90`). C'est le cœur et il est solide.
- **Dossier de données protégé** (`Core/DossierProtege.cs`) : création atomique avec DACL, refus sur lien/jonction/lien physique/volume différent/propriétaire non fiable, parcours par poignée. Rare à ce niveau.
- **Lancement PowerShell** : chemin absolu System32, environnement reconstruit depuis les API (`PowerShellLanceur.EnvironnementPropre`), `PSModulePath` verrouillé, scripts verrouillés `FileShare.Read` jusqu'à la fin, `-NoProfile -NonInteractive`.
- **Aucune concaténation de commande** : identifiants validés `^[a-z0-9][a-z0-9.\-]*$`, sélections passées en JSON dans un here-string, listes blanches côté script (`Resolve-GTListSelection`), `Quote()` qui double aussi les apostrophes typographiques.
- **Téléchargement NVIDIA** : https + domaine, dossier réservé aux administrateurs, fichier verrouillé puis signature Authenticode vérifiée avant exécution.
- **Pas de contournement** de Tamper Protection, de GPO, d'UAC ; `requireAdministrator` seulement en Release ; URL ouvertes via `explorer.exe` pour ne pas lancer le navigateur élevé.
- **Routine** : copie de l'exe et du profil dans un dossier protégé, XML passé en mémoire à `RegisterTask`, refus si le compte élevé n'est pas celui du bureau, `%` refusé dans le nom de profil.
- **Nettoyage** : racines autorisées, noms protégés, liens jamais suivis, `takeown` limité à `Windows.old` et par élément, orphelins MSI via l'API officielle avec quarantaine.
- **Tests** : 587 tests, parseur PowerShell sur chaque script, cycle Sandbox détecter/appliquer/annuler, tests de cohérence liste C# ↔ liste PowerShell.

---

## 5. Plan d'action priorisé

Quick wins (1-2 jours) :
1. Retirer `opt.spectre-off`, `opt.defender-rt-off`, `opt.bitlocker-off`, `tel.hosts`, `cln.prefetch` ; sortir `Set-TweakDefenderRealtime` de `Common.ps1` (C-1, E-3, E-4).
2. `Get-GTGpuMsiKeys` : ne renvoyer que les clés existantes (C-2).
3. `GT_USERTEMP` dans `Get-GTDefaultAllowedRoots` + `cln.temp`/`cln.apps-traces` (E-1) ; `$GT_WingetPath` injecté par `ScriptBuilder.Header` (E-2).
4. Retirer `Spooler`/`StorSvc` de la liste des services ; reclasser `cln.recycle`, `cln.logs`, `tel.location-off`, `tel.app-access-off`, `opt.bgapps-off`, `opt.plan-high` en Avancé ; « Recommandé » = liste explicite (E-6).
5. Services/processus anti-triche protégés dans Mode Jeu (M-4).
6. Version, `Deterministic`, DPI PerMonitorV2, build Windows dans `report.txt` (M-12).

Chantiers (1-3 semaines) :
7. Détection réelle VBS via `Win32_DeviceGuard` et verrou UEFI (E-5) ; passer `opt.vbs-off`/`opt.ipv6-off` en mode expert.
8. Cache `Get-ScheduledTask`/`powercfg`/`bcdedit` dans la détection ; détection pour les options à liste (M-2, M-9).
9. `MachineFacts.EstPortable`/`EstGere` et règles associées (M-10) ; refus de routine sur machine gérée.
10. `--uninstall` + suppression du dossier de tâches + bouton dans Historique (M-7).
11. Mutex avec DACL (M-1) ; suppression de tâche par COM (M-11).
12. Scinder `Common.ps1` en modules chargés à la demande ; appeler `Catalogue.Validate` au démarrage ; journal applicatif (F-1, F-8).
13. CI GitHub Actions `windows-latest` : `dotnet build -c Release`, `dotnet test`, artefact exe, hash SHA-256 publié ; signature Authenticode quand la distribution dépassera le cercle privé (le README et la spec disent « usage privé », mais `THIRD-PARTY-NOTICES.md` prévoit déjà la diffusion AGPL).

---

## 6. Tests manquants (VM)

| Scénario | Attendu |
|---|---|
| Windows 11 24H2 Home, Defender actif, exe dans Téléchargements : démarrage | Détection en moins de 10 s, aucune option « Inconnu », aucune alerte Defender (E-4) |
| Windows 10 22H2 Pro FR : `cln.temp`, `cln.apps-traces` | Option « Appliquée » avec taille libérée (E-1) |
| `upd.apps` avec winget installé : Spotify/Discord en attente | Mises à jour installées ; `blt.apps` puis annulation : réinstallation Store réussie (E-2) |
| `tel.hosts` sur Defender à jour | Fichier hosts intact 5 min après, alerte dans Sécurité Windows (E-3) |
| GPU avec clé MSI absente (VM Hyper-V, iGPU Intel ancien) : `opt.gpu-msi` | Option ignorée, aucune clé créée, redémarrage OK (C-2) |
| Portable OEM avec VBS verrouillé UEFI : `opt.vbs-off` | « Non appliquée » après redémarrage, note explicite (E-5) |
| Session non administrateur, double-clic sur l'exe | UAC ; refus propre si annulé ; `--profile --silent` → code 2 et message |
| Élévation avec un autre compte admin (« Exécuter en tant que ») | Bandeau, options HKCU ignorées, routine refusée |
| Machine jointe au domaine + GPO `AllowTelemetry` | Option « Appliquée » puis GPO écrase ; conflit signalé à l'annulation ; bandeau « machine gérée » (M-10) |
| Windows FR, nom d'utilisateur « Éric Dupont », profil OneDrive (Documents redirigés) | Nettoyage navigateurs/caches OK ; `Documents` jamais touché ; chemins avec accents dans le journal relisibles |
| Système installé sur D: (`%SystemDrive%` = D:) | `Windows.old`, `Prefetch`, `hosts` résolus sur D: ; `DossiersSysteme()` cohérent |
| Kill de `GamingTweaker.exe` pendant `blt.features` (DISM) | Session « interrompue », journal `dismfeature` rejouable, DISM pas corrompu |
| Coupure pendant `cln.windows-old` | Pas de boucle takeown, dossier partiel, aucune donnée hors Windows.old touchée |
| « Tout restaurer » après 3 sessions dont une tronquée | Toutes options Télémétrie « Non appliquée », `last-applied.json` vidé |
| Routine hebdomadaire × 2 le même jour | Second lancement : « LIMIT » point de restauration, exécution poursuivie, rapport mentionne l'absence |
| BitLocker actif (TPM seul) + `opt.hpet` | Pas de demande de clé de récupération au redémarrage [T] |
| Valorant/Vanguard, FACEIT AC, BF6/Javelin avec PawnIO installé et `opt.vbs-off` | Jeu lance sans avertissement ; sinon documenter l'incompatibilité |
| Écran 4K 150 % + écran 1080p 100 % | Interface nette sur les deux (DPI) |
| Suppression de l'exe sans « Supprimer la routine » | Tâche toujours planifiée : à documenter ou corriger (M-7) |

---

## 7. Questions ouvertes

1. **Distribution réelle** : README/spec disent « usage personnel, exe non signé » ; la demande de revue parle d'utilisateurs joueurs non techniques. Si l'exe sort du cercle privé, AGPL-3.0 (privacy.sexy) impose la publication des sources, et la signature devient nécessaire.
2. **LibreHardwareMonitorLib 0.9.6** : je n'ai pas pu inspecter le paquet hors ligne. À confirmer qu'il n'embarque plus `WinRing0x64.sys` (pilote vulnérable, bloqué par HVCI et par plusieurs anti-triche) et que seul PawnIO est utilisé.
3. **PawnIO et anti-triche** : comportement de Vanguard/FACEIT face au pilote PawnIO chargé pendant une partie, non vérifié.
4. **BCD et BitLocker** : `disabledynamictick`/`useplatformclock` ne figurent pas dans le profil de validation BCD par défaut, mais ce n'est pas testé ici.
5. **Build/publication** : aucun script de publication (`dotnet publish`, ILMerge non utilisé, DLL embarquées par cible MSBuild custom). Où est produit l'exe distribué, par qui, avec quel hash ?
6. **Tests non exécutés dans cette revue** : environnement Linux, pas de `dotnet`/PowerShell Windows ; les 587 tests n'ont pas été lancés. Les constats « [V] » viennent de la lecture du code, pas d'une exécution.
7. **`opt.app-access-off` et Xbox app / Game Pass** : impact de `broadFileSystemAccess = Deny` sur l'installation de jeux Game Pass non vérifié.
