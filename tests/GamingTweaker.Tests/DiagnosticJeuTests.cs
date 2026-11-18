using System;
using System.Linq;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    public class DiagnosticJeuTests
    {
        private static readonly DateTime Maintenant = new DateTime(2026, 11, 18);

        private static PointDiag Seul(MesuresDiag m, string debutTitre) => DiagnosticJeu.Analyser(m, Maintenant).Single(p => p.Titre.StartsWith(debutTitre, StringComparison.Ordinal));

        [Fact]
        public void Ecran_sous_sa_frequence_maximale()
        {
            var m = new MesuresDiag();
            m.Ecrans.Add(new EcranDiag { Nom = "A", Largeur = 2560, Hauteur = 1440, HzActuel = 60, HzMax = 144 });
            var p = Seul(m, "Écran à 60 Hz au lieu de 144 Hz");
            Assert.Equal(NiveauDiag.Attention, p.Niveau);
            Assert.Equal("ms-settings:display-advanced", p.Action);
            m.Ecrans[0].HzActuel = 144;
            Assert.Equal(NiveauDiag.Ok, Seul(m, "Écran à sa fréquence maximale").Niveau);
        }

        [Fact]
        public void Ecart_d_un_hertz_ignore() // 59/60 Hz, 143.9/144 Hz
        {
            var m = new MesuresDiag();
            m.Ecrans.Add(new EcranDiag { Nom = "A", HzActuel = 59, HzMax = 60 });
            Assert.Equal(NiveauDiag.Ok, Seul(m, "Écran à sa fréquence maximale").Niveau);
        }

        [Theory]
        [InlineData("F4-3200C16-8GVKB", 3200)]
        [InlineData("CMK16GX4M2B3200C16", 3200)]
        [InlineData("KF432C16BB/8", 3200)]
        [InlineData("KF560C40BBK2-32", 6000)]
        [InlineData("F5-6000J3038F16G", 6000)]
        [InlineData("M378A1K43CB2-CTD", 0)]
        [InlineData("", 0)]
        public void Vitesse_lue_dans_la_reference(string reference, int attendu) => Assert.Equal(attendu, DiagnosticJeu.VitesseReference(reference));

        [Fact]
        public void Memoire_sans_xmp_et_simple_canal()
        {
            var m = new MesuresDiag();
            m.Barrettes.Add(new BarretteDiag { Vitesse = 2133, Configuree = 2133, Reference = "F4-3200C16-8GVKB" });
            Assert.Equal(NiveauDiag.Attention, Seul(m, "Mémoire à 2133 MT/s au lieu de 3200").Niveau);
            Assert.Equal(NiveauDiag.Attention, Seul(m, "Un seul module").Niveau);
            m.EstPortable = true;
            Assert.Equal(NiveauDiag.Info, Seul(m, "Un seul module").Niveau);
        }

        [Fact]
        public void Memoire_a_sa_vitesse_en_double_canal()
        {
            var m = new MesuresDiag();
            m.Barrettes.Add(new BarretteDiag { Vitesse = 4800, Configuree = 6000, Reference = "F5-6000J3038F16G" });
            m.Barrettes.Add(new BarretteDiag { Vitesse = 4800, Configuree = 6000, Reference = "F5-6000J3038F16G" });
            var pts = DiagnosticJeu.Analyser(m, Maintenant);
            Assert.Contains(pts, p => p.Niveau == NiveauDiag.Ok && p.Titre == "Mémoire à sa vitesse (6000 MT/s)");
            Assert.DoesNotContain(pts, p => p.Titre.StartsWith("Un seul module"));
        }

        [Fact]
        public void Reglages_windows_proposent_l_option_qui_corrige()
        {
            var m = new MesuresDiag { ModeJeuWindows = false, Hags = 1, CaptureArrierePlan = true, Plan = DiagnosticJeu.PlanEquilibre };
            Assert.Equal("option:opt.gamemode-on", Seul(m, "Mode Jeu de Windows désactivé").Action);
            Assert.Equal("option:opt.hags", Seul(m, "Planification GPU").Action);
            Assert.Equal("option:opt.gamedvr-off", Seul(m, "Enregistrement en arrière-plan").Action);
            Assert.Equal("option:opt.plan-high", Seul(m, "Plan d'alimentation Équilibré").Action);
            m.EstPortable = true;
            Assert.DoesNotContain(DiagnosticJeu.Analyser(m, Maintenant), p => p.Titre.StartsWith("Plan d'alimentation"));
        }

        [Fact]
        public void Les_options_proposees_existent_dans_le_catalogue()
        {
            var ids = Catalogue.LoadEmbedded().Select(c => c.Id).ToList();
            var m = new MesuresDiag { ModeJeuWindows = false, Hags = 1, CaptureArrierePlan = true, Plan = DiagnosticJeu.PlanEquilibre };
            foreach (var p in DiagnosticJeu.Analyser(m, Maintenant).Where(p => (p.Action ?? "").StartsWith("option:")))
                Assert.Contains(p.Action.Substring(7), ids);
        }

        [Fact]
        public void Disque_plein_batterie_et_pilote_ancien()
        {
            var m = new MesuresDiag { LibreSysteme = 15L * 1073741824, TotalSysteme = 500L * 1073741824, SurBatterie = true, DatePiloteGpu = Maintenant.AddDays(-300), NomGpu = "GPU" };
            Assert.Equal("menu:Nettoyage", Seul(m, "Disque système presque plein").Action);
            Assert.Equal(NiveauDiag.Attention, Seul(m, "Sur batterie").Niveau);
            Assert.Equal("menu:Mises à jour", Seul(m, "Pilote graphique ancien").Action);
            m.LibreSysteme = 200L * 1073741824; m.DatePiloteGpu = Maintenant.AddDays(-20); m.SurBatterie = false;
            var pts = DiagnosticJeu.Analyser(m, Maintenant);
            Assert.Equal(new[] { NiveauDiag.Ok, NiveauDiag.Ok }, pts.Select(p => p.Niveau).ToArray());
        }

        [Fact]
        public void Points_a_corriger_en_premier()
        {
            var m = new MesuresDiag { ModeJeuWindows = true, Hags = 1, SurBatterie = true };
            Assert.Equal(new[] { NiveauDiag.Attention, NiveauDiag.Info, NiveauDiag.Ok }, DiagnosticJeu.Analyser(m, Maintenant).Select(p => p.Niveau).ToArray());
        }

        [Fact]
        public void Action_option_coche_l_option_et_ouvre_son_menu()
        {
            var vm = new GamingTweaker.Ui.MainViewModel(new[] { new CatalogItem { Id = "opt.hags", Menu = "Optimisation", Groupe = "G", Titre = "HAGS", Risque = RiskLevel.Avance, Reversible = true } },
                new MachineFacts { WindowsMajor = "11" }, new DataPaths(), its => System.Threading.Tasks.Task.FromResult(its.ToDictionary(i => i.Id, i => DetectState.NotApplied)), null, o => true);
            vm.ExecuterActionDiagnostic("option:opt.hags");
            Assert.Equal("Optimisation", vm.MenuCourant);
            Assert.True(vm.Options.Single().IsChecked);
            vm.ExecuterActionDiagnostic("menu:Accueil");
            Assert.True(vm.EstAccueil);
            string ouvert = null; vm.OuvrirParametres = u => ouvert = u;
            vm.ExecuterActionDiagnostic("ms-settings:display-advanced");
            Assert.Equal("ms-settings:display-advanced", ouvert);
        }
    }
}
