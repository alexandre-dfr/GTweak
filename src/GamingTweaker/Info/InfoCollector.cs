using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace GamingTweaker.Info
{
    public class InfoSection
    {
        public string Titre { get; set; }
        public List<KeyValuePair<string, string>> Lignes { get; } = new List<KeyValuePair<string, string>>();
    }

    public class InfoReport
    {
        public List<InfoSection> Sections { get; } = new List<InfoSection>();

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("GamingTweaker - informations système - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            foreach (var s in Sections)
            {
                sb.AppendLine();
                sb.AppendLine("== " + s.Titre + " ==");
                foreach (var l in s.Lignes) sb.AppendLine(l.Key + " : " + l.Value);
            }
            return sb.ToString();
        }
    }

    /// <summary>Collecte WMI / nvidia-smi. Chaque section est indépendante ; une erreur donne « indisponible ». Aucun accès à LibreHardwareMonitor.</summary>
    public static class InfoCollector
    {
        private static List<KeyValuePair<string, string>> L(params string[] kv)
        {
            var l = new List<KeyValuePair<string, string>>();
            for (int i = 0; i + 1 < kv.Length; i += 2) l.Add(new KeyValuePair<string, string>(kv[i], kv[i + 1]));
            return l;
        }

        public static InfoReport Collect()
        {
            return Build(new List<KeyValuePair<string, Func<List<KeyValuePair<string, string>>>>>
            {
                Sec("Windows", Windows), Sec("CPU", Cpu), Sec("Carte mère", CarteMere), Sec("RAM", Ram), Sec("GPU", Gpu),
                Sec("Disques", Disques), Sec("Réseau", Reseau), Sec("Écrans", Ecrans), Sec("Pilotes", Pilotes),
            });
        }

        private static KeyValuePair<string, Func<List<KeyValuePair<string, string>>>> Sec(string t, Func<List<KeyValuePair<string, string>>> f)
            => new KeyValuePair<string, Func<List<KeyValuePair<string, string>>>>(t, f);

        public static InfoReport Build(IEnumerable<KeyValuePair<string, Func<List<KeyValuePair<string, string>>>>> sections)
        {
            var r = new InfoReport();
            foreach (var s in sections)
            {
                var sec = new InfoSection { Titre = s.Key };
                try
                {
                    var lignes = s.Value();
                    if (lignes == null || lignes.Count == 0) sec.Lignes.Add(new KeyValuePair<string, string>("État", "indisponible : aucune donnée"));
                    else sec.Lignes.AddRange(lignes);
                }
                catch (Exception ex)
                {
                    sec.Lignes.Clear();
                    sec.Lignes.Add(new KeyValuePair<string, string>("État", "indisponible : " + ex.Message));
                }
                r.Sections.Add(sec);
            }
            return r;
        }

        // ---------- WMI ----------
        private static List<Dictionary<string, object>> Wmi(string scope, string query)
        {
            var res = new List<Dictionary<string, object>>();
            using (var s = new ManagementObjectSearcher(scope, query))
            using (var col = s.Get())
                foreach (ManagementBaseObject o in col)
                {
                    var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData p in o.Properties) d[p.Name] = p.Value;
                    res.Add(d);
                    o.Dispose();
                }
            return res;
        }
        private static List<Dictionary<string, object>> Wmi(string query) { return Wmi(@"root\cimv2", query); }

        private static string S(Dictionary<string, object> d, string k)
        {
            object v; if (!d.TryGetValue(k, out v) || v == null) return "—";
            var s = Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
            return s.Length == 0 ? "—" : s;
        }

        private static string Go(object bytes)
        {
            if (bytes == null) return "—";
            double b; if (!double.TryParse(Convert.ToString(bytes, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out b)) return "—";
            return FormatTaille(b);
        }

        public static string FormatTaille(double octets)
        {
            if (octets >= 1099511627776d) return (octets / 1099511627776d).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " To";
            return (octets / 1073741824d).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " Go";
        }

        private static List<KeyValuePair<string, string>> Windows()
        {
            var o = Wmi("SELECT Caption,Version,BuildNumber,OSArchitecture,InstallDate,LastBootUpTime FROM Win32_OperatingSystem").First();
            var l = L("Système", S(o, "Caption"), "Version", S(o, "Version") + " (build " + S(o, "BuildNumber") + ")", "Architecture", S(o, "OSArchitecture"));
            try { l.Add(new KeyValuePair<string, string>("Dernier démarrage", ManagementDateTimeConverter.ToDateTime(S(o, "LastBootUpTime")).ToString("g", CultureInfo.GetCultureInfo("fr-FR")))); } catch { }
            l.Add(new KeyValuePair<string, string>("Ordinateur", Environment.MachineName));
            return l;
        }

        private static List<KeyValuePair<string, string>> Cpu()
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var c in Wmi("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,L3CacheSize FROM Win32_Processor"))
            {
                l.Add(new KeyValuePair<string, string>("Processeur", S(c, "Name")));
                l.Add(new KeyValuePair<string, string>("Cœurs / threads", S(c, "NumberOfCores") + " / " + S(c, "NumberOfLogicalProcessors")));
                l.Add(new KeyValuePair<string, string>("Fréquence max", S(c, "MaxClockSpeed") + " MHz"));
                l.Add(new KeyValuePair<string, string>("Cache L3", S(c, "L3CacheSize") + " Ko"));
            }
            return l;
        }

        private static List<KeyValuePair<string, string>> CarteMere()
        {
            var b = Wmi("SELECT Manufacturer,Product,Version FROM Win32_BaseBoard").First();
            var l = L("Fabricant", S(b, "Manufacturer"), "Modèle", S(b, "Product"));
            var bios = Wmi("SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS").FirstOrDefault();
            if (bios != null)
            {
                l.Add(new KeyValuePair<string, string>("BIOS", S(bios, "Manufacturer") + " " + S(bios, "SMBIOSBIOSVersion")));
                try { l.Add(new KeyValuePair<string, string>("Date du BIOS", ManagementDateTimeConverter.ToDateTime(S(bios, "ReleaseDate")).ToString("d", CultureInfo.GetCultureInfo("fr-FR")))); } catch { }
            }
            return l;
        }

        private static List<KeyValuePair<string, string>> Ram()
        {
            var l = new List<KeyValuePair<string, string>>();
            double total = 0; int n = 0;
            foreach (var m in Wmi("SELECT BankLabel,Capacity,Speed,ConfiguredClockSpeed,Manufacturer,PartNumber,SMBIOSMemoryType FROM Win32_PhysicalMemory"))
            {
                n++;
                double c; double.TryParse(S(m, "Capacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out c); total += c;
                var type = S(m, "SMBIOSMemoryType"); var t = type == "26" ? "DDR4" : type == "34" ? "DDR5" : type == "24" ? "DDR3" : "";
                l.Add(new KeyValuePair<string, string>("Barrette " + n, Go(m["Capacity"]) + " " + t + " " + S(m, "ConfiguredClockSpeed") + " MHz (" + S(m, "Manufacturer") + " " + S(m, "PartNumber") + ")"));
            }
            l.Insert(0, new KeyValuePair<string, string>("Total", FormatTaille(total)));
            return l;
        }

        private static List<KeyValuePair<string, string>> Gpu()
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var g in Wmi("SELECT Name,DriverVersion,AdapterRAM FROM Win32_VideoController"))
                l.Add(new KeyValuePair<string, string>(S(g, "Name"), "pilote " + S(g, "DriverVersion")));
            var smi = NvidiaSmi();
            if (smi != null) l.Add(new KeyValuePair<string, string>("nvidia-smi", smi));
            return l;
        }

        private static string NvidiaSmi()
        {
            try
            {
                var exe = new[] { Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe") }.FirstOrDefault(File.Exists);
                if (exe == null) return null;
                var psi = new ProcessStartInfo(exe, "--query-gpu=name,memory.total,driver_version --format=csv,noheader")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return null; }
                    o = o.Trim();
                    return o.Length == 0 ? null : o.Replace("\r\n", " | ").Replace("\n", " | ");
                }
            }
            catch { return null; }
        }

        private static List<KeyValuePair<string, string>> Disques()
        {
            var l = new List<KeyValuePair<string, string>>();
            List<Dictionary<string, object>> phys = null;
            try { phys = Wmi(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName,MediaType,BusType,Size,HealthStatus,OperationalStatus FROM MSFT_PhysicalDisk"); } catch { }
            if (phys != null)
                foreach (var d in phys)
                {
                    var media = S(d, "MediaType"); media = media == "3" ? "HDD" : media == "4" ? "SSD" : media == "5" ? "SCM" : "";
                    var health = S(d, "HealthStatus"); health = health == "0" ? "Sain" : health == "1" ? "Avertissement" : health == "2" ? "Défaillant" : health;
                    l.Add(new KeyValuePair<string, string>(S(d, "FriendlyName"), (media + " " + Go(d["Size"]) + " - état : " + health).Trim()));
                }
            else
                foreach (var d in Wmi("SELECT Model,Size,MediaType FROM Win32_DiskDrive"))
                    l.Add(new KeyValuePair<string, string>(S(d, "Model"), Go(d["Size"])));
            try
            {
                var smart = Wmi(@"root\wmi", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus");
                var ko = smart.Count(x => S(x, "PredictFailure") == "True");
                l.Add(new KeyValuePair<string, string>("SMART", smart.Count == 0 ? "indisponible" : ko == 0 ? "aucune défaillance prévue" : ko + " disque(s) annoncent une défaillance"));
            }
            catch (ManagementException ex)
            {
                l.Add(new KeyValuePair<string, string>("SMART", ex.ErrorCode == ManagementStatus.AccessDenied ? "indisponible sans droits administrateur" : "indisponible : " + ex.Message));
            }
            catch (UnauthorizedAccessException) { l.Add(new KeyValuePair<string, string>("SMART", "indisponible sans droits administrateur")); }
            foreach (var v in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
                l.Add(new KeyValuePair<string, string>("Volume " + v.Name, FormatTaille(v.TotalSize - v.AvailableFreeSpace) + " utilisés sur " + FormatTaille(v.TotalSize)));
            return l;
        }

        private static List<KeyValuePair<string, string>> Reseau()
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var a in Wmi("SELECT Description,MACAddress,Speed,NetEnabled,NetConnectionID FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True AND NetConnectionStatus=2"))
            {
                double sp; double.TryParse(S(a, "Speed"), NumberStyles.Float, CultureInfo.InvariantCulture, out sp);
                l.Add(new KeyValuePair<string, string>(S(a, "NetConnectionID"), S(a, "Description") + (sp > 0 ? " - " + (sp / 1e6).ToString("0", CultureInfo.InvariantCulture) + " Mb/s" : "")));
            }
            if (l.Count == 0) l.Add(new KeyValuePair<string, string>("État", "aucune carte réseau connectée"));
            return l;
        }

        // ---------- Écrans ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] private static extern bool EnumDisplayDevices(string device, int index, ref DISPLAY_DEVICE dd, int flags);

        private static List<KeyValuePair<string, string>> Ecrans()
        {
            var l = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < 16; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & 1) == 0) continue; // ATTACHED_TO_DESKTOP
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (!EnumDisplaySettings(dd.DeviceName, -1, ref dm)) continue;
                l.Add(new KeyValuePair<string, string>(dd.DeviceName.TrimStart('\\', '.') + " (" + dd.DeviceString + ")",
                    dm.dmPelsWidth + "x" + dm.dmPelsHeight + " à " + dm.dmDisplayFrequency + " Hz"));
            }
            return l;
        }

        private static List<KeyValuePair<string, string>> Pilotes()
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var cls in new[] { "DISPLAY", "NET", "MEDIA", "SYSTEM" })
            {
                var etiquette = cls == "DISPLAY" ? "Affichage" : cls == "NET" ? "Réseau" : cls == "MEDIA" ? "Audio" : "Chipset";
                var q = cls == "SYSTEM"
                    ? "SELECT DeviceName,DriverVersion,DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass='SYSTEM' AND (DeviceName LIKE '%chipset%' OR DeviceName LIKE '%SMBus%' OR DeviceName LIKE '%PCI Express Root%')"
                    : "SELECT DeviceName,DriverVersion,DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass='" + cls + "'";
                foreach (var d in Wmi(q).Where(x => S(x, "DeviceName") != "—").Take(6))
                {
                    string date = "";
                    try { date = ", " + ManagementDateTimeConverter.ToDateTime(S(d, "DriverDate")).ToString("d", CultureInfo.GetCultureInfo("fr-FR")); } catch { }
                    l.Add(new KeyValuePair<string, string>(etiquette + " : " + S(d, "DeviceName"), S(d, "DriverVersion") + date));
                }
            }
            return l;
        }
    }
}
