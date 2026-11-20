using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace GamingTweaker.Core
{
    /// <summary>Une carte graphique lue dans le registre (classe Display).</summary>
    public class CarteGraphique
    {
        public string Nom { get; set; }
        /// <summary>Mémoire vidéo dédiée en octets (0 : inconnue).</summary>
        public long Memoire { get; set; }
        public string Pilote { get; set; }
    }

    /// <summary>Tuile matériel de l'accueil : processeur, carte graphique, mémoire ou disque système.</summary>
    public class TuileMateriel
    {
        public string Libelle { get; set; }
        /// <summary>Icône Windows (Segoe Fluent Icons).</summary>
        public string Icone { get; set; }
        public string Valeur { get; set; }
        public string Detail { get; set; }
        /// <summary>Part occupée (0 à 1) pour une jauge ; null : pas de jauge.</summary>
        public double? Proportion { get; set; }
        public bool AJauge { get { return Proportion.HasValue; } }
        /// <summary>Section du menu Informations ouverte par un clic sur la tuile (« CPU », « GPU », « RAM », « Disques »).</summary>
        public string Section { get; set; }
        /// <summary>Alerte à afficher sur la tuile (disque presque plein), ou null.</summary>
        public string Alerte { get; set; }
        public bool AAlerte { get { return !string.IsNullOrEmpty(Alerte); } }
        public double ValeurJauge { get { return Proportion ?? 0; } }
    }

    /// <summary>
    /// Résumé du matériel pour l'accueil : processeur, carte graphique, mémoire, disque système et son espace.
    /// Lecture rapide (registre, DriveInfo, quelques requêtes WMI ciblées), hors du thread de l'interface ; chaque partie échoue seule.
    /// </summary>
    public static class MaterielPc
    {
        public const string Lecture = "Lecture…";
        public const string Inconnu = "Inconnu";
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public const string IconeCpu = "\uE950", IconeGpu = "\uE7F4", IconeRam = "\uE81E", IconeDisque = "\uEDA2";

        /// <summary>Section du menu Informations correspondant à une tuile.</summary>
        public static string SectionDe(string libelle)
        {
            switch (libelle) { case "Processeur": return "CPU"; case "Carte graphique": return "GPU"; case "Mémoire": return "RAM"; case "Disque système": return "Disques"; default: return null; }
        }

        /// <summary>Même seuil que le diagnostic : moins de 20 Go ou moins de 10 % libres.</summary>
        public static bool DisquePresquePlein(double totalOctets, double libreOctets)
        {
            return totalOctets > 0 && (libreOctets < 20 * 1073741824d || libreOctets / totalOctets < 0.10);
        }

        /// <summary>Tuiles d'attente, avant la lecture.</summary>
        public static IReadOnlyList<TuileMateriel> EnLecture()
        {
            return new[]
            {
                new TuileMateriel { Libelle = "Processeur", Icone = IconeCpu, Valeur = Lecture, Section = "CPU" },
                new TuileMateriel { Libelle = "Carte graphique", Icone = IconeGpu, Valeur = Lecture, Section = "GPU" },
                new TuileMateriel { Libelle = "Mémoire", Icone = IconeRam, Valeur = Lecture, Section = "RAM" },
                new TuileMateriel { Libelle = "Disque système", Icone = IconeDisque, Valeur = Lecture, Section = "Disques" },
            };
        }

        public static IReadOnlyList<TuileMateriel> Lire()
        {
            return new[] { Essayer("Processeur", IconeCpu, Processeur), Essayer("Carte graphique", IconeGpu, Graphique),
                           Essayer("Mémoire", IconeRam, Memoire), Essayer("Disque système", IconeDisque, Disque) };
        }

        private static TuileMateriel Essayer(string libelle, string icone, Func<TuileMateriel> f)
        {
            TuileMateriel t;
            try { t = f() ?? new TuileMateriel { Valeur = Inconnu }; }
            catch (Exception ex) { TraceAppli.Erreur("accueil : lecture « " + libelle + " »", ex); t = new TuileMateriel { Valeur = Inconnu }; }
            t.Libelle = libelle; t.Icone = icone; t.Section = SectionDe(libelle);
            if (string.IsNullOrWhiteSpace(t.Valeur)) t.Valeur = Inconnu;
            return t;
        }

        // ---------- Mise en forme (pure, testée) ----------

        public static string Taille(double octets)
        {
            if (octets >= 1099511627776d) return (octets / 1099511627776d).ToString("0.#", Fr) + " To";
            return (octets / 1073741824d).ToString(octets >= 100 * 1073741824d ? "0" : "0.#", Fr) + " Go";
        }

        /// <summary>« Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz » → « Intel Core i7-12700K » ; espaces multiples réduits.</summary>
        public static string NettoyerNomCpu(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return null;
            var s = nom.Replace("(R)", "").Replace("(r)", "").Replace("(TM)", "").Replace("(tm)", "").Replace("®", "").Replace("™", "");
            var at = s.IndexOf(" @ ", StringComparison.Ordinal);
            if (at > 0) s = s.Substring(0, at);
            s = s.Trim();
            foreach (var fin in new[] { " CPU", " Processor" })
                if (s.EndsWith(fin, StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - fin.Length);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+\d+-Core$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s{2,}", " ").Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>Adaptateurs à ignorer : affichage de base, bureau à distance, écrans virtuels.</summary>
        public static bool EstVirtuelle(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return true;
            var n = nom.ToLowerInvariant();
            return n.Contains("basic display") || n.Contains("basic render") || n.Contains("remote display") || n.Contains("virtual")
                || n.Contains("parsec") || n.Contains("indirect display") || n.Contains("hyper-v") || n.Contains("vmware") || n.Contains("virtualbox");
        }

        /// <summary>Carte principale : une carte dédiée (NVIDIA, AMD Radeon, Intel Arc) avant une carte intégrée, puis la plus grosse mémoire.</summary>
        public static CarteGraphique ChoisirCarte(IEnumerable<CarteGraphique> cartes)
        {
            return (cartes ?? Enumerable.Empty<CarteGraphique>()).Where(c => c != null && !EstVirtuelle(c.Nom))
                .OrderByDescending(c => RangCarte(c.Nom)).ThenByDescending(c => c.Memoire).FirstOrDefault();
        }

        private static int RangCarte(string nom)
        {
            var n = nom.ToLowerInvariant();
            if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("quadro")) return 3;
            if (n.Contains("radeon rx") || n.Contains("radeon pro")) return 3;
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\barc\b")) return 2;
            if (n.Contains("amd") || n.Contains("radeon")) return 1;
            return 0;
        }

        public static TuileMateriel TuileGraphique(IList<CarteGraphique> cartes)
        {
            var c = ChoisirCarte(cartes);
            if (c == null) return new TuileMateriel { Valeur = Inconnu };
            var details = new List<string>();
            if (c.Memoire > 0) details.Add(Taille(c.Memoire) + " de mémoire vidéo");
            if (!string.IsNullOrWhiteSpace(c.Pilote)) details.Add("pilote " + c.Pilote.Trim());
            var autres = cartes.Where(x => x != null && x != c && !EstVirtuelle(x.Nom)).Select(x => x.Nom.Trim()).Distinct().ToList();
            if (autres.Count > 0) details.Add("+ " + string.Join(", ", autres));
            return new TuileMateriel { Valeur = c.Nom.Trim(), Detail = string.Join(" · ", details) };
        }

        public static TuileMateriel TuileMemoire(double totalOctets, string type, int vitesseMhz, int barrettes, double disponibleOctets)
        {
            if (totalOctets <= 0) return new TuileMateriel { Valeur = Inconnu };
            var valeur = Math.Round(totalOctets / 1073741824d).ToString("0", Fr) + " Go" + (string.IsNullOrEmpty(type) ? "" : " " + type);
            var details = new List<string>();
            if (vitesseMhz > 0) details.Add(vitesseMhz.ToString(Fr) + " MT/s");
            if (barrettes > 0) details.Add(barrettes + " barrette" + (barrettes > 1 ? "s" : ""));
            double? part = null;
            if (disponibleOctets > 0 && disponibleOctets <= totalOctets)
            {
                part = 1 - disponibleOctets / totalOctets;
                details.Add(Taille(disponibleOctets) + " libres");
            }
            return new TuileMateriel { Valeur = valeur, Detail = string.Join(" · ", details), Proportion = part };
        }

        /// <summary>Type de disque d'après MSFT_PhysicalDisk : MediaType 3 = HDD, 4 = SSD ; BusType 17 = NVMe.</summary>
        public static string TypeDisque(int mediaType, int busType)
        {
            if (busType == 17) return "SSD NVMe";
            if (mediaType == 4) return "SSD";
            if (mediaType == 3) return "HDD";
            return null;
        }

        public static TuileMateriel TuileDisque(string lecteur, string type, string modele, double totalOctets, double libreOctets)
        {
            if (totalOctets <= 0) return new TuileMateriel { Valeur = Inconnu };
            var titre = (lecteur ?? "").TrimEnd('\\') + (string.IsNullOrEmpty(type) ? "" : " · " + type);
            var detail = Taille(libreOctets) + " libres sur " + Taille(totalOctets) + (string.IsNullOrWhiteSpace(modele) ? "" : " · " + modele.Trim());
            return new TuileMateriel
            {
                Valeur = titre.Trim(), Detail = detail, Proportion = Math.Max(0, Math.Min(1, 1 - libreOctets / totalOctets)),
                Alerte = DisquePresquePlein(totalOctets, libreOctets) ? "Disque presque plein : " + Taille(libreOctets) + " libres." : null,
            };
        }

        public static string TypeMemoire(int smbios)
        {
            switch (smbios) { case 24: return "DDR3"; case 26: return "DDR4"; case 34: return "DDR5"; default: return null; }
        }

        // ---------- Lecture sur ce PC ----------

        private static TuileMateriel Processeur()
        {
            string nom = null;
            using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                if (k != null) nom = k.GetValue("ProcessorNameString") as string;
            int coeurs = 0, threads = Environment.ProcessorCount;
            try
            {
                foreach (var p in Wmi(@"root\cimv2", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    coeurs += Entier(p, "NumberOfCores");
                    if (nom == null) nom = Texte(p, "Name");
                }
            }
            catch (Exception ex) { TraceAppli.Erreur("accueil : cœurs du processeur", ex); }
            var detail = coeurs > 0 ? coeurs + " cœurs · " + threads + " threads" : threads + " threads";
            return new TuileMateriel { Valeur = NettoyerNomCpu(nom), Detail = detail };
        }

        private const string ClasseAffichage = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private static TuileMateriel Graphique()
        {
            var cartes = new List<CarteGraphique>();
            using (var classe = Registry.LocalMachine.OpenSubKey(ClasseAffichage))
                if (classe != null)
                    foreach (var sous in classe.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
                        try
                        {
                            using (var k = classe.OpenSubKey(sous))
                            {
                                var nom = k == null ? null : k.GetValue("DriverDesc") as string;
                                if (string.IsNullOrWhiteSpace(nom)) continue;
                                cartes.Add(new CarteGraphique { Nom = nom, Pilote = k.GetValue("DriverVersion") as string, Memoire = MemoireVideo(k) });
                            }
                        }
                        catch (System.Security.SecurityException) { }
            if (cartes.Count == 0)
                foreach (var g in Wmi(@"root\cimv2", "SELECT Name,DriverVersion FROM Win32_VideoController"))
                    cartes.Add(new CarteGraphique { Nom = Texte(g, "Name"), Pilote = Texte(g, "DriverVersion") });
            return TuileGraphique(cartes);
        }

        /// <summary>HardwareInformation.qwMemorySize (QWORD, exact au-delà de 4 Go), sinon HardwareInformation.MemorySize.</summary>
        private static long MemoireVideo(RegistryKey k)
        {
            foreach (var nom in new[] { "HardwareInformation.qwMemorySize", "HardwareInformation.MemorySize" })
            {
                var v = k.GetValue(nom);
                if (v is long) return (long)v;
                if (v is int) return (uint)(int)v;
                var b = v as byte[];
                if (b != null && b.Length >= 8) return BitConverter.ToInt64(b, 0);
                if (b != null && b.Length >= 4) return BitConverter.ToUInt32(b, 0);
            }
            return 0;
        }

        private static TuileMateriel Memoire()
        {
            double total = 0; int barrettes = 0, vitesse = 0; string type = null;
            foreach (var m in Wmi(@"root\cimv2", "SELECT Capacity,ConfiguredClockSpeed,Speed,SMBIOSMemoryType FROM Win32_PhysicalMemory"))
            {
                barrettes++;
                total += Reel(m, "Capacity");
                var v = Entier(m, "ConfiguredClockSpeed"); if (v <= 0) v = Entier(m, "Speed");
                if (v > 0) vitesse = vitesse == 0 ? v : Math.Min(vitesse, v);
                type = type ?? TypeMemoire(Entier(m, "SMBIOSMemoryType"));
            }
            double dispo = 0;
            foreach (var o in Wmi(@"root\cimv2", "SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem"))
            {
                dispo = Reel(o, "FreePhysicalMemory") * 1024;
                if (total <= 0) total = Reel(o, "TotalVisibleMemorySize") * 1024;
            }
            return TuileMemoire(total, type, vitesse, barrettes, dispo);
        }

        private static TuileMateriel Disque()
        {
            var racine = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var d = new DriveInfo(racine);
            string type = null, modele = null;
            try
            {
                var lettre = racine.Substring(0, 1);
                var partition = Wmi(@"root\Microsoft\Windows\Storage", "SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter='" + lettre + "'").FirstOrDefault();
                if (partition != null)
                {
                    var numero = Entier(partition, "DiskNumber");
                    var phys = Wmi(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName,MediaType,BusType FROM MSFT_PhysicalDisk WHERE DeviceId='" + numero + "'").FirstOrDefault();
                    if (phys != null) { type = TypeDisque(Entier(phys, "MediaType"), Entier(phys, "BusType")); modele = Texte(phys, "FriendlyName"); }
                }
            }
            catch (Exception ex) { TraceAppli.Erreur("accueil : type du disque système", ex); }
            return TuileDisque(racine, type, modele, d.TotalSize, d.AvailableFreeSpace);
        }

        // ---------- WMI ----------

        private static List<Dictionary<string, object>> Wmi(string scope, string requete)
        {
            var res = new List<Dictionary<string, object>>();
            using (var s = new ManagementObjectSearcher(scope, requete))
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

        private static string Texte(Dictionary<string, object> d, string k)
        {
            object v; if (!d.TryGetValue(k, out v) || v == null) return null;
            var s = Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
            return s.Length == 0 ? null : s;
        }

        private static double Reel(Dictionary<string, object> d, string k)
        {
            double r; return double.TryParse(Texte(d, k), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        private static int Entier(Dictionary<string, object> d, string k) { return (int)Math.Max(0, Math.Min(int.MaxValue, Reel(d, k))); }
    }
}
