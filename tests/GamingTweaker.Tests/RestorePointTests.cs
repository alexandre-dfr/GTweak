using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    public class RestorePointTests
    {
        [Fact]
        public void Succes_avec_nouveau_numero_cree_le_point()
        {
            var r = RestorePoint.Interpreter(0, 41, 42);
            Assert.True(r.Created);
        }

        [Fact]
        public void Succes_sans_nouveau_numero_signale_la_limite()
        {
            var r = RestorePoint.Interpreter(0, 42, 42);
            Assert.False(r.Created);
            Assert.Contains("24 h", r.Message);
        }

        [Theory]
        [InlineData(1058u)]
        [InlineData(0x80070422u)]
        public void Service_desactive_explique_la_protection(uint code)
        {
            var r = RestorePoint.Interpreter(code, 0, 0);
            Assert.False(r.Created);
            Assert.Contains("protection du système est désactivée", r.Message);
        }

        [Fact]
        public void Code_inconnu_donne_le_code()
        {
            var r = RestorePoint.Interpreter(5, 3, 4);
            Assert.False(r.Created);
            Assert.Contains("code 5", r.Message);
        }

        [Fact]
        public void Delai_par_defaut_depasse_trois_minutes()
        {
            Assert.True(RestorePoint.Delai.TotalMinutes >= 10);
        }
    }
}
