using System;
using System.Diagnostics;
using System.Threading;

namespace GamingTweaker.Core
{
    /// <summary>Sorties d'un processus terminé de lui-même (code de sortie, sortie standard, sortie d'erreur).</summary>
    public class ResultatProcessus
    {
        public int CodeSortie { get; set; }
        public string Sortie { get; set; } = "";
        public string Erreur { get; set; } = "";
    }

    /// <summary>
    /// Lancement borné d'un processus à sorties redirigées : lecture des deux flux en parallèle (aucun blocage sur un tampon plein),
    /// attente limitée et annulable. Délai dépassé ou annulation : le processus ET ses enfants sont arrêtés (PowerShellLanceur.Tuer,
    /// taskkill /T), un simple Kill() laissait par exemple winget lancé par PowerShell tourner en arrière-plan.
    /// </summary>
    public static class ProcessusBorne
    {
        /// <summary>
        /// Exécute <paramref name="psi"/> (RedirectStandardOutput et RedirectStandardError requis). Lève TimeoutException au-delà de
        /// <paramref name="delaiMs"/> (message : « <paramref name="nom"/> n'a pas répondu à temps. ») et OperationCanceledException à l'annulation.
        /// </summary>
        public static ResultatProcessus Executer(ProcessStartInfo psi, int delaiMs, CancellationToken ct, string nom)
        {
            using (var p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException(nom + " n'a pas pu être lancé.");
                if (psi.RedirectStandardInput) p.StandardInput.Close();
                var err = p.StandardError.ReadToEndAsync();
                var sortie = p.StandardOutput.ReadToEndAsync();
                var limite = DateTime.UtcNow.AddMilliseconds(delaiMs);
                while (!p.WaitForExit(100))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PowerShellLanceur.Tuer(p);
                        ct.ThrowIfCancellationRequested();
                    }
                    if (DateTime.UtcNow >= limite)
                    {
                        PowerShellLanceur.Tuer(p);
                        TraceAppli.Ecrire(nom + " : délai de " + (delaiMs / 1000) + " s dépassé, processus et enfants arrêtés");
                        throw new TimeoutException(nom + " n'a pas répondu à temps.");
                    }
                }
                p.WaitForExit(); // vide les flux asynchrones
                return new ResultatProcessus { CodeSortie = p.ExitCode, Sortie = sortie.Result ?? "", Erreur = err.Result ?? "" };
            }
        }
    }
}
