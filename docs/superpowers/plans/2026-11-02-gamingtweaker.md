# GamingTweaker : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construire un exe portable qui exécute uniquement les optimisations, nettoyages et mises à jour cochés, avec annulation, profils et routines.

**Architecture:** Une application WPF sert d'orchestrateur. Un catalogue JSON intégré décrit chaque option et ses scripts PowerShell. Un pipeline unique, partagé par l'interface et `--silent`, assemble le script, enregistre l'état pour l'annulation, exécute et produit le rapport.

**Tech Stack:** C#, .NET Framework 4.8, WPF, x64, PowerShell 5.1, Newtonsoft.Json, LibreHardwareMonitorLib, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-02-gamingtweaker-design.md`

## Global Constraints

- Windows 10 version 1903 ou plus récente et Windows 11, x64. Interface en français uniquement.
- Un seul exe : toutes les DLL sont intégrées en ressources et résolues par `AppDomain.AssemblyResolve`. LibreHardwareMonitor n'est chargé qu'à l'activation des capteurs.
- Manifeste `requireAdministrator`. PowerShell lancé avec `-NoProfile -ExecutionPolicy Bypass` pour ce seul processus.
- Fichiers `.ps1` générés en UTF-8 **avec BOM**. Sortie lue en UTF-8.
- Les tests ne modifient que `HKCU:\Software\GamingTweakerTest` et le dossier temporaire. Toute action réelle sur le système se teste dans Windows Sandbox.
- Jamais de redémarrage automatique. Jamais de WinRing0. Les profils ne contiennent aucune commande.
- `Ui` ne contient aucune logique système.
- Git local uniquement, sans `push`. Chaque message de commit se termine par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Élévation sous un autre compte administrateur :** `HKCU` pointe vers le mauvais utilisateur. Attendu : avertissement, options `HKCU` ignorées et signalées (test en Task 6).
2. **Chemin d'exe ou nom d'utilisateur avec accents, espaces ou apostrophe :** attendu : exécution normale, texte non corrompu (test en Task 3).
3. **Journal impossible à écrire (disque plein, droits) :** attendu : l'option échoue et ne modifie rien (test en Task 4).
4. **Profil corrompu, vide ou de version future :** attendu : message clair en interface, code de sortie `2` en routine, sans plantage (test en Task 5).
5. **Deux instances simultanées (interface et routine, ou double clic) :** attendu : une seule exécution ; la seconde refuse et renvoie `2` en routine (test en Task 6).

---

## Structure des fichiers

```text
GamingTweaker.sln
src/GamingTweaker/
  Core/        Catalogue, Compatibilite, SystemInfo, ScriptBuilder, Runner, Journal,
               UndoService, Profils, RunPipeline, DataPaths, Planification, Mesures, ModeJeu
  Lists/       IListProvider et fournisseurs (winget, WU, appx, services, démarrage)
  Info/        InfoCollector, Capteurs
  Ui/          MainWindow, vues, ViewModels
  Assets/catalog/*.json     un fichier par menu
  Assets/scripts/<id>.{detect,apply,revert,estimate}.ps1
  Assets/runtime/Common.ps1 fonctions communes
tests/GamingTweaker.Tests/
tools/sandbox/              cycle de test en Windows Sandbox
docs/checklist-premier-pc.md
```

---

### Task 1: Socle, détection de la machine, compatibilité

**Files:**
- Create: `GamingTweaker.sln`, `.gitignore`, `src/GamingTweaker/GamingTweaker.csproj`, `src/GamingTweaker/app.manifest`, `src/GamingTweaker/Core/AssemblyLoader.cs`, `Core/Models.cs`, `Core/SystemInfo.cs`, `Core/Compatibilite.cs`
- Test: `tests/GamingTweaker.Tests/CompatibiliteTests.cs`

**Interfaces:**
- Produces: `record`-like classes `MachineFacts { string WindowsMajor; string Edition; string CpuVendor; List<string> GpuVendors; string MachineName; string MachineGuid; }` et `CompatRule { List<string> Windows, Editions, Cpu, Gpu; }`, avec liste vide = tout accepté.
- Produces: `MachineFacts SystemInfo.Detect()`, `bool Compatibilite.IsCompatible(CompatRule rule, MachineFacts facts)`, `AssemblyLoader.Register()`.

- [ ] **Step 1 : Initialiser `git init`, la solution, les projets et le `.csproj`.** SDK-style, `net48`, `UseWPF`, `PlatformTarget` x64, `Microsoft.NETFramework.ReferenceAssemblies`. Une cible MSBuild ajoute chaque `ReferenceCopyLocalPaths` `.dll` en `EmbeddedResource` et empêche leur copie.
- [ ] **Step 2 : Écrire le test en échec.**

```csharp
[Fact] public void Rule_vide_accepte_tout() => Assert.True(Compatibilite.IsCompatible(new CompatRule(), Pro11Intel));
[Fact] public void Windows_incompatible_refuse() => Assert.False(Compatibilite.IsCompatible(new CompatRule{Windows={"11"}}, Pro10Intel));
[Fact] public void Gpu_accepte_si_un_des_gpu_correspond() => Assert.True(Compatibilite.IsCompatible(new CompatRule{Gpu={"NVIDIA"}}, FactsAvecGpu("Intel","NVIDIA")));
```

- [ ] **Step 3 : Vérifier l'échec.** `dotnet test --filter Compatibilite`. Attendu : erreur de compilation sur `Compatibilite`.
- [ ] **Step 4 : Implémenter `Compatibilite`, `SystemInfo`, `AssemblyLoader`.** Windows 11 si `CurrentBuild >= 22000`. Édition depuis `EditionID` du registre, normalisée en `Home`, `Pro`, `Enterprise`, `Education`. CPU et GPU depuis `Win32_Processor` et `Win32_VideoController`, normalisés en `Intel`, `AMD`, `NVIDIA`.
- [ ] **Step 5 : Vérifier.** `dotnet test --filter Compatibilite` passe, et `dotnet build -c Release` produit l'exe.
- [ ] **Step 6 : Commit.** `git commit -m "feat: socle du projet et détection de la machine"`

### Task 2: Catalogue et validation

**Files:**
- Create: `src/GamingTweaker/Core/Catalogue.cs`, `Assets/catalog/optimisation.json`, `Assets/scripts/opt.mouse.precision-off.{detect,apply,revert}.ps1`
- Test: `tests/GamingTweaker.Tests/CatalogueTests.cs`, `tests/GamingTweaker.Tests/Helpers/PsSyntax.cs`

**Interfaces:**
- Consumes: `CompatRule`.
- Produces: `CatalogItem { string Id, Menu, Groupe, Titre, Description; RiskLevel Risque; bool Reversible, Redemarrage, Testable, Hkcu; int DelaiSec; string Liste; CompatRule Compatibilite; string Detect, Apply, Revert, Estimate; }`, `enum RiskLevel { Sur, Avance, Risque }`, `IReadOnlyList<CatalogItem> Catalogue.LoadEmbedded()`, `List<string> Catalogue.Validate(items)`.
- Valeurs par défaut : `Testable = true`, `Hkcu = false`, `DelaiSec = 300`. `Liste` est l'identifiant optionnel d'un fournisseur de liste. `Estimate` renvoie un nombre d'octets. `Hkcu` marque les options qui écrivent dans le profil de l'utilisateur.

