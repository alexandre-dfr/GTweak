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
            public Dictionary<string, List<string>> Journaux = new Dictionary<string, List<string>>();
            public HashSet<string> JournalEnEchec = new HashSet<string>();
            public List<string> Appels = new List<string>();
            public List<string> Oublies = new List<string>();
            public bool Occupe;
            public bool MemeUser = true;
            public bool VerrouLibere;
            public HashSet<string> RevertIgnore = new HashSet<string>();

            public IReadOnlyList<string> TrouverJournauxActifs(string id) { List<string> j; return Journaux.TryGetValue(id, out j) ? j : new List<string>(); }

            public Task<ItemOutcome> AnnulerParJournalAsync(string journal, string id)
            {
                Appels.Add("journal:" + id + "@" + journal);
                return Task.FromResult(new ItemOutcome { Id = id, Status = JournalEnEchec.Contains(id) ? OutcomeStatus.Failed : OutcomeStatus.Ok, Message = JournalEnEchec.Contains(id) ? "valeur verrouillée" : null });
            }

            public Task<ItemOutcome> ExecuterRevertAsync(CatalogItem item)
            {
                Appels.Add("revert:" + item.Id);
                return Task.FromResult(new ItemOutcome { Id = item.Id, Status = RevertIgnore.Contains(item.Id) ? OutcomeStatus.Skipped : OutcomeStatus.Ok, Message = RevertIgnore.Contains(item.Id) ? "journal seul" : null });
            }

            public IDisposable PrendreVerrou() { return Occupe ? null : new Verrou(this); }
            public bool MemeUtilisateur() { return MemeUser; }
            public void OublierDernieresApplications(IEnumerable<string> ids) { Oublies.AddRange(ids); }

            private class Verrou : IDisposable
            {
                private readonly Faux f; public Verrou(Faux f) { this.f = f; }
                public void Dispose() { f.VerrouLibere = true; }
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
            faux.Journaux["tel.a"] = new List<string> { @"C:\S\20260101-100000\journal.jsonl" };
            var vm = Cree(faux);

            var r = await vm.ToutRestaurerAsync();

            Assert.Equal(new[] { @"journal:tel.a@C:\S\20260101-100000\journal.jsonl", "revert:tel.b" }, faux.Appels);
            Assert.Equal(1, r.AnnuleesParJournal);
            Assert.Equal(1, r.AnnuleesParRevert);
            Assert.Empty(r.Echecs);
            Assert.Contains("tel.definitif", r.Ignorees);   // appliquée mais sans journal ni revert
            Assert.DoesNotContain("tel.win11", r.Ignorees); // incompatible : n'existe pas pour ce PC
            Assert.Equal(2, detections);                    // détection avant, puis nouvelle détection après
            Assert.Equal(new[] { "tel.a", "tel.b" }, faux.Oublies);   // plus « appliquées par l'outil » (last-applied.json)
            Assert.True(faux.VerrouLibere);
        }

        [Fact]
        public async Task Un_revert_ignore_n_est_ni_compte_comme_restaure_ni_oublie()
        {
            etats["tel.a"] = DetectState.Applied; etats["tel.b"] = DetectState.Applied;
            var faux = new Faux();
            faux.RevertIgnore.Add("tel.a");
            var vm = Cree(faux);

            var r = await vm.ToutRestaurerAsync();

            Assert.Equal(1, r.AnnuleesParRevert);
            Assert.Contains("tel.a", r.Ignorees);
            Assert.Equal(new[] { "tel.b" }, faux.Oublies);
            Assert.Contains(r.Remarques, x => x.Contains("tel.a") && x.Contains("journal seul"));
        }

        [Fact]
        public async Task Un_echec_du_journal_n_est_pas_remplace_par_un_revert_et_il_est_signale()
        {
            etats["tel.a"] = DetectState.Applied;
            var faux = new Faux();
            faux.Journaux["tel.a"] = new List<string> { "j" };
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
        public async Task Annule_tous_les_journaux_actifs_du_plus_recent_au_plus_ancien_meme_si_non_detectee_appliquee()
        {
            etats["tel.a"] = DetectState.NotApplied;     // appliquée en partie : la détection ne la voit pas
            etats["tel.b"] = DetectState.Unknown;
            var faux = new Faux();
            faux.Journaux["tel.a"] = new List<string> { "B", "A" };
            faux.Journaux["tel.b"] = new List<string> { "A" };
            var vm = Cree(faux);

            var r = await vm.ToutRestaurerAsync();

            Assert.Equal(new[] { "journal:tel.a@B", "journal:tel.a@A", "journal:tel.b@A" }, faux.Appels);
            Assert.Equal(2, r.AnnuleesParJournal);
            Assert.Empty(r.Echecs);
        }

        [Fact]
        public async Task Un_echec_sur_un_journal_arrete_les_suivants_de_cette_option()
        {
            var faux = new Faux();
            faux.Journaux["tel.a"] = new List<string> { "B", "A" };
            faux.JournalEnEchec.Add("tel.a");
            var vm = Cree(faux);
            var r = await vm.ToutRestaurerAsync();
            Assert.Equal(new[] { "journal:tel.a@B" }, faux.Appels);
            Assert.Single(r.Echecs);
            Assert.Empty(faux.Oublies);
        }

        [Fact]
        public async Task Le_revert_reste_limite_aux_options_detectees_appliquees_sans_journal()
        {
            etats["tel.a"] = DetectState.Unknown; etats["tel.b"] = DetectState.NotApplied;
            var faux = new Faux();
            var vm = Cree(faux);
            await vm.ToutRestaurerAsync();
            Assert.Empty(faux.Appels);
        }

        [Fact]
        public async Task Une_autre_execution_en_cours_bloque_tout_restaurer()
        {
            etats["tel.a"] = DetectState.Applied;
            var faux = new Faux { Occupe = true };
            faux.Journaux["tel.a"] = new List<string> { "j" };
            var vm = Cree(faux);
            await vm.ToutRestaurerAsync();
            Assert.Empty(faux.Appels);
            Assert.Contains("autre exécution", vm.Message);
        }

        [Fact]
        public async Task Sous_un_autre_compte_les_options_hkcu_sont_ignorees_et_signalees()
        {
            etats["tel.a"] = DetectState.Applied; etats["tel.b"] = DetectState.Applied;
            var faux = new Faux { MemeUser = false };
            faux.Journaux["tel.a"] = new List<string> { "j" };
            faux.Journaux["tel.b"] = new List<string> { "j" };
            var vm = Cree(faux);
            vm.Options.Single(o => o.Id == "tel.a").Item.Hkcu = true;

            var r = await vm.ToutRestaurerAsync();

            Assert.Equal(new[] { "journal:tel.b@j" }, faux.Appels);
            Assert.Equal(new[] { "tel.a" }, r.IgnoreesAutreCompte);
            Assert.Contains("autre compte", vm.Message);
            Assert.Equal(new[] { "tel.b" }, faux.Oublies);
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
