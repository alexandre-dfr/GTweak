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
                var psi = Core.PowerShellLanceur.Creer(Core.PowerShellLanceur.ArgumentsEncodes(texte));
                // Délai dépassé ou annulation : PowerShell et les processus qu'il a lancés sont arrêtés (ProcessusBorne).
                return Core.ProcessusBorne.Executer(psi, timeoutMs, ct, "PowerShell").Sortie;
            }, ct);
        }
    }
}
