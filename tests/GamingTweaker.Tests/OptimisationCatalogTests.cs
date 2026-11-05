using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GamingTweaker.Core;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    public class OptimisationCatalogTests
    {
        private const string Menu = "Optimisation";

        /// <summary>Tableau minimal du plan (Task 9) : id, risque.</summary>
        private static readonly Dictionary<string, RiskLevel> Minimum = new Dictionary<string, RiskLevel>
        {
            { "opt.mouse.precision-off", RiskLevel.Sur }, { "opt.stickykeys-off", RiskLevel.Sur }, { "opt.usb-power-off", RiskLevel.Sur },
            { "opt.nic-power-off", RiskLevel.Sur }, { "opt.usb-suspend-off", RiskLevel.Sur }, { "opt.pcie-aspm-off", RiskLevel.Avance },
            { "opt.plan-high", RiskLevel.Sur }, { "opt.plan-ultimate", RiskLevel.Avance }, { "opt.coreparking-off", RiskLevel.Avance },
            { "opt.gamemode-on", RiskLevel.Sur }, { "opt.gamedvr-off", RiskLevel.Sur }, { "opt.hags", RiskLevel.Avance },
            { "opt.fso-off", RiskLevel.Avance }, { "opt.mpo-off", RiskLevel.Avance }, { "opt.gpu-msi", RiskLevel.Avance },
            { "opt.vbs-off", RiskLevel.Risque }, { "opt.bgapps-off", RiskLevel.Sur }, { "opt.widgets-off", RiskLevel.Sur },
            { "opt.visualfx", RiskLevel.Sur }, { "opt.sysmain", RiskLevel.Avance }, { "opt.wsearch", RiskLevel.Avance },
            { "opt.hibernate-off", RiskLevel.Avance }, { "opt.startup", RiskLevel.Avance }, { "opt.services", RiskLevel.Avance },
            { "opt.xbox-services", RiskLevel.Risque }, { "opt.nagle-off", RiskLevel.Avance }, { "opt.netthrottle-off", RiskLevel.Avance },
            { "opt.prioritysep", RiskLevel.Avance }, { "opt.hpet", RiskLevel.Risque }, { "opt.spectre-off", RiskLevel.Risque },
            { "opt.defender-rt-off", RiskLevel.Risque },
        };

        private static readonly string[] Redemarrage = { "opt.hags", "opt.mpo-off", "opt.gpu-msi", "opt.vbs-off", "opt.hpet", "opt.spectre-off" };
        private static readonly string[] NonTestables = { "opt.usb-power-off", "opt.nic-power-off", "opt.gpu-msi", "opt.hags", "opt.pcie-aspm-off", "opt.vbs-off", "opt.hpet" };
        private static readonly string[] Hkcu = { "opt.mouse.precision-off", "opt.stickykeys-off", "opt.gamemode-on", "opt.gamedvr-off", "opt.fso-off", "opt.visualfx", "opt.startup" };

        private static Dictionary<string, CatalogItem> Catalogue() =>
            GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == Menu).ToDictionary(i => i.Id);

        public static IEnumerable<object[]> Lignes() => Minimum.Select(kv => new object[] { kv.Key, kv.Value });

        [Fact]
        public void Le_tableau_minimal_compte_31_options()
        {
            Assert.Equal(31, Minimum.Count);
        }

        [Theory]
        [MemberData(nameof(Lignes))]
        public void Chaque_option_du_tableau_existe_avec_le_bon_risque(string id, RiskLevel risque)
        {
            var cat = Catalogue();
            Assert.True(cat.ContainsKey(id), "option absente : " + id);
            var it = cat[id];
            Assert.Equal(risque, it.Risque);
            Assert.True(it.Reversible, id + " doit être réversible");
            Assert.Equal(Redemarrage.Contains(id), it.Redemarrage);
            Assert.Equal(!NonTestables.Contains(id), it.Testable);
            Assert.False(string.IsNullOrWhiteSpace(it.Groupe));
            Assert.False(string.IsNullOrWhiteSpace(it.Description), id + " : description manquante");
            Assert.False(string.IsNullOrWhiteSpace(it.Detect), id + " : detect");
            Assert.False(string.IsNullOrWhiteSpace(it.Apply), id + " : apply");
            Assert.False(string.IsNullOrWhiteSpace(it.Revert), id + " : revert");
            Assert.StartsWith("opt.", id);
        }

        [Fact]
        public void Le_catalogue_optimisation_ne_contient_que_le_tableau_et_le_recapitulatif()
        {
            var cat = Catalogue();
            var recap = Couverture.Recapitulatif(Menu);
            Assert.NotEmpty(recap);
            // Le tableau minimal et le récapitulatif ne se recouvrent pas.
            Assert.Empty(recap.Select(r => r.Id).Intersect(Minimum.Keys));
            // La partie A couvre le tableau minimal ; les options du récapitulatif (partie B) peuvent venir plus tard.
            var permis = new HashSet<string>(Minimum.Keys.Concat(recap.Select(r => r.Id)));
            Assert.Empty(cat.Keys.Where(k => !permis.Contains(k)));
            foreach (var r in recap.Where(r => cat.ContainsKey(r.Id)))
            {
                Assert.True(cat[r.Id].Risque == r.Risque, r.Id + " : risque " + cat[r.Id].Risque + " au lieu de " + r.Risque);
                Assert.True(cat[r.Id].Reversible == r.EstReversible, r.Id + " : réversible différent du récapitulatif");
                Assert.Equal(r.Groupe, cat[r.Id].Groupe);
            }
        }

        [Fact]
        public void La_couverture_cite_les_options_du_tableau_minimal_qui_existent()
        {
            var cites = Couverture.IdsCites().Where(id => Minimum.ContainsKey(id)).ToList();
            Assert.NotEmpty(cites);
            var cat = Catalogue();
            foreach (var id in cites) Assert.True(cat.ContainsKey(id), id + " cité par la couverture mais absent du catalogue");
        }

        [Fact]
        public void Les_scripts_opt_sont_syntaxiquement_valides()
        {
            Assert.Empty(PsSyntax.CheckAll(Catalogue().Values));
        }

        [Fact]
        public void Les_options_visant_HKCU_sont_marquees_hkcu_et_les_autres_non()
        {
            var cat = Catalogue();
            foreach (var id in Minimum.Keys)
                Assert.Equal(Hkcu.Contains(id), cat[id].Hkcu);
            foreach (var it in cat.Values)
                if ((it.Apply ?? "").Contains("HKCU:"))
                    Assert.True(it.Hkcu, it.Id + " : apply écrit dans HKCU mais hkcu=false");
        }

        [Fact]
        public void Les_scripts_apply_ne_modifient_le_systeme_que_par_les_fonctions_journalisees()
        {
            var interdits = new[] { "Set-ItemProperty", "New-ItemProperty", "Remove-ItemProperty", "Set-Service", "sc.exe", "reg add", "reg.exe", "reg delete",
                                    "Disable-ScheduledTask", "Enable-ScheduledTask", "Out-File", "Set-Content", "Add-Content", "WriteAllLines", "Remove-AppxPackage",
                                    "powercfg", "bcdedit", "Set-MpPreference", "Set-CimInstance", "Set-WmiInstance", "Invoke-CimMethod", "Remove-Item", "New-Item",
                                    "Start-Service", "Restart-Service", "Stop-Process", "Invoke-Expression", "iex ", "Start-Process", "netsh", "Disable-NetAdapter" };
            foreach (var it in Catalogue().Values)
            {
                foreach (var m in interdits)
                    Assert.False((it.Apply ?? "").IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0, it.Id + " : apply contient « " + m + " »");
                // Un arrêt de service n'est pas journalisé : seuls SysMain et WSearch (« manuel, arrêté ») peuvent le faire.
                if (it.Id != "opt.sysmain" && it.Id != "opt.wsearch") Assert.DoesNotContain("Stop-Service", it.Apply ?? "");
            }
        }

        [Fact]
        public void Aucun_service_ne_passe_en_desactive_dans_le_menu_optimisation()
        {
            foreach (var it in Catalogue().Values)
            {
                var apply = it.Apply ?? "";
                // Aucun appel de service ne vise « Disabled », ni directement ni par une variable.
                Assert.False(Regex.IsMatch(apply, @"(?is)Set-TweakService\w*[^\r\n]*Disabled"), it.Id + " : un service passe en Disabled");
                if (new[] { "opt.sysmain", "opt.wsearch", "opt.services", "opt.xbox-services" }.Contains(it.Id))
                {
                    Assert.DoesNotContain("Disabled", apply);
                    // Seule fonction de service utilisée : « en manuel », sans jamais réactiver un service désactivé.
                    Assert.Contains("Set-TweakServiceManualIfNeeded", apply);
                    Assert.DoesNotContain("Set-TweakService ", apply);
                    Assert.DoesNotContain("Set-TweakServiceIfNeeded", apply);
                }
            }
        }

        [Theory]
        [InlineData("opt.services", "services")]
        [InlineData("opt.xbox-services", "xbox-services")]
        [InlineData("opt.startup", "startup")]
        public void Les_options_a_liste_declarent_leur_fournisseur_et_limitent_les_noms_acceptes(string id, string liste)
        {
            var it = Catalogue()[id];
            Assert.Equal(liste, it.Liste);
            Assert.Contains("$Selection", it.Apply);
            Assert.Contains("Resolve-GTListSelection", it.Apply);
            Assert.Contains("Unknown", it.Apply);   // les noms hors de la liste autorisée font abandonner l'option
            Assert.Contains("Skip-GTItem", it.Apply);
        }

        [Fact]
        public void Les_options_services_ont_un_texte_de_risque_et_le_Game_Pass_est_cite()
        {
            var cat = Catalogue();
            Assert.Contains("Game Pass", cat["opt.xbox-services"].Description);
            Assert.Contains("manuel", cat["opt.services"].Description);
            Assert.Contains("imprim", cat["opt.services"].Description);   // Spooler : plus d'impression tant qu'il n'est pas démarré
        }

        [Fact]
        public void Les_options_windows_recentes_sont_restreintes()
        {
            var cat = Catalogue();
            Assert.Equal(new[] { "11" }, cat["opt.widgets-off"].Compatibilite.Windows);
            Assert.Equal(new[] { "10", "11" }, cat["opt.hags"].Compatibilite.Windows);
            Assert.Contains("2004", cat["opt.hags"].Description);
            Assert.Contains("19041", cat["opt.hags"].Apply);
        }

        [Fact]
        public void Defender_echoue_proprement_si_la_protection_anti_falsification_est_active()
        {
            var it = Catalogue()["opt.defender-rt-off"];
            Assert.Contains("IsTamperProtected", it.Apply);
            Assert.Contains("throw", it.Apply);
            Assert.Contains("Sécurité Windows", it.Apply);
            Assert.Contains("Set-TweakDefenderRealtime", it.Apply);
            Assert.Contains("DisableRealtimeMonitoring $false", it.Revert);
            foreach (var interdit in new[] { "DisableTamper", "TamperProtection", "Add-MpPreference", "ExclusionPath", "Stop-Service", "WinDefend", "Disable-WindowsOptionalFeature" })
                Assert.DoesNotContain(interdit, it.Apply);
        }

        [Fact]
        public void La_souris_enrichie_garde_son_identifiant_et_actualise_le_pointeur()
        {
            var it = Catalogue()["opt.mouse.precision-off"];
            Assert.Contains("Set-TweakRegistryIfNeeded", it.Apply);
            Assert.Contains("Update-GTMouseParams", it.Apply);
            Assert.Contains("Update-GTMouseParams", it.Revert);
        }

        [Fact]
        public void Les_options_a_reglage_de_plan_precisent_qu_elles_visent_le_plan_actif()
        {
            var cat = Catalogue();
            foreach (var id in new[] { "opt.usb-suspend-off", "opt.pcie-aspm-off", "opt.coreparking-off" })
                Assert.Contains("plan d'alimentation actif", cat[id].Description);
        }

        [Fact]
        public void Les_identifiants_de_GUID_de_alimentation_sont_ceux_du_plan()
        {
            var cat = Catalogue();
            Assert.Contains("2a737441-1930-4402-8d77-b2bebba308a3", cat["opt.usb-suspend-off"].Apply);
            Assert.Contains("48e6b7a6-50f5-4782-a5d4-53bb8f07e226", cat["opt.usb-suspend-off"].Apply);
            Assert.Contains("501a4d13-42af-4429-9fd1-a8218c268e20", cat["opt.pcie-aspm-off"].Apply);
            Assert.Contains("ee12f906-d277-404b-b6da-e5fa1a576df5", cat["opt.pcie-aspm-off"].Apply);
            Assert.Contains("54533251-82be-4824-96c1-47b60b740d00", cat["opt.coreparking-off"].Apply);
            Assert.Contains("0cc5b647-c1df-4637-891a-dec35c318583", cat["opt.coreparking-off"].Apply);
            Assert.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", cat["opt.plan-high"].Apply);
            Assert.Contains("e9a42b02-d5df-448d-aa00-03f14749eb61", cat["opt.plan-ultimate"].Apply);
        }

        [Fact]
        public void NetworkThrottlingIndex_est_ecrit_en_non_signe()
        {
            // 0xFFFFFFFF vaut -1 en PowerShell 5.1 : la valeur doit être écrite en décimal non signé.
            Assert.Contains("4294967295", Catalogue()["opt.netthrottle-off"].Apply);
            Assert.DoesNotContain("0xFFFFFFFF", Catalogue()["opt.netthrottle-off"].Apply);
        }

        [Fact]
        public void Les_plans_d_alimentation_passent_avant_les_reglages_de_plan()
        {
            var cat = Catalogue();
            foreach (var plan in new[] { "opt.plan-high", "opt.plan-ultimate" })
                foreach (var reglage in new[] { "opt.usb-suspend-off", "opt.pcie-aspm-off", "opt.coreparking-off" })
                    Assert.True(cat[plan].Ordre < cat[reglage].Ordre, plan + " doit précéder " + reglage);
            Assert.Equal(100, cat["opt.mpo-off"].Ordre);
            var ids = GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == Menu).Select(i => i.Id).ToList();
            Assert.True(ids.IndexOf("opt.plan-ultimate") < ids.IndexOf("opt.usb-suspend-off"));
        }

        private static CatalogItem Factice(string id, int ordre) => new CatalogItem { Id = id, Menu = Menu, Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Ordre = ordre, Apply = "# " + id, Detect = "d" };

        [Fact]
        public void ScriptBuilder_trie_par_ordre_puis_par_position_dans_le_catalogue()
        {
            var items = new[] { Factice("opt.usb-suspend-off", 100), Factice("opt.autre", 100), Factice("opt.plan-high", 10) };
            var sel = new Selection { OptionIds = new HashSet<string>(items.Select(i => i.Id)) };
            var script = ScriptBuilder.Build(items, sel, "j.jsonl");
            int plan = script.IndexOf("Invoke-GTItem 'opt.plan-high'", StringComparison.Ordinal);
            int usb = script.IndexOf("Invoke-GTItem 'opt.usb-suspend-off'", StringComparison.Ordinal);
            int autre = script.IndexOf("Invoke-GTItem 'opt.autre'", StringComparison.Ordinal);
            Assert.True(plan > 0 && plan < usb && usb < autre);
            Assert.True(script.IndexOf("'plan' 'opt.plan-high'", StringComparison.Ordinal) < script.IndexOf("'plan' 'opt.usb-suspend-off'", StringComparison.Ordinal));
        }

        [Fact]
        public void Le_script_expose_les_options_choisies_a_toutes_les_options()
        {
            var items = new[] { Factice("opt.plan-high", 10) };
            var sel = new Selection { OptionIds = new HashSet<string> { "opt.plan-high", "opt.plan-ultimate" } };
            var script = ScriptBuilder.Build(items, sel, "j.jsonl");
            Assert.Contains("$GT_SelectedIds", script);
            Assert.Contains("'opt.plan-ultimate'", script);
        }

        [Fact]
        public async System.Threading.Tasks.Task Performances_elevees_est_ignore_quand_performances_ultimes_est_choisi()
        {
            var high = Catalogue()["opt.plan-high"];
            var sel = new Selection { OptionIds = new HashSet<string> { "opt.plan-high", "opt.plan-ultimate" } };
            var journal = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gt-excl-" + Guid.NewGuid().ToString("N"), "j.jsonl");
            try
            {
                // Seule l'option « high » est exécutée : le refus se produit avant tout accès au plan d'alimentation.
                var r = await Runner.RunAsync(ScriptBuilder.Build(new[] { high }, sel, journal), null, System.Threading.CancellationToken.None, _ => 120);
                var o = r.Items.Single();
                Assert.Equal(OutcomeStatus.Skipped, o.Status);
                Assert.Contains("Performances ultimes", o.Message);
                Assert.False(System.IO.File.Exists(journal));
            }
            finally { try { System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(journal), true); } catch { } }
        }

        [Theory]
        [InlineData("opt.services")] [InlineData("opt.xbox-services")] [InlineData("opt.startup")]
        public void La_detection_des_listes_est_inconnue_car_elle_ignore_la_selection(string id)
        {
            var d = Catalogue()[id].Detect;
            Assert.Contains("Unknown", d);
            Assert.DoesNotContain("Applied", d);
            Assert.StartsWith("#", d.TrimStart());
        }

        [Fact]
        public void Defender_detecte_par_l_etat_reel_et_verifie_apres_application()
        {
            var it = Catalogue()["opt.defender-rt-off"];
            Assert.Contains("RealTimeProtectionEnabled", it.Detect);
            Assert.Contains("RealTimeProtectionEnabled", it.Apply);
            Assert.Contains("stratégie", it.Apply);
            Assert.True(it.Apply.IndexOf("Set-TweakDefenderRealtime", StringComparison.Ordinal) < it.Apply.LastIndexOf("RealTimeProtectionEnabled", StringComparison.Ordinal));
        }
    }
}