- [ ] **Step 1 : Écrire les tests en échec.**

```csharp
[Fact] public void Catalogue_integre_est_valide() => Assert.Empty(Catalogue.Validate(Catalogue.LoadEmbedded()));
[Fact] public void Id_en_double_detecte() => Assert.Contains(Catalogue.Validate(new[]{Ok("a"),Ok("a")}), e => e.Contains("a"));
[Fact] public void Reversible_sans_revert_detecte() => Assert.NotEmpty(Catalogue.Validate(new[]{Ok("a", revert:null)}));
[Fact] public void Tous_les_scripts_sont_syntaxiquement_valides() => Assert.Empty(PsSyntax.CheckAll(Catalogue.LoadEmbedded()));
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Catalogue`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter le chargement, `Validate` et `PsSyntax`.** `PsSyntax` lance `powershell.exe` avec `[Management.Automation.Language.Parser]::ParseFile`. Ajouter `opt.mouse.precision-off` (spec, section 4.4) en exemple.
- [ ] **Step 4 : Vérifier.** `dotnet test --filter Catalogue` passe.
- [ ] **Step 5 : Commit.** `git commit -m "feat: catalogue d'options et validation"`

### Task 3: Assemblage du script et exécution

**Files:**
- Create: `src/GamingTweaker/Core/ScriptBuilder.cs`, `Core/Runner.cs`, `Assets/runtime/Common.ps1`
- Test: `tests/GamingTweaker.Tests/RunnerTests.cs`

**Interfaces:**
- Consumes: `CatalogItem`.
- Produces: `Selection { HashSet<string> OptionIds; Dictionary<string, List<string>> Listes; }`, `string ScriptBuilder.Build(IEnumerable<CatalogItem> items, Selection s, string journalPath)`, `Task<RunResult> Runner.RunAsync(string script, Action<string> onLog, CancellationToken ct, Func<string,int> delaiFor)`.
- Produces: `RunResult { List<ItemOutcome> Items; }`, `ItemOutcome { string Id; OutcomeStatus Status; string Message; }`, `enum OutcomeStatus { Ok, Failed, Skipped, AlreadyApplied }`.
- Protocole de sortie, une ligne par événement : `##GT|start|<id>`, `##GT|ok|<id>`, `##GT|fail|<id>|<message>`, `##GT|skip|<id>|<raison>`.

- [ ] **Step 1 : Écrire les tests en échec.**

```csharp
[Fact] public async Task Erreur_d_une_option_n_arrete_pas_la_suivante() { /* a: throw, b: ok */ Assert.Equal(new[]{Failed, Ok}, statuts); }
[Fact] public async Task Selection_de_liste_transmise_au_script() { /* $Selection contient "x y" avec espace */ Assert.Contains("x y", log); }
[Fact] public async Task Delai_depasse_marque_echec_et_continue() { /* a: Start-Sleep 30, délai 1 s, b: ok */ Assert.Equal(new[]{Failed, Ok}, statuts); }
[Fact] public async Task Chemin_avec_accents_et_apostrophe() { /* journal dans %TEMP%\Jérôme D'Arc\ ; texte "été" */ Assert.Contains("été", log); }
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Runner`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter `ScriptBuilder`.** En-tête : `Common.ps1`, encodage UTF-8. Chaque option dans un `try/catch` qui émet le protocole. La liste de sélection passe par une chaîne JSON en here-string littérale pour neutraliser apostrophes et `$`.
- [ ] **Step 4 : Implémenter `Runner`.** Le délai démarre à chaque `start`. À son dépassement, tuer l'arbre de processus, marquer l'option `Failed`, puis relancer le script en sautant les options déjà traitées via un paramètre `-SkipIds`.
- [ ] **Step 5 : Vérifier.** `dotnet test --filter Runner` passe.
- [ ] **Step 6 : Commit.** `git commit -m "feat: assemblage et exécution des scripts"`

### Task 4: Journal et annulation

**Files:**
- Modify: `src/GamingTweaker/Assets/runtime/Common.ps1`
- Create: `src/GamingTweaker/Core/Journal.cs`, `Core/UndoService.cs`
- Test: `tests/GamingTweaker.Tests/UndoTests.cs`

**Interfaces:**
- Consumes: `ScriptBuilder`, `Runner`.
- Produces côté PowerShell : `Set-TweakRegistry -Path -Name -Type -Value`, `Set-TweakDefenderRealtime -Enabled`, `Set-TweakService -Name -StartType`, `Set-TweakTask -Path -Enabled`, `Set-TweakPower -Sub -Setting -Value`, `Set-TweakBcd -Name -Value`, `Add-TweakHosts -Domains`, `Remove-TweakApp -Package`, `Move-TweakQuarantine -File`.
- Produces côté C# : `IReadOnlyList<JournalEntry> Journal.Read(string path)`, `string UndoService.BuildScript(string journalPath, IEnumerable<string> itemIds)`, `List<Conflict> UndoService.FindConflicts(string journalPath, IEnumerable<string> itemIds)`.
- Format : `journal.jsonl`, une ligne JSON par entrée, avec `item`, `kind`, `target`, `existed`, `before`, `after`.

- [ ] **Step 1 : Écrire les tests en échec.** Tous sous `HKCU:\Software\GamingTweakerTest`.

```csharp
[Fact] public async Task Annulation_restaure_valeur_existante() { /* 5 -> 1 -> annuler */ Assert.Equal(5, Lire("X")); }
[Fact] public async Task Annulation_supprime_valeur_creee() { Assert.False(Existe("X")); }
[Fact] public async Task Annulation_en_ordre_inverse() { /* 5 -> 1 -> 2 -> annuler */ Assert.Equal(5, Lire("X")); }
[Fact] public async Task Annulation_par_option_ne_touche_pas_les_autres() { Assert.Equal(1, Lire("Y")); }
[Fact] public async Task Valeur_modifiee_entre_temps_signalee() => Assert.Single(UndoService.FindConflicts(journal, ids));
[Fact] public async Task Journal_inaccessible_aucune_modification() { /* journal en lecture seule */ Assert.Equal(Failed, statut); Assert.Equal(5, Lire("X")); }
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Undo`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter les fonctions PowerShell.** Chacune écrit puis vide l'entrée sur disque avant de modifier, et lève une erreur si l'écriture échoue. Le type de la valeur de registre est conservé.
- [ ] **Step 4 : Implémenter `Journal` et `UndoService`.** Lecture en ordre inverse. Une ligne tronquée en fin de journal est ignorée.
- [ ] **Step 5 : Vérifier.** `dotnet test --filter Undo` passe.
- [ ] **Step 6 : Commit.** `git commit -m "feat: journal d'annulation"`

### Task 5: Profils

**Files:**
- Create: `src/GamingTweaker/Core/Profils.cs`
- Test: `tests/GamingTweaker.Tests/ProfilsTests.cs`

**Interfaces:**
- Consumes: `Selection`, `CatalogItem`, `MachineFacts`.
- Produces: `Profil` conforme au JSON de la spec section 2, `ProfilLoad Profils.Load(string path)` avec `{ Profil Profil; string Erreur; }`, `void Profils.Save(Profil p, string path)`, `ResolvedProfil Profils.Resolve(Profil p, IReadOnlyList<CatalogItem> c, MachineFacts f)` avec `{ Selection Selection; List<string> Inconnues; List<string> Incompatibles; }`, `bool Profils.ContientRisque(Profil p, IReadOnlyList<CatalogItem> c)`.

