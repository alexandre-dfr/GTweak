using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamingTweaker.Core
{
    public enum OutcomeStatus { Ok, Failed, Skipped, AlreadyApplied }

    public class ItemOutcome
    {
        public string Id { get; set; }
        public OutcomeStatus Status { get; set; }
        public string Message { get; set; }
    }

    public class RunResult
    {
        public List<ItemOutcome> Items { get; set; } = new List<ItemOutcome>();
    }

    public static class Runner
    {
        public static Task<RunResult> RunAsync(string script, Action<string> onLog, CancellationToken ct, Func<string, int> delaiFor)
            => Task.Run(() => Run(script, onLog ?? (_ => { }), ct, delaiFor ?? (_ => 300)));

        private static RunResult Run(string script, Action<string> onLog, CancellationToken ct, Func<string, int> delaiFor)
        {
            var state = new RunState();
            var dir = Path.Combine(Path.GetTempPath(), "GamingTweaker");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "run-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(file, script, new UTF8Encoding(true));
            bool cancelled = false; int exit = 0;
            try
            {
                while (true)
                {
                    int before = state.FinishedCount;
                    bool relaunch = RunOnce(file, state, onLog, ct, delaiFor, out exit, out cancelled);
                    if (cancelled || !relaunch || state.FinishedCount == before) break;
                }
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
            // Toute option planifiée sans état final : Skipped "annulé" si annulation, sinon Failed avec le code de sortie.
            state.Complete(cancelled, exit);
            return new RunResult { Items = state.Items.ToList() };
        }

        /// <summary>Une exécution du script. Renvoie true s'il faut relancer (délai dépassé ou arrêt pendant une option).</summary>
        private static bool RunOnce(string file, RunState state, Action<string> onLog, CancellationToken ct, Func<string, int> delaiFor, out int exitCode, out bool cancelled)
        {
            exitCode = 0; cancelled = false;
            var args = "-NoProfile -ExecutionPolicy Bypass -File \"" + file + "\"";
            // Les identifiants sont validés (a-z0-9.-) : liste sans guillemets, redécoupée dans le script.
            var finishedIds = state.FinishedIds.ToList();
            if (finishedIds.Count > 0) args += " -SkipIds " + string.Join(",", finishedIds);
            var psi = new ProcessStartInfo("powershell.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            Action<string> handle = line =>
            {
                if (line == null) return;
                lock (state.Sync)
                {
                    if (state.Stopped) return;
                    onLog(line);
                    state.HandleLine(line);
                }
            };

            state.BeginLaunch();
            using (var proc = new Process { StartInfo = psi })
            {
                proc.OutputDataReceived += (s, e) => handle(e.Data);
                proc.ErrorDataReceived += (s, e) => handle(e.Data);
                proc.Start();
                proc.StandardInput.Close();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                while (!proc.WaitForExit(50))
                {
                    if (ct.IsCancellationRequested)
                    {
                        KillTree(proc);
                        state.Stop();
                        cancelled = true;
                        return false;
                    }
                    int sec;
                    var cand = state.TimeoutCandidate(delaiFor, out sec);
                    if (cand != null)
                    {
                        KillTree(proc);
                        // Si l'option s'est terminée entre-temps, elle garde son résultat ; la relance
                        // reprend alors les options non terminées (y compris une démarrée juste avant le kill).
                        state.ApplyTimeout(cand, sec);
                        return true;
                    }
                }
                proc.WaitForExit(); // vide les flux asynchrones
                exitCode = proc.ExitCode;
                if (ct.IsCancellationRequested) { cancelled = true; return false; }
                return state.MarkCurrentDied(exitCode);
            }
        }

        private static void KillTree(Process p)
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
