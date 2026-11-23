using System;
using System.Diagnostics;
using System.IO;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Retrait de la marque Internet (Zone.Identifier) qui déclenche SmartScreen sur un exe téléchargé.</summary>
    public class MarqueInternetTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "gt-motw-" + Guid.NewGuid().ToString("N"));

        public MarqueInternetTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch (Exception) { } }

        /// <summary>Pose la marque comme un navigateur (zone 3 = Internet) ; FileStream de .NET Framework refuse « : » dans un chemin.</summary>
        private static void Marquer(string fichier)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c echo [ZoneTransfer]>\"" + fichier + ":Zone.Identifier\" & echo ZoneId=3>>\"" + fichier + ":Zone.Identifier\"")
            { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi)) p.WaitForExit();
        }

        [Fact]
        public void La_marque_est_retiree_et_le_fichier_reste_intact()
        {
            var f = Path.Combine(dir, "GamingTweaker.exe");
            File.WriteAllText(f, "contenu");
            Marquer(f);
            Assert.True(MarqueInternet.Presente(f));
            Assert.True(MarqueInternet.Retirer(f));
            Assert.False(MarqueInternet.Presente(f));
            Assert.Equal("contenu", File.ReadAllText(f));
            Assert.False(MarqueInternet.Retirer(f));   // déjà retirée : rien à faire
        }

        [Fact]
        public void Un_fichier_sans_marque_ou_absent_n_est_pas_touche()
        {
            var f = Path.Combine(dir, "local.exe");
            File.WriteAllText(f, "x");
            Assert.False(MarqueInternet.Presente(f));
            Assert.False(MarqueInternet.Retirer(f));
            Assert.False(MarqueInternet.Presente(Path.Combine(dir, "absent.exe")));
            Assert.False(MarqueInternet.Presente(null));
        }
    }
}
