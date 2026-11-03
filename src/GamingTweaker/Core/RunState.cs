using System;
using System.Collections.Generic;
using System.Linq;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Comptabilité des résultats d'une exécution (une ou plusieurs relances du script).
    /// Thread-safe ; indépendante du processus pour pouvoir tester les entrelacements.
    /// </summary>
    public class RunState
    {
        public readonly object Sync = new object();
        private readonly List<ItemOutcome> items = new List<ItemOutcome>();
        private readonly Dictionary<string, ItemOutcome> byId = new Dictionary<string, ItemOutcome>(StringComparer.Ordinal);
        private readonly HashSet<string> finished = new HashSet<string>(StringComparer.Ordinal);
        private int launches;
        private DateTime startedAt = DateTime.UtcNow;

        /// <summary>Vrai après un kill : la suite de la sortie du processus est ignorée.</summary>
        public bool Stopped { get; private set; }
        public ItemOutcome Current { get; private set; }

        public IReadOnlyList<ItemOutcome> Items { get { lock (Sync) return items.ToList(); } }
        public IEnumerable<string> FinishedIds { get { lock (Sync) return finished.ToList(); } }
        public int FinishedCount { get { lock (Sync) return finished.Count; } }

        public void BeginLaunch()
        {
            lock (Sync) { launches++; Stopped = false; Current = null; }
        }

        public void Stop() { lock (Sync) Stopped = true; }

        private ItemOutcome Get(string id)
        {
            ItemOutcome o;
            if (!byId.TryGetValue(id, out o))
            {
                o = new ItemOutcome { Id = id, Status = OutcomeStatus.Skipped };
                byId[id] = o; items.Add(o);
            }
            return o;
        }

        public void HandleLine(string line)
        {
            if (line == null || !line.StartsWith("##GT|", StringComparison.Ordinal)) return;
            var p = line.Split(new[] { '|' }, 4);
            if (p.Length < 3) return;
            var kind = p[1]; var id = p[2]; var msg = p.Length > 3 ? p[3] : null;
            lock (Sync)
            {
                if (Stopped) return;
                if (kind == "plan")
                {
                    // Seule la première exécution connaît la liste complète.
                    if (launches <= 1) Get(id);
                    return;
                }
                if (kind == "start")
                {
                    var o = Get(id);
                    o.Status = OutcomeStatus.Failed; o.Message = null;
                    Current = o; startedAt = DateTime.UtcNow;
                    return;
                }
                if (kind != "ok" && kind != "fail" && kind != "skip") return;
                var r = Get(id);
                if (kind == "ok") { r.Status = OutcomeStatus.Ok; r.Message = null; }
                else if (kind == "fail") { r.Status = OutcomeStatus.Failed; r.Message = msg; }
                else { r.Status = OutcomeStatus.Skipped; r.Message = msg; }
                finished.Add(id);
                if (Current != null && Current.Id == id) Current = null;
            }
        }

        /// <summary>Option en cours dont le délai est écoulé, ou null.</summary>
        public ItemOutcome TimeoutCandidate(Func<string, int> delaiFor, out int sec)
        {
            lock (Sync)
            {
                sec = 0;
                if (Current == null || finished.Contains(Current.Id)) return null;
                sec = delaiFor(Current.Id);
                return (DateTime.UtcNow - startedAt).TotalSeconds >= sec ? Current : null;
            }
        }

        /// <summary>À appeler après le kill. Faux si l'option a atteint un état final entre-temps (elle n'est pas écrasée).</summary>
        public bool ApplyTimeout(ItemOutcome candidate, int sec)
        {
            lock (Sync)
            {
                Stopped = true;
                if (finished.Contains(candidate.Id)) return false;
                candidate.Status = OutcomeStatus.Failed;
                candidate.Message = "délai dépassé (" + sec + " s)";
                finished.Add(candidate.Id);
                if (Current == candidate) Current = null;
                return true;
            }
        }

        /// <summary>Le processus s'est arrêté pendant une option.</summary>
        public bool MarkCurrentDied(int exitCode)
        {
            lock (Sync)
            {
                if (Current == null || finished.Contains(Current.Id)) return false;
                Current.Status = OutcomeStatus.Failed;
                Current.Message = "le processus PowerShell s'est arrêté (code " + exitCode + ")";
                finished.Add(Current.Id);
                Current = null;
                return true;
            }
        }

        /// <summary>Fin de l'exécution : toute option planifiée sans état final reçoit un résultat.</summary>
        public void Complete(bool cancelled, int exitCode)
        {
            lock (Sync)
            {
                bool any = false;
                foreach (var o in items)
                {
                    if (finished.Contains(o.Id)) continue;
                    any = true;
                    if (cancelled) { o.Status = OutcomeStatus.Skipped; o.Message = "annulé"; }
                    else { o.Status = OutcomeStatus.Failed; o.Message = "non exécuté : PowerShell arrêté (code " + exitCode + ")"; }
                    finished.Add(o.Id);
                }
                Current = null;
                if (!cancelled && !any && exitCode != 0 && items.Count == 0)
                    items.Add(new ItemOutcome { Id = "", Status = OutcomeStatus.Failed, Message = "PowerShell a quitté avec le code " + exitCode });
            }
        }
    }
}
