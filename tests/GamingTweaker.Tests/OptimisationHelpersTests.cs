using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Tests.Helpers;
using Microsoft.Win32;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Aides de Common.ps1 propres au menu Optimisation. Fonctions pures évaluées dans un vrai powershell.exe ;
    /// aucune modification du système (les scripts qui en feraient un ne sont lancés qu'avec des sélections refusées).
    /// </summary>
    public class OptimisationHelpersTests : IDisposable
    {
        private readonly string sub = @"Software\GamingTweakerTest\" + Guid.NewGuid().ToString("N");
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "GTOpt-" + Guid.NewGuid().ToString("N"));
        private string Journal => Path.Combine(tmp, "Sessions", "20260101-100000", "journal.jsonl");

        public OptimisationHelpersTests() { Directory.CreateDirectory(Path.GetDirectoryName(Journal)); }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(sub, false); } catch { }
            try { Directory.Delete(tmp, true); } catch { }
        }

        private static string Un(string snippet) => string.Join("\n", PsCommon.Eval(snippet));

        // ---- sélection des listes ---------------------------------------------------------------

        [Theory]
        [InlineData("@('*')", "Fax,Spooler,lfsvc|")]
        [InlineData("@('*','!Fax')", "Spooler,lfsvc|")]
        [InlineData("@('*','!Inconnu')", "Fax,Spooler,lfsvc|")]       // exclusion d'un nom inconnu : sans effet
        [InlineData("@('spooler','FAX')", "Fax,Spooler|")]            // casse ignorée, ordre de la liste autorisée
        [InlineData("@('Fax','Winmgmt')", "Fax|Winmgmt")]             // nom hors liste : signalé
        [InlineData("@('*','Winmgmt')", "Fax,Spooler,lfsvc|Winmgmt")]
        [InlineData("@('!Fax')", "|")]                                 // exclusions seules : rien de choisi
        [InlineData("@()", "|")]
        public void Resolve_GTListSelection_applique_la_convention_des_listes(string selection, string attendu)
        {
            var r = Un("$r = Resolve-GTListSelection -Selection " + selection + " -Allowed @('Fax','Spooler','lfsvc')\n"
                     + "Write-Output (($r.Names -join ',') + '|' + ($r.Unknown -join ','))");
            Assert.Equal(attendu, r);
        }

        // ---- plans d'alimentation ---------------------------------------------------------------

        [Fact]
        public void La_liste_des_plans_est_lue_en_anglais_et_en_francais()
        {
            var en = "Existing Power Schemes (* Active)\n-----------------------------------\n"
                   + "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\n"
                   + "Power Scheme GUID: e9a42b02-d5df-448d-aa00-03f14749eb61  (Ultimate Performance)\n";
            var fr = "Modes de gestion de l’alimentation existants (* Actif)\n-----------------------------------\n"
                   + "GUID du mode de gestion de l’alimentation : 381b4222-f694-41f0-9685-ff5bb260df2e  (Utilisation normale) *\n"
                   + "GUID du mode de gestion de l’alimentation : 8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C  (Performances élevées (test))\n";
            foreach (var texte in new[] { en, fr })
            {
                var r = Un("$lignes = @'\n" + texte + "'@ -split \"`n\"\n$s = @(ConvertFrom-GTPowerSchemeList $lignes)\n"
                         + "Write-Output ($s.Count)\nforeach ($x in $s) { Write-Output ($x.Guid + '|' + $x.Name + '|' + $x.Active) }");
                var l = r.Split('\n');
                Assert.Equal("2", l[0]);
                Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e|" + (texte == en ? "Balanced" : "Utilisation normale") + "|True", l[1]);
                Assert.EndsWith("|False", l[2]);
            }
            Assert.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c|Performances élevées (test)|False",
                Un("$s = @(ConvertFrom-GTPowerSchemeList @('GUID du mode : 8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C  (Performances élevées (test))'))\nWrite-Output ($s[0].Guid + '|' + $s[0].Name + '|' + $s[0].Active)"));
        }

        [Fact]
        public void L_existence_d_un_plan_se_teste_par_son_guid_sans_tenir_compte_de_la_casse()
        {
            var r = Un("$l = @('Power Scheme GUID: E9A42B02-D5DF-448D-AA00-03F14749EB61  (Ultimate Performance)')\n"
                     + "Write-Output ((Test-GTSchemeInList $l 'e9a42b02-d5df-448d-aa00-03f14749eb61').ToString() + '|' + (Test-GTSchemeInList $l '381b4222-f694-41f0-9685-ff5bb260df2e').ToString())");
            Assert.Equal("True|False", r);
        }

        [Fact]
        public void L_etat_de_la_veille_prolongee_vient_du_registre_et_non_d_un_texte_traduit()
        {
            var r = Un("Write-Output ((ConvertTo-GTHibernateEnabled 1).ToString() + '|' + (ConvertTo-GTHibernateEnabled 0).ToString() + '|' + (ConvertTo-GTHibernateEnabled $null).ToString())");
            Assert.Equal("True|False|False", r);
            // Lecture réelle (lecture seule) : un booléen, sans erreur.
            Assert.Matches("^(True|False)$", Un("Write-Output (Get-GTHibernateEnabled).ToString()"));
        }

        // ---- périphériques ----------------------------------------------------------------------

        [Fact]
        public void Les_instances_USB_et_reseau_sont_choisies_par_leur_identifiant_PnP()
        {
            var noms = "@('USB\\ROOT_HUB30\\4&1&0&0_0','USB\\VID_046D&PID_C08B\\6&2&0&1_0','HID\\VID_046D\\7&1_0','PCI\\VEN_8086&DEV_15F3\\4&2_0','PCI\\VEN_8086&DEV_15F3\\4&2_01')";
            Assert.Equal("USB\\ROOT_HUB30\\4&1&0&0_0;USB\\VID_046D&PID_C08B\\6&2&0&1_0",
                Un("Write-Output ((Select-GTUsbInstances " + noms + ") -join ';')"));
            // Une instance porte l'identifiant PnP suivi de _ et d'un numéro ; un autre identifiant ne correspond pas.
            Assert.Equal("PCI\\VEN_8086&DEV_15F3\\4&2_0;PCI\\VEN_8086&DEV_15F3\\4&2_01",
                Un("Write-Output ((Select-GTInstancesByPnpIds " + noms + " @('pci\\ven_8086&dev_15f3\\4&2')) -join ';')"));
            Assert.Equal("", Un("Write-Output ((Select-GTInstancesByPnpIds " + noms + " @('PCI\\VEN_10EC&DEV_8168\\1')) -join ';')"));
        }

        // ---- souris, démarrage ------------------------------------------------------------------

        [Fact]
        public void Les_parametres_SPI_SETMOUSE_viennent_des_valeurs_du_registre()
        {
            Assert.Equal("0,0,0", Un("Write-Output ((ConvertTo-GTMouseSpiArgs '0' '0' '0') -join ',')"));
            Assert.Equal("6,10,1", Un("Write-Output ((ConvertTo-GTMouseSpiArgs '6' '10' '1') -join ',')"));
            Assert.Equal("0,0,0", Un("Write-Output ((ConvertTo-GTMouseSpiArgs $null 'abc' '') -join ',')"));   // illisible : 0
        }

        [Fact]
        public void Une_entree_de_demarrage_est_desactivee_si_le_premier_octet_est_impair()
        {
            Assert.Equal("False|True|True|False", Un(
                "Write-Output ((Test-GTStartupDisabled ([byte[]](2,0,0,0,0,0,0,0,0,0,0,0))).ToString() + '|' + (Test-GTStartupDisabled ([byte[]](3,0,0,0,1,2,3,4,5,6,7,8))).ToString()"
              + " + '|' + (Test-GTStartupDisabled ([byte[]](7,0,0,0,0,0,0,0,0,0,0,0))).ToString() + '|' + (Test-GTStartupDisabled ([byte[]](6,0,0,0,0,0,0,0,0,0,0,0))).ToString())"));
            Assert.Equal("False", Un("Write-Output (Test-GTStartupDisabled $null).ToString()"));
            var v = Un("$b = New-GTStartupDisabledValue\nWrite-Output ($b.Length.ToString() + '|' + $b[0] + '|' + (Test-GTStartupDisabled $b).ToString())");
            Assert.Equal("12|3|True", v);
        }

        [Fact]
        public void La_cle_d_une_entree_de_demarrage_est_portee_sur_trois_champs()
        {
            Assert.Equal("HKCU:Run:Discord|HKLM:Run32:Foo: bar|HKCU:StartupFolder:a.lnk",
                Un("Write-Output ((Get-GTStartupKey 'HKCU' 'Run' 'Discord') + '|' + (Get-GTStartupKey 'HKLM' 'Run32' 'Foo: bar') + '|' + (Get-GTStartupKey 'HKCU' 'StartupFolder' 'a.lnk'))"));
        }

        // ---- valeurs 32 bits non signées (NetworkThrottlingIndex) -----------------------------------

        [Fact]
        public async Task Une_valeur_DWord_non_signee_maximale_est_ecrite_puis_reconnue_comme_appliquee()
        {
            var k = "'HKCU:\\" + sub + "'";
            var apply = "Set-TweakRegistryIfNeeded -Path " + k + " -Name 'N' -Type DWord -Value 4294967295";
            var r = await Executer("opt.test", apply);
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            using (var key = Registry.CurrentUser.OpenSubKey(sub)) Assert.Equal(-1, key.GetValue("N"));
            var lignes = File.ReadAllLines(Journal).Length;
            await Executer("opt.test", apply);
            Assert.Equal(lignes, File.ReadAllLines(Journal).Length);   // idempotent : rien de nouveau au journal
        }

        private async Task<RunResult> Executer(string id, string apply, List<string> liste = null, string menu = "Optimisation")
        {
            var it = new CatalogItem { Id = id, Menu = menu, Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Reversible = true, Detect = "d", Apply = apply };
            var sel = new Selection { OptionIds = new HashSet<string> { id } };
            if (liste != null) sel.Listes[id] = liste;
            return await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
        }

        // ---- scripts HKCU : cycle complet sous la clé de test --------------------------------------

        private static readonly string[][] Redirections =
        {
            new[] { @"HKCU:\System\GameConfigStore", "GameConfigStore" },
            new[] { @"HKCU:\Software\Microsoft\GameBar", "GameBar" },
            new[] { @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualEffects" },
            new[] { @"HKCU:\Control Panel\Accessibility", "Accessibility" },
            new[] { @"HKCU:\Control Panel\Mouse", "Mouse" },
            new[] { @"HKCU:\Control Panel\Keyboard", "Keyboard" },
            new[] { @"HKCU:\Control Panel\Desktop", "Desktop" },
            new[] { @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ExplorerAdvanced" },
            new[] { @"HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "StoragePolicy" },
            new[] { @"HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "Personalize" },
            new[] { @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "PoliciesExplorer" },
            new[] { @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Search", "Search" },
            new[] { @"HKCU:\Software\Policies\Microsoft\Windows\Explorer", "PolicyWindowsExplorer" },
            new[] { @"HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications", "PushNotifications" },
            new[] { @"HKCU:\Software\Classes\CLSID", "CLSID" },
            new[] { @"HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell", "Shell" },
        };

        private string Redirige(string script)
        {
            foreach (var r in Redirections) script = script.Replace(r[0], @"HKCU:\" + sub + "\\" + r[1]);
            // Plus aucun chemin HKCU hors de la clé de test.
            foreach (Match m in Regex.Matches(script, @"HKCU:\[^']*"))
                Assert.StartsWith(@"HKCU:\" + sub, m.Value);
            return script;
        }

        private Dictionary<string, string> Instantane()
        {
            var res = new Dictionary<string, string>();
            using (var k = Registry.CurrentUser.OpenSubKey(sub)) if (k != null) Lire(k, "", res);
            return res;
        }

        private static void Lire(RegistryKey k, string chemin, Dictionary<string, string> res)
        {
            foreach (var n in k.GetValueNames()) res[chemin + "|" + n] = Convert.ToString(k.GetValue(n)) + "/" + k.GetValueKind(n);
            foreach (var s in k.GetSubKeyNames()) using (var c = k.OpenSubKey(s)) Lire(c, chemin + "\\" + s, res);
        }

        [Theory]
        [InlineData("opt.gamemode-on")] [InlineData("opt.gamedvr-off")] [InlineData("opt.fso-off")] [InlineData("opt.visualfx")]
        [InlineData("opt.stickykeys-off")] [InlineData("opt.mouse.precision-off")]
        public async Task Les_scripts_HKCU_s_appliquent_se_detectent_et_s_annulent_sous_la_cle_de_test(string id)
        {
            var cat = Catalogue.LoadEmbedded().Single(i => i.Id == id);
            var it = new CatalogItem
            {
                Id = id, Menu = "Optimisation", Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Reversible = true,
                Apply = Redirige(cat.Apply), Detect = Redirige(cat.Detect), Revert = Redirige(cat.Revert),
            };
            Assert.Empty(Instantane());
            Assert.NotEqual(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);   // clé absente : NotApplied (ou Unknown pour la souris)

            var sel = new Selection { OptionIds = new HashSet<string> { id } };
            var r = await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            var applique = Instantane();
            Assert.NotEmpty(applique);
            Assert.Equal(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);

            // Deuxième application : idempotente, rien de nouveau au journal ni dans le registre.
            var lignes = File.ReadAllLines(Journal).Length;
            await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(lignes, File.ReadAllLines(Journal).Length);
            Assert.Equal(applique, Instantane());

            // Annulation par le journal : l'état d'origine (clé de test vide) revient.
            var svc = new RestaurationService(new DataPaths { SessionsDir = Path.Combine(tmp, "Sessions"), MachineDir = Path.Combine(tmp, "M") });
            Assert.Equal(OutcomeStatus.Ok, (await svc.AnnulerParJournalAsync(Journal, id)).Status);
            Assert.Empty(Instantane());

            // Retour aux valeurs par défaut de Windows par le script revert : plus détecté comme appliqué.
            await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal + "2"), null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Ok, (await svc.ExecuterRevertAsync(it)).Status);
            Assert.NotEqual(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);
        }

        [Theory]
        [InlineData("opt.storagesense-off")] [InlineData("opt.show-extensions")] [InlineData("opt.show-hidden")] [InlineData("opt.dark-mode")]
        [InlineData("opt.scrollbars-visible")] [InlineData("opt.numlock-on")] [InlineData("opt.snap-off")] [InlineData("opt.settings-home-off")]
        [InlineData("opt.taskbar-left")] [InlineData("opt.taskbar-search-off")] [InlineData("opt.taskview-off")] [InlineData("opt.endtask-taskbar")]
        [InlineData("opt.explorer-home-off")] [InlineData("opt.classic-context-menu")] [InlineData("opt.notifications-off")]
        public async Task Les_options_confort_HKCU_s_appliquent_se_detectent_et_s_annulent_sous_la_cle_de_test(string id)
        {
            var cat = Catalogue.LoadEmbedded().Single(i => i.Id == id);
            Assert.True(cat.Hkcu);
            var it = Redirigee(cat);
            Registry.CurrentUser.CreateSubKey(sub + @"\CLSID").Dispose();   // parent existant : l'annulation d'une clé créée ne remonte pas au-dessus
            Assert.Empty(Instantane());
            Assert.Equal(DetectState.NotApplied, (await Detection.RunAsync(new[] { it }))[id]);

            var sel = new Selection { OptionIds = new HashSet<string> { id } };
            var r = await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            var applique = Instantane();
            Assert.NotEmpty(applique);
            Assert.Equal(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);

            var lignes = File.ReadAllLines(Journal).Length;
            await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(lignes, File.ReadAllLines(Journal).Length);   // idempotent
            Assert.Equal(applique, Instantane());

            var svc = Service();
            Assert.Equal(OutcomeStatus.Ok, (await svc.AnnulerParJournalAsync(Journal, id)).Status);
            Assert.Empty(Instantane());

            await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal + "2"), null, CancellationToken.None, _ => 120);
            Assert.Equal(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);
            Assert.Equal(OutcomeStatus.Ok, (await svc.ExecuterRevertAsync(it)).Status);
            Assert.NotEqual(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);
        }

        private CatalogItem Redirigee(CatalogItem cat) => new CatalogItem
        {
            Id = cat.Id, Menu = "Optimisation", Groupe = "G", Titre = "T", Risque = RiskLevel.Avance, Reversible = true,
            Apply = Redirige(cat.Apply), Detect = Redirige(cat.Detect), Revert = Redirige(cat.Revert),
        };

        private RestaurationService Service() =>
            new RestaurationService(new DataPaths { SessionsDir = Path.Combine(tmp, "Sessions"), MachineDir = Path.Combine(tmp, "M") });

        [Fact]
        public async Task La_page_d_accueil_des_Parametres_garde_les_autres_pages_masquees()
        {
            var id = "opt.settings-home-off";
            var it = Redirigee(Catalogue.LoadEmbedded().Single(i => i.Id == id));
            using (var k = Registry.CurrentUser.CreateSubKey(sub + "\\PoliciesExplorer")) k.SetValue("SettingsPageVisibility", "hide:about;windowsupdate", RegistryValueKind.String);
            var sel = new Selection { OptionIds = new HashSet<string> { id } };
            Assert.Equal(DetectState.NotApplied, (await Detection.RunAsync(new[] { it }))[id]);
            await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
            using (var k = Registry.CurrentUser.OpenSubKey(sub + "\\PoliciesExplorer")) Assert.Equal("hide:about;windowsupdate;home", k.GetValue("SettingsPageVisibility"));
            Assert.Equal(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);
            Assert.Equal(OutcomeStatus.Ok, (await Service().ExecuterRevertAsync(it)).Status);   // le script revert ne retire que « home »
            using (var k = Registry.CurrentUser.OpenSubKey(sub + "\\PoliciesExplorer")) Assert.Equal("hide:about;windowsupdate", k.GetValue("SettingsPageVisibility"));
        }

        [Fact]
        public async Task La_decouverte_automatique_exporte_Bags_et_BagMRU_puis_l_annulation_les_reimporte()
        {
            var id = "opt.explorer-autodiscovery-off";
            var it = Redirigee(Catalogue.LoadEmbedded().Single(i => i.Id == id));
            using (var k = Registry.CurrentUser.CreateSubKey(sub + "\\Shell\\Bags\\5\\Shell\\{guid}"))
            {
                k.SetValue("Mode", 4, RegistryValueKind.DWord); k.SetValue("Texte", "é à", RegistryValueKind.String);
                k.SetValue("Bin", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(sub + "\\Shell\\BagMRU\\0")) k.SetValue("NodeSlot", 5, RegistryValueKind.DWord);
            var avant = Instantane();
            var sel = new Selection { OptionIds = new HashSet<string> { id } };
            Assert.Equal(DetectState.NotApplied, (await Detection.RunAsync(new[] { it }))[id]);

            var r = await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            var apres = Instantane();
            Assert.Single(apres);
            Assert.Equal("NotSpecified/String", apres[@"\Shell\Bags\AllFolders\Shell|FolderType"]);
            Assert.Equal(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);
            // Deux exports .reg dans le dossier de la session, deux entrées « regkey » puis l'écriture de la valeur.
            Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(Journal), "*.reg").Length);
            var kinds = File.ReadAllLines(Journal).Select(l => (string)Newtonsoft.Json.Linq.JObject.Parse(l)["kind"]).ToList();
            Assert.Equal(new[] { "regkey", "regkey", "registry" }, kinds);

            var lignes = File.ReadAllLines(Journal).Length;
            await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(lignes, File.ReadAllLines(Journal).Length);   // idempotent
            Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(Journal), "*.reg").Length);

            Assert.Equal(OutcomeStatus.Ok, (await Service().AnnulerParJournalAsync(Journal, id)).Status);
            Assert.Equal(avant, Instantane());   // vues de dossiers d'origine rétablies à l'identique
        }

        [Fact]
        public async Task Le_revert_de_la_decouverte_automatique_retire_seulement_le_type_generique()
        {
            var id = "opt.explorer-autodiscovery-off";
            var it = Redirigee(Catalogue.LoadEmbedded().Single(i => i.Id == id));
            var sel = new Selection { OptionIds = new HashSet<string> { id } };
            await Runner.RunAsync(ScriptBuilder.Build(new[] { it }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(DetectState.Applied, (await Detection.RunAsync(new[] { it }))[id]);
            Assert.Equal(OutcomeStatus.Ok, (await Service().ExecuterRevertAsync(it)).Status);
            Assert.Equal(DetectState.NotApplied, (await Detection.RunAsync(new[] { it }))[id]);
        }

        // ---- clés entières : Remove-TweakRegistryKey et Add-TweakRegistryKey ------------------------------

        private string Cle(string nom) => @"HKCU:\" + sub + "\\" + nom;

        [Fact]
        public async Task Remove_TweakRegistryKey_exporte_la_cle_puis_la_supprime_et_l_annulation_la_reimporte()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(sub + "\\Victime\\Sous"))
            {
                k.SetValue("A", 7, RegistryValueKind.DWord); k.SetValue("B", "accentué é", RegistryValueKind.String);
                k.SetValue("C", new[] { "x", "y" }, RegistryValueKind.MultiString);
            }
            using (var k = Registry.CurrentUser.OpenSubKey(sub + "\\Victime", true)) k.SetValue("", "defaut", RegistryValueKind.String);
            var avant = Instantane();
            var r = await Executer("opt.test", "Remove-TweakRegistryKey -Path '" + Cle("Victime") + "' | Out-Null");
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            Assert.Empty(Instantane());
            var reg = Directory.GetFiles(Path.GetDirectoryName(Journal), "*.reg").Single();
            Assert.Contains("Victime", File.ReadAllText(reg));
            var e = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllLines(Journal).Single());
            Assert.Equal("regkey", (string)e["kind"]);
            Assert.Equal(Cle("Victime"), (string)e["target"]);
            Assert.Equal(reg, (string)e["before"]);

            // Clé absente : rien n'est fait ni journalisé.
            var lignes = File.ReadAllLines(Journal).Length;
            await Executer("opt.test", "Remove-TweakRegistryKey -Path '" + Cle("Victime") + "' | Out-Null");
            Assert.Equal(lignes, File.ReadAllLines(Journal).Length);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Journal), "*.reg"));

            Assert.Equal(OutcomeStatus.Ok, (await Service().AnnulerParJournalAsync(Journal, "opt.test")).Status);
            Assert.Equal(avant, Instantane());
        }

        [Fact]
        public async Task Remove_TweakRegistryKey_remplace_une_cle_recreee_depuis_par_la_sauvegarde()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(sub + "\\V2")) k.SetValue("Orig", 1, RegistryValueKind.DWord);
            var avant = Instantane();
            await Executer("opt.test", "Remove-TweakRegistryKey -Path '" + Cle("V2") + "' | Out-Null\nSet-TweakRegistryIfNeeded -Path '" + Cle("V2") + "' -Name 'Nouveau' -Type DWord -Value 9");
            Assert.Equal(OutcomeStatus.Ok, (await Service().AnnulerParJournalAsync(Journal, "opt.test")).Status);
            Assert.Equal(avant, Instantane());   // la valeur « Nouveau » créée après la suppression disparaît aussi
        }

        [Fact]
        public async Task Add_TweakRegistryKey_cree_la_cle_avec_une_valeur_par_defaut_vide_et_l_annulation_retire_les_cles_creees()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(sub + "\\Existant")) k.SetValue("Garde", 1, RegistryValueKind.DWord);
            var avant = Instantane();
            var cible = Cle(@"Existant\{abc}\InprocServer32");
            var r = await Executer("opt.test", "Add-TweakRegistryKey -Path '" + cible + "' | Out-Null");
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            using (var k = Registry.CurrentUser.OpenSubKey(sub + @"\Existant\{abc}\InprocServer32"))
            {
                Assert.NotNull(k);
                Assert.Contains("", k.GetValueNames());
                Assert.Equal("", k.GetValue(""));
            }
            Assert.Equal("True", Un("Write-Output (Test-GTRegistryKeyDefault '" + cible + "').ToString()"));
            var lignes = File.ReadAllLines(Journal).Length;
            await Executer("opt.test", "Add-TweakRegistryKey -Path '" + cible + "' | Out-Null");   // idempotent
            Assert.Equal(lignes, File.ReadAllLines(Journal).Length);

            Assert.Equal(OutcomeStatus.Ok, (await Service().AnnulerParJournalAsync(Journal, "opt.test")).Status);
            Assert.Equal(avant, Instantane());
            using (var k = Registry.CurrentUser.OpenSubKey(sub + @"\Existant\{abc}")) Assert.Null(k);   // la clé parente créée est supprimée aussi
            using (var k = Registry.CurrentUser.OpenSubKey(sub + @"\Existant")) Assert.NotNull(k);
        }

        [Fact]
        public async Task Add_TweakRegistryKey_sur_une_cle_sans_valeur_par_defaut_ne_retire_que_cette_valeur()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(sub + "\\SansDefaut")) k.SetValue("Autre", 3, RegistryValueKind.DWord);
            var avant = Instantane();
            await Executer("opt.test", "Add-TweakRegistryKey -Path '" + Cle("SansDefaut") + "' | Out-Null");
            using (var k = Registry.CurrentUser.OpenSubKey(sub + "\\SansDefaut")) Assert.Contains("", k.GetValueNames());
            Assert.Equal(OutcomeStatus.Ok, (await Service().AnnulerParJournalAsync(Journal, "opt.test")).Status);
            Assert.Equal(avant, Instantane());
        }

        // ---- note de fin d'option -----------------------------------------------------------------------

        [Fact]
        public async Task Une_option_peut_joindre_une_note_a_son_succes()
        {
            var r = await Executer("opt.test", "Set-GTItemNote 'Déchiffrement lancé | en arrière-plan'");
            var o = r.Items.Single();
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            Assert.Equal("Déchiffrement lancé / en arrière-plan", o.Message);
            var r2 = await Executer("opt.test", "$null = 1");
            Assert.Null(r2.Items.Single().Message);
        }

        // ---- listes : refus des noms inconnus (aucune modification n'est possible dans ces cas) -----------

        [Theory]
        [InlineData("opt.services", "Winmgmt")]
        [InlineData("opt.xbox-services", "Spooler")]         // un service de l'autre liste n'est pas accepté
        [InlineData("opt.startup", "HKLM:Run:__GamingTweakerTest_inexistant__")]
        public async Task Une_selection_hors_de_la_liste_autorisee_est_refusee_sans_rien_modifier(string id, string nom)
        {
            var item = Catalogue.LoadEmbedded().Single(i => i.Id == id);
            var avant = File.Exists(Journal) ? File.ReadAllLines(Journal).Length : 0;
            var sel = new Selection { OptionIds = new HashSet<string> { id } };
            sel.Listes[id] = new List<string> { nom };
            var r = await Runner.RunAsync(ScriptBuilder.Build(new[] { item }, sel, Journal), null, CancellationToken.None, _ => 120);
            var o = r.Items.Single();
            Assert.Equal(OutcomeStatus.Skipped, o.Status);
            Assert.Contains(nom, o.Message);
            Assert.Equal(avant, File.Exists(Journal) ? File.ReadAllLines(Journal).Length : 0);
        }

        [Theory]
        [InlineData("opt.services")]
        [InlineData("opt.xbox-services")]
        [InlineData("opt.startup")]
        public async Task Une_selection_vide_ne_fait_rien(string id)
        {
            var item = Catalogue.LoadEmbedded().Single(i => i.Id == id);
            var sel = new Selection { OptionIds = new HashSet<string> { id } };
            sel.Listes[id] = new List<string> { "!Fax" };
            var r = await Runner.RunAsync(ScriptBuilder.Build(new[] { item }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Skipped, r.Items.Single().Status);
            Assert.False(File.Exists(Journal));
        }

        // ---- détections en lecture seule ------------------------------------------------------------

        [Fact]
        public async Task Les_detections_du_menu_s_executent_en_lecture_seule_sans_erreur_de_script()
        {
            var items = Catalogue.LoadEmbedded().Where(i => i.Menu == "Optimisation").ToList();
            var etats = await Detection.RunAsync(items, TimeSpan.FromSeconds(180));
            Assert.Equal(items.Count, etats.Count);
            // Registre HKCU et listes : toujours lisibles sans droits particuliers.
            foreach (var id in new[] { "opt.mouse.precision-off", "opt.stickykeys-off", "opt.gamemode-on", "opt.gamedvr-off", "opt.fso-off", "opt.visualfx", "opt.sysmain", "opt.wsearch" })
                Assert.NotEqual(DetectState.Unknown, etats[id]);
        }


        [Fact]
        public async Task Les_detections_des_listes_renvoient_Unknown()
        {
            var items = Catalogue.LoadEmbedded().Where(i => new[] { "opt.services", "opt.xbox-services", "opt.startup" }.Contains(i.Id)).ToList();
            var etats = await Detection.RunAsync(items, TimeSpan.FromSeconds(120));
            Assert.All(etats.Values, e => Assert.Equal(DetectState.Unknown, e));
        }

        [Fact]
        public void Les_reglages_d_alimentation_sont_lus_avec_qh_pour_inclure_les_reglages_caches()
        {
            Assert.Contains("/qh", Fonction(PsCommon.CommonText(), "Get-GTPowerValue"));
        }

        [Fact]
        public void Un_reglage_cache_est_lu_dans_la_sortie_de_qh()
        {
            var qh = "GUID du mode de gestion de l’alimentation : 381b4222-f694-41f0-9685-ff5bb260df2e  (Utilisation normale)\n"
                   + "  GUID du sous-groupe : 54533251-82be-4824-96c1-47b60b740d00  (Gestion de l’alimentation du processeur)\n"
                   + "    GUID du paramètre d’alimentation : 0cc5b647-c1df-4637-891a-dec35c318583\n"
                   + "      Alias de GUID : CPMINCORES\n"
                   + "      Valeur minimale possible : 0x00000000\n"
                   + "      Valeur maximale possible : 0x00000064\n"
                   + "    Index actuel du paramètre de courant alternatif : 0x00000004\n"
                   + "    Index actuel du paramètre de courant continu : 0x00000002\n";
            var r = Un("$l = @'\n" + qh + "'@ -split \"`n\"\n$v = ConvertFrom-GTPowerQuery $l\nWrite-Output ($v.AC.ToString() + '|' + $v.DC.ToString())");
            Assert.Equal("4|2", r);
            Assert.Equal("True", Un("Write-Output ($null -ne (Get-GTPowerValueOrNull (Get-GTActiveScheme) '54533251-82be-4824-96c1-47b60b740d00' '0cc5b647-c1df-4637-891a-dec35c318583')).ToString()"));
        }

        [Theory]
        [InlineData("$null", "$null", "True")]
        [InlineData("$null", "0", "False")]
        [InlineData("$null", "1", "True")]
        [InlineData("0", "1", "False")]
        [InlineData("1", "0", "True")]
        public void La_veille_prolongee_se_replie_sur_la_valeur_par_defaut_puis_sur_active(string valeur, string defaut, string attendu)
        {
            Assert.Equal(attendu, Un("Write-Output (Resolve-GTHibernateEnabled " + valeur + " " + defaut + ").ToString()"));
        }

        [Fact]
        public void La_detection_de_la_veille_prolongee_ne_leve_pas_d_erreur_si_la_valeur_est_absente()
        {
            var d = Catalogue.LoadEmbedded().Single(i => i.Id == "opt.hibernate-off").Detect;
            Assert.Contains("Get-GTHibernateEnabled", d);
            Assert.DoesNotContain("-ErrorAction Stop", d);
        }

        [Fact]
        public void Un_peripherique_qui_disparait_en_cours_d_execution_n_echoue_pas_avec_le_marqueur_GTSKIP()
        {
            var r = Un("$script:n = 0\n"
                     + "function Get-GTDevicePowerTargets { return @('A_0') }\n"
                     + "function Get-GTPowerDevices { $script:n++; if ($script:n -eq 1) { return @([pscustomobject]@{ InstanceName = 'A_0'; Enable = $true }) }; return @() }\n"
                     + "$ok = Set-TweakDevicePowerKind -Kind Usb -Enabled $false\nWrite-Output $ok.ToString()");
            Assert.Equal("True", r);
        }

        [Theory]
        [InlineData("SecurityHealth", true)] [InlineData("SecurityHealthSystray", true)] [InlineData("Windows Defender", true)]
        [InlineData("WindowsDefender", true)] [InlineData("MSASCuiL", true)] [InlineData("Sophos UI", true)]
        [InlineData("OneDrive", false)] [InlineData("Discord", false)] [InlineData("RtkAudUService", false)]
        public void Les_entrees_de_securite_ne_sont_jamais_proposees_et_les_deux_implementations_concordent(string nom, bool securite)
        {
            Assert.Equal(securite, Lists.StartupSecurity.EstSecurite(nom));
            Assert.Equal(securite.ToString(), Un("Write-Output (Test-GTStartupSecurity '" + nom + "').ToString()"));
        }

        [Fact]
        public async Task Les_entrees_de_securite_sont_refusees_par_l_apply_et_exclues_de_l_etoile()
        {
            Assert.DoesNotContain(PsCommon.Eval("Get-GTStartupEntries | ForEach-Object { Write-Output $_.Name }"), n => Lists.StartupSecurity.EstSecurite(n));
            var item = Catalogue.LoadEmbedded().Single(i => i.Id == "opt.startup");
            var sel = new Selection { OptionIds = new HashSet<string> { "opt.startup" } };
            sel.Listes["opt.startup"] = new List<string> { "HKLM:Run:SecurityHealth" };
            var r = await Runner.RunAsync(ScriptBuilder.Build(new[] { item }, sel, Journal), null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Skipped, r.Items.Single().Status);
            Assert.False(File.Exists(Journal));
        }

        // ---- journalisation des nouvelles fonctions ---------------------------------------------------

        private static string Fonction(string texte, string nom)
        {
            var m = Regex.Match(texte, @"(?ms)^function " + Regex.Escape(nom) + @"\b.*?^\}\r?$");
            Assert.True(m.Success, "fonction introuvable : " + nom);
            return m.Value;
        }

        [Theory]
        [InlineData("Set-TweakActiveScheme", "Set-GTActiveScheme")]
        [InlineData("Initialize-GTScheme", "powercfg")]
        [InlineData("Set-TweakHibernate", "Set-GTHibernate")]
        [InlineData("Set-TweakDevicePower", "Set-GTDevicePower")]
        [InlineData("Remove-TweakBcd", "bcdedit")]
        [InlineData("Set-TweakStartupDisabled", "Set-TweakRegistry")]
        [InlineData("Remove-TweakRegistryKey", "Remove-GTRegistryKey")]
        [InlineData("Add-TweakRegistryKey", "SetValue")]
        public void Les_nouvelles_fonctions_journalisent_avant_de_modifier(string fonction, string modification)
        {
            var f = Fonction(PsCommon.CommonText(), fonction);
            int j = f.IndexOf("Write-GTJournal", StringComparison.Ordinal);
            int m = f.IndexOf(modification, StringComparison.Ordinal);
            if (fonction == "Set-TweakStartupDisabled") { Assert.True(m >= 0); return; }   // Set-TweakRegistry journalise lui-même
            Assert.True(j >= 0, fonction + " : pas d'écriture au journal");
            Assert.True(m > j, fonction + " : la modification précède le journal");
        }

        [Theory]
        [InlineData("powerplan")] [InlineData("powerscheme")] [InlineData("hibernate")] [InlineData("devicepower")] [InlineData("regkey")] [InlineData("regkeynew")]
        public void L_annulation_connait_les_nouveaux_types_d_entree(string kind)
        {
            var restore = Fonction(PsCommon.CommonText(), "Restore-GTEntry");
            Assert.Contains("'" + kind + "'", restore);
        }

        [Fact]
        public void L_annulation_du_pointeur_actualise_la_souris_apres_restauration_du_registre()
        {
            Assert.Contains("Update-GTMouseParams", Fonction(PsCommon.CommonText(), "Restore-GTEntry"));
        }
    }
}
