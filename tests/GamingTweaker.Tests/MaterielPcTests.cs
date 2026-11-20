using System.Linq;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    public class MaterielPcTests
    {
        private const double Go = 1073741824d;

        [Theory]
        [InlineData("Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz", "Intel Core i7-12700K")]
        [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor           ", "AMD Ryzen 7 7800X3D")]
        [InlineData("13th Gen Intel(R) Core(TM) i5-13600KF", "13th Gen Intel Core i5-13600KF")]
        [InlineData("   ", null)]
        public void Nom_du_processeur_nettoye(string brut, string attendu)
        {
            Assert.Equal(attendu, MaterielPc.NettoyerNomCpu(brut));
        }

        [Fact]
        public void Carte_dediee_choisie_avant_l_integree_et_les_adaptateurs_virtuels()
        {
            var cartes = new[]
            {
                new CarteGraphique { Nom = "Microsoft Basic Display Adapter" },
                new CarteGraphique { Nom = "Intel(R) UHD Graphics 770", Memoire = (long)(0.1 * Go) },
                new CarteGraphique { Nom = "NVIDIA GeForce RTX 4070", Memoire = (long)(12 * Go), Pilote = "32.0.15.6094" },
                new CarteGraphique { Nom = "Parsec Virtual Display Adapter" },
            };
            Assert.Equal("NVIDIA GeForce RTX 4070", MaterielPc.ChoisirCarte(cartes).Nom);
            var t = MaterielPc.TuileGraphique(cartes);
            Assert.Equal("NVIDIA GeForce RTX 4070", t.Valeur);
            Assert.Equal("12 Go de mémoire vidéo · pilote 32.0.15.6094 · + Intel(R) UHD Graphics 770", t.Detail);
            Assert.False(t.AJauge);
        }

        [Fact]
        public void Sans_carte_reelle_la_carte_graphique_est_inconnue()
        {
            Assert.Equal(MaterielPc.Inconnu, MaterielPc.TuileGraphique(new[] { new CarteGraphique { Nom = "Microsoft Basic Render Driver" } }).Valeur);
            Assert.Null(MaterielPc.ChoisirCarte(null));
        }

        [Fact]
        public void Memoire_totale_type_vitesse_et_occupation()
        {
            var t = MaterielPc.TuileMemoire(32 * Go, "DDR5", 6000, 2, 8 * Go);
            Assert.Equal("32 Go DDR5", t.Valeur);
            Assert.Equal("6000 MT/s · 2 barrettes · 8 Go libres", t.Detail);
            Assert.True(t.AJauge);
            Assert.Equal(0.75, t.ValeurJauge, 3);
            Assert.Equal(MaterielPc.Inconnu, MaterielPc.TuileMemoire(0, null, 0, 0, 0).Valeur);
        }

        [Theory]
        [InlineData(4, 17, "SSD NVMe")]
        [InlineData(4, 11, "SSD")]
        [InlineData(3, 11, "HDD")]
        [InlineData(0, 11, null)]
        public void Type_du_disque(int media, int bus, string attendu)
        {
            Assert.Equal(attendu, MaterielPc.TypeDisque(media, bus));
        }

        [Fact]
        public void Disque_systeme_avec_espace_libre_et_jauge()
        {
            var t = MaterielPc.TuileDisque(@"C:\", "SSD NVMe", "Samsung SSD 990 PRO 1TB", 1000204886016d, 412 * Go);
            Assert.Equal("C: · SSD NVMe", t.Valeur);
            Assert.Equal("412 Go libres sur 932 Go · Samsung SSD 990 PRO 1TB", t.Detail);
            Assert.InRange(t.ValeurJauge, 0.55, 0.56);
            Assert.Equal("C:", MaterielPc.TuileDisque(@"C:\", null, null, 500 * Go, 100 * Go).Valeur);
        }

        [Fact]
        public void Tailles_en_go_et_en_to()
        {
            Assert.Equal("8,5 Go", MaterielPc.Taille(8.5 * Go));
            Assert.Equal("932 Go", MaterielPc.Taille(1000204886016d));
            Assert.Equal("2 To", MaterielPc.Taille(2 * 1024 * Go));
        }

        [Fact]
        public void Disque_presque_plein_signale_comme_le_diagnostic()
        {
            var plein = MaterielPc.TuileDisque(@"C:\", "SSD", null, 500 * Go, 15 * Go);
            Assert.True(plein.AAlerte);
            Assert.Contains("15 Go libres", plein.Alerte);
            Assert.True(MaterielPc.DisquePresquePlein(2000 * Go, 150 * Go));    // plus de 20 Go mais moins de 10 %
            Assert.False(MaterielPc.TuileDisque(@"C:\", "SSD", null, 500 * Go, 200 * Go).AAlerte);
        }

        [Fact]
        public void Chaque_tuile_ouvre_sa_section_d_informations()
        {
            Assert.Equal(new[] { "CPU", "GPU", "RAM", "Disques" }, MaterielPc.EnLecture().Select(t => t.Section).ToArray());
            Assert.Equal("Disques", MaterielPc.SectionDe("Disque système"));
        }

        [Fact]
        public void Tuiles_d_attente_dans_l_ordre()
        {
            Assert.Equal(new[] { "Processeur", "Carte graphique", "Mémoire", "Disque système" }, MaterielPc.EnLecture().Select(t => t.Libelle).ToArray());
        }
    }
}