- [ ] **Step 1 : Écrire les tests en échec.**

```csharp
[Fact] public void Aller_retour_conserve_le_profil() { /* options + exclusions winget */ }
[Fact] public void Id_inconnu_est_signale() => Assert.Contains("x.y", Resolve(P("x.y")).Inconnues);
[Fact] public void Option_incompatible_est_signalee() => Assert.Contains("win11.only", ResolveSur(Pro10, P("win11.only")).Incompatibles);
[Theory, InlineData(""), InlineData("{"), InlineData("{\"version\":999}")]
public void Profil_invalide_renvoie_une_erreur(string json) => Assert.NotNull(Profils.Load(Ecrire(json)).Erreur);
[Fact] public void Risque_detecte() => Assert.True(Profils.ContientRisque(P("opt.spectre-off"), catalogue));
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Profils`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter `Profils`.** Écriture atomique par fichier temporaire puis remplacement. `version` différente de 1 : erreur « profil créé par une version plus récente ».
- [ ] **Step 4 : Vérifier.** `dotnet test --filter Profils` passe.
- [ ] **Step 5 : Commit.** `git commit -m "feat: profils"`

### Task 6: Pipeline commun, données par PC, mode `--silent`

**Files:**
- Create: `src/GamingTweaker/Core/DataPaths.cs`, `Core/RunPipeline.cs`, `Core/RestorePoint.cs`, `Core/Detection.cs`, `Core/ElevationCheck.cs`, `Core/SingleInstance.cs`, `Program.cs`
- Test: `tests/GamingTweaker.Tests/RunPipelineTests.cs`

**Interfaces:**
- Consumes: `ScriptBuilder`, `Runner`, `Journal`, `Profils`, `Catalogue`.
- Produces: `DataPaths.Resolve(string exeDir, MachineFacts f)` avec `ProfilsDir`, `MachineDir`, `SessionsDir`, `QuarantineDir`.
- Produces: `IRestorePoint { RestorePointResult TryCreate(string description); }`, `Task<SessionReport> RunPipeline.ExecuteAsync(RunRequest req, IProgress<string> log, CancellationToken ct)`, `RunRequest { Selection Selection; bool Silent; Func<string,bool> ConfirmSansPointDeRestauration; }`.
- Produces: `Task<Dictionary<string,DetectState>> Detection.RunAsync(items)`, `enum DetectState { Applied, NotApplied, Unknown, Detecting }`, `bool ElevationCheck.SameUserAsDesktop()`, `IDisposable SingleInstance.TryAcquire()`, `SessionInterrompue` dans `RunPipeline.FindInterrupted()`.
- Session : `Machines\<id>\Sessions\<yyyyMMdd-HHmmss>\` avec `state.json`, `journal.jsonl`, `log.txt`, `report.json`, `report.txt`.

- [ ] **Step 1 : Écrire les tests en échec.** `Runner` réel, `IRestorePoint` factice, options de test sous `GamingTweakerTest`.

```csharp
[Fact] public async Task Option_deja_appliquee_est_sautee() => Assert.Equal(AlreadyApplied, Statut("a"));
[Fact] public async Task Option_retablie_par_windows_est_signalee() => Assert.Contains("a", rapport.Retablies);
[Fact] public async Task Point_de_restauration_impossible_en_silencieux_continue() { Assert.Contains(Attention, rapport.Avertissements); Assert.Equal(Ok, Statut("a")); }
[Fact] public async Task Point_de_restauration_impossible_avec_refus_n_execute_rien() => Assert.Empty(rapport.Items);
[Fact] public async Task Session_running_est_retrouvee_comme_interrompue() => Assert.NotNull(RunPipeline.FindInterrupted(dir));
[Fact] public async Task Autre_compte_admin_ignore_les_options_hkcu() { Assert.Equal(Skipped, Statut("hkcu")); }
[Fact] public void Dossier_exe_en_lecture_seule_bascule_sur_programdata() => Assert.StartsWith(@"C:\ProgramData\GamingTweaker", paths.ProfilsDir);
[Fact] public void Seconde_instance_refusee() { using(SingleInstance.TryAcquire()) Assert.Null(SingleInstance.TryAcquire()); }
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter RunPipeline`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter `DataPaths`, `SingleInstance`, `ElevationCheck`, `Detection`, `RestorePoint`.** `Checkpoint-Computer` ne lève pas d'erreur sur la limite de fréquence : analyser l'avertissement et comparer la dernière entrée de `Get-ComputerRestorePoint`. `Detection` exécute toutes les détections dans un seul processus. `ElevationCheck` compare le compte du processus au propriétaire de `explorer.exe`. Le champ `Hkcu` du catalogue marque les options concernées.
- [ ] **Step 4 : Implémenter `RunPipeline`.** Ordre : point de restauration, détection préalable, exécution, rapport, redétection. `Machines\<id>\last-applied.json` permet de distinguer « déjà appliquée » et « rétablie par Windows ». Les options non réversibles s'exécutent toujours.
- [ ] **Step 5 : Implémenter `Program.Main`.** `--profile <fichier> --silent` s'exécute sans WPF et renvoie `0`, `1` ou `2` selon la spec. Sans argument, lance l'interface.
- [ ] **Step 6 : Vérifier.** `dotnet test --filter RunPipeline` passe, puis `GamingTweaker.exe --profile inexistant.json --silent` depuis une console élevée renvoie `2`.
- [ ] **Step 7 : Commit.** `git commit -m "feat: pipeline commun et mode silencieux"`

### Task 7: Interface principale

**Files:**
- Create: `src/GamingTweaker/App.xaml`, `Ui/MainWindow.xaml`, `Ui/Theme.xaml`, `Ui/MainViewModel.cs`, `Ui/OptionViewModel.cs`, `Ui/PreviewWindow.xaml`, `Ui/RunWindow.xaml`, `Lists/IListProvider.cs`
- Test: `tests/GamingTweaker.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `Catalogue`, `Compatibilite`, `Detection`, `RunPipeline`, `Profils`.
- Produces: `IListProvider { string ListId; Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct); }`, `ListEntry { string Key, Titre, Detail; RiskLevel Risque; }`, `MainViewModel` avec `int SelectedCount`, `void ApplyRecommended(string menu)`, `Selection BuildSelection()`, `void LoadProfil(Profil p)`, `Profil ToProfil(string nom)`.
- Les vues `Mode Jeu`, `Historique` et `Informations` sont des emplacements remplis par les Tasks 13 à 15.

- [ ] **Step 1 : Écrire les tests en échec.**

