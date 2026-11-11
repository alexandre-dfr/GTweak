using System.IO;
using System.Text;

namespace GamingTweaker.Tests.Helpers
{
    /// <summary>Fichiers de tests/GamingTweaker.Tests/fixtures (sorties réelles de winget et de l'accès NVIDIA, capturées sur le PC de développement).</summary>
    public static class Fixtures
    {
        public static string Chemin(string nom) => Path.Combine(PsCommon.Racine(), "tests", "GamingTweaker.Tests", "fixtures", nom);

        public static string Lire(string nom) => File.ReadAllText(Chemin(nom), Encoding.UTF8);
    }
}
