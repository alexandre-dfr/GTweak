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
    public class RaccourciCompteurTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-racc-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private sealed class Source : ISourceCompteur
        {
            public bool TCpu, TGpu;
            public void Demarrer() { }
            public void Arreter() { }
            public void Choisir(bool cpu, bool gpu, bool ram) { }
            public void ChoisirTemperatures(bool cpu, bool gpu) { TCpu = cpu; TGpu = gpu; }
            public ReleveCompteur Lire() { return null; }
            public string Erreur { get { return null; } }
        }

        [Theory]
        [InlineData(2u | 4u, 0x7Au, null)]          // Ctrl+Maj+F11 (défaut)
        [InlineData(1u, 0x46u, null)]               // Alt+F
        [InlineData(4u, 0x46u, "Ctrl ou Alt")]      // Maj+F seul : gênerait le jeu
        [InlineData(0u, 0x7Au, "Ctrl ou Alt")]      // F11 seul
        [InlineData(2u, 0x7Bu, "F12")]              // F12 réservée
        [InlineData(2u, 0x11u, "en plus")]          // Ctrl seul
        [InlineData(2u, 0x5Bu, "Windows")]
        public void Combinaisons_acceptees_ou_refusees(uint mods, uint touche, string refus)
        {
            var r = CompteurConfig.RefusRaccourci(mods, touche);
            if (refus == null) Assert.Null(r); else Assert.Contains(refus, r);
        }

        [Fact]
        public void Texte_des_combinaisons()
        {
            Assert.Equal("Ctrl+Maj+F11", CompteurViewModel.TexteCombinaison(6, 0x7A));
            Assert.Equal("Ctrl+Alt+K", CompteurViewModel.TexteCombinaison(3, 0x4B));
            Assert.Equal("Alt+5", CompteurViewModel.TexteCombinaison(1, 0x35));
        }

        [Fact]
        public void Changer_le_raccourci_enregistre_et_previent_l_application()
        {
            var vm = new CompteurViewModel(tmp, new Source(), new CompteurConfig());
            int changements = 0; vm.RaccourciChange += () => changements++;
            Assert.NotNull(vm.DefinirRaccourci(4, 0x46));      // refusé : rien ne change
            Assert.Equal("Ctrl+Maj+F11", vm.Raccourci);
            Assert.Null(vm.DefinirRaccourci(3, 0x4B));
            Assert.Equal("Ctrl+Alt+K", vm.Raccourci);
            Assert.Equal(1, changements);
            vm.RaccourciDisponible = false;
            Assert.Contains("indisponible", vm.TexteRaccourci);
            vm.Enregistrer();
            var c = CompteurConfig.Charger(tmp);
            Assert.Equal(3u, c.RaccourciModificateurs);
            Assert.Equal(0x4Bu, c.RaccourciTouche);
        }

        [Fact]
        public void Raccourci_invalide_dans_le_fichier_remis_par_defaut()
        {
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, CompteurConfig.NomFichier), "{ \"raccourciModificateurs\": 0, \"raccourciTouche\": 123 }");
            var c = CompteurConfig.Charger(tmp);
            Assert.Equal(CompteurConfig.RaccourciModificateursParDefaut, c.RaccourciModificateurs);
            Assert.Equal(CompteurConfig.RaccourciToucheParDefaut, c.RaccourciTouche);
        }

        [Fact]
        public void Temperatures_demandees_seulement_si_la_ligne_est_affichee_et_affichage()
        {
            var src = new Source();
            var vm = new CompteurViewModel(tmp, src, new CompteurConfig { Actif = true });
            Assert.False(src.TGpu);
            vm.TempGpu = true; vm.TempCpu = true;
            Assert.True(src.TGpu); Assert.True(src.TCpu);
            vm.Cpu = false;
            Assert.False(src.TCpu);   // ligne CPU masquée : pas de lecture de la température du processeur
            vm.Cpu = true;
            vm.Rafraichir(new ReleveCompteur { Charge = new ChargeSysteme { Cpu = 20, Gpu = 90, TempGpu = 71.6, TempCpu = null } });
            Assert.Equal("GPU  90 %   72 °C", vm.TexteGpu);
            Assert.Equal("CPU  20 %    — °C", vm.TexteCpu);
        }
    }

    public class ChoixTemperatureTests
    {
        private static LectureTemperature L(string m, string n, double? v) { return new LectureTemperature { Materiel = m, Nom = n, Valeur = v }; }

        [Fact]
        public void Processeur_intel_puis_amd()
        {
            Assert.Equal(64.0, ChoixTemperature.Cpu(new[] { L("Cpu", "CPU Core #1", 58), L("Cpu", "CPU Package", 64), L("Cpu", "Core Max", 66) }));
            Assert.Equal(71.0, ChoixTemperature.Cpu(new[] { L("Cpu", "CCD1 (Tdie)", 69), L("Cpu", "Core (Tctl/Tdie)", 71) }));
            Assert.Equal(69.0, ChoixTemperature.Cpu(new[] { L("Cpu", "Inconnu A", 60), L("Cpu", "Inconnu B", 69) }));
        }

        [Fact]
        public void Carte_dediee_avant_integree_et_valeurs_absurdes_ignorees()
        {
            var l = new[] { L("GpuIntel", "GPU Core", 50), L("GpuNvidia", "GPU Hot Spot", 80), L("GpuNvidia", "GPU Core", 72) };
            Assert.Equal(72.0, ChoixTemperature.Gpu(l));
            Assert.Null(ChoixTemperature.Cpu(new[] { L("Cpu", "CPU Package", 0), L("Cpu", "Core Max", 255), L("Cpu", "Core Average", null) }));
            Assert.Null(ChoixTemperature.Gpu(new LectureTemperature[0]));
        }
    }

    public class DemarrageEtVersionTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-guide-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        [Fact]
        public void Version_avec_numero_de_build()
        {
            Assert.Equal("0.9.1 (build 58)", VersionAppli.Formater(new Version(0, 9, 1, 58)));
            Assert.Equal("0.9.1", VersionAppli.Formater(new Version(0, 9, 1, 0)));
        }

        [Fact]
        public void Lancement_interrompu_detecte_par_la_derniere_etape()
        {
            Assert.Null(RapportErreur.DerniereEtapeInachevee(new[] { "2026-11-20 10:00:00.000 lancement", "2026-11-20 10:00:02.000 " + RapportErreur.JalonFin }));
            Assert.Equal("dossier de données vérifié", RapportErreur.DerniereEtapeInachevee(new[] { "2026-11-20 10:00:00.000 lancement", "2026-11-20 10:00:01.123 dossier de données vérifié" }));
            Assert.Null(RapportErreur.DerniereEtapeInachevee(new string[0]));
        }

        [Fact]
        public void Guide_de_l_accueil_masque_pour_de_bon()
        {
            Directory.CreateDirectory(tmp);
            Func<MainViewModel> creer = () => new MainViewModel(new List<CatalogItem>(), new MachineFacts(), new DataPaths { MachineDir = tmp },
                its => Task.FromResult(new Dictionary<string, DetectState>()), null, o => true);
            var vm = creer();
            Assert.True(vm.AfficherGuide);
            vm.MasquerGuide();
            Assert.False(vm.AfficherGuide);
            Assert.False(creer().AfficherGuide);
            Assert.StartsWith("GamingTweaker ", vm.TexteVersion);
        }
    }

    public class SurveillanceReactiveTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "gt-reac-" + Guid.NewGuid().ToString("N"));
        public SurveillanceReactiveTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        [Fact]
        public void Un_demarrage_de_programme_avance_le_releve_sans_attendre_5_secondes()
        {
            int releves = 0;
            var cfg = new ModeJeuAutoConfig { Actif = true, Jeux = new List<JeuSurveille> { new JeuSurveille { Nom = "A", Chemin = @"D:\Jeux\A", Source = "Steam" } } };
            using (var s = new SurveillanceJeux(new ModeJeu(dir, new Inertes()), () => cfg, () => { Interlocked.Increment(ref releves); return new string[0]; }))
            {
                s.SignalerDemarrage(); // sans surveillance démarrée : sans effet
                s.Demarrer();
                var t0 = DateTime.UtcNow;
                while (Volatile.Read(ref releves) < 1 && DateTime.UtcNow - t0 < TimeSpan.FromSeconds(3)) Thread.Sleep(20);
                Assert.Equal(1, Volatile.Read(ref releves));
                var t1 = DateTime.UtcNow;
                s.SignalerDemarrage();
                while (Volatile.Read(ref releves) < 2 && DateTime.UtcNow - t1 < TimeSpan.FromSeconds(4.5)) Thread.Sleep(20);
                var ecoule = DateTime.UtcNow - t1;
                Assert.Equal(2, Volatile.Read(ref releves));
                Assert.True(ecoule < TimeSpan.FromSeconds(4), "relevé après " + ecoule.TotalSeconds + " s");
            }
        }

        private sealed class Inertes : IModeJeuActions
        {
            public ServiceState StopService(string nom) { return default(ServiceState); }
            public void StartService(string nom) { }
            public bool KillProcess(string nom, string chemin) { return true; }
            public void RestartProcess(string chemin) { }
            public string SetPlan(string guid) { return null; }
            public string GetPlan() { return null; }
        }
    }
}
