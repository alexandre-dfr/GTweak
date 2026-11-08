using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GamingTweaker.Core
{
    /// <summary>Emplacements des données : à côté de l'exe si inscriptible, sinon sous %ProgramFiles%\GamingTweaker (racine protégée).</summary>
    public class DataPaths
    {
        public string ProfilsDir { get; set; }
        public string MachineDir { get; set; }
        public string SessionsDir { get; set; }
        /// <summary>Toujours sous la racine protégée (jamais à côté de l'exe).</summary>
        public string QuarantineDir { get; set; }

        /// <summary>
        /// Racine protégée des données écrites avec des droits élevés et de la copie de la routine : %ProgramFiles%\GamingTweaker.
        /// Un utilisateur standard ne peut rien créer sous Program Files (ni pré-créer ce dossier, ni y garder une poignée ouverte).
        /// L'ancien emplacement C:\ProgramData\GamingTweaker n'est plus ni lu ni écrit.
        /// </summary>
        public static string RacineProtegeeParDefaut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GamingTweaker"); }
        }

        public static string MachineId(MachineFacts f)
        {
            var hex = new string((f.MachineGuid ?? "").Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
            if (hex.Length > 8) hex = hex.Substring(0, 8);
            if (hex.Length == 0) hex = "00000000";
            var invalid = Path.GetInvalidFileNameChars();
            var name = string.Concat((string.IsNullOrEmpty(f.MachineName) ? "PC" : f.MachineName).Select(c => invalid.Contains(c) ? '_' : c));
            return name + "-" + hex;
        }

        /// <summary>
        /// Racine des données : la racine protégée si l'exe est dedans (copie lancée par la routine, App\),
        /// sinon à côté de l'exe s'il est inscriptible, sinon la racine protégée.
        /// </summary>
        public static DataPaths Resolve(string exeDir, MachineFacts f, string racineProtegee)
        {
            var root = !EstSous(exeDir, racineProtegee) && IsWritable(exeDir) ? exeDir : racineProtegee;
            return Build(root, f, racineProtegee);
        }

        /// <summary>Emplacements utilisés par la routine planifiée (racine protégée).</summary>
        public static DataPaths ResolveRoutine(MachineFacts f, string racineProtegee)
        {
            return Build(racineProtegee, f, racineProtegee);
        }

        /// <summary>Racines à lire par l'historique et « Tout restaurer » : la racine interactive puis celle de la routine (sans doublon).</summary>
        public static List<DataPaths> Racines(DataPaths interactive, MachineFacts f, string racineProtegee)
        {
            var res = new List<DataPaths> { interactive };
            var routine = ResolveRoutine(f, racineProtegee);
            if (!MemeDossier(routine.MachineDir, interactive.MachineDir)) res.Add(routine);
            return res;
        }

        internal static bool MemeDossier(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        internal static bool EstSous(string dir, string racine)
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(racine)) return false;
            try
            {
                var d = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                var r = Path.GetFullPath(racine).TrimEnd('\\') + "\\";
                return d.StartsWith(r, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static DataPaths Build(string root, MachineFacts f, string racineProtegee)
        {
            var machine = Path.Combine(root, "Machines", MachineId(f));
            return new DataPaths
            {
                ProfilsDir = Path.Combine(root, "Profils"),
                MachineDir = machine,
                SessionsDir = Path.Combine(machine, "Sessions"),
                QuarantineDir = Path.Combine(racineProtegee, "Quarantaine"),
            };
        }

        /// <summary>Crée puis supprime un fichier sonde. Un dossier inexistant est considéré non inscriptible.</summary>
        internal static bool IsWritable(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                var probe = Path.Combine(dir, ".gt-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
