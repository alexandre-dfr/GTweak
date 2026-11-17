using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class MainViewModelTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-vm-" + Guid.NewGuid().ToString("N"));
        private readonly MainViewModel vm;
        private int confirmations;
        private bool reponseConfirmation = true;

        public MainViewModelTests()
        {
            Directory.CreateDirectory(tmp);
            vm = Cree(new[]
            {
                It("opt.sur", "Optimisation", "Souris", "Précision du pointeur", RiskLevel.Sur),
                It("opt.avance", "Optimisation", "Souris", "Option avancée", RiskLevel.Avance),
                It("opt.risque", "Optimisation", "Sécurité", "Protections désactivées", RiskLevel.Risque),
                It("tel.sur", "Télémétrie", "Windows", "Télémétrie réduite", RiskLevel.Sur),
                It("win11.only", "Optimisation", "Windows 11", "Seulement Windows 11", RiskLevel.Sur, "11"),
            });
        }

        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static CatalogItem It(string id, string menu, string groupe, string titre, RiskLevel r, string win = null)
        {
            var it = new CatalogItem { Id = id, Menu = menu, Groupe = groupe, Titre = titre, Description = "Description de " + titre, Risque = r, Reversible = r != RiskLevel.Risque };
            if (win != null) it.Compatibilite.Windows.Add(win);
            return it;
        }

        private MainViewModel Cree(IEnumerable<CatalogItem> items, Func<IEnumerable<CatalogItem>, Task<Dictionary<string, DetectState>>> detect = null)
        {
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = Path.Combine(tmp, "M"), SessionsDir = Path.Combine(tmp, "M", "Sessions"), QuarantineDir = Path.Combine(tmp, "Q") };
            return new MainViewModel(items.ToList(), new MachineFacts { WindowsMajor = "10", Edition = "Pro" }, paths,
                detect ?? (its => Task.FromResult(its.ToDictionary(i => i.Id, i => DetectState.NotApplied))),
                null, o => { confirmations++; return reponseConfirmation; });
        }

        private bool Coche(string id) => vm.Options.Single(o => o.Id == id).IsChecked;
        private void Cocher(string id) => vm.Options.Single(o => o.Id == id).IsChecked = true;
        private List<string> Visibles() => vm.Visibles.Select(o => o.Id).ToList();
        private static Profil P(params string[] ids) => new Profil { Nom = "t", Options = ids.ToList() };

        [Fact] public void Selection_regroupe_tous_les_menus() { Cocher("opt.sur"); Cocher("tel.sur"); Assert.Equal(2, vm.SelectedCount); }

        [Fact]
        public void Recommande_coche_uniquement_le_sur_du_menu_affiche()
        {
            vm.ApplyRecommended("Optimisation");
            Assert.True(Coche("opt.sur")); Assert.False(Coche("opt.avance")); Assert.False(Coche("opt.risque")); Assert.False(Coche("tel.sur"));
        }

        [Fact] public void Option_incompatible_n_est_pas_listee() => Assert.DoesNotContain("win11.only", vm.Options.Select(o => o.Id));

        [Fact] public void Charger_un_profil_coche_ses_options() { vm.LoadProfil(P("opt.sur")); Assert.True(Coche("opt.sur")); Assert.Equal(1, vm.SelectedCount); }

        [Fact]
        public void Recherche_filtre_sans_perdre_les_coches()
        {
            Cocher("opt.sur"); vm.Recherche = "telemetrie";
            Assert.DoesNotContain("opt.sur", Visibles()); Assert.Equal(1, vm.SelectedCount);
        }

        [Fact] public void Recherche_ignore_accents_et_casse() { vm.Recherche = "TELEMETRIE"; Assert.Contains("tel.sur", Visibles()); }

        [Fact]
        public void Sans_recherche_seul_le_menu_courant_est_visible()
        {
            vm.MenuCourant = "Télémétrie";
            Assert.Equal(new[] { "tel.sur" }, Visibles());
            vm.MenuCourant = "Optimisation";
            Assert.Equal(new[] { "opt.sur", "opt.avance", "opt.risque" }, Visibles());
        }

        [Fact]
        public void Menus_dans_l_ordre_avec_separateur()
        {
            Assert.Equal(new[] { "Mises à jour", "Télémétrie", "Nettoyage", "Optimisation", "Bloatware", "Informations", null, "Mode Jeu", "Historique" },
                vm.Menus.Select(m => m.EstSeparateur ? null : m.Nom).ToArray());
        }

        [Fact]
        public void Risque_demande_confirmation_une_seule_fois_par_session()
        {
            var o = vm.Options.Single(x => x.Id == "opt.risque");
            o.IsChecked = true; Assert.True(o.IsChecked); Assert.Equal(1, confirmations);
            o.IsChecked = false; o.IsChecked = true; Assert.Equal(1, confirmations);
        }

        [Fact]
        public void Risque_refuse_reste_decoche()
        {
            reponseConfirmation = false;
            vm.Options.Single(x => x.Id == "opt.risque").IsChecked = true;
            Assert.False(Coche("opt.risque")); Assert.Equal(0, vm.SelectedCount);
        }

        [Fact]
        public void Recommande_ne_demande_aucune_confirmation() { vm.ApplyRecommended("Optimisation"); Assert.Equal(0, confirmations); }

        [Fact]
        public void Profil_avec_risque_refuse_n_active_pas_l_option()
        {
            reponseConfirmation = false; vm.LoadProfil(P("opt.sur", "opt.risque"));
            Assert.True(Coche("opt.sur")); Assert.False(Coche("opt.risque"));
        }

        [Fact]
        public void Le_texte_de_confirmation_mentionne_le_classement_Risque()
        {
            var o = vm.Options.Single(x => x.Id == "opt.risque");
            Assert.Contains("Cette option est classée Risqué", o.TexteConfirmation);
            Assert.Contains(o.Description, o.TexteConfirmation);
        }

        [Fact]
        public void Badges_de_risque()
        {
            Assert.Equal("Sûr", vm.Options.Single(o => o.Id == "opt.sur").Risque);
            Assert.Equal("Avancé", vm.Options.Single(o => o.Id == "opt.avance").Risque);
            Assert.Equal("Risqué", vm.Options.Single(o => o.Id == "opt.risque").Risque);
        }

        [Fact]
        public async Task Etat_passe_de_Detection_a_la_valeur_finale()
        {
            var gate = new TaskCompletionSource<Dictionary<string, DetectState>>();
            var v = Cree(new[] { It("a.b", "Nettoyage", "G", "T", RiskLevel.Sur), It("a.c", "Nettoyage", "G", "U", RiskLevel.Sur), It("a.d", "Nettoyage", "G", "V", RiskLevel.Sur) }, its => gate.Task);
            Assert.All(v.Options, o => Assert.Equal("Détection…", o.Etat));
            var t = v.DetectAsync();
            gate.SetResult(new Dictionary<string, DetectState> { { "a.b", DetectState.Applied }, { "a.c", DetectState.NotApplied } });
            await t;
            Assert.Equal("Déjà appliqué", v.Options[0].Etat); Assert.Equal("Non appliqué", v.Options[1].Etat); Assert.Equal("Inconnu", v.Options[2].Etat);
        }

        [Fact]
        public async Task Detection_en_erreur_donne_Inconnu()
        {
            var v = Cree(new[] { It("a.b", "Nettoyage", "G", "T", RiskLevel.Sur) }, its => { throw new InvalidOperationException("x"); });
            await v.DetectAsync();
            Assert.Equal("Inconnu", v.Options[0].Etat);
        }

        [Fact]
        public void BuildSelection_et_ToProfil_reprennent_les_coches()
        {
            Cocher("opt.sur"); Cocher("tel.sur");
            Assert.Equal(new[] { "opt.sur", "tel.sur" }, vm.BuildSelection().OptionIds.OrderBy(x => x).ToArray());
            var p = vm.ToProfil("gaming");
            Assert.Equal("gaming", p.Nom); Assert.Equal(new[] { "opt.sur", "tel.sur" }, p.Options.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Sauver_puis_recharger_un_profil()
        {
            Cocher("opt.sur");
            Assert.Null(vm.SauverProfil("Mon profil"));
            Assert.Contains("Mon profil", vm.ProfilsDisponibles);
            vm.LoadProfil(P());
            Assert.Equal(0, vm.SelectedCount);
            vm.ProfilSelectionne = null; vm.ProfilSelectionne = "Mon profil";
            Assert.True(Coche("opt.sur"));
        }

        [Fact]
        public void Les_menus_affichent_le_nombre_d_options_cochees()
        {
            var opt = vm.Menus.Single(m => m.Nom == "Optimisation");
            Assert.Equal("Optimisation", opt.Libelle);
            Cocher("opt.sur"); Cocher("opt.avance");
            Assert.Equal(2, opt.Coches);
            Assert.Equal("Optimisation  ·  2", opt.Libelle);
            Assert.Equal(0, vm.Menus.Single(m => m.Nom == "Télémétrie").Coches);
            vm.LoadProfil(P("tel.sur"));
            Assert.Equal(0, opt.Coches);
            Assert.Equal(1, vm.Menus.Single(m => m.Nom == "Télémétrie").Coches);
        }

        [Fact]
        public void Decocher_ne_touche_que_les_options_affichees()
        {
            Cocher("opt.sur"); Cocher("tel.sur");
            vm.MenuCourant = "Optimisation";
            Assert.True(vm.DecocherCommand.CanExecute(null));
            vm.DecocherCommand.Execute(null);
            Assert.False(Coche("opt.sur"));
            Assert.True(Coche("tel.sur"));
            Assert.False(vm.DecocherCommand.CanExecute(null));
        }

        [Fact]
        public async Task Masquer_les_options_deja_appliquees_garde_les_cochees()
        {
            var v = Cree(new[]
            {
                It("a.fait", "Optimisation", "G", "Déjà fait", RiskLevel.Sur),
                It("a.coche", "Optimisation", "G", "Fait mais coché", RiskLevel.Sur),
                It("a.afaire", "Optimisation", "G", "À faire", RiskLevel.Sur),
            }, its => Task.FromResult(its.ToDictionary(i => i.Id, i => i.Id == "a.afaire" ? DetectState.NotApplied : DetectState.Applied)));
            v.MenuCourant = "Optimisation";
            await v.DetectAsync();
            v.Options.Single(o => o.Id == "a.coche").IsChecked = true;
            Assert.Equal(3, v.Visibles.Count);
            v.MasquerAppliquees = true;
            Assert.Equal(new[] { "a.coche", "a.afaire" }, v.Visibles.Select(o => o.Id).ToArray());
            v.MasquerAppliquees = false;
            Assert.Equal(3, v.Visibles.Count);
        }

        [Fact]
        public void Profil_corrompu_donne_un_message_sans_exception()
        {
            Directory.CreateDirectory(Path.Combine(tmp, "Profils"));
            File.WriteAllText(Path.Combine(tmp, "Profils", "casse.json"), "{pas du json");
            vm.RafraichirProfils();
            vm.ProfilSelectionne = "casse";
            Assert.False(string.IsNullOrEmpty(vm.Message)); Assert.Equal(0, vm.SelectedCount);
        }

        [Fact]
        public void Apercu_indique_definitif_et_script_exact()
        {
            Cocher("opt.sur"); Cocher("opt.risque");
            var rows = vm.BuildPreview();
            Assert.Equal(2, rows.Count);
            Assert.Equal("définitif", rows.Single(r => r.Titre == "Protections désactivées").Reversibilite);
            Assert.Equal("Annulable", rows.Single(r => r.Titre == "Précision du pointeur").Reversibilite);
            Assert.Equal("—", rows[0].Taille);
        }

        [Fact]
        public void Le_journal_en_direct_affiche_la_note_d_un_succes()
        {
            var v = new MainViewModel(new List<CatalogItem> { It("a.b", "Nettoyage", "G", "Titre", RiskLevel.Sur) }, new MachineFacts(),
                new DataPaths { ProfilsDir = tmp }, its => Task.FromResult(new Dictionary<string, DetectState>()), null, o => true);
            Assert.Equal("Appliqué : Titre (Déchiffrement lancé)", v.FormaterLigne("##GT|ok|a.b|Déchiffrement lancé"));
            Assert.Equal("Appliqué : Titre", v.FormaterLigne("##GT|ok|a.b"));
        }

        [Fact]
        public async Task Lancer_transmet_la_selection_au_pipeline()
        {
            RunRequest recu = null;
            var v = new MainViewModel(new List<CatalogItem> { It("a.b", "Nettoyage", "G", "T", RiskLevel.Sur) }, new MachineFacts(),
                new DataPaths { ProfilsDir = tmp }, its => Task.FromResult(new Dictionary<string, DetectState>()),
                (r, l, ct) => { recu = r; return Task.FromResult(new SessionReport { ExitCode = 0 }); }, o => true);
            v.Options[0].IsChecked = true;
            var rep = await v.LancerAsync(null, CancellationToken.None);
            Assert.Equal(0, rep.ExitCode); Assert.Contains("a.b", recu.Selection.OptionIds);
        }

        [Fact]
        public void Rapport_sans_dossier_de_session_affiche_les_avertissements()
        {
            var t = MainViewModel.RapportTexte(new SessionReport { SessionDir = null, ExitCode = 2, Avertissements = { "Une autre instance est déjà en cours." } }, vm.Options.Select(o => o.Item).ToList());
            Assert.Contains("Une autre instance est déjà en cours.", t);
        }

        [Fact]
        public void Lancer_rend_la_main_meme_si_le_pipeline_bloque_avant_son_premier_await()
        {
            var gate = new ManualResetEventSlim();
            var v = new MainViewModel(new List<CatalogItem> { It("a.b", "Nettoyage", "G", "T", RiskLevel.Sur) }, new MachineFacts(),
                new DataPaths { ProfilsDir = tmp }, its => Task.FromResult(new Dictionary<string, DetectState>()),
                (r, l, ct) => { gate.Wait(5000); return Task.FromResult(new SessionReport()); }, o => true);
            v.Options[0].IsChecked = true;
            var t = v.LancerAsync(null, CancellationToken.None);
            Assert.False(t.IsCompleted);
            gate.Set();
            Assert.True(t.Wait(5000));
        }

        [Fact]
        public void Recommande_desactive_pendant_une_recherche()
        {
            vm.MenuCourant = "Optimisation";
            Assert.True(vm.RecommanderCommand.CanExecute(null));
            vm.Recherche = "souris";
            Assert.False(vm.RecommanderCommand.CanExecute(null));
        }

        [Fact]
        public void Recharger_le_profil_courant_recoche_ses_options()
        {
            Cocher("opt.sur");
            Assert.Null(vm.SauverProfil("p1"));
            vm.Options.Single(o => o.Id == "opt.sur").IsChecked = false;
            vm.RechargerProfil();
            Assert.True(Coche("opt.sur"));
        }

        [Fact]
        public void Script_exporte_commence_par_un_en_tete_explicatif()
        {
            Cocher("opt.sur");
            var t = vm.BuildExportScript();
            Assert.StartsWith("#", t);
            Assert.Contains("déjà appliquées ou ignorées ne seront pas exécutées", t.Substring(0, 400));
        }
    }
}
