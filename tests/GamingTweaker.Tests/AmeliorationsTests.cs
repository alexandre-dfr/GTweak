using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Diagnostic (démarrage, HDR, fréquence variable), libération de mémoire, aperçu, mesures avant / après.</summary>
    public class AmeliorationsTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "gt-amel-" + Guid.NewGuid().ToString("N"));
        private readonly string verrou = "Local\\GT.Test.Amel." + Guid.NewGuid().ToString("N");
        public AmeliorationsTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        private static readonly DateTime Maintenant = new DateTime(2026, 11, 18);

        // ---------- EDID ----------

        private static byte[] Edid(int min, int max, int drapeaux = 0, string nom = "VG27AQ")
        {
            var e = new byte[128];
            // Descripteur 1 (octet 54) : nom (0xFC) ; descripteur 2 (octet 72) : plage (0xFD).
            e[57] = 0xFC; var n = System.Text.Encoding.ASCII.GetBytes(nom + "\n"); Array.Copy(n, 0, e, 59, n.Length);
            e[75] = 0xFD; e[76] = (byte)drapeaux; e[77] = (byte)min; e[78] = (byte)max;
            return e;
        }

        [Fact]
        public void Plage_de_frequence_lue_dans_l_edid()
        {
            Assert.Equal(Tuple.Create(48, 144), Affichage.PlageVerticale(Edid(48, 144)));
            Assert.Equal(Tuple.Create(48, 360), Affichage.PlageVerticale(Edid(48, 105, drapeaux: 0x02)));   // maximum + 255
            Assert.Equal("VG27AQ", Affichage.NomEdid(Edid(48, 144)));
            Assert.Null(Affichage.PlageVerticale(new byte[128]));
            Assert.Null(Affichage.PlageVerticale(null));
        }

        [Fact]
        public void Entree_de_demarrage_desactivee_si_premier_octet_impair()
        {
            Assert.True(ProgrammesDemarrage.Desactive(new byte[] { 3, 0, 0 }));
            Assert.True(ProgrammesDemarrage.Desactive(new byte[] { 7 }));
            Assert.False(ProgrammesDemarrage.Desactive(new byte[] { 2, 0 }));
            Assert.False(ProgrammesDemarrage.Desactive(new byte[] { 6 }));
            Assert.False(ProgrammesDemarrage.Desactive(null));
        }

        // ---------- Diagnostic ----------

        private static PointDiag Seul(MesuresDiag m, string debut) => DiagnosticJeu.Analyser(m, Maintenant).Single(p => p.Titre.StartsWith(debut, StringComparison.Ordinal));

        [Fact]
        public void Programmes_au_demarrage()
        {
            Assert.Equal(NiveauDiag.Attention, Seul(new MesuresDiag { ProgrammesDemarrage = 14 }, "14 programmes").Niveau);
            Assert.Equal("option:opt.startup", Seul(new MesuresDiag { ProgrammesDemarrage = 14 }, "14 programmes").Action);
            Assert.Equal(NiveauDiag.Info, Seul(new MesuresDiag { ProgrammesDemarrage = 7 }, "7 programmes").Niveau);
            Assert.Equal(NiveauDiag.Ok, Seul(new MesuresDiag { ProgrammesDemarrage = 3 }, "Peu de programmes").Niveau);
        }

        [Fact]
        public void Hdr_compatible_mais_desactive()
        {
            var m = new MesuresDiag(); m.Hdr.Add(new HdrEcran { Pris = true, Actif = false });
            Assert.Equal("ms-settings:display-hdr", Seul(m, "Écran compatible HDR").Action);
            m.Hdr[0].Actif = true;
            Assert.Equal(NiveauDiag.Ok, Seul(m, "HDR activé").Niveau);
            Assert.Empty(DiagnosticJeu.Analyser(new MesuresDiag(), Maintenant));
        }

        [Fact]
        public void Frequence_variable_seulement_pour_une_plage_large()
        {
            var m = new MesuresDiag();
            m.Plages.Add(new PlageEcran { Nom = "VG27AQ", HzMin = 48, HzMax = 165 });
            m.Plages.Add(new PlageEcran { Nom = "Bureau", HzMin = 56, HzMax = 61 });
            var pts = DiagnosticJeu.Analyser(m, Maintenant);
            Assert.Single(pts);
            Assert.Equal("Écran à fréquence variable (48–165 Hz)", pts[0].Titre);
            Assert.Equal("ms-settings:display-advancedgraphics", pts[0].Action);
        }

        // ---------- Libération de mémoire ----------

        private class Actions : IModeJeuActions, IViderMemoire
        {
            public List<string> Exclusions;
            public ServiceState StopService(string nom) { return ServiceState.Running; }
            public void StartService(string nom) { }
            public bool KillProcess(string nom, string chemin) { return true; }
            public void RestartProcess(string chemin) { }
            public string SetPlan(string guid) { return "p"; }
            public string GetPlan() { return "p"; }
            public long ViderMemoire(IEnumerable<string> exclusions) { Exclusions = exclusions.ToList(); return 512; }
        }

        [Fact]
        public void Liberation_de_memoire_a_l_activation_avec_les_jeux_exclus()
        {
            var a = new Actions();
            var mj = new ModeJeu(dir, a, verrou);
            var cfg = new ModeJeuAutoConfig { LibererMemoire = true, Plan = false, Jeux = new List<JeuSurveille> { new JeuSurveille { Nom = "J", Chemin = @"D:\Jeux\A", Source = "Steam" } } };
            var b = mj.Activate(cfg.VersModeJeu());
            Assert.Contains("Mémoire libérée : 512 Mo disponibles en plus.", b.Actions);
            Assert.Equal(new[] { @"D:\Jeux\A" }, a.Exclusions);
            mj.Restore();
            Assert.False(mj.IsActive);
        }

        [Fact]
        public void Sans_l_option_rien_n_est_libere()
        {
            var a = new Actions();
            new ModeJeu(dir, a, verrou).Activate(new ModeJeuConfig { PlanGuid = ModeJeu.PlanPerformances });
            Assert.Null(a.Exclusions);
        }

        // ---------- Aperçu ----------

        [Fact]
        public void Synthese_de_l_apercu()
        {
            var l = new List<PreviewRow>
            {
                new PreviewRow { Reversibilite = "Annulable", Redemarrage = "" },
                new PreviewRow { Reversibilite = "Annulable", Redemarrage = "Redémarrage requis" },
                new PreviewRow { Reversibilite = "définitif", Redemarrage = "" },
            };
            Assert.Equal("2 annulables · 1 définitive · 1 nécessite un redémarrage", MainViewModel.ResumeApercu(l));
        }

        // ---------- Mesures avant / après ----------

        private static MesureSnapshot M(string date, double dpc, double cpu, int services, long ram, int demarrage) =>
            new MesureSnapshot { Date = date, DpcTime = dpc, InterruptTime = 0.1, Cpu = cpu, Services = services, Processus = 200, RamDisponibleMo = ram, Demarrage = demarrage };

        [Fact]
        public void Comparaison_avec_verdict_par_mesure()
        {
            // Libellés en français (virgule décimale), quelle que soit la langue de la machine de test.
            var avant = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            try { VerifierComparaison(); } finally { System.Threading.Thread.CurrentThread.CurrentCulture = avant; }
        }

        private static void VerifierComparaison()
        {
            var t = HistoriqueViewModel.FormaterComparaison(M("2026-11-18T10:00:00", 0.5, 4.0, 120, 8000, 12), M("2026-11-18T11:00:00", 0.2, 1.5, 100, 9000, 5));
            Assert.Contains("Temps DPC : 0,50 % → 0,20 % (-0,30 point(s)) : mieux", t);
            Assert.Contains("Processeur au repos : 4,00 % → 1,50 %", t);
            Assert.Contains("Programmes au démarrage : 12 → 5 : mieux", t);
            Assert.Contains("Mémoire disponible : 8000 Mo → 9000 Mo (+1000 Mo) : mieux", t);
            Assert.Contains("Temps d'interruptions : 0,10 % → 0,10 % (0,00 point(s)) : stable", t);
            Assert.Contains("Bilan : 5 mesure(s) en progrès, 0 en recul.", t);
        }

        [Fact]
        public void Avant_apres_compare_les_deux_derniers_releves()
        {
            var paths = new DataPaths { MachineDir = dir, SessionsDir = Path.Combine(dir, "Sessions"), QuarantineDir = Path.Combine(dir, "Q") };
            Mesures.Save(M("2026-11-18T09:00:00", 0.9, 9, 130, 7000, 15), dir);
            Mesures.Save(M("2026-11-18T10:00:00", 0.5, 4, 120, 8000, 12), dir);
            Mesures.Save(M("2026-11-18T11:00:00", 0.2, 1.5, 100, 9000, 5), dir);
            var vm = new HistoriqueViewModel(paths, null, new List<CatalogItem>(), new QuarantineService(paths.QuarantineDir, paths.SessionsDir));
            vm.Charger();
            vm.AvantApres();
            Assert.StartsWith("Du relevé du 2026-11-18T10:00:00 à celui du 2026-11-18T11:00:00", vm.Comparaison);
            // Deux relevés cochés : ce sont eux qui sont comparés (un seul bouton « Avant / après »).
            vm.Releves.Single(r => r.Mesure.Date == "2026-11-18T09:00:00").IsChecked = true;
            vm.Releves.Single(r => r.Mesure.Date == "2026-11-18T11:00:00").IsChecked = true;
            Assert.True(vm.ComparerCommand.CanExecute(null));
            vm.ComparerCommand.Execute(null);
            Assert.StartsWith("Du relevé du 2026-11-18T09:00:00 à celui du 2026-11-18T11:00:00", vm.Comparaison);
        }
    }
}
