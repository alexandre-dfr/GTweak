using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Lists;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Menu Bloatware. Aucun retrait réel : Get-AppxPackage, Remove-AppxPackage, DISM et winget sont remplacés par des doubles dans le processus de test.
    /// </summary>
    public class BloatwareTests : IDisposable
    {
        private const string Menu = "Bloatware";
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "GTBlt-" + Guid.NewGuid().ToString("N"));

        public BloatwareTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private static IReadOnlyList<KnownApp> Known => KnownApps.LoadEmbedded();
        private static Dictionary<string, CatalogItem> Catalogue(string menu = Menu) =>
            GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == menu).ToDictionary(i => i.Id);

        private string Jrn => Path.Combine(tmp, "journal.jsonl");

        /// <summary>Exécute un script apply du catalogue dans Invoke-GTItem, avec une sélection et des doubles.</summary>
        private List<string> Apply(string apply, string id, string[] selection, string preambule = "")
        {
            var sel = Newtonsoft.Json.JsonConvert.SerializeObject(selection);
            return PsCommon.Eval("$GT_JournalPath = " + Q(Jrn) + "\r\n$Selection = [string[]]@(ConvertFrom-Json -InputObject " + Q(sel) + " | ForEach-Object { $_ })\r\n"
                + preambule + "\r\nInvoke-GTItem " + Q(id) + " ([scriptblock]::Create(" + Q(apply) + "))");
        }

        private string Restore(string id, string preambule = "") => string.Join("\n",
            PsCommon.Eval("$GT_JournalPath = " + Q(Jrn) + "\r\n" + preambule + "\r\nInvoke-GTItem " + Q(id) + " { Restore-GTItem -Item " + Q(id) + " }"));

        /// <summary>Doubles : une appli installée (Name/PackageFullName), image provisionnée, journal des retraits dans removed.log.</summary>
        private string FauxAppx(params string[] noms)
        {
            var log = Path.Combine(tmp, "removed.log");
            var liste = string.Join(",", noms.Select(n => "[pscustomobject]@{ Name = " + Q(n) + "; PackageFullName = " + Q(n + "_1.0.0.0_x64__8wekyb3d8bbwe") + " }"));
            var prov = string.Join(",", noms.Select(n => "[pscustomobject]@{ DisplayName = " + Q(n) + "; PackageName = " + Q(n + "_1.0.0.0_neutral_~_8wekyb3d8bbwe") + " }"));
            return "$script:fakeApps = @(" + liste + ")\r\n$script:fakeProv = @(" + prov + ")\r\n"
                 + "function Get-AppxPackage { [CmdletBinding()] param([string]$Name, [switch]$AllUsers) @($script:fakeApps | Where-Object { $_.Name -eq $Name }) }\r\n"
                 + "function Get-AppxProvisionedPackage { [CmdletBinding()] param([switch]$Online) $script:fakeProv }\r\n"
                 + "function Remove-AppxPackage { [CmdletBinding()] param([string]$Package, [switch]$AllUsers) Add-Content -LiteralPath " + Q(log) + " -Value ('user ' + $Package) }\r\n"
                 + "function Remove-AppxProvisionedPackage { [CmdletBinding()] param([switch]$Online, [string]$PackageName) Add-Content -LiteralPath " + Q(log) + " -Value ('prov ' + $PackageName) }\r\n";
        }

        private string Retires() { var f = Path.Combine(tmp, "removed.log"); return File.Exists(f) ? File.ReadAllText(f) : ""; }

        /// <summary>winget factice : journalise ses arguments dans winget.log et renvoie le code demandé.</summary>
        private string FauxWinget(int code = 0)
        {
            var cmd = Path.Combine(tmp, "winget-factice.cmd");
            File.WriteAllText(cmd, "@echo off\r\necho %* >>\"" + Path.Combine(tmp, "winget.log") + "\"\r\nexit /b " + code + "\r\n");
            return "$GT_WingetPath = " + Q(cmd) + "\r\n";
        }

        private string LogWinget() { var f = Path.Combine(tmp, "winget.log"); return File.Exists(f) ? File.ReadAllText(f) : ""; }

        // ---- tests du plan --------------------------------------------------------------------------

        private static readonly IReadOnlyList<KnownApp> known = KnownApps.LoadEmbedded();

        [Fact] public void Seules_les_applis_presentes_sont_listees() => Assert.Single(BloatwareProvider.Match(new[] { "Microsoft.BingNews", "Foo" }, known));

        [Fact] public void Store_et_WebView2_jamais_proposes()
        {
            Assert.DoesNotContain(known, a => a.Package.Contains("WindowsStore") || a.Package.Contains("WebView"));
            foreach (var interdit in new[] { "Store", "XboxGamingOverlay", "XboxIdentityProvider", "GameCallableUI", "GamingServices", "Xbox.TCUI", "DesktopAppInstaller", "VCLibs", "UI.Xaml", "NET.Native", "NVIDIA", "AdvancedMicroDevices", "Realtek", "Winget", "MicrosoftEdge" })
                Assert.DoesNotContain(known, a => a.Package.IndexOf(interdit, StringComparison.OrdinalIgnoreCase) >= 0);
            // Défense en profondeur : Remove-TweakApp refuse aussi ces noms, même si un script les réclamait.
            var refuses = PsCommon.Eval("foreach ($n in @('Microsoft.WindowsStore','Microsoft.DesktopAppInstaller','Microsoft.WebView2','Microsoft.XboxGamingOverlay','Microsoft.XboxIdentityProvider','Microsoft.VCLibs.140.00','Microsoft.UI.Xaml.2.8','NVIDIACorp.NVIDIAControlPanel','RealtekSemiconductorCorp.RealtekAudioControl','Microsoft.MicrosoftEdge.Stable','Microsoft.GamingServices','Microsoft.Xbox.TCUI')) { if (-not (Test-GTProtectedApp $n)) { 'AUTORISE:' + $n } }\r\n'FIN'");
            Assert.Equal("FIN", string.Join("", refuses));
            Assert.Empty(PsCommon.Eval("foreach ($a in @(" + string.Join(",", known.Select(a => Q(a.Package))) + ")) { if (Test-GTProtectedApp $a) { 'PROTEGE:' + $a } }"));
        }

        [Fact] public void Suppression_edge_conserve_le_profil_et_webview2()
        {
            var scriptEdge = Catalogue()["blt.edge-remove"].Apply;
            Assert.DoesNotContain("--delete-profile", scriptEdge);
            Assert.DoesNotContain("EdgeWebView", scriptEdge);
            Assert.DoesNotContain("WebView", scriptEdge);
            Assert.Contains("--uninstall --system-level --force-uninstall", scriptEdge);
            Assert.Contains("Write-GTJournal 'wingetapp' 'Microsoft.Edge'", scriptEdge);
        }

        [Fact] public void Annulation_indique_le_nom_du_paquet()
        {
            var r = Apply("Remove-TweakApp -Package 'Microsoft.BingNews' -StoreId '9WZDNCRFHVFW'", "blt.apps", new string[0], FauxAppx("Microsoft.BingNews"));
            Assert.Contains("##GT|ok|blt.apps", string.Join("\n", r));
            Assert.Contains("Microsoft.BingNews", Journal.Read(Jrn).Single().Target);
            Assert.Equal("app", Journal.Read(Jrn).Single().Kind);
        }

        // ---- retrait et annulation -------------------------------------------------------------------

        [Fact] public void Le_retrait_enleve_aussi_l_image_provisionnee_et_ecrit_le_journal_avant()
        {
            Apply("Remove-TweakApp -Package 'Microsoft.BingNews' -StoreId '9WZDNCRFHVFW'", "blt.apps", new string[0], FauxAppx("Microsoft.BingNews"));
            var log = Retires();
            Assert.Contains("user Microsoft.BingNews_1.0.0.0_x64__8wekyb3d8bbwe", log);
            Assert.Contains("prov Microsoft.BingNews_1.0.0.0_neutral_~_8wekyb3d8bbwe", log);
            var e = Journal.Read(Jrn).Single();
            Assert.Equal("9WZDNCRFHVFW", (string)e.After);
        }

        [Fact] public void Un_journal_impossible_a_ecrire_ne_retire_rien()
        {
            var bloque = Path.Combine(tmp, "dossier-bloquant");   // un dossier à la place du fichier de journal
            Directory.CreateDirectory(bloque);
            var r = string.Join("\n", PsCommon.Eval("$GT_JournalPath = " + Q(bloque) + "\r\n" + FauxAppx("Microsoft.BingNews")
                + "Invoke-GTItem 'blt.apps' { Remove-TweakApp -Package 'Microsoft.BingNews' }"));
            Assert.Contains("##GT|fail|blt.apps", r);
            Assert.Equal("", Retires());
        }

        [Fact] public void Un_paquet_absent_donne_un_saut_sans_journal()
        {
            var r = string.Join("\n", Apply("Remove-TweakApp -Package 'Microsoft.BingNews'", "blt.apps", new string[0], FauxAppx("Autre.Chose")));
            Assert.Contains("##GT|skip|blt.apps|Application Microsoft.BingNews absente", r);
            Assert.False(File.Exists(Jrn));
        }

        [Fact] public void Annulation_reinstalle_depuis_le_Store_quand_l_identifiant_est_connu()
        {
            Apply("Remove-TweakApp -Package 'Microsoft.BingNews' -StoreId '9WZDNCRFHVFW'", "blt.apps", new string[0], FauxAppx("Microsoft.BingNews"));
            var r = Restore("blt.apps", FauxWinget());
            Assert.Contains("##GT|ok|blt.apps", r);
            var log = LogWinget();
            Assert.Contains("install --source msstore --id 9WZDNCRFHVFW --exact --accept-package-agreements --accept-source-agreements --disable-interactivity --silent", log);
            Assert.Contains(Journal.Read(Jrn), e => e.Kind == "undone");
        }

        [Fact] public void Annulation_sans_identifiant_demande_une_reinstallation_manuelle()
        {
            Apply("Remove-TweakApp -Package 'Microsoft.Getstarted'", "blt.apps", new string[0], FauxAppx("Microsoft.Getstarted"));
            var r = Restore("blt.apps", FauxWinget());
            Assert.Contains("##GT|skip|blt.apps|Réinstallation manuelle nécessaire (Microsoft Store) : Microsoft.Getstarted", r);
            Assert.Equal("", LogWinget());
        }

        [Fact] public void Annulation_sans_winget_demande_une_reinstallation_manuelle()
        {
            Apply("Remove-TweakApp -Package 'Microsoft.BingNews' -StoreId '9WZDNCRFHVFW'", "blt.apps", new string[0], FauxAppx("Microsoft.BingNews"));
            var r = Restore("blt.apps", "function Get-GTWingetPath { $null }");
            Assert.Contains("Réinstallation manuelle nécessaire", r);
        }

        [Fact] public void Annulation_echoue_si_winget_echoue_et_reste_rejouable()
        {
            Apply("Remove-TweakApp -Package 'Microsoft.BingNews' -StoreId '9WZDNCRFHVFW'", "blt.apps", new string[0], FauxAppx("Microsoft.BingNews"));
            var r = Restore("blt.apps", FauxWinget(1));
            Assert.Contains("##GT|fail|blt.apps", r);
            Assert.DoesNotContain(Journal.Read(Jrn), e => e.Kind == "undone");
        }

        // ---- script apply de la liste ---------------------------------------------------------------------

        [Fact] public void Le_script_apply_refuse_un_nom_hors_de_la_liste_sans_rien_modifier()
        {
            var it = Catalogue()["blt.apps"];
            var r = string.Join("\n", Apply(it.Apply, "blt.apps", new[] { "Microsoft.BingNews", "Microsoft.WindowsStore" }, FauxAppx("Microsoft.BingNews", "Microsoft.WindowsStore")));
            Assert.Contains("##GT|skip|blt.apps|Applications refusées", r);
            Assert.Contains("Microsoft.WindowsStore", r);
            Assert.Equal("", Retires());
            Assert.False(File.Exists(Jrn));
        }

        [Fact] public void Le_script_apply_refuse_Tout_et_les_exclusions()
        {
            var it = Catalogue()["blt.apps"];
            foreach (var sel in new[] { new[] { "*" }, new[] { "*", "!Microsoft.BingNews" } })
            {
                var r = string.Join("\n", Apply(it.Apply, "blt.apps", sel, FauxAppx("Microsoft.BingNews")));
                Assert.Contains("##GT|skip|blt.apps|Choisissez les applications une à une", r);
            }
            Assert.Equal("", Retires());
        }

        [Fact] public void Le_script_apply_retire_les_applications_choisies_et_ignore_les_absentes()
        {
            var it = Catalogue()["blt.apps"];
            var r = string.Join("\n", Apply(it.Apply, "blt.apps", new[] { "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.Getstarted" }, FauxAppx("Microsoft.BingNews", "Microsoft.Getstarted")));
            Assert.Contains("##GT|ok|blt.apps|2 application(s) retirée(s)", r);
            Assert.Equal(2, Journal.Read(Jrn).Count(e => e.Kind == "app"));
            Assert.DoesNotContain("BingWeather", Retires());
            // Idempotent : une relance, plus rien à retirer.
            var r2 = string.Join("\n", Apply(it.Apply, "blt.apps", new[] { "Microsoft.BingNews" }, "$script:fakeApps = @(); $script:fakeProv = @()\r\nfunction Get-AppxPackage { [CmdletBinding()] param([string]$Name, [switch]$AllUsers) }\r\nfunction Get-AppxProvisionedPackage { [CmdletBinding()] param([switch]$Online) }"));
            Assert.Contains("##GT|skip|blt.apps|Aucune des applications choisies", r2);
        }

        [Fact] public void Le_script_apply_garde_l_identifiant_Store_dans_le_journal_et_la_liste_correspond_au_json()
        {
            foreach (var id in new[] { "blt.apps", "blt.apps-tierces" })
            {
                var apply = Catalogue()[id].Apply;
                var liste = id == "blt.apps" ? "bloatware" : "bloatware-tierces";
                var attendu = known.Where(a => a.Liste == liste).ToDictionary(a => a.Package, a => a.StoreId ?? "");
                var dansScript = Regex.Matches(apply, @"^\s+'([^']+)' = '([^']*)'\s*$", RegexOptions.Multiline).Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
                Assert.Equal(attendu.OrderBy(k => k.Key), dansScript.OrderBy(k => k.Key));
            }
        }

        // ---- catalogue et niveaux ------------------------------------------------------------------------

        [Theory]
        [InlineData("Microsoft.BingNews", RiskLevel.Sur)]
        [InlineData("Microsoft.BingWeather", RiskLevel.Sur)]
        [InlineData("Microsoft.MicrosoftSolitaireCollection", RiskLevel.Sur)]
        [InlineData("Clipchamp.Clipchamp", RiskLevel.Sur)]
        [InlineData("Microsoft.Getstarted", RiskLevel.Sur)]
        [InlineData("MicrosoftTeams", RiskLevel.Sur)]
        [InlineData("MSTeams", RiskLevel.Sur)]
        [InlineData("Microsoft.MicrosoftOfficeHub", RiskLevel.Sur)]
        [InlineData("Microsoft.WindowsFeedbackHub", RiskLevel.Sur)]
        [InlineData("Microsoft.WindowsMaps", RiskLevel.Sur)]
        [InlineData("OneDrive", RiskLevel.Avance)]
        [InlineData("Microsoft.GamingApp", RiskLevel.Avance)]
        [InlineData("Microsoft.XboxApp", RiskLevel.Avance)]
        [InlineData("Microsoft.YourPhone", RiskLevel.Avance)]
        [InlineData("microsoft.windowscommunicationsapps", RiskLevel.Avance)]
        [InlineData("Microsoft.ZuneVideo", RiskLevel.Risque)]
        [InlineData("Microsoft.People", RiskLevel.Risque)]
        [InlineData("Microsoft.GetHelp", RiskLevel.Risque)]
        public void Chaque_application_a_le_niveau_de_la_table_de_couverture(string package, RiskLevel attendu)
        {
            var a = known.Single(x => x.Package == package);
            Assert.True(a.Risque == attendu, package + " : " + a.Risque + " au lieu de " + attendu);
        }

        [Fact] public void Les_applications_tierces_sont_toutes_Risque_et_dans_une_liste_distincte()
        {
            var t = known.Where(a => a.Liste == "bloatware-tierces").ToList();
            Assert.True(t.Count >= 8);
            Assert.All(t, a => Assert.Equal(RiskLevel.Risque, a.Risque));
            Assert.Contains(t, a => a.Package.Contains("Spotify"));
            Assert.Contains(t, a => a.Package.Contains("CandyCrush"));
            Assert.DoesNotContain(known.Where(a => a.Liste == "bloatware"), a => a.Package.Contains("Spotify") || a.Package.Contains("CandyCrush"));
            Assert.Equal(RiskLevel.Risque, Catalogue()["blt.apps-tierces"].Risque);
            Assert.Equal(RiskLevel.Sur, Catalogue()["blt.apps"].Risque);
        }

        [Fact] public void La_liste_connue_est_bien_formee()
        {
            Assert.Equal(known.Count, known.Select(a => a.Package).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(known, a =>
            {
                Assert.False(string.IsNullOrWhiteSpace(a.Titre), a.Package);
                Assert.Matches(@"^[A-Za-z0-9][A-Za-z0-9_.\-]*$", a.Package);        // accepté par la validation des profils
                Assert.True(a.Liste == "bloatware" || a.Liste == "bloatware-tierces", a.Package);
                if (a.StoreId != null) Assert.Matches(@"^[A-Z0-9]{12}$", a.StoreId);
            });
        }

        [Fact] public async Task Le_fournisseur_signale_la_reinstallation_possible_ou_manuelle()
        {
            var src = new SourceFactice("Microsoft.BingNews", "Microsoft.Getstarted", "Foo");
            var l = await new BloatwareProvider("bloatware", known, src).LoadAsync(CancellationToken.None);
            Assert.Equal(new[] { "Microsoft.BingNews", "Microsoft.Getstarted" }, l.Select(e => e.Key));
            Assert.Contains("depuis le Store", l[0].Detail);
            Assert.Contains("à la main", l[1].Detail);
            Assert.Empty(await new BloatwareProvider("bloatware-tierces", known, src).LoadAsync(CancellationToken.None));
        }

        private class SourceFactice : IInstalledAppsSource
        {
            private readonly string[] noms;
            public SourceFactice(params string[] noms) { this.noms = noms; }
            public Task<IReadOnlyList<string>> LireAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(noms);
        }

        [Fact] public void Le_catalogue_bloatware_contient_exactement_le_recapitulatif_et_Edge()
        {
            var cat = Catalogue();
            var recap = Couverture.Recapitulatif("Bloatware");
            Assert.Equal(7, recap.Count);
            var attendu = new HashSet<string>(recap.Select(r => r.Id)) { "blt.edge-remove" };
            Assert.Empty(cat.Keys.Where(k => !attendu.Contains(k)));
            Assert.Empty(attendu.Where(k => !cat.ContainsKey(k)));
            foreach (var r in recap)
            {
                var it = cat[r.Id];
                Assert.True(it.Risque == r.Risque, r.Id + " : risque " + it.Risque + " au lieu de " + r.Risque);
                Assert.True(it.Reversible == r.EstReversible, r.Id + " : réversible différent du récapitulatif");
                Assert.Equal(r.Groupe, it.Groupe);
                if (r.Reversible.StartsWith("partiel", StringComparison.Ordinal)) { /* annulation partielle signalée dans l'aperçu : voir test dédié */ }
            }
            var edge = cat["blt.edge-remove"];
            Assert.Equal(RiskLevel.Risque, edge.Risque);
            Assert.False(edge.Testable);
            Assert.True(edge.Reversible);
            foreach (var it in cat.Values)
            {
                Assert.False(string.IsNullOrWhiteSpace(it.Description), it.Id + " : description");
                Assert.False(string.IsNullOrWhiteSpace(it.Detect), it.Id + " : detect");
                Assert.False(string.IsNullOrWhiteSpace(it.Apply), it.Id + " : apply");
                Assert.Equal(it.Reversible, !string.IsNullOrWhiteSpace(it.Revert));
                Assert.Equal(new[] { "10", "11" }, it.Compatibilite.Windows);
                Assert.StartsWith("blt.", it.Id);
                // OneDrive s'installe par utilisateur : blt.apps dépend du profil de l'utilisateur courant.
                Assert.Equal(it.Id == "blt.apps", it.Hkcu);
            }
            Assert.Equal("bloatware", cat["blt.apps"].Liste);
            Assert.Equal("bloatware-tierces", cat["blt.apps-tierces"].Liste);
        }

        [Fact] public void Les_annulations_partielles_sont_signalees_dans_l_apercu()
        {
            var cat = Catalogue();
            foreach (var id in new[] { "blt.apps", "blt.apps-tierces", "blt.features", "blt.capabilities" })
                Assert.False(string.IsNullOrWhiteSpace(cat[id].AnnulationPartielle), id);
            Assert.True(string.IsNullOrWhiteSpace(cat["blt.wpbt-off"].AnnulationPartielle));
            Assert.Contains("à la main", cat["blt.apps"].AnnulationPartielle);
        }

        [Fact] public void Les_scripts_blt_sont_syntaxiquement_valides() => Assert.Empty(PsSyntax.CheckAll(Catalogue().Values));

        [Fact] public void Les_scripts_apply_blt_ne_modifient_le_systeme_que_par_les_fonctions_journalisees()
        {
            var interdits = new[] { "Set-ItemProperty", "New-ItemProperty", "Remove-ItemProperty", "Set-Service", "sc.exe", "reg add", "reg.exe", "reg delete",
                                    "Out-File", "Set-Content", "Add-Content", "WriteAllLines", "Remove-AppxPackage", "Remove-AppxProvisionedPackage", "dism.exe",
                                    "Disable-WindowsOptionalFeature", "Remove-WindowsCapability", "Remove-Item", "New-Item", "Stop-Process", "Invoke-Expression", "iex ",
                                    "Invoke-WebRequest", "powercfg", "bcdedit", "Set-MpPreference" };
            foreach (var it in Catalogue().Values)
            {
                foreach (var m in interdits)
                    Assert.False(it.Apply.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0, it.Id + " : apply contient « " + m + " »");
                // Seul le désinstalleur officiel d'Edge est lancé directement.
                if (it.Id != "blt.edge-remove") Assert.DoesNotContain("Start-Process", it.Apply);
            }
        }

        [Fact] public void Les_options_a_liste_declarent_leur_fournisseur_et_limitent_les_noms_acceptes()
        {
            foreach (var id in new[] { "blt.apps", "blt.apps-tierces" })
            {
                var it = Catalogue()[id];
                Assert.Contains("$Selection", it.Apply);
                Assert.Contains("Resolve-GTListSelection", it.Apply);
                Assert.Contains("Unknown", it.Apply);
                Assert.Contains("Skip-GTItem", it.Apply);
                Assert.Contains("Remove-TweakApp", it.Apply);
            }
        }

        // ---- DISM : fonctionnalités et capacités ----------------------------------------------------------

        /// <summary>DISM factice qui journalise ses arguments et bascule un fichier d'état.</summary>
        private string FauxDism(string etat)
        {
            var cmd = Path.Combine(tmp, "dism-factice.cmd");
            File.WriteAllText(cmd, "@echo off\r\necho %* >>\"" + Path.Combine(tmp, "dism.log") + "\"\r\n"
                + "echo %* | findstr /C:\"/Disable-Feature\" >nul && (echo Disabled>\"" + etat + "\")\r\n"
                + "echo %* | findstr /C:\"/Enable-Feature\" >nul && (echo Enabled>\"" + etat + "\")\r\n"
                + "exit /b 0\r\n");
            return "$GT_DismPath = " + Q(cmd) + "\r\n";
        }

        [Fact] public void Les_fonctionnalites_sont_desactivees_journalisees_puis_reactivees_a_l_annulation()
        {
            var etat = Path.Combine(tmp, "etat.txt");
            File.WriteAllText(etat, "Enabled");
            var pre = FauxDism(etat) + "function Get-GTOptionalFeatureState([string]$Name) { if ($Name -eq 'FaxServicesClientPackage') { return ([IO.File]::ReadAllText(" + Q(etat) + ").Trim()) } return $null }\r\n";
            var r = string.Join("\n", Apply(Catalogue()["blt.features"].Apply, "blt.features", new string[0], pre));
            Assert.Contains("##GT|ok|blt.features|1 fonctionnalité(s) désactivée(s)", r);
            var e = Journal.Read(Jrn).Single();
            Assert.Equal("dismfeature", e.Kind);
            Assert.Equal("FaxServicesClientPackage", e.Target);
            var log = File.ReadAllText(Path.Combine(tmp, "dism.log"));
            Assert.Contains("/Online /Disable-Feature /FeatureName:FaxServicesClientPackage /NoRestart", log);
            // Relance : déjà désactivée, plus rien (idempotent, pas de nouvelle entrée).
            var r2 = string.Join("\n", Apply(Catalogue()["blt.features"].Apply, "blt.features", new string[0], pre));
            Assert.Contains("##GT|skip|blt.features", r2);
            Assert.Single(Journal.Read(Jrn));
            // Annulation : réactivation.
            var rr = Restore("blt.features", pre);
            Assert.Contains("##GT|ok|blt.features", rr);
            Assert.Contains("/Online /Enable-Feature /FeatureName:FaxServicesClientPackage /NoRestart", File.ReadAllText(Path.Combine(tmp, "dism.log")));
            Assert.Equal("Enabled", File.ReadAllText(etat).Trim());
        }

        [Fact] public void Les_fonctionnalites_a_la_demande_sont_retirees_avec_leur_nom_complet_et_reinstallees()
        {
            var etat = Path.Combine(tmp, "cap.txt");
            File.WriteAllText(etat, "Installed");
            var cmd = Path.Combine(tmp, "dism-cap.cmd");
            File.WriteAllText(cmd, "@echo off\r\necho %* >>\"" + Path.Combine(tmp, "dism.log") + "\"\r\n"
                + "echo %* | findstr /C:\"/Remove-Capability\" >nul && (echo NotPresent>\"" + etat + "\")\r\n"
                + "echo %* | findstr /C:\"/Add-Capability\" >nul && (echo Installed>\"" + etat + "\")\r\nexit /b 3010\r\n");
            var pre = "$GT_DismPath = " + Q(cmd) + "\r\nfunction Get-WindowsCapability { [CmdletBinding()] param([switch]$Online) [pscustomobject]@{ Name = 'App.StepsRecorder~~~~0.0.1.0'; State = ([IO.File]::ReadAllText(" + Q(etat) + ").Trim()) }; [pscustomobject]@{ Name = 'OpenSSH.Client~~~~0.0.1.0'; State = 'Installed' } }\r\n";
            var r = string.Join("\n", Apply(Catalogue()["blt.capabilities"].Apply, "blt.capabilities", new string[0], pre));
            Assert.Contains("##GT|ok|blt.capabilities|1 fonctionnalité(s) retirée(s) ; un redémarrage est nécessaire.", r);
            var e = Journal.Read(Jrn).Single();
            Assert.Equal("dismcapability", e.Kind);
            Assert.Equal("App.StepsRecorder~~~~0.0.1.0", e.Target);
            var log = File.ReadAllText(Path.Combine(tmp, "dism.log"));
            Assert.Contains("/Online /Remove-Capability /CapabilityName:App.StepsRecorder~~~~0.0.1.0", log);
            Assert.DoesNotContain("OpenSSH", log);
            var rr = Restore("blt.capabilities", pre);
            Assert.Contains("##GT|ok|blt.capabilities", rr);
            Assert.Contains("/Online /Add-Capability /CapabilityName:App.StepsRecorder~~~~0.0.1.0", File.ReadAllText(Path.Combine(tmp, "dism.log")));
        }

        [Fact] public void La_restauration_d_une_capacite_se_compare_sans_le_numero_de_version()
        {
            var cmd = Path.Combine(tmp, "dism-cap2.cmd");
            File.WriteAllText(cmd, "@echo off\r\necho %* >>\"" + Path.Combine(tmp, "dism.log") + "\"\r\nexit /b 0\r\n");
            // Journal : retirée en version 0.0.1.0 ; la machine a maintenant la version 0.0.2.0 installée : rien à réinstaller.
            File.WriteAllText(Jrn, "{\"item\":\"blt.capabilities\",\"kind\":\"dismcapability\",\"target\":\"App.StepsRecorder~~~~0.0.1.0\",\"existed\":true,\"before\":\"Installed\",\"after\":\"NotPresent\"}\r\n");
            var pre = "$GT_DismPath = " + Q(cmd) + "\r\nfunction Get-WindowsCapability { [CmdletBinding()] param([switch]$Online) [pscustomobject]@{ Name = 'App.StepsRecorder~~~~0.0.2.0'; State = 'Installed' } }\r\n";
            var rr = Restore("blt.capabilities", pre);
            Assert.Contains("##GT|ok|blt.capabilities", rr);
            Assert.False(File.Exists(Path.Combine(tmp, "dism.log")), "aucune réinstallation : la capacité est déjà présente (autre version)");
        }

        // ---- Nouvel Outlook : blocage de la réinstallation automatique -------------------------------------

        private const string CleTest = @"HKCU:\Software\GamingTweakerTest\BltOutlook\OutlookUpdate";

        private static void NettoyerCleTest()
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\GamingTweakerTest\BltOutlook", false); } catch { }
        }

        [Fact] public void Le_nouvel_Outlook_supprime_la_cle_de_reinstallation_avec_journal_et_la_restaure_a_l_annulation()
        {
            NettoyerCleTest();
            try
            {
                Assert.Contains(@"HKLM:\SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\OutlookUpdate", Catalogue()["blt.apps"].Apply);
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\GamingTweakerTest\BltOutlook\OutlookUpdate")) k.SetValue("Marqueur", 7, Microsoft.Win32.RegistryValueKind.DWord);
                var pre = FauxAppx("Microsoft.OutlookForWindows") + "$GT_OutlookOobeKey = " + Q(CleTest) + "\r\n";
                var r = string.Join("\n", Apply(Catalogue()["blt.apps"].Apply, "blt.apps", new[] { "Microsoft.OutlookForWindows" }, pre));
                Assert.Contains("##GT|ok|blt.apps", r);
                Assert.Contains("Outlook bloquée", r);
                Assert.Null(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\GamingTweakerTest\BltOutlook\OutlookUpdate"));
                Assert.Contains(Journal.Read(Jrn), e => e.Kind == "regkey" && e.Target == CleTest);
                // Annulation : la clé et sa valeur reviennent (le Store est laissé hors de portée par un winget factice).
                var rr = Restore("blt.apps", FauxWinget());
                Assert.Contains("##GT|", rr);
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\GamingTweakerTest\BltOutlook\OutlookUpdate")) { Assert.NotNull(k); Assert.Equal(7, (int)k.GetValue("Marqueur")); }
            }
            finally { NettoyerCleTest(); }
        }

        [Fact] public void Sans_la_cle_Outlook_rien_n_est_journalise_et_sans_choix_d_Outlook_elle_est_intacte()
        {
            NettoyerCleTest();
            try
            {
                var pre = FauxAppx("Microsoft.OutlookForWindows", "Microsoft.BingNews") + "$GT_OutlookOobeKey = " + Q(CleTest) + "\r\n";
                var r = string.Join("\n", Apply(Catalogue()["blt.apps"].Apply, "blt.apps", new[] { "Microsoft.OutlookForWindows" }, pre));
                Assert.Contains("##GT|ok|blt.apps", r);
                Assert.DoesNotContain("Outlook bloquée", r);
                Assert.DoesNotContain(Journal.Read(Jrn), e => e.Kind == "regkey");
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\GamingTweakerTest\BltOutlook\OutlookUpdate")) k.SetValue("Marqueur", 1);
                File.Delete(Jrn);
                Apply(Catalogue()["blt.apps"].Apply, "blt.apps", new[] { "Microsoft.BingNews" }, pre);
                Assert.NotNull(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\GamingTweakerTest\BltOutlook\OutlookUpdate"));
            }
            finally { NettoyerCleTest(); }
        }

        // ---- OneDrive : détection par OneDrive.exe, jamais par OneDriveSetup.exe ------------------------------

        /// <summary>Dossiers factices (LOCALAPPDATA, ProgramFiles, SystemRoot) ; Start-Process et Get-Process sont remplacés.</summary>
        private string FauxOneDrive(bool installe, bool setupPresent, out string lancements)
        {
            var local = Path.Combine(tmp, "local"); var pf = Path.Combine(tmp, "pf"); var win = Path.Combine(tmp, "win");
            Directory.CreateDirectory(Path.Combine(win, "SysWOW64")); Directory.CreateDirectory(pf);
            if (installe) { Directory.CreateDirectory(Path.Combine(local, "Microsoft", "OneDrive")); File.WriteAllText(Path.Combine(local, "Microsoft", "OneDrive", "OneDrive.exe"), "x"); }
            else { try { Directory.Delete(local, true); } catch { } Directory.CreateDirectory(local); }
            if (setupPresent) File.WriteAllText(Path.Combine(win, "SysWOW64", "OneDriveSetup.exe"), "x");
            lancements = Path.Combine(tmp, "start.log");
            return "$env:LOCALAPPDATA = " + Q(local) + "; $env:ProgramFiles = " + Q(pf) + "; ${env:ProgramFiles(x86)} = " + Q(Path.Combine(tmp, "pf86")) + "; $env:SystemRoot = " + Q(win) + "\r\n"
                 + "function Get-Process { [CmdletBinding()] param([string]$Name) }\r\n"
                 + "function Start-Process { [CmdletBinding()] param([string]$FilePath, [string]$ArgumentList, [switch]$Wait, [switch]$PassThru) Add-Content -LiteralPath " + Q(lancements) + " -Value ($FilePath + ' ' + $ArgumentList); [pscustomobject]@{ ExitCode = 0 } }\r\n";
        }

        [Fact] public void OneDriveSetup_exe_seul_ne_signifie_pas_que_OneDrive_est_installe()
        {
            string log;
            var pre = FauxOneDrive(installe: false, setupPresent: true, out log);
            Assert.Empty(PsCommon.Eval(pre + PowerShellInstalledApps.ScriptOneDrive));
            var r = string.Join("\r\n", Apply("Remove-TweakOneDrive", "blt.apps", new string[0], pre));
            Assert.Contains("##GT|skip|blt.apps|OneDrive absent", r);
            Assert.False(File.Exists(log));
            Assert.False(File.Exists(Jrn));
        }

        [Fact] public void OneDrive_installe_pour_l_utilisateur_est_detecte_puis_desinstalle_avec_journal()
        {
            string log;
            var pre = FauxOneDrive(installe: true, setupPresent: true, out log);
            Assert.Equal(new[] { "OneDrive" }, PsCommon.Eval(pre + PowerShellInstalledApps.ScriptOneDrive));
            var r = string.Join("\r\n", Apply("Remove-TweakOneDrive", "blt.apps", new string[0], pre));
            Assert.Contains("##GT|ok|blt.apps", r);
            Assert.Contains("OneDriveSetup.exe /uninstall", File.ReadAllText(log));
            Assert.Equal("Microsoft.OneDrive", Journal.Read(Jrn).Single().Target);
        }

        [Fact] public void La_detection_OneDrive_du_fournisseur_et_du_script_sont_les_memes()
        {
            string log;
            var pre = FauxOneDrive(installe: true, setupPresent: false, out log);
            Assert.Equal("OUI", string.Join("", PsCommon.Eval(pre + "if (Test-GTOneDriveInstalled) { 'OUI' } else { 'NON' }")));
            pre = FauxOneDrive(installe: false, setupPresent: false, out log);
            Assert.Equal("NON", string.Join("", PsCommon.Eval(pre + "if (Test-GTOneDriveInstalled) { 'OUI' } else { 'NON' }")));
        }

        [Fact] public void La_reinstallation_winget_impossible_cite_le_bon_identifiant()
        {
            File.WriteAllText(Jrn, "{\"item\":\"blt.apps\",\"kind\":\"wingetapp\",\"target\":\"Microsoft.OneDrive\",\"existed\":true,\"before\":\"winget\",\"after\":null}\r\n");
            var r = Restore("blt.apps", "function Get-GTWingetPath { $null }\r\n");
            Assert.Contains("##GT|fail|blt.apps", r);
            Assert.Contains("winget install Microsoft.OneDrive", r);
            Assert.DoesNotContain("Microsoft.Edge", r);
        }

        // ---- Edge : fichier factice journalisé, setup.exe choisi par version ------------------------------------

        private string FauxEdge(out string app, out string dummy, out string lancements, params string[] versions)
        {
            app = Path.Combine(tmp, "edge", "Application");
            dummy = Path.Combine(tmp, "sysapps", "Microsoft.MicrosoftEdge_8wekyb3d8bbwe", "MicrosoftEdge.exe");
            lancements = Path.Combine(tmp, "edge-start.log");
            foreach (var v in versions) { Directory.CreateDirectory(Path.Combine(app, v, "Installer")); File.WriteAllText(Path.Combine(app, v, "Installer", "setup.exe"), "x"); }
            return "$GT_EdgeAppDir = " + Q(app) + "\r\n$GT_EdgeDummyPath = " + Q(dummy) + "\r\n"
                 + "function Set-TweakRegistryIfNeeded { param($Path, $Name, $Type, $Value) Add-Content -LiteralPath " + Q(lancements) + " -Value ('reg ' + $Path + ' ' + $Name) }\r\n"
                 + "function Start-Process { [CmdletBinding()] param([string]$FilePath, [string]$ArgumentList, [switch]$Wait, [switch]$PassThru) "
                 + "Add-Content -LiteralPath " + Q(lancements) + " -Value ('start ' + $FilePath + ' ' + $ArgumentList + ' dummy=' + (Test-Path -LiteralPath $GT_EdgeDummyPath)); [pscustomobject]@{ ExitCode = 0 } }\r\n";
        }

        [Fact] public void Le_setup_d_Edge_est_choisi_par_version_et_non_par_ordre_alphabetique()
        {
            string app, dummy, log;
            var pre = FauxEdge(out app, out dummy, out log, "99.0.1.0", "120.0.2.0", "9.5.0.0");
            Directory.CreateDirectory(Path.Combine(app, "Dictionnaire")); File.WriteAllText(Path.Combine(app, "Dictionnaire", "x"), "x");
            var r = PsCommon.Eval(pre + "Get-GTEdgeSetup (Get-GTEdgeAppDir)");
            Assert.Equal(Path.Combine(app, "120.0.2.0", "Installer", "setup.exe"), r.Single());
            Assert.Empty(PsCommon.Eval("if ($null -eq (Get-GTEdgeSetup " + Q(Path.Combine(tmp, "absent")) + ")) { }"));
        }

        [Fact] public void Edge_cree_le_fichier_factice_journalise_avant_le_lancement_puis_l_annulation_le_supprime()
        {
            string app, dummy, log;
            var pre = FauxEdge(out app, out dummy, out log, "99.0.1.0", "120.0.2.0");
            var r = string.Join("\n", Apply(Catalogue()["blt.edge-remove"].Apply, "blt.edge-remove", new string[0], pre));
            Assert.Contains("##GT|ok|blt.edge-remove", r);
            var l = File.ReadAllText(log);
            Assert.Contains(Path.Combine("120.0.2.0", "Installer", "setup.exe") + " --uninstall --system-level --force-uninstall dummy=True", l);
            Assert.DoesNotContain("--delete-profile", l);
            Assert.True(File.Exists(dummy));
            var j = Journal.Read(Jrn).ToList();
            var iFichier = j.FindIndex(e => e.Kind == "newfile" && e.Target == dummy);
            var iWinget = j.FindIndex(e => e.Kind == "wingetapp" && e.Target == "Microsoft.Edge");
            Assert.True(iFichier >= 0 && iWinget > iFichier, "le fichier factice est journalisé avant le désinstalleur");
            var rr = Restore("blt.edge-remove", FauxWinget());
            Assert.Contains("##GT|ok|blt.edge-remove", rr);
            Assert.False(File.Exists(dummy));
            Assert.False(Directory.Exists(Path.GetDirectoryName(dummy)), "dossier créé par nous : supprimé aussi");
            Assert.Contains("install --source winget --id Microsoft.Edge --exact", LogWinget());
        }

        [Fact] public void Un_fichier_factice_deja_present_n_est_ni_journalise_ni_supprime()
        {
            string app, dummy, log;
            var pre = FauxEdge(out app, out dummy, out log, "120.0.2.0");
            Directory.CreateDirectory(Path.GetDirectoryName(dummy)); File.WriteAllText(dummy, "existant");
            var r = string.Join("\n", Apply(Catalogue()["blt.edge-remove"].Apply, "blt.edge-remove", new string[0], pre));
            Assert.Contains("##GT|ok|blt.edge-remove", r);
            Assert.DoesNotContain(Journal.Read(Jrn), e => e.Kind == "newfile");
            Restore("blt.edge-remove", FauxWinget());
            Assert.Equal("existant", File.ReadAllText(dummy));
        }

        [Fact] public void Edge_absent_est_ignore_sans_creer_de_fichier()
        {
            string app, dummy, log;
            var pre = FauxEdge(out app, out dummy, out log);
            var r = string.Join("\n", Apply(Catalogue()["blt.edge-remove"].Apply, "blt.edge-remove", new string[0], pre));
            Assert.Contains("##GT|skip|blt.edge-remove", r);
            Assert.False(File.Exists(dummy));
            Assert.False(File.Exists(Jrn));
        }

        [Fact] public void La_description_d_Edge_ne_promet_plus_de_blocage_de_reinstallation()
        {
            var d = Catalogue()["blt.edge-remove"].Description;
            Assert.DoesNotContain("Bloque aussi", d);
            Assert.DoesNotContain("réinstallation automatique par Windows Update", d);
            Assert.Contains("mise à jour majeure", d);
            Assert.DoesNotContain("DoNotUpdateToEdgeWithChromium", Catalogue()["blt.edge-remove"].Apply);
        }

        [Fact] public void Les_fonctionnalites_protegees_ne_sont_jamais_visees()
        {
            var cat = Catalogue();
            foreach (var jamais in new[] { "DirectPlay", "LegacyComponents", "MediaPlayback", "WindowsMediaPlayer", "Printing-PrintToPDFServices", "OpenSSH", "DirectX", "Microsoft-Hyper-V", "Windows-Subsystem-Linux", "WSearch" })
            {
                Assert.DoesNotContain(jamais, cat["blt.features"].Apply);
                Assert.DoesNotContain(jamais, cat["blt.capabilities"].Apply);
            }
        }

        // ---- réinstallation d'Edge et valeurs de registre ------------------------------------------------

        [Fact] public void L_annulation_d_Edge_passe_par_winget_install_Microsoft_Edge()
        {
            File.WriteAllText(Jrn, "{\"item\":\"blt.edge-remove\",\"kind\":\"wingetapp\",\"target\":\"Microsoft.Edge\",\"existed\":true,\"before\":\"winget\",\"after\":null}\n");
            var r = Restore("blt.edge-remove", FauxWinget());
            Assert.Contains("##GT|ok|blt.edge-remove", r);
            Assert.Contains("install --source winget --id Microsoft.Edge --exact", LogWinget());
        }

        [Theory]
        [InlineData("blt.wpbt-off", @"HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager", "DisableWpbtExecution", 1)]
        [InlineData("blt.device-metadata-off", @"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", 1)]
        [InlineData("blt.vendor-autoinstall-off", @"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer", "DisableCoInstallers", 1)]
        public void Les_options_de_registre_ecrivent_la_bonne_valeur_par_la_fonction_journalisee(string id, string chemin, string nom, int valeur)
        {
            var it = Catalogue()[id];
            Assert.Contains("Set-TweakRegistryIfNeeded", it.Apply);
            Assert.Contains(Q(chemin), it.Apply);
            Assert.Contains(Q(nom), it.Apply);
            Assert.Contains("-Value " + valeur, it.Apply);
            Assert.Contains("Reset-GTTweaks", it.Revert);
        }
    }
}
