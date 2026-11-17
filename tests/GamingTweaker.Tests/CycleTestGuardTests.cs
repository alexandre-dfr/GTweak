using System.IO;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    public class CycleTestGuardTests
    {
        [Fact]
        public void Cycle_refuse_hors_sandbox()
        {
            var sortie = Path.Combine(Path.GetTempPath(), "gt-cycle-garde-" + System.Guid.NewGuid().ToString("N") + ".json");
            Assert.Equal(2, CycleTest.Run(sortie, user: "adu"));
            Assert.False(File.Exists(sortie)); // refus : rien n'est exécuté ni écrit
        }

        // Une option qui écrit deux fois la même valeur : seule la première entrée porte la valeur d'origine.
        [Fact]
        public void Registre_compare_a_la_premiere_entree_de_chaque_cible()
        {
            var cible = @"HKCU:\Software\X|Valeur";
            var entrees = new[]
            {
                new JournalEntry { Item = "opt.x", Kind = "registry", Target = cible, Existed = true, Before = new Newtonsoft.Json.Linq.JValue(1), After = new Newtonsoft.Json.Linq.JValue(2) },
                new JournalEntry { Item = "opt.x", Kind = "registry", Target = cible, Existed = true, Before = new Newtonsoft.Json.Linq.JValue(2), After = new Newtonsoft.Json.Linq.JValue(3) },
            };
            Assert.Null(CycleTest.VerifierRegistre(entrees, _ => "1"));
            Assert.NotNull(CycleTest.VerifierRegistre(entrees, _ => "2"));
        }

        [Fact]
        public void Registre_absent_a_l_origine_doit_redevenir_absent()
        {
            var e = new[] { new JournalEntry { Item = "opt.x", Kind = "registry", Target = @"HKLM:\Software\X|V", Existed = false, After = new Newtonsoft.Json.Linq.JValue(1) } };
            Assert.Null(CycleTest.VerifierRegistre(e, _ => UndoService.Absent));
            Assert.NotNull(CycleTest.VerifierRegistre(e, _ => "1"));
        }

        [Fact]
        public void Le_cycle_annule_meme_apres_un_probleme_a_l_application()
        {
            var src = File.ReadAllText(Path.Combine(Helpers.PsCommon.Racine(), "src", "GamingTweaker", "Core", "CycleTest.cs"));
            Assert.Contains("bool annuler = entrees.Count > 0", src);
        }

        [Fact] public void Cycle_accepte_le_compte_sandbox() => Assert.True(CycleTest.IsSandbox("WDAGUtilityAccount"));
        [Fact] public void Cycle_compte_sandbox_insensible_a_la_casse() => Assert.True(CycleTest.IsSandbox("wdagutilityaccount"));
        [Theory] [InlineData("adu")] [InlineData("")] [InlineData(null)] [InlineData("Administrator")]
        public void Autres_comptes_refuses(string u) => Assert.False(CycleTest.IsSandbox(u));
    }
}
