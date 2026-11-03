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
    public class ToutRestaurerTests
    {
        private class Faux : IRestauration
        {
            public Dictionary<string, string> Journaux = new Dictionary<string, string>();
            public HashSet<string> JournalEnEchec = new HashSet<string>();
            public List<string> Appels = new List<string>();

            public string TrouverJournalActif(string id) { string j; return Journaux.TryGetValue(id, out j) ? j : null; }

            public Task<ItemOutcome> AnnulerParJournalAsync(string journal, string id)
            {
                Appels.Add("journal:" + id + "@" + journal);
                return Task.FromResult(new ItemOutcome { Id = id, Status = JournalEnEchec.Contains(id) ? OutcomeStatus.Failed : OutcomeStatus.Ok, Message = JournalEnEchec.Contains(id) ? "valeur verrouillée" : null });
            }

            public Task<ItemOutcome> ExecuterRevertAsync(CatalogItem item)
            {
                Appels.Add("revert:" + item.Id);
                return Task.FromResult(new ItemOutcome { Id = item.Id, Status = OutcomeStatus.Ok });
            }
        }

        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-restaurer-" + Guid.NewGuid().ToString("N"));
        private int detections;
        private Dictionary<string, DetectState> etats = new Dictionary<string, DetectState>();

        private static CatalogItem It(string id, string menu, bool reversible = true, string win = null)
        {
            var it = new CatalogItem { Id = id, Menu = menu, Groupe = "G", Titre = "Titre " + id, Description = "d", Risque = RiskLevel.Sur, Reversible = reversible, Revert = reversible ? "x" : null };
            if (win != null) it.Compatibilite.Windows.Add(win);
            return it;
        }

        private MainViewModel Cree(Faux faux)
        {
            var items = new[]
            {
                It("tel.a", "Télémétrie"), It("tel.b", "Télémétrie"), It("tel.c", "Télémétrie"),
                It("tel.definitif", "Télémétrie", reversible: false), It("tel.win11", "Télémétrie", win: "11"), It("opt.x", "Optimisation"),
            };
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "P"), MachineDir = Path.Combine(tmp, "M"), SessionsDir = Path.Combine(tmp, "M", "S"), QuarantineDir = Path.Combine(tmp, "Q") };
            var vm = new MainViewModel(items, new MachineFacts { WindowsMajor = "10", Edition = "Pro" }, paths,
                its => { detections++; var l = its.ToList(); return Task.FromResult(l.ToDictionary(i => i.Id, i => { DetectState s; return etats.TryGetValue(i.Id, out s) ? s : DetectState.NotApplied; })); },
                null, o => true);
            vm.Restauration = faux;
            return vm;
        }

        [Fact]
        public async Task Annule_par_le_journal_sinon_par_revert_et_ne_touche_que_les_options_appliquees_du_menu()
        {
            etats = new Dictionary<string, DetectState>
            {
                { "tel.a", DetectState.Applied }, { "tel.b", DetectState.Applied }, { "tel.c", DetectState.NotApplied },
                { "tel.definitif", DetectState.Applied }, { "opt.x", DetectState.Applied },
            };
            var faux = new Faux();
            faux.Journaux["tel.a"] = @"C:\S\20260101-100000\journal.jsonl";
            var vm = Cree(faux);

            var r = await vm.ToutRestaurerAsync();

            Assert.Equal(new[] { @"journal:tel.a@C:\S\20260101-100000\journal.jsonl", "revert:tel.b" }, faux.Appels);
            Assert.Equal(1, r.AnnuleesParJournal);
            Assert.Equal(1, r.AnnuleesParRevert);
            Assert.Empty(r.Echecs);
            Assert.Contains("tel.definitif", r.Ignorees);   // appliquée mais sans journal ni revert
            Assert.DoesNotContain("tel.win11", r.Ignorees); // incompatible : n'existe pas pour ce PC
            Assert.Equal(2, detections);                    // détection avant, puis nouvelle détection après
        }

        [Fact]
        public async Task Un_echec_du_journal_n_est_pas_remplace_par_un_revert_et_il_est_signale()
        {
            etats["tel.a"] = DetectState.Applied;
            var faux = new Faux();
            faux.Journaux["tel.a"] = "j";
            faux.JournalEnEchec.Add("tel.a");
            var vm = Cree(faux);

            var r = await vm.ToutRestaurerAsync();

            Assert.Equal(new[] { "journal:tel.a@j" }, faux.Appels);
            Assert.Single(r.Echecs);
            Assert.Contains("tel.a", r.Echecs[0]);
            Assert.Contains("valeur verrouillée", r.Echecs[0]);
            Assert.Contains("échec", vm.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Rien_a_restaurer_ne_lance_aucune_annulation()
        {
            var faux = new Faux();
            var vm = Cree(faux);
            var r = await vm.ToutRestaurerAsync();
            Assert.Empty(faux.Appels);
            Assert.Equal(0, r.AnnuleesParJournal + r.AnnuleesParRevert);
            Assert.Contains("Rien à restaurer", vm.Message);
        }

        [Fact]
        public async Task Sans_service_de_restauration_un_message_clair_est_affiche()
        {
            etats["tel.a"] = DetectState.Applied;
            var vm = Cree(new Faux());
            vm.Restauration = null;
            await vm.ToutRestaurerAsync();
            Assert.Contains("indisponible", vm.Message);
        }

        [Fact]
        public async Task Un_detect_qui_echoue_ne_restaure_rien()
        {
            var faux = new Faux();
            var items = new[] { It("tel.a", "Télémétrie") };
            var vm = new MainViewModel(items, new MachineFacts { WindowsMajor = "10", Edition = "Pro" },
                new DataPaths { ProfilsDir = tmp, MachineDir = tmp, SessionsDir = tmp, QuarantineDir = tmp },
                its => { throw new InvalidOperationException("boom"); }, null, o => true) { Restauration = faux };
            await vm.ToutRestaurerAsync();
            Assert.Empty(faux.Appels);
        }

        [Fact]
        public void Le_menu_telemetrie_expose_les_boutons_du_bandeau()
        {
            var vm = Cree(new Faux());
            Assert.False(vm.EstMenuTelemetrie);
            vm.MenuCourant = "Télémétrie";
            Assert.True(vm.EstMenuTelemetrie);
            vm.Recherche = "x";
            Assert.False(vm.EstMenuTelemetrie);
            Assert.Equal("https://www.oo-software.com/fr/shutup10", MainViewModel.UrlShutUp10);
        }
    }
}
