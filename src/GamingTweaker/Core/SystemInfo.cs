using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace GamingTweaker.Core
{
    public static class SystemInfo
    {
        /// <summary>Lecture fine du registre et de WMI. Chaque source en échec donne une valeur vide.</summary>
        public static MachineFacts Detect()
        {
            var facts = new MachineFacts { MachineName = Environment.MachineName };

            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                using (var cv = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    facts.WindowsMajor = WindowsMajorFromBuild(cv?.GetValue("CurrentBuild") as string);
                    int build;
                    facts.WindowsBuild = int.TryParse(cv?.GetValue("CurrentBuild") as string, out build) ? build : 0;
                    facts.Edition = NormalizeEdition(cv?.GetValue("EditionID") as string);
                }
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    facts.MachineGuid = key?.GetValue("MachineGuid") as string ?? "";
                }
            }

            // Les quatre lectures WMI sont indépendantes : lancées en parallèle, elles coûtent le temps de la plus lente au lieu de leur somme.
            var processeurs = System.Threading.Tasks.Task.Run(() => Query("SELECT Name FROM Win32_Processor", "Name"));
            var cartes = System.Threading.Tasks.Task.Run(() => Query("SELECT Name FROM Win32_VideoController", "Name"));
            var chassis = System.Threading.Tasks.Task.Run(() => QueryChassis());
            var batteries = System.Threading.Tasks.Task.Run(() => Query("SELECT Name FROM Win32_Battery", "Name"));
            foreach (var name in processeurs.Result)
            {
                var v = NormalizeVendor(name);
                if (v != "") { facts.CpuVendor = v; break; }
            }
            foreach (var name in cartes.Result)
            {
                var v = NormalizeVendor(name);
                if (v != "" && !facts.GpuVendors.Contains(v)) facts.GpuVendors.Add(v);
            }
            facts.EstPortable = EstPortable(chassis.Result, batteries.Result.Count > 0);
            facts.RamGo = RamGo();
            facts.ServiceExiste = ServiceExiste;
            facts.CleExiste = CleExiste;
            return facts;
        }

        /// <summary>Mémoire vive installée, en Go arrondis (0 si la lecture échoue).</summary>
        private static int RamGo()
        {
            try
            {
                var m = new MemoryStatusEx { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatusEx)) };
                return GlobalMemoryStatusEx(ref m) ? ArrondirGo((long)m.ullTotalPhys) : 0;
            }
            catch (Exception) { return 0; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        /// <summary>Octets vers Go arrondis au plus proche (16 Go affichés 15,9 par Windows donnent 16).</summary>
        public static int ArrondirGo(long octets) => octets <= 0 ? 0 : (int)Math.Round(octets / (1024.0 * 1024 * 1024));

        /// <summary>Un service existe si sa clé existe sous HKLM\SYSTEM\CurrentControlSet\Services.</summary>
        public static bool ServiceExiste(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom) || nom.IndexOf('\\') >= 0) return false;
            return CleExiste(@"HKLM\SYSTEM\CurrentControlSet\Services\" + nom);
        }

        /// <summary>« HKLM\… » (vue 64 bits) ou « HKCU\… » ; toute autre racine est refusée.</summary>
        public static bool CleExiste(string chemin)
        {
            string sous;
            var ruche = Ruche(chemin, out sous);
            if (ruche == null) return false;
            try
            {
                using (var racine = RegistryKey.OpenBaseKey(ruche.Value, RegistryView.Registry64))
                using (var k = racine.OpenSubKey(sous))
                    return k != null;
            }
            catch (Exception) { return true; }   // illisible (droits) : la clé existe
        }

        /// <summary>Sépare la ruche du chemin (« HKLM\X » → LocalMachine, « X ») ; null si la ruche n'est pas HKLM ou HKCU.</summary>
        public static RegistryHive? Ruche(string chemin, out string sous)
        {
            sous = null;
            if (string.IsNullOrWhiteSpace(chemin)) return null;
            var i = chemin.IndexOf('\\');
            if (i <= 0 || i == chemin.Length - 1) return null;
            var tete = chemin.Substring(0, i).ToUpperInvariant();
            sous = chemin.Substring(i + 1);
            if (tete == "HKLM" || tete == "HKEY_LOCAL_MACHINE") return RegistryHive.LocalMachine;
            if (tete == "HKCU" || tete == "HKEY_CURRENT_USER") return RegistryHive.CurrentUser;
            sous = null;
            return null;
        }

        /// <summary>Types de châssis SMBIOS portables : Portable, Laptop, Notebook, Sub Notebook, Tablet, Convertible, Detachable.</summary>
        private static readonly int[] ChassisPortables = { 8, 9, 10, 14, 30, 31, 32 };

        /// <summary>Décision pure : portable si un type de châssis le dit ou si une batterie est présente.</summary>
        public static bool EstPortable(IEnumerable<int> chassisTypes, bool batteriePresente)
        {
            if (batteriePresente) return true;
            foreach (var c in chassisTypes ?? new int[0]) if (Array.IndexOf(ChassisPortables, c) >= 0) return true;
            return false;
        }

        private static List<int> QueryChassis()
        {
            var res = new List<int>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                using (var items = searcher.Get())
                {
                    foreach (ManagementBaseObject o in items)
                    {
                        var t = o["ChassisTypes"] as ushort[];
                        if (t != null) foreach (var c in t) res.Add(c);
                        o.Dispose();
                    }
                }
            }
            catch (Exception) { /* WMI indisponible : non portable par défaut (la batterie reste testée) */ }
            return res;
        }

        public static string WindowsMajorFromBuild(string currentBuild)
        {
            int build;
            if (!int.TryParse(currentBuild, out build)) return "";
            return build >= 22000 ? "11" : "10";
        }

        public static string NormalizeEdition(string editionId)
        {
            if (string.IsNullOrWhiteSpace(editionId)) return "";
            var e = editionId.Trim().ToLowerInvariant();
            if (e.StartsWith("education") || e.StartsWith("professionaleducation")) return "Education";
            if (e.StartsWith("enterprise")) return "Enterprise";
            if (e.StartsWith("core")) return "Home";          // Core, CoreN, CoreSingleLanguage...
            if (e.StartsWith("professional")) return "Pro";   // Professional, ProfessionalWorkstation...
            return "";
        }

        /// <summary>Déduit Intel/AMD/NVIDIA d'un nom de matériel (Win32_Processor / Win32_VideoController).</summary>
        public static string NormalizeVendor(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var n = name.ToLowerInvariant();
            if (n.Contains("nvidia")) return "NVIDIA";
            if (n.Contains("intel")) return "Intel";
            if (n.Contains("amd") || n.Contains("advanced micro devices") || n.Contains("radeon") || n.Contains("ryzen")) return "AMD";
            return "";
        }

        private static List<string> Query(string wql, string prop)
        {
            var result = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(wql))
                using (var items = searcher.Get())
                {
                    foreach (ManagementBaseObject o in items)
                    {
                        result.Add(o[prop] as string ?? "");
                        o.Dispose();
                    }
                }
            }
            catch (Exception) { /* WMI indisponible : valeur inconnue */ }
            return result;
        }
    }
}
