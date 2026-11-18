using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GamingTweaker.Core
{
    public class HdrEcran { public bool Pris; public bool Actif; }

    /// <summary>Plage de fréquence verticale annoncée par l'écran (EDID) : une plage large indique la fréquence variable (FreeSync, G-SYNC Compatible).</summary>
    public class PlageEcran { public string Nom; public int HzMin, HzMax; }

    /// <summary>Lecture seule de l'état HDR (API DisplayConfig de Windows) et de la plage de fréquence des écrans (EDID).</summary>
    public static class Affichage
    {
        // ---------- EDID ----------

        /// <summary>
        /// Plage de fréquence verticale du descripteur « Display Range Limits » (balise 0xFD) d'un bloc EDID de base, ou null.
        /// Octet 4 du descripteur : bits de décalage +255 (bit 0 : minimum, bit 1 : maximum).
        /// </summary>
        public static Tuple<int, int> PlageVerticale(byte[] edid)
        {
            if (edid == null || edid.Length < 128) return null;
            for (int d = 54; d <= 108; d += 18)
            {
                if (edid[d] != 0 || edid[d + 1] != 0 || edid[d + 3] != 0xFD) continue;
                int drapeaux = edid[d + 4];
                int min = edid[d + 5] + ((drapeaux & 0x01) != 0 ? 255 : 0);
                int max = edid[d + 6] + ((drapeaux & 0x02) != 0 ? 255 : 0);
                if (min > 0 && max >= min) return Tuple.Create(min, max);
            }
            return null;
        }

        /// <summary>Nom de l'écran (descripteur 0xFC de l'EDID), ou null.</summary>
        public static string NomEdid(byte[] edid)
        {
            if (edid == null || edid.Length < 128) return null;
            for (int d = 54; d <= 108; d += 18)
            {
                if (edid[d] != 0 || edid[d + 1] != 0 || edid[d + 3] != 0xFC) continue;
                var s = new string(Enumerable.Range(d + 5, 13).Select(i => (char)edid[i]).TakeWhile(c => c != '\n').ToArray()).Trim();
                return s.Length == 0 ? null : s;
            }
            return null;
        }

        /// <summary>Écrans actifs (WMI WmiMonitorBasicDisplayParams) et leur EDID lu dans le registre.</summary>
        public static List<PlageEcran> PlagesEcrans()
        {
            var res = new List<PlageEcran>();
            using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, Active FROM WmiMonitorBasicDisplayParams"))
            using (var col = s.Get())
                foreach (ManagementBaseObject o in col)
                {
                    var actif = o["Active"] is bool && (bool)o["Active"];
                    var inst = Convert.ToString(o["InstanceName"]);
                    o.Dispose();
                    if (!actif || string.IsNullOrEmpty(inst)) continue;
                    // « DISPLAY\GSM5B7F\5&1a2b3c&0&UID4352_0 » → Enum\DISPLAY\GSM5B7F\5&1a2b3c&0&UID4352\Device Parameters\EDID
                    var chemin = inst.EndsWith("_0", StringComparison.Ordinal) ? inst.Substring(0, inst.Length - 2) : inst;
                    try
                    {
                        using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + chemin + @"\Device Parameters"))
                        {
                            var edid = k == null ? null : k.GetValue("EDID") as byte[];
                            var p = PlageVerticale(edid);
                            if (p != null) res.Add(new PlageEcran { Nom = NomEdid(edid) ?? chemin.Split('\\').Skip(1).FirstOrDefault(), HzMin = p.Item1, HzMax = p.Item2 });
                        }
                    }
                    catch (Exception) { }
                }
            return res;
        }

        // ---------- HDR (DisplayConfigGetDeviceInfo, DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO) ----------

        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint nPaths, byte[] paths, ref uint nModes, byte[] modes, IntPtr topology);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(byte[] packet);

        private const uint QdcOnlyActivePaths = 2;
        private const int TaillePath = 72, TailleMode = 64;

        /// <summary>État HDR de chaque écran actif (pris en charge / activé).</summary>
        public static List<HdrEcran> Hdr()
        {
            var res = new List<HdrEcran>();
            uint np, nm;
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out np, out nm) != 0) return res;
            var paths = new byte[np * TaillePath]; var modes = new byte[nm * TailleMode];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return res;
            for (int i = 0; i < np; i++)
            {
                int b = i * TaillePath;
                // targetInfo commence à l'octet 20 du chemin : adapterId (LUID, 8 octets) puis id (4 octets).
                var paquet = new byte[32];
                BitConverter.GetBytes(9).CopyTo(paquet, 0);              // type : GET_ADVANCED_COLOR_INFO
                BitConverter.GetBytes(32).CopyTo(paquet, 4);             // taille
                Array.Copy(paths, b + 20, paquet, 8, 8);                 // adapterId
                Array.Copy(paths, b + 28, paquet, 16, 4);                // id de la cible
                if (DisplayConfigGetDeviceInfo(paquet) != 0) continue;
                uint v = BitConverter.ToUInt32(paquet, 20);
                res.Add(new HdrEcran { Pris = (v & 1) != 0, Actif = (v & 2) != 0 });
            }
            return res;
        }
    }

    /// <summary>Programmes lancés à l'ouverture de session (clés Run et dossiers Démarrage), hors entrées désactivées dans le Gestionnaire des tâches.</summary>
    public static class ProgrammesDemarrage
    {
        /// <summary>Désactivée si le premier octet de la valeur StartupApproved est impair (03, 07…).</summary>
        public static bool Desactive(byte[] approuve) { return approuve != null && approuve.Length > 0 && (approuve[0] & 1) == 1; }

        public static int Compter()
        {
            int n = 0;
            const string cv = @"Software\Microsoft\Windows\CurrentVersion";
            n += CompterCle(Registry.CurrentUser, cv + @"\Run", Registry.CurrentUser, cv + @"\Explorer\StartupApproved\Run");
            n += CompterCle(Registry.LocalMachine, cv + @"\Run", Registry.LocalMachine, cv + @"\Explorer\StartupApproved\Run");
            n += CompterCle(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", Registry.LocalMachine, cv + @"\Explorer\StartupApproved\Run32");
            n += CompterDossier(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser);
            n += CompterDossier(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine);
            return n;
        }

        private static int CompterCle(RegistryKey ruche, string chemin, RegistryKey rucheApprouve, string cheminApprouve)
        {
            try
            {
                using (var k = ruche.OpenSubKey(chemin))
                using (var a = rucheApprouve.OpenSubKey(cheminApprouve))
                {
                    if (k == null) return 0;
                    return k.GetValueNames().Count(nom => !string.IsNullOrEmpty(nom) && !Desactive(a == null ? null : a.GetValue(nom) as byte[]));
                }
            }
            catch (Exception) { return 0; }
        }

        private static int CompterDossier(string dossier, RegistryKey ruche)
        {
            try
            {
                if (string.IsNullOrEmpty(dossier) || !System.IO.Directory.Exists(dossier)) return 0;
                using (var a = ruche.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"))
                    return System.IO.Directory.GetFiles(dossier).Select(System.IO.Path.GetFileName)
                        .Count(f => !string.Equals(f, "desktop.ini", StringComparison.OrdinalIgnoreCase) && !Desactive(a == null ? null : a.GetValue(f) as byte[]));
            }
            catch (Exception) { return 0; }
        }
    }
}
