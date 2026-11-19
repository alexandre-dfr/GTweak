using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class CompteurImagesTests
    {
        private static CompteurImages Regulier(double fps, double duree, double debut = 100)
        {
            var c = new CompteurImages();
            int n = (int)Math.Round(fps * duree);
            for (int i = 0; i <= n; i++) c.Ajouter(debut + i / fps);
            return c;
        }

        [Fact]
        public void Images_regulieres_donnent_les_fps_et_le_temps_d_image()
        {
            var c = Regulier(144, 3);
            var m = c.Mesurer(103.0);
            Assert.NotNull(m);
            Assert.InRange(m.Fps, 143.5, 144.5);
            Assert.InRange(m.TempsImageMs, 6.9, 7.0);
            Assert.InRange(m.UnPourcentBas.Value, 143.5, 144.5);
        }

        [Fact]
        public void Un_pourcent_bas_capte_les_saccades()
        {
            var c = new CompteurImages();
            double t = 100;
            // 600 images à 100 FPS, dont une sur 50 (2 %) qui prend 50 ms : le 1 % bas tombe à 20.
            for (int i = 0; i < 600; i++) { t += i % 50 == 0 ? 0.050 : 0.010; c.Ajouter(t); }
            var m = c.Mesurer(t);
            Assert.NotNull(m.UnPourcentBas);
            Assert.InRange(m.UnPourcentBas.Value, 19.9, 20.1);
            Assert.InRange(m.Fps, 80, 100);
        }

        [Fact]
        public void Pas_assez_d_images_pas_de_un_pourcent_bas()
        {
            var m = Regulier(30, 2).Mesurer(102.0);
            Assert.NotNull(m);
            Assert.Null(m.UnPourcentBas);
        }

        [Fact]
        public void Processus_silencieux_ne_donne_rien()
        {
            var c = Regulier(60, 2);
            Assert.NotNull(c.Mesurer(102.0));
            Assert.NotNull(c.Mesurer(104.0)); // paquet ETW pas encore arrivé : la mesure reste
            Assert.Null(c.Mesurer(105.0));
        }

        [Fact]
        public void Images_recues_par_paquets_et_dans_le_desordre_donnent_des_fps_stables()
        {
            // 144 FPS pendant 3 s, livrées par paquets d'une seconde, dont une partie en retard (autre processeur) :
            // la mesure ne doit ni chuter entre deux paquets, ni perdre les images en retard.
            var c = new CompteurImages();
            var pas = 1.0 / 144;
            for (int paquet = 0; paquet < 3; paquet++)
            {
                double debut = 100 + paquet, reception = debut + 1.0;
                var lot = Enumerable.Range(0, 144).Select(i => debut + i * pas).ToList();
                foreach (var t in lot.Where((x, i) => i % 2 == 0)) c.Ajouter(t, reception);
                foreach (var t in lot.Where((x, i) => i % 2 == 1)) c.Ajouter(t, reception); // en retard sur les autres
                // Juste avant le paquet suivant (0,9 s après réception), les FPS restent ceux de la dernière seconde reçue.
                var m = c.Mesurer(reception + 0.9);
                Assert.NotNull(m);
                Assert.InRange(m.Fps, 143, 145);
            }
        }

        [Fact]
        public void Instants_anciens_purges_et_instant_en_arriere_ignore()
        {
            var c = Regulier(60, 20);
            Assert.InRange(c.Nombre, 595, 605); // ~10 s gardées sur 20
            var n = c.Nombre;
            c.Ajouter(50);
            Assert.Equal(n, c.Nombre);
        }
    }

    public class CompteurSystemeTests
    {
        [Fact]
        public void Charge_cpu_depuis_get_system_times()
        {
            // 1000 de temps total (noyau 600 dont 250 inactif, utilisateur 400) : 75 % occupé.
            Assert.Equal(75, CompteurSysteme.ChargeCpu(0, 0, 0, 250, 600, 400).Value, 3);
            Assert.Null(CompteurSysteme.ChargeCpu(10, 10, 10, 10, 10, 10));
        }

        [Fact]
        public void Charge_gpu_somme_par_moteur_puis_le_plus_charge()
        {
            var l = new List<KeyValuePair<string, double>>
            {
                new KeyValuePair<string, double>("pid_100_luid_0x00000000_0x0000D1A2_phys_0_eng_0_engtype_3D", 60),
                new KeyValuePair<string, double>("pid_200_luid_0x00000000_0x0000D1A2_phys_0_eng_0_engtype_3D", 25),
                new KeyValuePair<string, double>("pid_100_luid_0x00000000_0x0000D1A2_phys_0_eng_5_engtype_Copy", 99),
                new KeyValuePair<string, double>("pid_300_luid_0x00000000_0x0000BEEF_phys_0_eng_0_engtype_3D", 40),
            };
            Assert.Equal(85, CompteurSysteme.ChargeGpu(l).Value, 3);
        }

        [Fact]
        public void Charge_gpu_plafonnee_et_inconnue_sans_moteur_3d()
        {
            var l = new[] { "pid_1", "pid_2" }.Select(p => new KeyValuePair<string, double>(p + "_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 70)).ToList();
            Assert.Equal(100, CompteurSysteme.ChargeGpu(l).Value, 3);
            Assert.Null(CompteurSysteme.ChargeGpu(new[] { new KeyValuePair<string, double>("_Total", 50) }));
        }

        [Fact]
        public void Releve_reel_cpu_et_ram()
        {
            using (var s = new CompteurSysteme())
            {
                s.Relever(gpu: false);
                System.Threading.Thread.Sleep(200);
                var c = s.Relever(gpu: false);
                Assert.InRange(c.Cpu.Value, 0, 100);
                Assert.InRange(c.Ram.Value, 1, 100);
                Assert.True(c.RamTotaleGo > 0.5);
            }
        }
    }

    public class CompteurConfigTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-compteur-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        [Fact]
        public void Desactive_par_defaut()
        {
            var c = CompteurConfig.Charger(tmp);
            Assert.False(c.Actif);
            Assert.Equal(CoinEcran.HautDroite, c.Position);
        }

        [Fact]
        public void Enregistrer_puis_charger()
        {
            var c = new CompteurConfig { Actif = true, Position = CoinEcran.BasGauche, Couleur = "#ff4d6d", Taille = TailleCompteur.Grande, Gpu = false, DureeCourbe = 60 };
            c.Enregistrer(tmp);
            var l = CompteurConfig.Charger(tmp);
            Assert.True(l.Actif);
            Assert.Equal(CoinEcran.BasGauche, l.Position);
            Assert.Equal("#FF4D6D", l.Couleur);
            Assert.Equal(TailleCompteur.Grande, l.Taille);
            Assert.False(l.Gpu);
            Assert.Equal(60, l.DureeCourbe);
        }

        [Fact]
        public void Valeurs_invalides_ramenees_aux_valeurs_par_defaut()
        {
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, CompteurConfig.NomFichier), "{ \"couleur\": \"red; x\", \"opaciteFond\": 7, \"dureeCourbe\": 999 }");
            var c = CompteurConfig.Charger(tmp);
            Assert.Equal(CompteurConfig.CouleurParDefaut, c.Couleur);
            Assert.Equal(1.0, c.OpaciteFond);
            Assert.Equal(30, c.DureeCourbe);
        }

        [Fact]
        public void Fichier_illisible_donne_les_valeurs_par_defaut()
        {
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, CompteurConfig.NomFichier), "pas du json");
            Assert.False(CompteurConfig.Charger(tmp).Actif);
        }
    }

    public class CompteurViewModelTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-compteurvm-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private class FausseSource : ISourceCompteur
        {
            public int Demarrages, Arrets; public bool Cpu, Gpu, Ram;
            public ReleveCompteur Releve = new ReleveCompteur();
            public void Demarrer() { Demarrages++; }
            public void Arreter() { Arrets++; }
            public void Choisir(bool cpu, bool gpu, bool ram) { Cpu = cpu; Gpu = gpu; Ram = ram; }
            public ReleveCompteur Lire() { return Releve; }
            public string Erreur { get { return null; } }
        }

        [Fact]
        public void Activer_demarre_la_source_desactiver_l_arrete()
        {
            var src = new FausseSource();
            var vm = new CompteurViewModel(tmp, src, new CompteurConfig());
            int changements = 0; vm.AffichageChange += () => changements++;
            Assert.Equal(0, src.Demarrages);
            vm.Actif = true;
            Assert.Equal(1, src.Demarrages);
            vm.Basculer();
            Assert.False(vm.Actif);
            Assert.Equal(1, src.Arrets);
            Assert.Equal(2, changements);
            vm.Enregistrer();
            Assert.False(CompteurConfig.Charger(tmp).Actif);
        }

        [Fact]
        public void Choix_cpu_gpu_ram_transmis_a_la_source()
        {
            var src = new FausseSource();
            var vm = new CompteurViewModel(tmp, src, new CompteurConfig { Actif = true });
            Assert.True(src.Gpu);
            vm.Gpu = false;
            Assert.False(src.Gpu);
            Assert.True(vm.AfficherCharge);
            vm.Cpu = false; vm.Ram = false;
            Assert.False(vm.AfficherCharge);
        }

        [Fact]
        public void Textes_affiches()
        {
            var vm = new CompteurViewModel(tmp, new FausseSource(), new CompteurConfig());
            vm.Rafraichir(new ReleveCompteur
            {
                Processus = "jeu",
                Images = new MesureImages { Fps = 143.6, TempsImageMs = 6.96, UnPourcentBas = 97.2 },
                Charge = new ChargeSysteme { Cpu = 23.4, Gpu = 87.6, Ram = 54.2, RamUtiliseeGo = 17.3, RamTotaleGo = 32 },
            });
            Assert.Equal("144", vm.TexteFps);
            Assert.Equal("  7,0 ms  ·  1% bas  97", vm.TexteDetails);
            Assert.Equal("CPU  23 %", vm.TexteCpu);
            Assert.Equal("GPU  88 %", vm.TexteGpu);
            Assert.StartsWith("RAM  54 %", vm.TexteRam);
            Assert.Equal("jeu", vm.TexteProcessus);

            vm.Rafraichir(new ReleveCompteur { Processus = "explorer" });
            Assert.Equal("—", vm.TexteFps);
            Assert.Equal("GPU   —  ", vm.TexteGpu);
            Assert.Contains("aucune image", vm.TexteProcessus);
        }

        [Fact]
        public void Placement_affiche_le_compteur_meme_desactive_et_le_deplacement_le_met_en_position_libre()
        {
            var src = new FausseSource();
            var vm = new CompteurViewModel(tmp, src, new CompteurConfig());
            Assert.False(vm.Visible);
            vm.ModePlacement = true;
            Assert.True(vm.Visible);
            Assert.Equal(1, src.Demarrages);
            vm.DeplaceA(1920.4, 300.6);
            Assert.Equal(CoinEcran.Libre, vm.Position);
            vm.ModePlacement = false;
            Assert.False(vm.Visible);
            Assert.Equal(1, src.Arrets);
            vm.Enregistrer();
            var c = CompteurConfig.Charger(tmp);
            Assert.Equal(CoinEcran.Libre, c.Position);
            Assert.Equal(1920, c.X);
            Assert.Equal(301, c.Y);
        }

        [Fact]
        public void Fermer_le_placement_laisse_tourner_un_compteur_actif()
        {
            var src = new FausseSource();
            var vm = new CompteurViewModel(tmp, src, new CompteurConfig { Actif = true });
            vm.ModePlacement = true; vm.ModePlacement = false;
            Assert.True(vm.Visible);
            Assert.Equal(0, src.Arrets);
        }

        [Fact]
        public void Couleur_invalide_ignoree()
        {
            var vm = new CompteurViewModel(tmp, new FausseSource(), new CompteurConfig());
            vm.Couleur = "#123abc";
            Assert.Equal("#123ABC", vm.Couleur);
            vm.Couleur = "rouge";
            Assert.Equal("#123ABC", vm.Couleur);
        }

        [Fact]
        public void Courbe_recents_a_droite_et_echelle_minimale_60()
        {
            var pc = CompteurViewModel.CalculerCourbe(new[] { 30.0, double.NaN }, 5);
            Assert.Equal(2, pc.Count);
            Assert.Equal(CompteurViewModel.LargeurCourbe, pc[1].X, 3);
            Assert.Equal(CompteurViewModel.HauteurCourbe, pc[1].Y, 3); // sans image : en bas
            Assert.Equal(CompteurViewModel.HauteurCourbe / 2, pc[0].Y, 3); // 30 sur une échelle de 60
        }
    }

    /// <summary>Session ETW réelle (droits administrateur : la CI GitHub les a).</summary>
    public class CompteurEtwTests
    {
        [Fact]
        public void Session_demarre_et_s_arrete_proprement()
        {
            if (!ElevationCheck.EstAdministrateur()) return;
            using (var e = new CompteurEtw())
            {
                Assert.True(e.Demarrer(), e.Erreur);
                Assert.Null(e.Mesurer(4));
                Assert.True(e.Maintenant > 0);
            }
            // Relançable après arrêt (même nom de session).
            using (var e = new CompteurEtw()) Assert.True(e.Demarrer(), e.Erreur);
        }
    }
}
