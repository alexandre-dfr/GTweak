using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GamingTweaker.Tests.Helpers
{
    /// <summary>Évalue un extrait PowerShell après Common.ps1 (fonctions pures uniquement : aucune modification du système).</summary>
    public static class PsCommon
    {
        public static string Racine() => Path.GetDirectoryName(Path.GetDirectoryName(Couverture.Chemin()));

        public static string CommonPath() => Path.Combine(Racine(), "src", "GamingTweaker", "Assets", "runtime", "Common.ps1");

        public static string CommonText() => File.ReadAllText(CommonPath(), Encoding.UTF8);

        /// <summary>Lignes écrites sur la sortie standard ; échoue si le processus renvoie une erreur.</summary>
        public static List<string> Eval(string snippet)
        {
            var dir = Path.Combine(Path.GetTempPath(), "gt-pscommon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var script = Path.Combine(dir, "t.ps1");
                var texte = "[Console]::OutputEncoding = [Text.Encoding]::UTF8\r\n$ErrorActionPreference = 'Stop'\r\n$GT_JournalPath = ''\r\n"
                          + CommonText() + "\r\n" + snippet + "\r\n";
                File.WriteAllText(script, texte, new UTF8Encoding(true));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var sortie = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new InvalidOperationException("powershell.exe code " + p.ExitCode + " : " + err.Result + sortie);
                    var res = new List<string>();
                    foreach (var l in sortie.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) res.Add(l);
                    return res;
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
