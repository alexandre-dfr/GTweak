using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GamingTweaker.Core
{
    /// <summary>Charge du processeur, de la carte graphique et de la mémoire, en pourcentage (null : inconnue).</summary>
    public class ChargeSysteme
    {
        public double? Cpu { get; set; }
        public double? Gpu { get; set; }
        public double? Ram { get; set; }
        /// <summary>Mémoire utilisée et totale, en Go.</summary>
        public double RamUtiliseeGo { get; set; }
        public double RamTotaleGo { get; set; }
    }

    /// <summary>
    /// Relevés de charge, mêmes sources que le Gestionnaire des tâches :
    /// processeur par GetSystemTimes (temps hors inactivité entre deux relevés), mémoire par GlobalMemoryStatusEx,
    /// carte graphique par le compteur de performances « GPU Engine » (moteur 3D le plus chargé, Windows 10 1709 ou plus récent).
    /// <see cref="Relever"/> est à appeler environ une fois par seconde, hors du thread de l'interface (le compteur GPU coûte quelques ms).
    /// </summary>
    public sealed class CompteurSysteme : IDisposable
    {
        private long inactifAvant, noyauAvant, utilisateurAvant;
        private bool premierCpu = true;
        private IntPtr requete = IntPtr.Zero, compteurGpu = IntPtr.Zero;
        private DateTime requeteCreee;
        private bool gpuIndisponible;

        public ChargeSysteme Relever(bool cpu = true, bool gpu = true, bool ram = true)
        {
            var c = new ChargeSysteme();
            if (cpu) c.Cpu = ReleverCpu();
            if (ram) ReleverRam(c);
            if (gpu) c.Gpu = ReleverGpu();
            return c;
        }

        private double? ReleverCpu()
        {
            long inactif, noyau, utilisateur;
            if (!Natif.GetSystemTimes(out inactif, out noyau, out utilisateur)) return null;
            var r = ChargeCpu(inactifAvant, noyauAvant, utilisateurAvant, inactif, noyau, utilisateur);
            inactifAvant = inactif; noyauAvant = noyau; utilisateurAvant = utilisateur;
            if (premierCpu) { premierCpu = false; return null; }
            return r;
        }

        /// <summary>Charge du processeur entre deux relevés de GetSystemTimes (le temps noyau inclut le temps inactif).</summary>
        public static double? ChargeCpu(long inactif0, long noyau0, long utilisateur0, long inactif1, long noyau1, long utilisateur1)
        {
            var total = (noyau1 - noyau0) + (utilisateur1 - utilisateur0);
            if (total <= 0) return null;
            var occupe = total - (inactif1 - inactif0);
            return Math.Max(0, Math.Min(100, 100.0 * occupe / total));
        }

        private static void ReleverRam(ChargeSysteme c)
        {
            var m = new Natif.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Natif.MEMORYSTATUSEX)) };
            if (!Natif.GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0) return;
            c.RamTotaleGo = m.ullTotalPhys / 1073741824.0;
            c.RamUtiliseeGo = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            c.Ram = 100.0 * (m.ullTotalPhys - m.ullAvailPhys) / m.ullTotalPhys;
        }

        // ---------- Carte graphique ----------

        private const uint PdhFmtDouble = 0x00000200;
        private const int PdhMoreData = unchecked((int)0x800007D2);
        private static readonly Regex Moteur = new Regex(@"luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_(\d+)_eng_(\d+)_engtype_3d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Instances « pid_…_luid_…_phys_…_eng_…_engtype_3D » et leur utilisation : somme par moteur (tous processus confondus),
        /// puis le moteur le plus chargé, plafonné à 100 (comme la colonne GPU du Gestionnaire des tâches). Null sans instance 3D.
        /// </summary>
        public static double? ChargeGpu(IEnumerable<KeyValuePair<string, double>> instances)
        {
            var parMoteur = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in instances)
            {
                var m = Moteur.Match(kv.Key ?? "");
                if (!m.Success || double.IsNaN(kv.Value) || kv.Value < 0) continue;
                var cle = m.Groups[1].Value + "/" + m.Groups[2].Value + "/" + m.Groups[3].Value;
                double v; parMoteur.TryGetValue(cle, out v);
                parMoteur[cle] = v + kv.Value;
            }
            if (parMoteur.Count == 0) return null;
            double max = 0;
            foreach (var v in parMoteur.Values) max = Math.Max(max, v);
            return Math.Min(100, max);
        }

        private double? ReleverGpu()
        {
            if (gpuIndisponible) return null;
            try
            {
                // Requête recréée régulièrement : les processus lancés depuis (le jeu) y figurent sûrement.
                if (requete != IntPtr.Zero && DateTime.UtcNow - requeteCreee > TimeSpan.FromSeconds(20)) FermerRequete();
                bool nouvelle = false;
                if (requete == IntPtr.Zero)
                {
                    if (Natif.PdhOpenQueryW(null, IntPtr.Zero, out requete) != 0) { requete = IntPtr.Zero; gpuIndisponible = true; return null; }
                    if (Natif.PdhAddEnglishCounterW(requete, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out compteurGpu) != 0)
                    {
                        FermerRequete(); gpuIndisponible = true; return null;
                    }
                    requeteCreee = DateTime.UtcNow;
                    nouvelle = true;
                }
                if (Natif.PdhCollectQueryData(requete) != 0) return null;
                if (nouvelle) return null; // un compteur de taux a besoin de deux relevés
                uint taille = 0, nombre;
                int r = Natif.PdhGetFormattedCounterArrayW(compteurGpu, PdhFmtDouble, ref taille, out nombre, IntPtr.Zero);
                if (r != PdhMoreData || taille == 0) return null;
                var buf = Marshal.AllocHGlobal((int)taille);
                try
                {
                    r = Natif.PdhGetFormattedCounterArrayW(compteurGpu, PdhFmtDouble, ref taille, out nombre, buf);
                    if (r != 0) return null;
                    var res = new List<KeyValuePair<string, double>>((int)nombre);
                    // PDH_FMT_COUNTERVALUE_ITEM_W (x64) : nom (8), CStatus (4) + alignement (4), valeur double (8).
                    for (int i = 0; i < nombre; i++)
                    {
                        var item = buf + i * 24;
                        var statut = Marshal.ReadInt32(item, 8);
                        if (statut != 0 && statut != 1) continue; // PDH_CSTATUS_VALID_DATA, PDH_CSTATUS_NEW_DATA
                        var nom = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item, 0));
                        var v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                        res.Add(new KeyValuePair<string, double>(nom, v));
                    }
                    return ChargeGpu(res);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch (Exception ex)
            {
                TraceAppli.Erreur("compteur en jeu : charge GPU", ex);
                gpuIndisponible = true;
                FermerRequete();
                return null;
            }
        }

        private void FermerRequete()
        {
            if (requete != IntPtr.Zero) { try { Natif.PdhCloseQuery(requete); } catch (Exception) { } }
            requete = IntPtr.Zero; compteurGpu = IntPtr.Zero;
        }

        public void Dispose() { FermerRequete(); }

        private static class Natif
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct MEMORYSTATUSEX
            {
                public uint dwLength, dwMemoryLoad;
                public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

            [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
            public static extern int PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);

            [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
            public static extern int PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

            [DllImport("pdh.dll")]
            public static extern int PdhCollectQueryData(IntPtr query);

            [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
            public static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

            [DllImport("pdh.dll")]
            public static extern int PdhCloseQuery(IntPtr query);
        }
    }
}
