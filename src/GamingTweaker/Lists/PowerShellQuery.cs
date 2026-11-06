using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamingTweaker.Lists
{
    /// <summary>Lecture seule : exécute un court script PowerShell et renvoie sa sortie standard (UTF-8). Aucun profil, script passé en -EncodedCommand.</summary>
    public static class PowerShellQuery
    {
        public static Task<string> RunAsync(string script, CancellationToken ct, int timeoutMs = 120000)
        {
            return Task.Run(() =>
            {
                var texte = "[Console]::OutputEncoding = [Text.Encoding]::UTF8\r\n$ProgressPreference = 'SilentlyContinue'\r\n" + script;
                var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(texte));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var sortie = p.StandardOutput.ReadToEndAsync();
                    using (ct.Register(() => { try { p.Kill(); } catch (Exception) { } }))
                    {
                        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } throw new TimeoutException("PowerShell n'a pas répondu à temps."); }
                    }
                    ct.ThrowIfCancellationRequested();
                    return sortie.Result;
                }
            }, ct);
        }
    }
}
