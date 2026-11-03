using System;
using System.IO;
using System.Linq;

namespace GamingTweaker.Core
{
    /// <summary>Emplacements des données : à côté de l'exe si inscriptible, sinon sous ProgramData.</summary>
    public class DataPaths
    {
        public string ProfilsDir { get; set; }
        public string MachineDir { get; set; }
        public string SessionsDir { get; set; }
        /// <summary>Toujours sous ProgramData (jamais à côté de l'exe).</summary>
        public string QuarantineDir { get; set; }

        public static string DefaultProgramDataRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GamingTweaker"); }
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

        public static DataPaths Resolve(string exeDir, MachineFacts f, string programDataRoot)
        {
            var root = IsWritable(exeDir) ? exeDir : programDataRoot;
            var machine = Path.Combine(root, "Machines", MachineId(f));
            return new DataPaths
            {
                ProfilsDir = Path.Combine(root, "Profils"),
                MachineDir = machine,
                SessionsDir = Path.Combine(machine, "Sessions"),
                QuarantineDir = Path.Combine(programDataRoot, "Quarantaine"),
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
