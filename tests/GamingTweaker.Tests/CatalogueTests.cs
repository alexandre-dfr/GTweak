using System.Linq;
using GamingTweaker.Core;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    public class CatalogueTests
    {
        private static CatalogItem Ok(string id, string revert = "x") => new CatalogItem
        {
            Id = id, Menu = "Optimisation", Groupe = "G", Titre = "T", Risque = RiskLevel.Sur,
            Reversible = true, Detect = "d", Apply = "a", Revert = revert,
        };

        [Fact] public void Catalogue_integre_est_valide() => Assert.Empty(Catalogue.Validate(Catalogue.LoadEmbedded()));
        [Fact] public void Id_en_double_detecte() => Assert.Contains(Catalogue.Validate(new[] { Ok("a"), Ok("a") }), e => e.Contains("a"));
        [Fact] public void Reversible_sans_revert_detecte() => Assert.NotEmpty(Catalogue.Validate(new[] { Ok("a", revert: null) }));

        [Fact] public void Non_reversible_avec_revert_detecte()
        {
            var it = Ok("a"); it.Reversible = false;
            Assert.Contains(Catalogue.Validate(new[] { it }), e => e.Contains("a"));
        }

        [Fact] public void Champs_obligatoires_et_delai_detectes()
        {
            var it = Ok("a"); it.Titre = ""; it.Detect = null; it.DelaiSec = 0;
            Assert.True(Catalogue.Validate(new[] { it }).Count >= 3);
        }

        [Fact] public void Valeurs_par_defaut_et_exemple_charge()
        {
            var it = Catalogue.LoadEmbedded().Single(i => i.Id == "opt.mouse.precision-off");
            Assert.True(it.Testable); Assert.True(it.Hkcu); Assert.Equal(300, it.DelaiSec);
            Assert.Equal(RiskLevel.Sur, it.Risque);
            Assert.Equal("Désactiver la précision du pointeur", it.Titre);
            Assert.Null(it.Estimate);
            Assert.Contains("MouseSpeed", it.Detect);
            Assert.Equal(new[] { "10", "11" }, it.Compatibilite.Windows);
        }

        [Fact] public void Tous_les_scripts_sont_syntaxiquement_valides() => Assert.Empty(PsSyntax.CheckAll(Catalogue.LoadEmbedded()));

        [Fact] public void PsSyntax_detecte_une_erreur()
        {
            var it = Ok("bad"); it.Apply = "if (";
            Assert.Contains(PsSyntax.CheckAll(new[] { it }), e => e.StartsWith("bad/apply"));
        }
    }
}
