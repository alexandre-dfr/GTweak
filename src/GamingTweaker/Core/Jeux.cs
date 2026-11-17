using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Jeu surveillé par le Mode Jeu automatique. Chemin : un .exe (ajouté à la main), ou le dossier d'installation d'un jeu Steam ou Epic
    /// (tout exécutable lancé depuis ce dossier ou ses sous-dossiers compte : ces bibliothèques n'enregistrent pas toujours l'exe lancé).
    /// </summary>
    public class JeuSurveille
    {
        [JsonProperty("nom")] public string Nom { get; set; }
        [JsonProperty("chemin")] public string Chemin { get; set; }
        /// <summary>« exe », « Steam » ou « Epic ».</summary>
        [JsonProperty("source")] public string Source { get; set; }
        [JsonIgnore] public bool EstDossier { get { return !string.Equals(Source, "exe", StringComparison.OrdinalIgnoreCase); } }
    }

    public static class Jeux
    {
        /// <summary>Exécutables jamais considérés comme un jeu, même dans un dossier de jeu (lanceurs, outils de plantage, anti-triche).</summary>
        public static readonly string[] JamaisUnJeu =
        {
            "steam", "steamwebhelper", "steamerrorreporter", "steamerrorreporter64", "EpicGamesLauncher", "EpicWebHelper", "UnityCrashHandler64", "UnityCrashHandler32",
            "CrashReportClient", "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat_Setup", "BEService", "BEService_x64", "vc_redist.x64", "vc_redist.x86",
            "DXSETUP", "dotNetFx40_Full_x86_x64", "UE4PrereqSetup_x64", "UEPrereqSetup_x64",
        };

        /// <summary>Vrai si le processus (chemin complet de son exe) correspond à ce jeu.</summary>
        public static bool Correspond(string cheminProcessus, JeuSurveille j)
        {
            if (string.IsNullOrWhiteSpace(cheminProcessus) || j == null || string.IsNullOrWhiteSpace(j.Chemin)) return false;
            string p, c;
            try { p = Path.GetFullPath(cheminProcessus.Trim()); c = Path.GetFullPath(j.Chemin.Trim()).TrimEnd('\\'); }
            catch (Exception) { return false; }
            var nom = Path.GetFileNameWithoutExtension(p);
            if (JamaisUnJeu.Any(x => string.Equals(x, nom, StringComparison.OrdinalIgnoreCase))) return false;
            if (!j.EstDossier) return string.Equals(p, c, StringComparison.OrdinalIgnoreCase);
            if (c.Length <= 3) return false;   // jamais une racine de disque
            return p.StartsWith(c + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Jeux de la liste qui tournent (au moins un processus correspondant), dans l'ordre de la liste.</summary>
        public static List<JeuSurveille> EnCours(IEnumerable<string> cheminsProcessus, IEnumerable<JeuSurveille> jeux)
        {
            var chemins = (cheminsProcessus ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            return (jeux ?? Enumerable.Empty<JeuSurveille>()).Where(j => chemins.Any(p => Correspond(p, j))).ToList();
        }

        // ---------- Steam ----------

        private static readonly Regex VdfPaire = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);

        private static string Vdf(string s) { return s.Replace("\\\\", "\\").Replace("\\\"", "\""); }

        /// <summary>Dossiers de bibliothèque dans libraryfolders.vdf (clés « path »).</summary>
        public static List<string> BibliothequesSteam(string libraryfoldersVdf)
        {
            var res = new List<string>();
            foreach (Match m in VdfPaire.Matches(libraryfoldersVdf ?? ""))
                if (string.Equals(m.Groups[1].Value, "path", StringComparison.OrdinalIgnoreCase)) res.Add(Vdf(m.Groups[2].Value));
            return res.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Nom et dossier (installdir) d'un appmanifest_*.acf ; null s'il en manque un.</summary>
        public static Tuple<string, string> ManifesteSteam(string acf)
        {
            string nom = null, dossier = null;
            foreach (Match m in VdfPaire.Matches(acf ?? ""))
            {
                var k = m.Groups[1].Value;
                if (nom == null && string.Equals(k, "name", StringComparison.OrdinalIgnoreCase)) nom = Vdf(m.Groups[2].Value);
                else if (dossier == null && string.Equals(k, "installdir", StringComparison.OrdinalIgnoreCase)) dossier = Vdf(m.Groups[2].Value);
            }
            if (string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(dossier) || dossier.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || dossier.Contains("..")) return null;
            return Tuple.Create(nom, dossier);
        }

        /// <summary>Outils Steam qui ne sont pas des jeux.</summary>
        private static bool OutilSteam(string nom)
        {
            return Regex.IsMatch(nom ?? "", @"^(Steamworks Common Redistributables|Steam Linux Runtime|Proton|SteamVR)", RegexOptions.IgnoreCase);
        }

        /// <summary>Jeux Steam installés (dossier racine de Steam d'après le registre de l'utilisateur, sinon Program Files (x86)\Steam).</summary>
        public static List<JeuSurveille> JeuxSteam(string dossierSteam = null)
        {
            var res = new List<JeuSurveille>();
            dossierSteam = dossierSteam ?? DossierSteam();
            if (string.IsNullOrEmpty(dossierSteam)) return res;
            var bibliotheques = new List<string> { dossierSteam };
            try
            {
                var vdf = Path.Combine(dossierSteam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) bibliotheques.AddRange(BibliothequesSteam(File.ReadAllText(vdf, Encoding.UTF8)));
            }
            catch (Exception) { }
            foreach (var b in bibliotheques.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var apps = Path.Combine(b, "steamapps");
                    if (!Directory.Exists(apps)) continue;
                    foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                    {
                        Tuple<string, string> m;
                        try { m = ManifesteSteam(File.ReadAllText(acf, Encoding.UTF8)); } catch (Exception) { continue; }
                        if (m == null || OutilSteam(m.Item1)) continue;
                        var dossier = Path.Combine(apps, "common", m.Item2);
                        if (Directory.Exists(dossier)) res.Add(new JeuSurveille { Nom = m.Item1, Chemin = dossier, Source = "Steam" });
                    }
                }
                catch (Exception) { }
            }
            return Dedoublonner(res);
        }

        private static string DossierSteam()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var p = k == null ? null : k.GetValue("SteamPath") as string;
                    if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) return Path.GetFullPath(p);
                }
            }
            catch (Exception) { }
            var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return Directory.Exists(pf) ? pf : null;
        }

        // ---------- Epic Games ----------

        /// <summary>Nom et dossier d'installation d'un manifeste Epic (*.item, JSON) ; null si incomplet ou si ce n'est pas un jeu.</summary>
        public static Tuple<string, string> ManifesteEpic(string json)
        {
            try
            {
                var o = JObject.Parse(json ?? "");
                var nom = (string)o["DisplayName"];
                var dossier = (string)o["InstallLocation"];
                var categories = o["AppCategories"] as JArray;
                if (categories != null && categories.Count > 0 && !categories.Any(c => string.Equals((string)c, "games", StringComparison.OrdinalIgnoreCase))) return null;
                if (string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(dossier) || !Path.IsPathRooted(dossier)) return null;
                return Tuple.Create(nom, dossier.Replace('/', '\\').TrimEnd('\\'));
            }
            catch (Exception) { return null; }
        }

        public static List<JeuSurveille> JeuxEpic(string dossierManifestes = null)
        {
            var res = new List<JeuSurveille>();
            dossierManifestes = dossierManifestes ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            try
            {
                if (!Directory.Exists(dossierManifestes)) return res;
                foreach (var f in Directory.GetFiles(dossierManifestes, "*.item"))
                {
                    Tuple<string, string> m;
                    try { m = ManifesteEpic(File.ReadAllText(f, Encoding.UTF8)); } catch (Exception) { continue; }
                    if (m != null && Directory.Exists(m.Item2)) res.Add(new JeuSurveille { Nom = m.Item1, Chemin = m.Item2, Source = "Epic" });
                }
            }
            catch (Exception) { }
            return Dedoublonner(res);
        }

        private static List<JeuSurveille> Dedoublonner(IEnumerable<JeuSurveille> l)
        {
            return l.GroupBy(j => j.Chemin, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(j => j.Nom, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Jeu ajouté à la main : un .exe existant.</summary>
        public static JeuSurveille DepuisExe(string chemin)
        {
            if (string.IsNullOrWhiteSpace(chemin) || !chemin.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choisissez un fichier .exe.");
            var plein = Path.GetFullPath(chemin);
            if (Correspond(plein, new JeuSurveille { Chemin = plein, Source = "exe" }) == false) throw new ArgumentException("Cet exécutable n'est pas un jeu (lanceur ou outil) : " + Path.GetFileName(plein));
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(plein);
            var nom = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName.Trim() : Path.GetFileNameWithoutExtension(plein);
            return new JeuSurveille { Nom = nom, Chemin = plein, Source = "exe" };
        }

        // ---------- Processus en cours ----------

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
        private const uint QueryLimitedInformation = 0x1000;

        /// <summary>Chemins des exécutables en cours (ceux qu'on ne peut pas lire sont ignorés).</summary>
        public static List<string> CheminsProcessus()
        {
            var res = new List<string>();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    var h = OpenProcess(QueryLimitedInformation, false, p.Id);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        var sb = new StringBuilder(1024); int n = sb.Capacity;
                        if (QueryFullProcessImageNameW(h, 0, sb, ref n)) res.Add(sb.ToString(0, n));
                    }
                    finally { CloseHandle(h); }
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return res;
        }
    }
}
