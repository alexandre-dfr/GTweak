using System.Linq;
using GamingTweaker.Core;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    public class CouvertureTests
    {
        [Fact] public void Toute_entree_source_a_une_decision() => Assert.Empty(Couverture.SansDecision());

        [Fact(Skip = "réactivé après la Task 11")]
        public void Tout_id_cite_existe_dans_le_catalogue() => Assert.Empty(Couverture.IdsInconnus(Catalogue.LoadEmbedded()));

        [Fact] public void Toute_exclusion_a_une_raison() => Assert.Empty(Couverture.ExclusionsSansRaison());

        [Fact] public void Toute_decision_est_exclu_ou_un_id_valide() => Assert.Empty(Couverture.DecisionsInvalides());

        [Fact] public void Toutes_les_entrees_winutil_sont_presentes() =>
            Assert.Equal(68, Couverture.Lire().Count(l => l.Source == "WinUtil"));

        [Fact] public void Parse_lit_une_ligne_et_detecte_une_decision_vide()
        {
            var md = "x\n\n| source | entrée | niveau d'origine | décision | raison |\n|---|---|---|---|---|\n| WinUtil | A | Essential |  |  |\n| WinUtil | B | Advanced | exclu | r |\n\nfin";
            var l = Couverture.Parse(md);
            Assert.Equal(2, l.Count);
            Assert.Equal("", l[0].Decision);
            Assert.Equal("exclu", l[1].Decision);
        }
    }
}
