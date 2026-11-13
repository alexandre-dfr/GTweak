using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Estimation de la taille à libérer (aperçu du menu Nettoyage). Un seul processus PowerShell lit les scripts « estimate »
    /// (lecture seule, ils renvoient un nombre d'octets). Script absent, en erreur, illisible ou en délai : null (affiché « — »).
    /// </summary>
    public static class Estimation
    {
        public static readonly TimeSpan DelaiGlobal = TimeSpan.FromSeconds(60);

        public static Task<Dictionary<string, long?>> RunAsync(IEnumerable<CatalogItem> items) => RunAsync(items, DelaiGlobal);

        public static Task<Dictionary<string, long?>> RunAsync(IEnumerable<CatalogItem> items, TimeSpan timeout)
        {
            var list = items.ToList();
            return Task.Run(() => Run(list, timeout));
        }

        private static Dictionary<string, long?> Run(List<CatalogItem> items, TimeSpan timeout)
        {
            var res = new Dictionary<string, long?>();
            foreach (var i in items) res[i.Id] = null;
            // Les scripts marqués « # lent » passent en dernier : le délai global ne doit pas priver les autres options de leur estimation.
            var withScript = items.Where(i => !string.IsNullOrWhiteSpace(i.Estimate))
                                  .OrderBy(i => i.Estimate.TrimStart().StartsWith("# lent", StringComparison.Ordinal)).ToList();
            if (withScript.Count == 0) return res;

            ScriptVerrouille script = null;
            try
            {
                // Dossier Run protégé ; script verrouillé jusqu'à la fin de PowerShell.
                script = PowerShellLanceur.EcrireScript("estimate", BuildScript(withScript));
                var psi = PowerShellLanceur.Creer(PowerShellLanceur.ArgumentsFichier(script.Chemin));
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null || !e.Data.StartsWith("##GT|estimate|", StringComparison.Ordinal)) return;
                        var parts = e.Data.Split(new[] { '|' }, 4);
                        if (parts.Length < 4) return;
                        long v;
                        if (!long.TryParse(parts[3].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out v)) return;
                        lock (res) { if (res.ContainsKey(parts[2])) res[parts[2]] = v; }
                    };
                    p.ErrorDataReceived += (s, e) => { };
                    p.Start();
                    p.StandardInput.Close();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit((int)timeout.TotalMilliseconds)) Detection.Kill(p);
                    else PowerShellLanceur.AttendreFlux(p);
                }
            }
            catch (Exception) { /* échec de lancement : tout reste null */ }
            finally { if (script != null) script.Dispose(); }
            lock (res) return new Dictionary<string, long?>(res);
        }

        private static string BuildScript(List<CatalogItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(ScriptBuilder.Header("", null, false));
            foreach (var it in items)
            {
                var id = ScriptBuilder.Quote(it.Id);
                sb.Append("try {\r\n$gtOut = @(& {\r\n").Append(it.Estimate).Append("\r\n})\r\n");
                sb.Append("$gtLast = if ($gtOut.Count -gt 0) { [string]$gtOut[$gtOut.Count - 1] } else { '' }\r\n");
                sb.Append("if ($gtLast.Trim() -match '^[0-9]{1,18}$') { Write-GTEvent 'estimate' ").Append(id).Append(" $gtLast.Trim() }\r\n");
                sb.Append("else { Write-GTEvent 'estimate' ").Append(id).Append(" '' }\r\n");
                sb.Append("} catch { Write-GTEvent 'estimate' ").Append(id).Append(" '' }\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Taille lisible en français : « 512 o », « 1,0 Ko », « 1,5 Mo », « 2,3 Go » (base 1024).</summary>
        public static string FormatTaille(long octets)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (octets < 1024) return octets.ToString(fr) + " o";
            double v = octets; string[] u = { "Ko", "Mo", "Go", "To" }; int i = -1;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString("0.0", fr) + " " + u[i];
        }
    }
}
