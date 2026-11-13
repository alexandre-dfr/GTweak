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
                    facts.Edition = NormalizeEdition(cv?.GetValue("EditionID") as string);
                }
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    facts.MachineGuid = key?.GetValue("MachineGuid") as string ?? "";
                }
            }

            foreach (var name in Query("SELECT Name FROM Win32_Processor", "Name"))
            {
                var v = NormalizeVendor(name);
                if (v != "") { facts.CpuVendor = v; break; }
            }
            foreach (var name in Query("SELECT Name FROM Win32_VideoController", "Name"))
            {
                var v = NormalizeVendor(name);
                if (v != "" && !facts.GpuVendors.Contains(v)) facts.GpuVendors.Add(v);
            }
            facts.EstPortable = EstPortable(QueryChassis(), Query("SELECT Name FROM Win32_Battery", "Name").Count > 0);
            return facts;
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
