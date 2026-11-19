using System;
using System.Diagnostics;
using System.IO;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Run\ vidé au démarrage (dossier temporaire des scripts) : des éléments laissés par winget, au nom de l'utilisateur, bloquaient le
    /// contrôle de sécurité. La purge ne suit jamais une jonction.
    /// </summary>
    public class ViderRunTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-run-" + Guid.NewGuid().ToString("N"));
        public ViderRunTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static void Jonction(string lien, string cible)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + lien + "\" \"" + cible + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); Assert.Equal(0, p.ExitCode); }
        }

        [Fact]
        public void Run_est_vide_sans_suivre_les_jonctions()
        {
            var run = Path.Combine(tmp, "Run");
            var winget = Path.Combine(run, "WinGet", "defaultState");
            Directory.CreateDirectory(winget);
            File.WriteAllText(Path.Combine(winget, "log.txt"), "x");
            File.WriteAllText(Path.Combine(run, "run-ancien.ps1"), "x");
            var lecture = Path.Combine(run, "lecture-seule.txt");
            File.WriteAllText(lecture, "x"); File.SetAttributes(lecture, FileAttributes.ReadOnly);
            var cible = Path.Combine(tmp, "Precieux"); Directory.CreateDirectory(cible);
            var precieux = Path.Combine(cible, "a-garder.txt"); File.WriteAllText(precieux, "x");
            Jonction(Path.Combine(run, "WinGet", "lien"), cible);

            DossierProtege.ViderSansSuivre(run);

            Assert.True(Directory.Exists(run), "le dossier Run lui-même est conservé");
            Assert.Empty(Directory.GetFileSystemEntries(run));
            Assert.True(File.Exists(precieux), "la cible d'une jonction n'est jamais touchée");
        }

        [Fact]
        public void Dossier_absent_sans_erreur() => DossierProtege.ViderSansSuivre(Path.Combine(tmp, "Absent"));
    }
}
