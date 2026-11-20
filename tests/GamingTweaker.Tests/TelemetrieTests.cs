using System.Collections.Generic;
using System.Linq;
using GamingTweaker.Core;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    public class TelemetrieTests
    {
        private const string Menu = "Télémétrie";

        /// <summary>Tableau minimal du plan (Task 8) : id, risque.</summary>
        private static readonly Dictionary<string, RiskLevel> Minimum = new Dictionary<string, RiskLevel>
        {
            { "tel.diagtrack-off", RiskLevel.Sur }, { "tel.level-min", RiskLevel.Sur }, { "tel.tasks-off", RiskLevel.Sur },
            { "tel.adid-off", RiskLevel.Sur }, { "tel.suggestions-off", RiskLevel.Sur }, { "tel.activity-off", RiskLevel.Sur },
            { "tel.wer-off", RiskLevel.Sur }, { "tel.bing-off", RiskLevel.Sur }, { "tel.copilot-off", RiskLevel.Sur },
            { "tel.recall-off", RiskLevel.Sur },
        };

        private static Dictionary<string, CatalogItem> Catalogue() =>
            GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == Menu).ToDictionary(i => i.Id);

        [Fact]
        public void Telemetrie_a_toutes_ses_options()
        {
            var cat = Catalogue();
            foreach (var kv in Minimum)
            {
                Assert.True(cat.ContainsKey(kv.Key), "option absente : " + kv.Key);
                Assert.Equal(kv.Value, cat[kv.Key].Risque);
                Assert.True(cat[kv.Key].Reversible, kv.Key + " doit être réversible");
            }
        }

        [Fact]
        public void Telemetrie_correspond_au_recapitulatif_de_la_couverture()
        {
            var cat = Catalogue();
            var recap = Couverture.Recapitulatif(Menu);
            Assert.NotEmpty(recap);

            var attendus = new HashSet<string>(Minimum.Keys.Concat(recap.Select(r => r.Id)));
            Assert.Equal(attendus.OrderBy(x => x), cat.Keys.OrderBy(x => x));

            foreach (var r in recap)
            {
                var it = cat[r.Id];
                Assert.True(it.Risque == r.Risque, r.Id + " : risque " + it.Risque + " au lieu de " + r.Risque);
                Assert.True(it.Reversible == r.EstReversible, r.Id + " : réversible " + it.Reversible + " au lieu de « " + r.Reversible + " »");
                Assert.Equal(r.Groupe, it.Groupe);
                Assert.False(string.IsNullOrWhiteSpace(it.Description), r.Id + " : description manquante");
            }
        }

        [Fact]
        public void Le_catalogue_et_le_recapitulatif_n_ont_pas_de_doublon()
        {
            var ids = Couverture.Recapitulatif(Menu).Select(r => r.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Empty(ids.Intersect(Minimum.Keys));
        }

        [Fact]
        public void Chaque_option_a_ses_trois_scripts_et_le_menu_telemetrie()
        {
            foreach (var it in Catalogue().Values)
            {
                Assert.StartsWith("tel.", it.Id);
                Assert.False(string.IsNullOrWhiteSpace(it.Detect), it.Id + " : detect");
                Assert.False(string.IsNullOrWhiteSpace(it.Apply), it.Id + " : apply");
                Assert.False(string.IsNullOrWhiteSpace(it.Revert), it.Id + " : revert");
            }
        }

        [Fact]
        public void Le_niveau_0_est_signale_comme_complet_seulement_sur_Enterprise_et_Education()
        {
            var d = Catalogue()["tel.level-min"].Description;
            Assert.Contains("Enterprise", d);
            Assert.Contains("Education", d);
        }

        [Theory]
        [InlineData("tel.copilot-off")] [InlineData("tel.recall-off")] [InlineData("tel.ai-off")] [InlineData("tel.clicktodo-off")] [InlineData("tel.start-reco-off")]
        public void Les_options_Windows_11_sont_restreintes_a_Windows_11(string id)
        {
            Assert.Equal(new[] { "11" }, Catalogue()[id].Compatibilite.Windows);
        }

        [Fact]
        public void Les_options_visant_HKCU_sont_marquees_hkcu_et_les_autres_non()
        {
            var cat = Catalogue();
            foreach (var id in new[] { "tel.adid-off", "tel.suggestions-off", "tel.bing-off", "tel.copilot-off" })
                Assert.True(cat[id].Hkcu, id + " écrit dans HKCU");
            foreach (var id in new[] { "tel.diagtrack-off", "tel.activity-off", "tel.recall-off" })
                Assert.False(cat[id].Hkcu, id + " n'écrit pas dans HKCU");
            // Un script qui écrit dans HKCU: doit être marqué hkcu (sinon il serait exécuté sous un autre compte).
            foreach (var it in cat.Values)
                if ((it.Apply ?? "").Contains("HKCU:"))
                    Assert.True(it.Hkcu, it.Id + " : apply écrit dans HKCU mais hkcu=false");
        }

        [Fact]
        public void Les_scripts_apply_ne_modifient_le_systeme_que_par_les_fonctions_journalisees()
        {
            // Aucun apply n'utilise directement les cmdlets qui modifient le registre, les services ou les tâches.
            var interdits = new[] { "Set-ItemProperty", "New-ItemProperty", "Remove-ItemProperty", "Set-Service", "sc.exe", "reg add", "reg.exe",
                                    "Disable-ScheduledTask", "Enable-ScheduledTask", "Out-File", "Set-Content", "Add-Content", "WriteAllLines", "Remove-AppxPackage" };
            foreach (var it in Catalogue().Values)
            {
                foreach (var m in interdits)
                    Assert.DoesNotContain(m, it.Apply ?? "");
                // Arrêter/démarrer un service n'est pas journalisé : seul diagtrack-off peut arrêter le service (l'annulation rétablit le type de démarrage).
                Assert.DoesNotContain("Start-Service", it.Apply ?? "");
                if (it.Id != "tel.diagtrack-off") Assert.DoesNotContain("Stop-Service", it.Apply ?? "");
            }
        }

        [Fact]
        public void Les_descriptions_signalent_les_limites_de_diagtrack_et_le_fichier_hosts_n_est_plus_touche()
        {
            var cat = Catalogue();
            Assert.Contains("prochain démarrage", cat["tel.diagtrack-off"].Description);
            // tel.hosts retiré (Defender le détecte comme HostsFileHijack et défait la modification) : aucun script n'écrit dans hosts.
            Assert.DoesNotContain("tel.hosts", cat.Keys);
            foreach (var it in cat.Values) Assert.DoesNotContain("Add-TweakHosts", it.Apply ?? "");
            Assert.Equal(RiskLevel.Avance, cat["tel.location-off"].Risque);
            Assert.Equal(RiskLevel.Avance, cat["tel.app-access-off"].Risque);
        }
    }
}
