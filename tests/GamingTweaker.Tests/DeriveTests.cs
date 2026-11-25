using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Newtonsoft.Json;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Alerte de l'accueil : réglages appliqués par GamingTweaker puis rétablis (mise à jour de Windows, pilote, à la main).</summary>
    public class DeriveTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-derive-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static CatalogItem It(string id, RiskLevel r = RiskLevel.Sur)
        {
            return new CatalogItem { Id = id, Menu = "Optimisation", Groupe = "G", Titre = "Titre " + id, Description = "D", Risque = r, Reversible = true };
        }

        private MainViewModel Cree(string[] appliquees, Dictionary<string, DetectState> etats, Func<OptionViewModel, bool> confirmer = null)
        {
            var machine = Path.Combine(tmp, "M");
            Directory.CreateDirectory(machine);
            if (appliquees != null) File.WriteAllText(Path.Combine(machine, MainViewModel.FichierDernieresApplications), JsonConvert.SerializeObject(appliquees));
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = machine, SessionsDir = Path.Combine(machine, "Sessions"), QuarantineDir = Path.Combine(tmp, "Q") };
            var items = etats.Keys.Select(k => It(k, k.EndsWith("risque") ? RiskLevel.Risque : RiskLevel.Sur)).ToList();
            return new MainViewModel(items, new MachineFacts { WindowsMajor = "10", Edition = "Pro" }, paths,
                its => Task.FromResult(new Dictionary<string, DetectState>(etats)), null, confirmer ?? (o => true));
        }

        [Fact]
        public async Task Option_appliquee_puis_retablie_est_signalee()
        {
            var vm = Cree(new[] { "opt.a", "opt.b" }, new Dictionary<string, DetectState>
            {
                { "opt.a", DetectState.NotApplied }, { "opt.b", DetectState.Applied }, { "opt.c", DetectState.NotApplied },
            });
            await vm.DetectAsync();
            Assert.True(vm.ADerive);
            Assert.Equal(new[] { "opt.a" }, vm.Derives.Select(o => o.Id));
            Assert.Contains("1 réglage appliqué par GamingTweaker n'est plus en place", vm.TexteDerive);
            Assert.Contains("« Titre opt.a »", vm.TexteDerive);
        }

        [Fact]
        public async Task Etat_inconnu_ou_jamais_applique_n_est_pas_signale()
        {
            var vm = Cree(new[] { "opt.a" }, new Dictionary<string, DetectState> { { "opt.a", DetectState.Unknown }, { "opt.c", DetectState.NotApplied } });
            await vm.DetectAsync();
            Assert.False(vm.ADerive);
            Assert.Equal("", vm.TexteDerive);
        }

        [Fact]
        public async Task Sans_fichier_ou_fichier_corrompu_rien_n_est_signale()
        {
            var vm = Cree(null, new Dictionary<string, DetectState> { { "opt.a", DetectState.NotApplied } });
            await vm.DetectAsync();
            Assert.False(vm.ADerive);

            File.WriteAllText(Path.Combine(tmp, "M", MainViewModel.FichierDernieresApplications), "{pas du json");
            await vm.DetectAsync();
            Assert.False(vm.ADerive);
        }

        [Fact]
        public async Task Recocher_coche_les_options_retablies_sans_rien_lancer()
        {
            var vm = Cree(new[] { "opt.a", "opt.b" }, new Dictionary<string, DetectState> { { "opt.a", DetectState.NotApplied }, { "opt.b", DetectState.NotApplied } });
            await vm.DetectAsync();
            vm.ReappliquerDerive();
            Assert.True(vm.Options.Single(o => o.Id == "opt.a").IsChecked);
            Assert.True(vm.Options.Single(o => o.Id == "opt.b").IsChecked);
            Assert.Contains("2 réglages recochés", vm.Message);
            Assert.Contains("« LANCER »", vm.Message);
        }

        [Fact]
        public async Task Option_risquee_refusee_n_est_pas_recochee()
        {
            var vm = Cree(new[] { "opt.risque" }, new Dictionary<string, DetectState> { { "opt.risque", DetectState.NotApplied } }, o => false);
            await vm.DetectAsync();
            Assert.True(vm.ADerive);
            vm.ReappliquerDerive();
            Assert.False(vm.Options.Single().IsChecked);
            Assert.Equal("Aucun réglage n'a été recoché.", vm.Message);
        }

        [Fact]
        public async Task Plus_de_trois_options_sont_resumees()
        {
            var ids = new[] { "opt.a", "opt.b", "opt.c", "opt.d", "opt.e" };
            var vm = Cree(ids, ids.ToDictionary(i => i, i => DetectState.NotApplied));
            await vm.DetectAsync();
            Assert.StartsWith("5 réglages appliqués par GamingTweaker ne sont plus en place", vm.TexteDerive);
            Assert.EndsWith("et 2 autres.", vm.TexteDerive);
        }
    }
}
