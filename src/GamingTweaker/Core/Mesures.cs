using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    public class MesureSnapshot
    {
        /// <summary>Début du relevé, « yyyy-MM-ddTHH:mm:ss ».</summary>
        [JsonProperty("date")] public string Date { get; set; }
        [JsonProperty("dureeSec")] public double DureeSec { get; set; }
        /// <summary>Moyenne de Processor(_Total)\% DPC Time ; null si le compteur est indisponible.</summary>
        [JsonProperty("dpcTime")] public double? DpcTime { get; set; }
        /// <summary>Moyenne de Processor(_Total)\% Interrupt Time ; null si le compteur est indisponible.</summary>
        [JsonProperty("interruptTime")] public double? InterruptTime { get; set; }
        /// <summary>Services en cours d'exécution.</summary>
        [JsonProperty("services")] public int Services { get; set; }
        [JsonProperty("processus")] public int Processus { get; set; }
        [JsonProperty("ramDisponibleMo")] public long RamDisponibleMo { get; set; }
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string Note { get; set; }
    }

    /// <summary>Écart « b moins a » : négatif = en baisse.</summary>
    public class MesureDiff
    {
        public double? DpcTime { get; set; }
        public double? InterruptTime { get; set; }
        public int Services { get; set; }
        public int Processus { get; set; }
        public long RamDisponibleMo { get; set; }
    }

    public static class Mesures
    {
        public const string DossierMesures = "Mesures";

        public static MesureDiff Compare(MesureSnapshot a, MesureSnapshot b)
        {
            return new MesureDiff
            {
                DpcTime = a.DpcTime.HasValue && b.DpcTime.HasValue ? b.DpcTime - a.DpcTime : null,
                InterruptTime = a.InterruptTime.HasValue && b.InterruptTime.HasValue ? b.InterruptTime - a.InterruptTime : null,
                Services = b.Services - a.Services,
                Processus = b.Processus - a.Processus,
                RamDisponibleMo = b.RamDisponibleMo - a.RamDisponibleMo,
            };
        }

        // ---------- Stockage ----------

        public static string Save(MesureSnapshot s, string machineDir)
        {
            var dir = Path.Combine(machineDir, DossierMesures);
            Directory.CreateDirectory(dir);
            var baseName = (s.Date ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Replace(":", "").Replace("-", "").Replace("T", "-");
            var path = Path.Combine(dir, baseName + ".json");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, baseName + "-" + n + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(s, Formatting.Indented), new UTF8Encoding(false));
            return path;
        }

        /// <summary>Relevés enregistrés, du plus récent au plus ancien ; les fichiers illisibles sont ignorés.</summary>
        public static List<MesureSnapshot> List(string machineDir)
        {
            var res = new List<MesureSnapshot>();
            var dir = Path.Combine(machineDir ?? "", DossierMesures);
            if (!Directory.Exists(dir)) return res;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var s = JsonConvert.DeserializeObject<MesureSnapshot>(File.ReadAllText(f, Encoding.UTF8), new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                    if (s != null) res.Add(s);
                }
                catch (Exception) { }
            }
            return res.OrderByDescending(s => s.Date, StringComparer.Ordinal).ToList();
        }

        // ---------- Relevé ----------

        public static Task<MesureSnapshot> TakeAsync(TimeSpan duree) { return TakeAsync(duree, null, CancellationToken.None); }

        /// <summary>Échantillonne une fois par seconde pendant « duree » hors du thread appelant ; progress reçoit 0 à 100.</summary>
        public static Task<MesureSnapshot> TakeAsync(TimeSpan duree, IProgress<double> progress, CancellationToken ct)
        {
            return Task.Run(() => Prendre(duree, progress, ct), ct);
        }

        private static MesureSnapshot Prendre(TimeSpan duree, IProgress<double> progress, CancellationToken ct)
        {
            var s = new MesureSnapshot { Date = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), DureeSec = duree.TotalSeconds };
            PerformanceCounter dpc = null, irq = null;
            try
            {
                string objet, cDpc, cIrq;
                ResoudreNoms(out objet, out cDpc, out cIrq);
                dpc = new PerformanceCounter(objet, cDpc, "_Total", true);
                irq = new PerformanceCounter(objet, cIrq, "_Total", true);
                dpc.NextValue(); irq.NextValue(); // la première lecture est toujours 0
            }
            catch (Exception ex)
            {
                s.Note = "Compteurs de performance indisponibles : " + ex.Message;
                if (dpc != null) dpc.Dispose();
                if (irq != null) irq.Dispose();
                dpc = irq = null;
            }
            var echDpc = new List<double>(); var echIrq = new List<double>();
            var fin = DateTime.UtcNow + duree;
            if (progress != null) progress.Report(0);
            while (true)
            {
                var reste = fin - DateTime.UtcNow;
                if (reste <= TimeSpan.Zero) break;
                if (ct.WaitHandle.WaitOne(reste < TimeSpan.FromSeconds(1) ? reste : TimeSpan.FromSeconds(1))) ct.ThrowIfCancellationRequested();
                try
                {
                    if (dpc != null) { echDpc.Add(dpc.NextValue()); echIrq.Add(irq.NextValue()); }
                }
                catch (Exception ex) { s.Note = "Lecture des compteurs interrompue : " + ex.Message; }
                if (progress != null) progress.Report(Math.Min(100, 100.0 * (1 - Math.Max(0, (fin - DateTime.UtcNow).TotalSeconds) / duree.TotalSeconds)));
            }
            if (dpc != null) dpc.Dispose();
            if (irq != null) irq.Dispose();
            if (echDpc.Count > 0) { s.DpcTime = Math.Round(echDpc.Average(), 3); s.InterruptTime = Math.Round(echIrq.Average(), 3); }
            else if (s.Note == null) s.Note = "Aucun échantillon de compteur.";

            try { foreach (var sc in ServiceController.GetServices()) using (sc) if (sc.Status == ServiceControllerStatus.Running) s.Services++; } catch (Exception) { }
            try { foreach (var p in Process.GetProcesses()) { s.Processus++; p.Dispose(); } } catch (Exception) { }
            s.RamDisponibleMo = RamDisponibleMo();
            if (progress != null) progress.Report(100);
            return s;
        }

        // ---------- Noms de compteurs : localisés, donc résolus par index ----------

        private const int IndexProcessorRepli = 238, IndexDpcRepli = 696, IndexInterruptRepli = 698;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhLookupPerfNameByIndex(string machine, uint index, StringBuilder buffer, ref uint size);

        /// <summary>Noms localisés de l'objet Processor et des compteurs % DPC Time / % Interrupt Time.</summary>
        internal static void ResoudreNoms(out string objet, out string dpc, out string interruption)
        {
            var anglais = LireTable("009");
            Func<string, int, int> index = (nom, repli) =>
            {
                var e = anglais.FirstOrDefault(kv => string.Equals(kv.Value, nom, StringComparison.Ordinal));
                return e.Value == null ? repli : e.Key;
            };
            int iObjet = index("Processor", IndexProcessorRepli), iDpc = index("% DPC Time", IndexDpcRepli), iIrq = index("% Interrupt Time", IndexInterruptRepli);
            Dictionary<int, string> local = null;
            Func<int, string> nom = i =>
            {
                var sb = new StringBuilder(256); uint size = (uint)sb.Capacity;
                try { if (PdhLookupPerfNameByIndex(null, (uint)i, sb, ref size) == 0 && sb.Length > 0) return sb.ToString(); } catch (Exception) { }
                if (local == null) local = LireTable("CurrentLanguage");
                string n; return local.TryGetValue(i, out n) ? n : null;
            };
            objet = nom(iObjet); dpc = nom(iDpc); interruption = nom(iIrq);
            if (objet == null || dpc == null || interruption == null) throw new InvalidOperationException("noms de compteurs introuvables (index " + iObjet + ", " + iDpc + ", " + iIrq + ")");
        }

        /// <summary>Table index/nom de HKLM\...\Perflib\&lt;langue&gt; (valeur multi-chaîne « Counter »).</summary>
        private static Dictionary<int, string> LireTable(string langue)
        {
            var res = new Dictionary<int, string>();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib\" + langue))
                {
                    var t = k == null ? null : k.GetValue("Counter") as string[];
                    if (t == null) return res;
                    for (int i = 0; i + 1 < t.Length; i += 2)
                    {
                        int n;
                        if (int.TryParse(t[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && !res.ContainsKey(n)) res[n] = t[i + 1];
                    }
                }
            }
            catch (Exception) { }
            return res;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MemoryStatusEx
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys; public ulong ullTotalPageFile;
            public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        private static long RamDisponibleMo()
        {
            var m = new MemoryStatusEx();
            return GlobalMemoryStatusEx(m) ? (long)(m.ullAvailPhys / (1024 * 1024)) : 0;
        }
    }
}
