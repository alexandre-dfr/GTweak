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
            var result = new RunResult();
            var dir = Path.Combine(Path.GetTempPath(), "GamingTweaker");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "run-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(file, script, new UTF8Encoding(true));
            try
            {
                var done = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    int before = result.Items.Count;
                    bool relaunch = RunOnce(file, done, result, onLog, ct, delaiFor);
                    if (!relaunch || result.Items.Count == before) break;
                }
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
            return result;
        }

        /// <summary>Une exécution du script. Renvoie true s'il faut relancer (délai dépassé ou arrêt inattendu).</summary>
        private static bool RunOnce(string file, HashSet<string> done, RunResult result, Action<string> onLog, CancellationToken ct, Func<string, int> delaiFor)
        {
            var args = "-NoProfile -ExecutionPolicy Bypass -File \"" + file + "\"";
            if (done.Count > 0) args += " -SkipIds " + string.Join(",", done);
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

            var sync = new object();
            ItemOutcome current = null;
            DateTime startedAt = DateTime.UtcNow;
            bool stopped = false; // après un kill, on ignore la suite de la sortie

            Action<string> handle = line =>
            {
                if (line == null) return;
                lock (sync)
                {
                    if (stopped) return;
                    onLog(line);
                    if (!line.StartsWith("##GT|", StringComparison.Ordinal)) return;
                    var p = line.Split(new[] { '|' }, 4);
                    if (p.Length < 3) return;
                    var kind = p[1]; var id = p[2]; var msg = p.Length > 3 ? p[3] : null;
                    if (kind == "start")
                    {
                        current = new ItemOutcome { Id = id, Status = OutcomeStatus.Failed, Message = null };
                        result.Items.Add(current);
                        done.Add(id);
                        startedAt = DateTime.UtcNow;
                        return;
                    }
                    var o = current != null && current.Id == id ? current : null;
                    if (o == null) { o = new ItemOutcome { Id = id }; result.Items.Add(o); done.Add(id); }
                    if (kind == "ok") { o.Status = OutcomeStatus.Ok; o.Message = null; }
                    else if (kind == "fail") { o.Status = OutcomeStatus.Failed; o.Message = msg; }
                    else if (kind == "skip") { o.Status = OutcomeStatus.Skipped; o.Message = msg; }
                    else return;
                    current = null;
                }
            };

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
                        lock (sync)
                        {
                            stopped = true;
                            if (current != null) { current.Status = OutcomeStatus.Skipped; current.Message = "annulé"; current = null; }
                        }
                        return false;
                    }
                    ItemOutcome timedOut = null; int sec = 0;
                    lock (sync)
                    {
                        if (current != null)
                        {
                            sec = delaiFor(current.Id);
                            if ((DateTime.UtcNow - startedAt).TotalSeconds >= sec) timedOut = current;
                        }
                    }
                    if (timedOut != null)
                    {
                        KillTree(proc);
                        lock (sync)
                        {
                            stopped = true;
                            timedOut.Status = OutcomeStatus.Failed;
                            timedOut.Message = "délai dépassé (" + sec + " s)";
                            current = null;
                        }
                        return true;
                    }
                }
                proc.WaitForExit(); // vide les flux asynchrones

                lock (sync)
                {
                    if (ct.IsCancellationRequested) return false;
                    if (current != null)
                    {
                        current.Status = OutcomeStatus.Failed;
                        current.Message = "le processus PowerShell s'est arrêté (code " + proc.ExitCode + ")";
                        current = null;
                        return true;
                    }
                    if (proc.ExitCode != 0)
                        result.Items.Add(new ItemOutcome { Id = "", Status = OutcomeStatus.Failed, Message = "PowerShell a quitté avec le code " + proc.ExitCode });
                }
            }
            return false;
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