```csharp
[Fact] public void Selection_regroupe_tous_les_menus() => Assert.Equal(2, vm.SelectedCount);
[Fact] public void Recommande_coche_uniquement_le_sur_du_menu_affiche() { vm.ApplyRecommended("Optimisation"); Assert.True(Coche("opt.sur")); Assert.False(Coche("opt.avance")); Assert.False(Coche("tel.sur")); }
[Fact] public void Option_incompatible_n_est_pas_listee() => Assert.DoesNotContain("win11.only", vm.Options.Select(o => o.Id));
[Fact] public void Charger_un_profil_coche_ses_options() { vm.LoadProfil(P("opt.sur")); Assert.True(Coche("opt.sur")); }
[Fact] public void Recherche_filtre_sans_perdre_les_coches() { Cocher("opt.sur"); vm.Recherche = "telemetrie"; Assert.DoesNotContain("opt.sur", Visibles()); Assert.Equal(1, vm.SelectedCount); }
[Fact] public void Recherche_ignore_accents_et_casse() { vm.Recherche = "TELEMETRIE"; Assert.Contains("tel.sur", Visibles()); }
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter MainViewModel`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter le ViewModel.** La fenêtre s'affiche avant la détection, puis les états sont mis à jour.
- [ ] **Step 4 : Implémenter les vues.** Maquette de la spec section 3, thème sombre, groupes repliables et champ de recherche, avec virtualisation de la liste. Cocher une option `Risque` pour la première fois affiche sa conséquence et demande confirmation. `PreviewWindow` affiche options, risques, script exact, taille estimée, avec `Exporter en .ps1`. `RunWindow` affiche le journal en direct et le rapport. Au démarrage : session interrompue, Mode Jeu actif et avertissement de compte d'élévation.
- [ ] **Step 5 : Vérifier.** `dotnet test --filter MainViewModel` passe. Lancer l'exe sur le PC de développement, sans cliquer sur `Lancer`, et faire une capture à 1280x720 : tous les menus, états et aperçu s'affichent, sans texte tronqué.
- [ ] **Step 6 : Commit.** `git commit -m "feat: interface principale"`

### Task 7B: Inventaire des sources du catalogue

**Files:**
- Create: `docs/catalogue-couverture.md`, `THIRD-PARTY-NOTICES.md`, `tools/inventaire/sources/` (copies figées de `tweaks.json` et `windows.yaml`, hors exe), `tools/inventaire/inventaire.py`
- Test: `tests/GamingTweaker.Tests/CouvertureTests.cs`

**Interfaces:**
- Produces: `docs/catalogue-couverture.md`, une ligne par entrée source : `source | entrée | niveau d'origine | id de l'option ou "exclu" | raison`.
- Consumes (Tasks 8 à 11) : ce fichier fixe les options à ajouter, en plus des tableaux minimaux de ces tâches.

- [ ] **Step 1 : Figer les sources.** Télécharger `config/tweaks.json` de WinUtil et `src/application/collections/windows.yaml` de privacy.sexy, en notant le commit de chacun dans `THIRD-PARTY-NOTICES.md`, avec les textes MIT et AGPL-3.0.
- [ ] **Step 2 : Écrire `inventaire.py`.** Il liste les 68 entrées WinUtil et toutes les sous-catégories de privacy.sexy avec leur niveau `standard`, `strict` ou sans recommandation.
- [ ] **Step 3 : Écrire le test en échec.**

```csharp
[Fact] public void Toute_entree_source_a_une_decision() => Assert.Empty(Couverture.SansDecision());
[Fact] public void Tout_id_cite_existe_dans_le_catalogue() => Assert.Empty(Couverture.IdsInconnus(Catalogue.LoadEmbedded()));
[Fact] public void Toute_exclusion_a_une_raison() => Assert.Empty(Couverture.ExclusionsSansRaison());
```

Le test `Tout_id_cite_existe` est marqué `Skip` jusqu'à la fin de la Task 11, puis réactivé.

- [ ] **Step 4 : Remplir la couverture.** Regrouper par sous-catégorie, appliquer les niveaux et les exclusions de la spec, sections 4.9 et 5. Pour ShutUp10++, ajouter les domaines de réglages en se fondant sur la documentation Microsoft et noter qu'aucune correspondance point par point n'est garantie.
- [ ] **Step 5 : Vérifier.** `dotnet test --filter Couverture` passe, hors test `Skip`.
- [ ] **Step 6 : Commit.** `git commit -m "docs: inventaire et couverture des sources du catalogue"`

### Task 8: Catalogue Télémétrie

Le tableau ci-dessous est le minimum. Ajouter aussi les lignes de `docs/catalogue-couverture.md` qui reviennent à ce menu, écrites avec les fonctions `Set-Tweak*`. Ajouter un lien vers la page officielle d'O&O ShutUp10++.

**Files:**
- Create: `Assets/catalog/telemetrie.json`, `Assets/scripts/tel.*.ps1`
- Test: `tests/GamingTweaker.Tests/CatalogueTests.cs` (déjà exécuté sur tout le catalogue), ajouter un test ci-dessous.

**Interfaces:**
- Consumes: fonctions `Set-Tweak*` de la Task 4, schéma de la Task 2.

| Id | Risque | Mécanisme |
|---|---|---|
| `tel.diagtrack-off` | Sûr | Services `DiagTrack`, `dmwappushservice` désactivés et arrêtés |
| `tel.level-min` | Sûr | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` `AllowTelemetry`=0 |
| `tel.tasks-off` | Sûr | Tâches `Application Experience\{Microsoft Compatibility Appraiser, ProgramDataUpdater}`, `Autochk\Proxy`, `Customer Experience Improvement Program\{Consolidator, UsbCeip}`, `DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector`, `Feedback\Siuf\DmClient` |
| `tel.adid-off` | Sûr | `HKCU\...\CurrentVersion\AdvertisingInfo` `Enabled`=0 |
| `tel.suggestions-off` | Sûr | `HKCU\...\ContentDeliveryManager` `SilentInstalledAppsEnabled`, `SystemPaneSuggestionsEnabled`, `SoftLandingEnabled`, `SubscribedContent-338388Enabled`, `-338389Enabled`, `-353694Enabled`, `-353696Enabled` = 0 |
| `tel.activity-off` | Sûr | `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` `EnableActivityFeed`, `PublishUserActivities`, `UploadUserActivities` = 0 |
| `tel.wer-off` | Sûr | `HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting` `Disabled`=1 |
| `tel.bing-off` | Sûr | `HKCU\Software\Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions`=1 |
| `tel.copilot-off` | Sûr, Windows 11 | `HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot` `TurnOffWindowsCopilot`=1 |
| `tel.recall-off` | Sûr, Windows 11 | `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` `DisableAIDataAnalysis`=1 |
| `tel.hosts` | Avancé | `Add-TweakHosts` : `vortex.data.microsoft.com`, `vortex-win.data.microsoft.com`, `settings-win.data.microsoft.com`, `watson.telemetry.microsoft.com` |

- [ ] **Step 1 : Écrire le test en échec.** `Fact Telemetrie_a_toutes_ses_options` : les 11 identifiants existent, sont `Reversible` et `Risque` conforme au tableau.
- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Telemetrie`. Attendu : identifiants absents.
- [ ] **Step 3 : Écrire le JSON et les scripts.** Texte d'interface de `tel.level-min` : préciser que le niveau 0 n'est pleinement honoré que sur Enterprise et Education. Ajouter « Tout restaurer » dans le menu : il annule via journal ou, à défaut, exécute `revert`.
- [ ] **Step 4 : Vérifier.** `dotnet test --filter Catalogue` et `Telemetrie` passent.
- [ ] **Step 5 : Commit.** `git commit -m "feat: menu télémétrie"`

