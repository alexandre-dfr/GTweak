using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GamingTweaker.Core
{
    public class RunRequest
    {
        public Selection Selection { get; set; } = new Selection();
        public bool Silent { get; set; }
        /// <summary>Appelée (mode interactif) quand le point de restauration est impossible ; renvoie vrai pour continuer.</summary>
        public Func<string, bool> ConfirmSansPointDeRestauration { get; set; }
        public IReadOnlyList<CatalogItem> CatalogItems { get; set; } = new List<CatalogItem>();
        public MachineFacts Facts { get; set; } = new MachineFacts();
        public DataPaths Paths { get; set; }
        /// <summary>Avertissements déjà connus de l'appelant (profil : options inconnues ou incompatibles).</summary>
        public List<string> Avertissements { get; set; } = new List<string>();
    }

    public class SessionReport
    {
        public string SessionDir { get; set; }
        public List<ItemOutcome> Items { get; set; } = new List<ItemOutcome>();
        public List<string> Retablies { get; set; } = new List<string>();
        public List<string> Avertissements { get; set; } = new List<string>();
        public bool RedemarrageRequis { get; set; }
        /// <summary>0 : tout est Ok/AlreadyApplied/Skipped ; 1 : au moins un échec ; 2 : exécution impossible.</summary>
        public int ExitCode { get; set; }
    }

    public class SessionInterrompue
    {
        public string SessionDir { get; set; }
        public DateTime Debut { get; set; }
    }

    public class RunPipeline
    {
        private readonly IRestorePoint restorePoint;
        private readonly Func<bool> sameUserAsDesktop;

        public RunPipeline(IRestorePoint restorePoint, Func<bool> sameUserAsDesktop)
        {
            this.restorePoint = restorePoint;
            this.sameUserAsDesktop = sameUserAsDesktop;
        }

        private class SessionState
        {
            [JsonProperty("etat")] public string Etat { get; set; }
            [JsonProperty("debut")] public string Debut { get; set; }
            [JsonProperty("fin", NullValueHandling = NullValueHandling.Ignore)] public string Fin { get; set; }
        }

        private const string Format = "yyyy-MM-ddTHH:mm:ss";

        public async Task<SessionReport> ExecuteAsync(RunRequest req, IProgress<string> log, CancellationToken ct)
        {
            var report = new SessionReport();
            report.Avertissements.AddRange(req.Avertissements ?? new List<string>());
            var selected = req.CatalogItems.Where(i => req.Selection.OptionIds.Contains(i.Id)).ToList();

            // 1. Point de restauration (seulement si une option réversible est choisie).
            if (selected.Any(i => i.Reversible))
            {
                var rp = restorePoint.TryCreate("GamingTweaker " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                if (rp == null || !rp.Created)
                {
                    var msg = rp == null || string.IsNullOrEmpty(rp.Message) ? "Point de restauration impossible." : rp.Message;
                    bool continuer = req.Silent || (req.ConfirmSansPointDeRestauration != null && req.ConfirmSansPointDeRestauration(msg));
                    report.Avertissements.Add(msg + (continuer ? " Exécution sans point de restauration." : " Exécution annulée."));
                    if (!continuer) { report.ExitCode = 2; return report; }
                }
            }

            // 2. Dossier de session, état Running.
            var debut = DateTime.Now;
            report.SessionDir = CreateSessionDir(req.Paths.SessionsDir, debut);
            WriteState(report.SessionDir, new SessionState { Etat = "Running", Debut = debut.ToString(Format, CultureInfo.InvariantCulture) });
            var logFile = Path.Combine(report.SessionDir, "log.txt");
            var logLock = new object();
            Action<string> write = line =>
            {
                lock (logLock) { try { File.AppendAllText(logFile, line + "\r\n", new UTF8Encoding(true)); } catch (IOException) { } }
                if (log != null) log.Report(line);
            };

            var outcomes = new Dictionary<string, ItemOutcome>(StringComparer.Ordinal);
            var lastApplied = LoadLastApplied(req.Paths.MachineDir);

            // 3. Élévation : sous un autre compte, HKCU n'est pas celui de l'utilisateur.
            var aExecuter = new List<CatalogItem>();
            bool memeUtilisateur = sameUserAsDesktop == null || sameUserAsDesktop();
            if (!memeUtilisateur)
                report.Avertissements.Add("GamingTweaker tourne sous un autre compte que celui du bureau : les options qui modifient l'utilisateur courant (HKCU) sont ignorées.");
            foreach (var it in selected)
            {
                if (!memeUtilisateur && it.Hkcu)
                    outcomes[it.Id] = new ItemOutcome { Id = it.Id, Status = OutcomeStatus.Skipped, Message = "ignorée : exécution sous un autre compte que celui du bureau (réglage propre à l'utilisateur)" };
                else aExecuter.Add(it);
            }

            // 4. Détection préalable.
            write("Détection préalable de " + aExecuter.Count + " option(s)...");
            var pre = aExecuter.Count == 0 ? new Dictionary<string, DetectState>() : await Detection.RunAsync(aExecuter);
            var toRun = new List<CatalogItem>();
            foreach (var it in aExecuter)
            {
                DetectState st;
                if (!pre.TryGetValue(it.Id, out st)) st = DetectState.Unknown;
                if (it.Reversible && st == DetectState.Applied)
                {
                    outcomes[it.Id] = new ItemOutcome { Id = it.Id, Status = OutcomeStatus.AlreadyApplied, Message = "déjà appliquée" };
                    continue;
                }
                if (it.Reversible && st == DetectState.NotApplied && lastApplied.Contains(it.Id))
                    report.Retablies.Add(it.Id);
                toRun.Add(it);
            }

            // 5. Exécution.
            if (toRun.Count > 0 && !ct.IsCancellationRequested)
            {
                var delais = toRun.ToDictionary(i => i.Id, i => i.DelaiSec > 0 ? i.DelaiSec : 300);
                var script = ScriptBuilder.Build(toRun, req.Selection, Path.Combine(report.SessionDir, "journal.jsonl"), req.Paths.QuarantineDir);
                var res = await Runner.RunAsync(script, write, ct, id => delais.ContainsKey(id) ? delais[id] : 300);
                foreach (var o in res.Items) outcomes[o.Id] = o;
            }
            else if (toRun.Count > 0)
            {
                foreach (var it in toRun) outcomes[it.Id] = new ItemOutcome { Id = it.Id, Status = OutcomeStatus.Skipped, Message = "annulé" };
            }

            report.Items = selected.Where(i => outcomes.ContainsKey(i.Id)).Select(i => outcomes[i.Id]).ToList();
            // Erreur globale sans identifiant (PowerShell n'a rien produit).
            report.Items.AddRange(outcomes.Values.Where(o => !selected.Any(i => i.Id == o.Id)));

            // 6. Redétection : une option réversible « Ok » que la détection ne confirme pas est signalée.
            var okRev = selected.Where(i => i.Reversible && outcomes.ContainsKey(i.Id) && outcomes[i.Id].Status == OutcomeStatus.Ok && toRun.Contains(i)).ToList();
            if (okRev.Count > 0)
            {
                var post = await Detection.RunAsync(okRev);
                foreach (var it in okRev)
                    if (post[it.Id] == DetectState.NotApplied)
                        report.Avertissements.Add(it.Titre + " (" + it.Id + ") : appliquée sans erreur, mais la détection ne la confirme pas.");
            }

            // 7. Bilan.
            report.RedemarrageRequis = selected.Any(i => i.Redemarrage && outcomes.ContainsKey(i.Id) && outcomes[i.Id].Status == OutcomeStatus.Ok);
            report.ExitCode = report.Items.Any(i => i.Status == OutcomeStatus.Failed) ? 1 : 0;

            var okIds = report.Items.Where(i => i.Status == OutcomeStatus.Ok).Select(i => i.Id);
            SaveLastApplied(req.Paths.MachineDir, lastApplied.Union(okIds).OrderBy(x => x, StringComparer.Ordinal));

            WriteReports(report, selected, debut);
            WriteState(report.SessionDir, new SessionState
            {
                Etat = "Done",
                Debut = debut.ToString(Format, CultureInfo.InvariantCulture),
                Fin = DateTime.Now.ToString(Format, CultureInfo.InvariantCulture),
            });
            return report;
        }

        /// <summary>Dernière session restée à l'état Running (arrêt brutal, coupure), ou null.</summary>
        public static SessionInterrompue FindInterrupted(string machineDir)
        {
            var dir = Path.Combine(machineDir ?? "", "Sessions");
            if (!Directory.Exists(dir)) return null;
            foreach (var d in Directory.GetDirectories(dir).OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal))
            {
                try
                {
                    var f = Path.Combine(d, "state.json");
                    if (!File.Exists(f)) continue;
                    var st = JsonConvert.DeserializeObject<SessionState>(File.ReadAllText(f, Encoding.UTF8), new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                    if (st == null || st.Etat != "Running") continue;
                    DateTime debut;
                    if (!DateTime.TryParseExact(st.Debut ?? "", Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out debut)) debut = Directory.GetCreationTime(d);
                    return new SessionInterrompue { SessionDir = d, Debut = debut };
                }
                catch (Exception) { /* état illisible : ignoré */ }
            }
            return null;
        }

        private static string CreateSessionDir(string sessionsDir, DateTime debut)
        {
            var baseName = debut.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var dir = Path.Combine(sessionsDir, baseName);
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(sessionsDir, baseName + "-" + n);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteState(string sessionDir, SessionState st)
        {
            var f = Path.Combine(sessionDir, "state.json");
            var tmp = f + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(st, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(f)) File.Delete(f);
            File.Move(tmp, f);
        }

        private static HashSet<string> LoadLastApplied(string machineDir)
        {
            try
            {
                var f = Path.Combine(machineDir, "last-applied.json");
                if (File.Exists(f))
                    return new HashSet<string>(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(f, Encoding.UTF8)) ?? new List<string>(), StringComparer.Ordinal);
            }
            catch (Exception) { /* fichier corrompu : repart de zéro */ }
            return new HashSet<string>(StringComparer.Ordinal);
        }

        private static void SaveLastApplied(string machineDir, IEnumerable<string> ids)
        {
            try
            {
                Directory.CreateDirectory(machineDir);
                File.WriteAllText(Path.Combine(machineDir, "last-applied.json"), JsonConvert.SerializeObject(ids.ToList(), Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception) { /* non bloquant */ }
        }

        private static string Libelle(OutcomeStatus s)
        {
            switch (s)
            {
                case OutcomeStatus.Ok: return "Appliquée";
                case OutcomeStatus.Failed: return "ÉCHEC";
                case OutcomeStatus.Skipped: return "Ignorée";
                default: return "Déjà appliquée";
            }
        }

        private static void WriteReports(SessionReport r, List<CatalogItem> selected, DateTime debut)
        {
            File.WriteAllText(Path.Combine(r.SessionDir, "report.json"),
                JsonConvert.SerializeObject(r, Formatting.Indented, new StringEnumConverter()), new UTF8Encoding(false));

            var titres = selected.ToDictionary(i => i.Id, i => i.Titre ?? i.Id);
            var sb = new StringBuilder();
            sb.AppendLine("GamingTweaker - rapport du " + debut.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine();
            foreach (var i in r.Items)
            {
                string t;
                sb.Append("[" + Libelle(i.Status) + "] " + (titres.TryGetValue(i.Id, out t) ? t + " (" + i.Id + ")" : i.Id));
                if (!string.IsNullOrEmpty(i.Message)) sb.Append(" : " + i.Message);
                sb.AppendLine();
            }
            if (r.Retablies.Count > 0)
            {
                sb.AppendLine().AppendLine("Options rétablies par Windows depuis la dernière exécution (réappliquées) :");
                foreach (var id in r.Retablies) sb.AppendLine("  - " + id);
            }
            if (r.Avertissements.Count > 0)
            {
                sb.AppendLine().AppendLine("Avertissements :");
                foreach (var w in r.Avertissements) sb.AppendLine("  - " + w);
            }
            if (r.RedemarrageRequis) sb.AppendLine().AppendLine("Un redémarrage est nécessaire pour terminer l'application de certaines options.");
            sb.AppendLine().AppendLine("Les options annulées ensuite depuis l'historique restent listées dans les exécutions précédentes ; ce rapport décrit uniquement cette session.");
            sb.AppendLine("Code de sortie : " + r.ExitCode);
            File.WriteAllText(Path.Combine(r.SessionDir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
        }
    }
}
