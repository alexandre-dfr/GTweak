using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GamingTweaker.Core
{
    public enum DetectState { Applied, NotApplied, Unknown, Detecting }

    public static class Detection
    {
        public static Task<Dictionary<string, DetectState>> RunAsync(IEnumerable<CatalogItem> items)
            => RunAsync(items, TimeSpan.FromSeconds(120));

        /// <summary>Toutes les détections dans UN processus PowerShell. Absente, en erreur ou en délai : Unknown.</summary>
        public static Task<Dictionary<string, DetectState>> RunAsync(IEnumerable<CatalogItem> items, TimeSpan timeout)
        {
            var list = items.ToList();
            return Task.Run(() => Run(list, timeout));
        }

        private static Dictionary<string, DetectState> Run(List<CatalogItem> items, TimeSpan timeout)
        {
            var res = items.ToDictionary(i => i.Id, i => DetectState.Unknown);
            var withScript = items.Where(i => !string.IsNullOrWhiteSpace(i.Detect)).ToList();
            if (withScript.Count == 0) return res;

            var file = Path.Combine(Path.GetTempPath(), "gt-detect-" + Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                File.WriteAllText(file, BuildScript(withScript), new UTF8Encoding(true));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + file + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null || !e.Data.StartsWith("##GT|detect|", StringComparison.Ordinal)) return;
                        var parts = e.Data.Split(new[] { '|' }, 4);
                        if (parts.Length < 4) return;
                        lock (res)
                        {
                            if (!res.ContainsKey(parts[2])) return;
                            res[parts[2]] = parts[3] == "Applied" ? DetectState.Applied
                                          : parts[3] == "NotApplied" ? DetectState.NotApplied : DetectState.Unknown;
                        }
                    };
                    p.ErrorDataReceived += (s, e) => { };
                    p.Start();
                    p.StandardInput.Close();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit((int)timeout.TotalMilliseconds)) Kill(p);
                    else p.WaitForExit(); // vide les flux asynchrones
                }
            }
            catch (Exception) { /* échec de lancement : tout reste Unknown */ }
            finally { try { File.Delete(file); } catch { } }
            lock (res) return new Dictionary<string, DetectState>(res);
        }

        private static string BuildScript(List<CatalogItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(ScriptBuilder.Header("", null));
            foreach (var it in items)
            {
                var id = ScriptBuilder.Quote(it.Id);
                sb.Append("try {\r\n$gtOut = @(& {\r\n").Append(it.Detect).Append("\r\n})\r\n");
                sb.Append("$gtLast = if ($gtOut.Count -gt 0) { [string]$gtOut[$gtOut.Count - 1] } else { '' }\r\n");
                sb.Append("if ($gtLast.Trim() -eq 'Applied') { Write-GTEvent 'detect' ").Append(id).Append(" 'Applied' }\r\n");
                sb.Append("elseif ($gtLast.Trim() -eq 'NotApplied') { Write-GTEvent 'detect' ").Append(id).Append(" 'NotApplied' }\r\n");
                sb.Append("else { Write-GTEvent 'detect' ").Append(id).Append(" 'Unknown' }\r\n");
                sb.Append("} catch { Write-GTEvent 'detect' ").Append(id).Append(" 'Unknown' }\r\n");
            }
            return sb.ToString();
        }

        private static void Kill(Process p)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var k = Process.Start(psi)) { k.WaitForExit(10000); }
            }
            catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(5000); } catch { }
        }
    }
}