### Task 9: Catalogue Optimisation

Le tableau ci-dessous est le minimum. Ajouter aussi les lignes de `docs/catalogue-couverture.md` qui reviennent à ce menu, dont le groupe « Confort Windows ». Les mêmes règles s'appliquent aux Tasks 10 et 11 pour le Nettoyage et le Bloatware.

**Files:**
- Modify: `Assets/catalog/optimisation.json`
- Create: `Assets/scripts/opt.*.ps1`, `Lists/ServicesProvider.cs`, `Lists/StartupProvider.cs`
- Test: `tests/GamingTweaker.Tests/OptimisationCatalogTests.cs`

**Interfaces:**
- Consumes: fonctions `Set-Tweak*`, `IListProvider`.
- Produces: fournisseurs `services`, `xbox-services`, `startup`.

| Id | Risque | Mécanisme |
|---|---|---|
| `opt.mouse.precision-off` | Sûr | `HKCU\Control Panel\Mouse` `MouseSpeed`, `MouseThreshold1`, `MouseThreshold2` = "0", puis `SPI_SETMOUSE` |
| `opt.stickykeys-off` | Sûr | `HKCU\Control Panel\Accessibility\StickyKeys` `Flags`="506", `Keyboard Response` `Flags`="122", `ToggleKeys` `Flags`="58" |
| `opt.usb-power-off` | Sûr | `MSPower_DeviceEnable` `Enable`=false pour les périphériques USB |
| `opt.nic-power-off` | Sûr | `MSPower_DeviceEnable` `Enable`=false pour les cartes réseau |
| `opt.usb-suspend-off` | Sûr | Sous-groupe `2a737441-1930-4402-8d77-b2bebba308a3`, réglage `48e6b7a6-50f5-4782-a5d4-53bb8f07e226` = 0 |
| `opt.pcie-aspm-off` | Avancé | `SUB_PCIEXPRESS` `ASPM` = 0 |
| `opt.plan-high` | Sûr | `powercfg /setactive SCHEME_MIN` |
| `opt.plan-ultimate` | Avancé | `powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61`, puis activation |
| `opt.coreparking-off` | Avancé | `SUB_PROCESSOR` `CPMINCORES` = 100 |
| `opt.gamemode-on` | Sûr | `HKCU\Software\Microsoft\GameBar` `AutoGameModeEnabled`=1 |
| `opt.gamedvr-off` | Sûr | `HKCU\System\GameConfigStore` `GameDVR_Enabled`=0 |
| `opt.hags` | Avancé, Windows 10 2004+ et 11 | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` `HwSchMode`=2 |
| `opt.fso-off` | Avancé | `HKCU\System\GameConfigStore` `GameDVR_FSEBehaviorMode`=2, `GameDVR_HonorUserFSEBehaviorMode`=1 |
| `opt.mpo-off` | Avancé | `HKLM\SOFTWARE\Microsoft\Windows\Dwm` `OverlayTestMode`=5 |
| `opt.gpu-msi` | Avancé | Pour chaque GPU PCI, `...\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties` `MSISupported`=1 |
| `opt.vbs-off` | Risqué | `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard` `EnableVirtualizationBasedSecurity`=0, `Scenarios\HypervisorEnforcedCodeIntegrity` `Enabled`=0 |
| `opt.bgapps-off` | Sûr | `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy` `LetAppsRunInBackground`=2 |
| `opt.widgets-off` | Sûr, Windows 11 | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` `AllowNewsAndInterests`=0 |
| `opt.visualfx` | Sûr | `HKCU\...\Explorer\VisualEffects` `VisualFXSetting`=2 |
| `opt.sysmain` | Avancé | `SysMain` en manuel, arrêté |
| `opt.wsearch` | Avancé | `WSearch` en manuel, arrêté |
| `opt.hibernate-off` | Avancé | `powercfg /h off` |
| `opt.startup` | Avancé | Liste `startup`, via `...\Explorer\StartupApproved\{Run,Run32,StartupFolder}` |
| `opt.services` | Avancé | Liste `services` : `Fax`, `RemoteRegistry`, `MapsBroker`, `PhoneSvc`, `RetailDemo`, `WpcMonSvc`, `lfsvc`, `Spooler`, en manuel |
| `opt.xbox-services` | Risqué | Liste `xbox-services` : `XblAuthManager`, `XblGameSave`, `XboxGipSvc`, `XboxNetApiSvc`, en manuel. Texte : risque pour le Game Pass et certains jeux |
| `opt.nagle-off` | Avancé | Chaque `...\Tcpip\Parameters\Interfaces\{guid}` `TcpAckFrequency`=1, `TCPNoDelay`=1 |
| `opt.netthrottle-off` | Avancé | `HKLM\...\Multimedia\SystemProfile` `NetworkThrottlingIndex`=0xFFFFFFFF |
| `opt.prioritysep` | Avancé | `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl` `Win32PrioritySeparation`=0x26 |
| `opt.hpet` | Risqué | `bcdedit /deletevalue useplatformclock`, `/set disabledynamictick yes` |
| `opt.spectre-off` | Risqué | `HKLM\...\Session Manager\Memory Management` `FeatureSettingsOverride`=3, `FeatureSettingsOverrideMask`=3 |
| `opt.defender-rt-off` | Risqué | `Set-MpPreference -DisableRealtimeMonitoring $true`. Si `(Get-MpComputerStatus).IsTamperProtected` est vrai : `fail` avec la marche à suivre dans Sécurité Windows, sans aucun contournement. Annulation via `Set-TweakDefenderRealtime` et le journal |

