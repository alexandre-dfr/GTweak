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

        [Fact] public void Cycle_accepte_le_compte_sandbox() => Assert.True(CycleTest.IsSandbox("WDAGUtilityAccount"));
        [Fact] public void Cycle_compte_sandbox_insensible_a_la_casse() => Assert.True(CycleTest.IsSandbox("wdagutilityaccount"));
        [Theory] [InlineData("adu")] [InlineData("")] [InlineData(null)] [InlineData("Administrator")]
        public void Autres_comptes_refuses(string u) => Assert.False(CycleTest.IsSandbox(u));
    }
}
