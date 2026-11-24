using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class TailleFenetreTests
    {
        [Fact]
        public void Grand_ecran_garde_les_tailles_du_xaml()
        {
            var r = TailleFenetre.Adapter(1180, 720, 1000, 640, 1920, 1032);
            Assert.Equal(1180, r.Largeur); Assert.Equal(720, r.Hauteur);
            Assert.Equal(1000, r.MinLargeur); Assert.Equal(640, r.MinHauteur);
        }

        [Fact]
        public void Portable_1366x768_a_125_pourcent_tient_dans_la_zone_de_travail()
        {
            // 1366×768 à 125 % = 1092,8×614,4 unités WPF, moins la barre des tâches (≈ 38) : 1092,8×576.
            var r = TailleFenetre.Adapter(1180, 720, 1000, 640, 1092.8, 576);
            Assert.Equal(1092.8, r.Largeur); Assert.Equal(576, r.Hauteur);
            Assert.Equal(1000, r.MinLargeur); Assert.Equal(576, r.MinHauteur);
        }

        [Fact]
        public void Zone_illisible_ne_change_rien()
        {
            var r = TailleFenetre.Adapter(1180, 720, 1000, 640, 0, 0);
            Assert.Equal(1180, r.Largeur); Assert.Equal(640, r.MinHauteur);
        }
    }
}