- [ ] **Step 1 : Écrire le test en échec.** `Theory` sur le tableau : chaque id existe avec le bon risque, `Redemarrage=true` pour `opt.hags`, `opt.mpo-off`, `opt.gpu-msi`, `opt.vbs-off`, `opt.hpet`, `opt.spectre-off`. `Fact Aucun_service_ne_passe_en_desactive_dans_le_menu_optimisation` analyse les scripts `opt.*`.
- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter OptimisationCatalog`. Attendu : identifiants absents.
- [ ] **Step 3 : Écrire le JSON, les scripts et les fournisseurs.** Marquer `Testable=false` les options sans matériel réel en Sandbox : `opt.usb-power-off`, `opt.nic-power-off`, `opt.gpu-msi`, `opt.hags`, `opt.pcie-aspm-off`, `opt.vbs-off`, `opt.hpet`.
- [ ] **Step 4 : Vérifier.** `dotnet test --filter "Catalogue|OptimisationCatalog"` passe.
- [ ] **Step 5 : Commit.** `git commit -m "feat: menu optimisation"`

### Task 10: Catalogue Nettoyage

**Files:**
- Create: `Assets/catalog/nettoyage.json`, `Assets/scripts/cln.*.ps1`
- Modify: `Assets/runtime/Common.ps1` (`Remove-TweakPath`, `Get-TweakPathSize`)
- Test: `tests/GamingTweaker.Tests/NettoyageTests.cs`

**Interfaces:**
- Consumes: `Move-TweakQuarantine`, `Estimate` de la Task 2.
- Produces: `Remove-TweakPath -Path` limitée à `$AllowedRoots`, `Find-OrphanInstallerFiles`.

| Id | Risque | Cible |
|---|---|---|
| `cln.temp` | Sûr | `%TEMP%`, `C:\Windows\Temp` |
| `cln.recycle` | Sûr | `Clear-RecycleBin` |
| `cln.thumbs` | Sûr | `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db` |
| `cln.wu-cache` | Sûr | `SoftwareDistribution\Download`, `C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache` |
| `cln.crash` | Sûr | `C:\ProgramData\Microsoft\Windows\WER`, `%LOCALAPPDATA%\CrashDumps`, `C:\Windows\Minidump`, `C:\Windows\MEMORY.DMP` |
| `cln.logs` | Sûr | `C:\Windows\Logs\CBS`, `C:\Windows\Logs\DISM`, journaux Windows via `wevtutil cl` |
| `cln.browsers` | Sûr | Dossiers `Cache`, `Code Cache`, `GPUCache` de Chrome et Edge, `cache2` de Firefox, profils compris. Jamais `Login Data`, `History`, `Cookies` |
| `cln.launchers` | Sûr | `Steam\appcache\httpcache`, `%LOCALAPPDATA%\EpicGamesLauncher\Saved\webcache*` |
| `cln.dism` | Sûr | `DISM /Online /Cleanup-Image /StartComponentCleanup` |
| `cln.shaders` | Avancé | `%LOCALAPPDATA%\D3DSCache`, `NVIDIA\DXCache`, `NVIDIA\GLCache`, `AMD\DxCache` |
| `cln.windows-old` | Avancé | `C:\Windows.old` |
| `cln.old-drivers` | Avancé | `pnputil /enum-drivers`, suppression sans `/force` des versions non utilisées |
| `cln.orphans` | Avancé | Orphelins de `C:\Windows\Installer` vers la quarantaine |
| `cln.resetbase` | Avancé | `DISM /Online /Cleanup-Image /StartComponentCleanup /ResetBase` |
| `cln.prefetch` | Risqué | `C:\Windows\Prefetch\*.pf` |
| `cln.registry` | Risqué | Export `.reg` dans la session, puis retrait des entrées `Uninstall` dont le programme a disparu et de `MuiCache`. Réversible par ce fichier |

- [ ] **Step 1 : Écrire les tests en échec.** Dans un dossier temporaire avec `$AllowedRoots` injecté.

```csharp
[Fact] public async Task Chemin_hors_liste_refuse() => Assert.True(FichierExiste(hors));
[Fact] public async Task Jonction_non_suivie() => Assert.True(FichierExiste(cibleDeLaJonction));
[Fact] public async Task Fichier_verrouille_ne_fait_pas_echouer_l_option() { Assert.Equal(Ok, statut); Assert.Contains("verrouillé", log); }
[Fact] public async Task Estimation_renvoie_la_taille() => Assert.Equal(1024, taille);
[Fact] public async Task Orphelins_sans_liste_installer_ne_deplace_rien() { /* énumération factice vide */ Assert.True(FichierExiste(msp)); Assert.Equal(Failed, statut); }
[Fact] public void Navigateurs_ne_cible_jamais_les_donnees_personnelles() { Assert.DoesNotContain("Login Data", script); Assert.DoesNotContain("History", script); }
[Fact] public async Task Registre_export_echoue_aucune_suppression() { /* export impossible, clé de test sous GamingTweakerTest */ Assert.Equal(Failed, statut); Assert.True(CleExiste(cle)); }
[Fact] public async Task Registre_garde_une_entree_dont_le_programme_existe() => Assert.True(CleExiste(cleProgrammePresent));
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Nettoyage`. Attendu : fonctions absentes.
- [ ] **Step 3 : Implémenter `Remove-TweakPath`, `Get-TweakPathSize`, `Find-OrphanInstallerFiles`.** La liste vient de `WindowsInstaller.Installer` : `Products`, `Patches`, `ProductInfo(LocalPackage)`.
- [ ] **Step 4 : Écrire le catalogue.** `Reversible=false`, sauf `cln.orphans` et `cln.registry`. Chaque `Estimate` renvoie des octets. Les textes du `/ResetBase` et de `Windows.old` indiquent ce qui devient impossible.
- [ ] **Step 5 : Vérifier.** `dotnet test --filter "Nettoyage|Catalogue"` passe.
- [ ] **Step 6 : Commit.** `git commit -m "feat: menu nettoyage"`

### Task 11: Bloatware

**Files:**
- Create: `Assets/catalog/bloatware.json`, `Assets/scripts/blt.*.ps1`, `Lists/BloatwareProvider.cs`, `Assets/bloatware-list.json`
- Test: `tests/GamingTweaker.Tests/BloatwareTests.cs`

**Interfaces:**
- Consumes: `IListProvider`, `Remove-TweakApp`.
- Produces: fournisseur `bloatware`, `IReadOnlyList<ListEntry> BloatwareProvider.Match(IEnumerable<string> installed, IReadOnlyList<KnownApp> known)`.
- Sûr : `Microsoft.BingNews`, `Microsoft.BingWeather`, `Microsoft.MicrosoftSolitaireCollection`, `Clipchamp.Clipchamp`, `Microsoft.Getstarted`, `MicrosoftTeams`, `MSTeams`, `Microsoft.GetHelp`, `Microsoft.ZuneMusic`, `Microsoft.ZuneVideo`, `Microsoft.People`, `microsoft.windowscommunicationsapps`, `Microsoft.MicrosoftOfficeHub`, `Microsoft.WindowsFeedbackHub`, `Microsoft.WindowsMaps`, `Microsoft.YourPhone`.
- Avancé : `OneDrive` (traité par son désinstalleur, sans toucher au dossier de l'utilisateur), `Microsoft.GamingApp`, `Microsoft.XboxApp`.
- Risqué : option distincte `blt.edge-remove`, avec la méthode de WinUtil, sans `--delete-profile`, sans toucher à WebView2. Annulation par `winget install Microsoft.Edge`. `Testable=false`.

- [ ] **Step 1 : Écrire les tests en échec.**

```csharp
[Fact] public void Seules_les_applis_presentes_sont_listees() => Assert.Single(BloatwareProvider.Match(new[]{"Microsoft.BingNews","Foo"}, known));
[Fact] public void Store_et_WebView2_jamais_proposes() { Assert.DoesNotContain(known, a => a.Package.Contains("WindowsStore") || a.Package.Contains("WebView")); }
[Fact] public void Suppression_edge_conserve_le_profil_et_webview2() { Assert.DoesNotContain("--delete-profile", scriptEdge); Assert.DoesNotContain("EdgeWebView", scriptEdge); }
[Fact] public void Annulation_indique_le_nom_du_paquet() => Assert.Contains("Microsoft.BingNews", Journal.Read(journal).Single().Target);
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Bloatware`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter le fournisseur et les scripts.** `Get-AppxPackage -AllUsers` et `Get-AppxProvisionedPackage -Online`. Annulation : réinstallation depuis le Store via `winget install --source msstore` si un identifiant est connu dans `bloatware-list.json`, sinon message pour réinstaller à la main. L'aperçu le signale.
- [ ] **Step 4 : Vérifier.** `dotnet test --filter Bloatware` passe.
- [ ] **Step 5 : Commit.** `git commit -m "feat: menu bloatware"`

### Task 12: Mises à jour

**Files:**
- Create: `Assets/catalog/majs.json`, `Assets/scripts/upd.*.ps1`, `Lists/WingetProvider.cs`, `Lists/WindowsUpdateProvider.cs`, `Core/NvidiaLookup.cs`
- Test: `tests/GamingTweaker.Tests/WingetParserTests.cs`, `NvidiaLookupTests.cs`, `tests/GamingTweaker.Tests/fixtures/*`

**Interfaces:**
- Consumes: `IListProvider`, `Runner`.
- Produces: `IReadOnlyList<ListEntry> WingetProvider.Parse(string output)`, `NvidiaDriverInfo NvidiaLookup.Parse(string json)`, `Task<NvidiaDriverInfo> NvidiaLookup.FindLatestAsync(string gpuName, string branche)`, `{ string Version; string Url; }`, `null` en cas d'échec.
- Options : `upd.windows`, `upd.apps`, `upd.nvidia`, et `upd.wu-auto-off` (Risqué : `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU` `NoAutoUpdate`=1, services inchangés, donc `upd.windows` reste utilisable). Les profils utilisent `listes.winget`, `listes.windowsUpdate`, `listes.pilote`.

- [ ] **Step 1 : Capturer les fixtures réelles.** `winget upgrade` sur ce PC, en français (`fixtures/winget-fr.txt`). Réponse réelle de la recherche NVIDIA (`AjaxDriverService.php?func=DriverManualLookup` et `lookupValueSearch.aspx`) dans `fixtures/nvidia-*.json`. Si l'accès a changé, documenter le format réel et adapter avant la suite.
- [ ] **Step 2 : Écrire les tests en échec.**

```csharp
[Fact] public void Winget_analyse_le_tableau_francais() => Assert.Contains(Parse(fr), e => e.Key == "Microsoft.Edge" /* id réel de la fixture */);
[Fact] public void Winget_utilise_la_position_des_colonnes_avec_noms_tronques() => Assert.DoesNotContain(Parse(tronque), e => e.Key.Contains(" "));
[Fact] public void Winget_sans_mise_a_jour_renvoie_vide() => Assert.Empty(Parse(aucune));
[Fact] public void Nvidia_extrait_version_et_url() { Assert.Equal(version, info.Version); Assert.StartsWith("https://", info.Url); }
[Fact] public void Nvidia_reponse_inconnue_renvoie_null() => Assert.Null(NvidiaLookup.Parse("<html>"));
```

- [ ] **Step 3 : Vérifier l'échec.** `dotnet test --filter "Winget|Nvidia"`. Attendu : compilation en échec.
- [ ] **Step 4 : Implémenter les analyseurs.** Winget : positions lues sur la ligne d'en-tête au-dessus des tirets, indépendamment de la langue.
- [ ] **Step 5 : Écrire les scripts.**
  - `upd.windows` : API COM `Microsoft.Update.Session`, recherche `IsInstalled=0 and Type='Software' and IsHidden=0`.
  - `upd.apps` : pour chaque id, `winget upgrade --id <id> --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity`.
  - `upd.nvidia` : comparaison de version avec `nvidia-smi`, téléchargement, puis `Get-AuthenticodeSignature`. Statut `Valid` et sujet contenant `NVIDIA Corporation` obligatoires, sinon suppression et échec. Installation avec `-s -noreboot`.
- [ ] **Step 6 : Ajouter le repli UI.** Échec de recherche : message et lien vers la page officielle. GPU AMD ou Intel : version et lien.
- [ ] **Step 7 : Vérifier.** `dotnet test --filter "Winget|Nvidia|Catalogue"` passe. Afficher la liste Windows Update et `winget` sur le PC de développement sans rien installer.
- [ ] **Step 8 : Commit.** `git commit -m "feat: menu mises à jour"`

### Task 13: Informations et capteurs

**Files:**
- Create: `src/GamingTweaker/Info/InfoCollector.cs`, `Info/Capteurs.cs`, `Info/PawnIoInstaller.cs`, `Ui/InfoView.xaml`
- Test: `tests/GamingTweaker.Tests/InfoTests.cs`

**Interfaces:**
- Consumes: `MachineFacts`.
- Produces: `InfoReport InfoCollector.Collect()` avec `List<InfoSection>`, `{ string Titre; List<KeyValuePair<string,string>> Lignes; }`, `string InfoReport.ToText()`, `Capteurs : IDisposable` avec `IReadOnlyList<SensorReading> Read()`, `PawnIoInstaller.IsInstalled()`.

- [ ] **Step 1 : Vérifier PawnIO, avant tout code.** Dans un petit projet jetable : `LibreHardwareMonitorLib` 0.9.6 ou plus récent sur net48, sans WinRing0, avec et sans PawnIO, et l'identifiant `winget` de PawnIO. Si les capteurs ne peuvent pas fonctionner comme la spec le décrit, **arrêter et demander à l'utilisateur**.
- [ ] **Step 2 : Écrire les tests en échec.**

```csharp
[Fact] public void Toutes_les_sections_sont_presentes() { foreach (var s in new[]{"Windows","CPU","Carte mère","RAM","GPU","Disques","Réseau","Écrans","Pilotes"}) Assert.Contains(rapport.Sections, x => x.Titre == s); }
[Fact] public void Export_texte_contient_les_sections() => Assert.Contains("CPU", rapport.ToText());
[Fact] public void Collecte_ne_charge_pas_librehardwaremonitor() => Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.FullName.StartsWith("LibreHardwareMonitor"));
```

- [ ] **Step 3 : Vérifier l'échec.** `dotnet test --filter Info`. Attendu : compilation en échec.
- [ ] **Step 4 : Implémenter `InfoCollector`.** WMI et `MSFT_PhysicalDisk` et `MSStorageDriver_FailurePredictStatus` pour SMART. `nvidia-smi` si présent. Une section en erreur affiche « indisponible » sans arrêter les autres.
- [ ] **Step 5 : Implémenter `Capteurs`, `PawnIoInstaller`, `InfoView`.** L'installation demande un accord explicite. `--silent` n'accède jamais à `Capteurs`.
- [ ] **Step 6 : Vérifier.** `dotnet test --filter Info` passe. Ouvrir le menu sur le PC de développement, exporter le fichier texte et refuser PawnIO : le reste doit fonctionner.
- [ ] **Step 7 : Commit.** `git commit -m "feat: menu informations et capteurs"`

### Task 14: Mode Jeu

**Files:**
- Create: `src/GamingTweaker/Core/ModeJeu.cs`, `Ui/ModeJeuView.xaml`
- Test: `tests/GamingTweaker.Tests/ModeJeuTests.cs`

**Interfaces:**
- Consumes: `DataPaths`.
- Produces: `IModeJeuActions { ServiceState StopService(string); void StartService(string); bool KillProcess(string); void RestartProcess(string); string SetPlan(string); }`, `ModeJeu.Activate(ModeJeuConfig c)`, `ModeJeu.Restore()`, `bool ModeJeu.IsActive`.
- État : `Machines\<id>\modejeu.json`, écrit avant toute action.

- [ ] **Step 1 : Écrire les tests en échec.** Avec `IModeJeuActions` factice.

```csharp
[Fact] public void Activer_ecrit_l_etat_avant_d_agir() => Assert.Equal("etat", ordre[0]);
[Fact] public void Restaurer_ne_redemarre_que_ce_qui_tournait() => Assert.DoesNotContain("arrete", actions.Started);
[Fact] public void Restaurer_remet_le_plan_initial() => Assert.Equal(planInitial, actions.PlanActif);
[Fact] public void Etat_present_au_demarrage_signale_le_mode_actif() => Assert.True(new ModeJeu(dir, actions).IsActive);
[Fact] public void Service_deja_redemarre_apres_reboot_ne_provoque_pas_d_erreur() { /* restore idempotent */ }
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter ModeJeu`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter `ModeJeu` et la vue.** Liste à choisir parmi les services et processus actifs. Les processus système critiques ne sont pas proposés : `csrss`, `wininit`, `winlogon`, `lsass`, `services`, `smss`, `dwm`.
- [ ] **Step 4 : Vérifier.** `dotnet test --filter ModeJeu` passe.
- [ ] **Step 5 : Commit.** `git commit -m "feat: mode jeu"`

### Task 15: Historique et mesures

**Files:**
- Create: `src/GamingTweaker/Core/Mesures.cs`, `Core/QuarantineService.cs`, `Ui/HistoriqueView.xaml`, `Ui/HistoriqueViewModel.cs`
- Test: `tests/GamingTweaker.Tests/HistoriqueTests.cs`

**Interfaces:**
- Consumes: `Journal`, `UndoService`, `DataPaths`, `RunPipeline`.
- Produces: `Task<MesureSnapshot> Mesures.TakeAsync(TimeSpan duree)`, `MesureDiff Mesures.Compare(a, b)`, `QuarantineService.List()`, `.Restore(id)`, `.Purge(id)`.
- Durée de relevé : 30 secondes.

- [ ] **Step 1 : Écrire les tests en échec.**

```csharp
[Fact] public void Comparaison_calcule_les_ecarts() => Assert.Equal(-5, Mesures.Compare(A(services:100), A(services:95)).Services);
[Fact] public void Quarantaine_restaure_le_fichier() => Assert.True(File.Exists(origine));
[Fact] public void Purge_supprime_uniquement_dans_la_quarantaine() => Assert.Throws<InvalidOperationException>(() => svc.Purge(@"..\..\x"));
[Fact] public void Sessions_triees_du_plus_recent_au_plus_ancien() => Assert.Equal("b", vm.Sessions[0].Id);
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Historique`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter `Mesures`, `QuarantineService`, la vue.** Compteurs `Processor(_Total)\% DPC Time` et `% Interrupt Time`, noms résolus par index car localisés. La vue propose l'annulation par option ou session, avec confirmation en cas de conflit, et affiche que les mesures n'évaluent pas l'input lag.
- [ ] **Step 4 : Vérifier.** `dotnet test --filter Historique` passe. Prendre deux relevés sur le PC de développement et les comparer.
- [ ] **Step 5 : Commit.** `git commit -m "feat: historique et mesures"`

### Task 16: Planification de la routine

**Files:**
- Create: `src/GamingTweaker/Core/Planification.cs`
- Modify: `Ui/MainWindow.xaml`, `Ui/MainViewModel.cs`
- Test: `tests/GamingTweaker.Tests/PlanificationTests.cs`

**Interfaces:**
- Consumes: `DataPaths`, `Profils`.
- Produces: `string Planification.BuildTaskXml(RoutineConfig c, string exe, string profil, string user)`, `Install(RoutineConfig c)`, `Remove()`, `RoutineConfig { DayOfWeek Jour; TimeSpan Heure; string Profil; }`.
- Tâche : `\GamingTweaker\Routine`, droits élevés, `LogonType` `InteractiveToken`.

- [ ] **Step 1 : Écrire les tests en échec.**

```csharp
[Fact] public void Xml_utilise_la_copie_dans_programdata() => Assert.Contains(@"C:\ProgramData\GamingTweaker\GamingTweaker.exe", xml);
[Fact] public void Xml_passe_le_profil_et_silent() { Assert.Contains("--profile", xml); Assert.Contains("--silent", xml); }
[Fact] public void Xml_droits_eleves_compte_interactif() { Assert.Contains("HighestAvailable", xml); Assert.Contains("InteractiveToken", xml); }
[Fact] public void Chemin_avec_espace_est_echappe() => Assert.Contains("&quot;", XmlAvecEspace());
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter Planification`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter `Planification`.** `schtasks /Create /XML`. `Install` copie l'exe et le profil ; `Remove` retire la tâche et cette copie, en conservant `Machines`. Un profil avec option `Risque` exige la confirmation déjà prévue.
- [ ] **Step 4 : Vérifier.** `dotnet test --filter Planification` passe.
- [ ] **Step 5 : Commit.** `git commit -m "feat: routine planifiée"`

### Task 17: Cycle Sandbox, exe final, liste de contrôle

**Files:**
- Create: `tools/sandbox/cycle.wsb`, `tools/sandbox/start-cycle.ps1`, `src/GamingTweaker/Core/CycleTest.cs`, `docs/checklist-premier-pc.md`, `README.md`
- Test: `tests/GamingTweaker.Tests/CycleTestGuardTests.cs`

**Interfaces:**
- Consumes: `Catalogue`, `RunPipeline`, `UndoService`, `Detection`.
- Produces: `GamingTweaker.exe --cycle-test <fichier-résultat>`, qui écrit pour chaque option `{ id, couvert, avant, apresApply, apresRevert, resultat }`.

- [ ] **Step 1 : Écrire le test en échec.**

```csharp
[Fact] public void Cycle_refuse_hors_sandbox() => Assert.Equal(2, CycleTest.Run(sortie, user: "adu"));
[Fact] public void Cycle_accepte_le_compte_sandbox() => Assert.True(CycleTest.IsSandbox("WDAGUtilityAccount"));
```

- [ ] **Step 2 : Vérifier l'échec.** `dotnet test --filter CycleTestGuard`. Attendu : compilation en échec.
- [ ] **Step 3 : Implémenter `CycleTest` et `cycle.wsb`.** Refus si le compte n'est pas `WDAGUtilityAccount`. Dossier de l'exe mappé en lecture seule, dossier de résultats en écriture. Les options `Testable=false` et non réversibles sont listées « non couvertes ».
- [ ] **Step 4 : Lancer le cycle.** `dotnet publish -c Release`, puis ouvrir `tools/sandbox/cycle.wsb`. Attendu : `resultat: ok` pour toutes les options couvertes. Si Sandbox ne démarre pas, le dire et lister ce qui reste non vérifié.
- [ ] **Step 5 : Vérifier l'exe unique.** Le copier seul dans un dossier vide nommé `Jérôme D'Arc`, lancer l'interface et la routine à vide. Vérifier que le menu Informations affiche les capteurs, à la demande seulement.
- [ ] **Step 6 : Rédiger `docs/checklist-premier-pc.md`.** Pour le premier PC fixe : pilote NVIDIA, Windows Update, `winget`, capteurs et PawnIO, options `Testable=false`, annulation réelle, routine planifiée, Windows 10 si disponible. Chaque ligne donne l'action et le résultat attendu.
- [ ] **Step 7 : Lancer toute la suite.** `dotnet test`. Attendu : tous les tests passent.
- [ ] **Step 8 : Commit.** `git commit -m "feat: cycle sandbox, exe final et liste de contrôle"`
