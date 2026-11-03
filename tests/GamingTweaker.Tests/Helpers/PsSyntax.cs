using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using GamingTweaker.Core;

namespace GamingTweaker.Tests.Helpers
{
    /// <summary>Vérifie la syntaxe des scripts du catalogue avec le parseur PowerShell (un seul processus).</summary>
    public static class PsSyntax
    {
        private const string Checker = @"
$dir = $PSScriptRoot
foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $dir 'index.txt'), [System.Text.Encoding]::UTF8)) {
    if (-not $line) { continue }
    $parts = $line.Split('|')
    $tokens = $null; $errs = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $dir ($parts[0] + '.ps1')), [ref]$tokens, [ref]$errs)
    foreach ($e in $errs) { [Console]::Out.WriteLine($parts[1] + ' : ' + $e.Message + ' (ligne ' + $e.Extent.StartLineNumber + ')') }
}
";

        /// <summary>Renvoie une liste de ""id/type : message"" ; vide si tout est valide.</summary>
        public static List<string> CheckAll(IEnumerable<CatalogItem> items)
        {
            var dir = Path.Combine(Path.GetTempPath(), "gt-pssyntax-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var bom = new UTF8Encoding(true);
                var index = new List<string>();
                var n = 0;
                foreach (var it in items)
                {
                    var scripts = new[]
                    {
                        new KeyValuePair<string, string>("detect", it.Detect),
                        new KeyValuePair<string, string>("apply", it.Apply),
                        new KeyValuePair<string, string>("revert", it.Revert),
                        new KeyValuePair<string, string>("estimate", it.Estimate),
                    };
                    foreach (var s in scripts)
                    {
                        if (s.Value == null) continue;
                        n++;
                        File.WriteAllText(Path.Combine(dir, n + ".ps1"), s.Value, bom);
                        index.Add(n + "|" + it.Id + "/" + s.Key);
                    }
                }
                File.WriteAllLines(Path.Combine(dir, "index.txt"), index, bom);
                var runner = Path.Combine(dir, "check.ps1");
                File.WriteAllText(runner, Checker, bom);

                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + runner + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                var result = new List<string>();
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    foreach (var l in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) result.Add(l);
                    var e = err.Result.Trim();
                    if (p.ExitCode != 0 || e.Length > 0) result.Add("powershell.exe : code " + p.ExitCode + " " + e);
                }
                return result;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
