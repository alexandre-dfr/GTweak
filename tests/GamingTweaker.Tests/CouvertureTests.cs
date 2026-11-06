using System.IO;
using System.Linq;
using GamingTweaker.Core;
using GamingTweaker.Tests.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GamingTweaker.Tests
{
    public class CouvertureTests
    {
        private const string Tete = "| source | entrée | niveau d'origine | décision | raison |\n|---|---|---|---|---|\n";

        [Fact] public void Toute_entree_source_a_une_decision() => Assert.Empty(Couverture.SansDecision());

        [Fact]
        public void Tout_id_cite_existe_dans_le_catalogue() => Assert.Empty(Couverture.IdsInconnus(Catalogue.LoadEmbedded()));

        [Fact] public void Toute_exclusion_a_une_raison() => Assert.Empty(Couverture.ExclusionsSansRaison());

        [Fact] public void Toute_decision_est_exclu_ou_un_id_valide() => Assert.Empty(Couverture.DecisionsInvalides());

        [Fact] public void Chaque_entree_de_l_inventaire_figure_exactement_une_fois() => Assert.Empty(Couverture.EcartsAvecInventaire());

        [Fact] public void Nombre_de_lignes_privacysexy_egal_a_l_inventaire()
        {
            var racine = Path.GetDirectoryName(Path.GetDirectoryName(Couverture.Chemin()));
            var inv = JObject.Parse(File.ReadAllText(Path.Combine(racine, "tools", "inventaire", "inventaire.json")));
            Assert.Equal(((JArray)inv["privacysexy"]).Count, Couverture.Lire().Count(l => l.Source == "privacy.sexy"));
            Assert.Equal(68, Couverture.Lire().Count(l => l.Source == "WinUtil"));
        }

        [Fact] public void Parse_lit_les_lignes_jusqu_au_titre_suivant()
        {
            var l = Couverture.Parse("x\n\n" + Tete + "| WinUtil | A | Essential |  |  |\n| WinUtil | B | Advanced | exclu | r |\n\n## Suite\n| a | b |");
            Assert.Equal(2, l.Count);
            Assert.Equal("", l[0].Decision);
            Assert.Equal("exclu", l[1].Decision);
        }

        [Fact] public void Parse_echoue_sur_une_ligne_vide_dans_le_tableau() =>
            Assert.Throws<InvalidDataException>(() => Couverture.Parse(Tete + "| WinUtil | A | Essential | exclu | r |\n\n| WinUtil | B | Advanced | exclu | r |\n"));

        [Fact] public void Parse_echoue_sur_une_ligne_hors_tableau() =>
            Assert.Throws<InvalidDataException>(() => Couverture.Parse(Tete + "| WinUtil | A | Essential | exclu | r |\ntexte libre\n| WinUtil | B | Advanced | exclu | r |\n"));

        [Fact] public void Parse_echoue_sur_un_nombre_de_colonnes_faux() =>
            Assert.Throws<InvalidDataException>(() => Couverture.Parse(Tete + "| WinUtil | A | exclu | r |\n"));
    }
}
