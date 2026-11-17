using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class ProfilsIntegresTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-pi-" + Guid.NewGuid().ToString("N"));
        private static readonly MachineFacts Pc10 = new MachineFacts { WindowsMajor = "10", Edition = "Pro" };

        public ProfilsIntegresTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static CatalogItem It(string id, string menu, RiskLevel r, string liste = null, bool recommande = true, string win = null)
        {
            var it = new CatalogItem { Id = id, Menu = menu, Groupe = "G", Titre = id, Risque = r, Reversible = true, Liste = liste, Recommande = recommande };
            if (win != null) it.Compatibilite.Windows.Add(win);
            return it;
        }

        private static readonly List<CatalogItem> Cat = new List<CatalogItem>
        {
            It("opt.sur", "Optimisation", RiskLevel.Sur),
            It("opt.avance", "Optimisation", RiskLevel.Avance),
            It("opt.risque", "Optimisation", RiskLevel.Risque),
            It("opt.liste", "Optimisation", RiskLevel.Sur, liste: "services"),
            It("opt.nonreco", "Optimisation", RiskLevel.Sur, recommande: false),
            It("opt.w11", "Optimisation", RiskLevel.Sur, win: "11"),
            It("tel.sur", "Télémétrie", RiskLevel.Sur),
            It("tel.avance", "Télémétrie", RiskLevel.Avance),
            It("tel.risque", "Télémétrie", RiskLevel.Risque),
            It("cln.sur", "Nettoyage", RiskLevel.Sur),
            It("cln.avance", "Nettoyage", RiskLevel.Avance),
            It("blt.sur", "Bloatware", RiskLevel.Sur),
            It("upd.sur", "Mises à jour", RiskLevel.Sur),
        };

        private static List<string> Ids(string nom) =>
            ProfilsIntegres.Construire(ProfilsIntegres.Trouver(nom + ProfilsIntegres.Suffixe), Cat, Pc10).Options;

        [Fact] public void Trois_profils_integres() =>
            Assert.Equal(new[] { "Gaming léger", "Gaming max", "Confidentialité" }, ProfilsIntegres.Tous.Select(p => p.Nom).ToArray());

        [Fact] public void Gaming_leger_les_options_sur_d_optimisation_telemetrie_nettoyage() =>
            Assert.Equal(new[] { "opt.sur", "tel.sur", "cln.sur" }, Ids("Gaming léger"));

        [Fact] public void Gaming_max_ajoute_l_avance_d_optimisation_seulement() =>
            Assert.Equal(new[] { "opt.sur", "opt.avance", "tel.sur", "cln.sur" }, Ids("Gaming max"));

        [Fact] public void Confidentialite_toute_la_telemetrie_sur_et_avance() =>
            Assert.Equal(new[] { "tel.sur", "tel.avance" }, Ids("Confidentialité"));

        [Fact]
        public void Jamais_de_risque_de_liste_ni_d_option_exclue_de_la_recommandation()
        {
            foreach (var p in ProfilsIntegres.Tous)
            {
                var ids = ProfilsIntegres.Construire(p, Cat, Pc10).Options;
                Assert.DoesNotContain(ids, id => id.EndsWith(".risque") || id == "opt.liste" || id == "opt.nonreco" || id == "opt.w11");
            }
        }

        [Fact]
        public void Catalogue_reel_aucune_option_risquee_dans_un_profil_integre()
        {
            var cat = Catalogue.LoadEmbedded();
            var parId = cat.ToDictionary(c => c.Id);
            foreach (var p in ProfilsIntegres.Tous)
            {
                var ids = ProfilsIntegres.Construire(p, cat, new MachineFacts { WindowsMajor = "11", Edition = "Pro", GpuVendors = new List<string> { "NVIDIA" } }).Options;
                Assert.NotEmpty(ids);
                foreach (var id in ids)
                {
                    Assert.NotEqual(RiskLevel.Risque, parId[id].Risque);
                    Assert.True(string.IsNullOrEmpty(parId[id].Liste), id);
                }
            }
        }

        [Fact]
        public void Noms_reserves_insensibles_a_la_casse()
        {
            Assert.True(ProfilsIntegres.EstReserve("gaming LÉGER (intégré)"));
            Assert.False(ProfilsIntegres.EstReserve("Gaming léger"));
            Assert.Null(ProfilsIntegres.Trouver(null));
        }

        // ---------- Interface ----------

        private MainViewModel Vm()
        {
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = Path.Combine(tmp, "M"), SessionsDir = Path.Combine(tmp, "M", "Sessions"), QuarantineDir = Path.Combine(tmp, "Q") };
            return new MainViewModel(Cat, Pc10, paths, its => Task.FromResult(its.ToDictionary(i => i.Id, i => DetectState.NotApplied)), null, o => true);
        }

        [Fact]
        public void La_liste_commence_par_les_profils_integres()
        {
            var vm = Vm();
            Assert.Equal(ProfilsIntegres.Tous.Select(p => p.NomAffiche), vm.ProfilsDisponibles.Take(3));
        }

        [Fact]
        public void Choisir_un_profil_integre_coche_ses_options()
        {
            var vm = Vm();
            vm.ProfilSelectionne = "Gaming max" + ProfilsIntegres.Suffixe;
            Assert.Equal(new[] { "cln.sur", "opt.avance", "opt.sur", "tel.sur" }, vm.Options.Where(o => o.IsChecked).Select(o => o.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.True(vm.ProfilIntegreSelectionne);
            Assert.Equal("Gaming max", vm.NomPourEnregistrer);
            Assert.False(File.Exists(Path.Combine(tmp, "Profils", "Gaming max (intégré).json")));
        }

        [Fact]
        public void Enregistrer_sous_le_nom_d_un_profil_integre_est_refuse()
        {
            var vm = Vm();
            vm.ProfilSelectionne = "Confidentialité" + ProfilsIntegres.Suffixe;
            Assert.Equal(ProfilsIntegres.MessageNomReserve, vm.SauverProfil("Confidentialité (intégré)"));
            Assert.Null(vm.SauverProfil("Confidentialité"));
            Assert.Contains("Confidentialité", vm.ProfilsDisponibles);
            Assert.False(vm.ProfilIntegreSelectionne);
        }

        [Fact]
        public void Un_fichier_au_nom_d_un_profil_integre_n_est_pas_liste()
        {
            Directory.CreateDirectory(Path.Combine(tmp, "Profils"));
            File.WriteAllText(Path.Combine(tmp, "Profils", "Gaming léger (intégré).json"), "{\"version\":1,\"nom\":\"x\",\"options\":[\"opt.risque\"]}");
            var vm = Vm();
            Assert.Single(vm.ProfilsDisponibles, n => n == "Gaming léger (intégré)");
            vm.ProfilSelectionne = "Gaming léger (intégré)";
            Assert.DoesNotContain(vm.Options, o => o.IsChecked && o.Id == "opt.risque");
        }
    }
}
