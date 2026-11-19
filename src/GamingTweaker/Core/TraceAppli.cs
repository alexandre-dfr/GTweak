using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Journal applicatif pour le support : %ProgramFiles%\GamingTweaker\Machines\&lt;id&gt;\app.log (une ligne horodatée par événement,
    /// rotation à 1 Mo vers app.1.log). Aucune donnée personnelle n'y est écrite volontairement : identifiants d'options, codes, messages d'erreur.
    /// Avant la configuration du chemin (tests, tout début du démarrage), les lignes sont gardées en mémoire puis écrites à la configuration.
    /// Une erreur d'écriture est toujours avalée : le journal ne doit jamais faire échouer l'outil.
    /// </summary>
    public static class TraceAppli
    {
        public const string NomFichier = "app.log";
        private const long TailleMax = 1024 * 1024;
        private static readonly object sync = new object();
        private static readonly List<string> tampon = new List<string>();
        /// <summary>Dernières lignes écrites (rapport d'erreur), gardées même après leur écriture dans le fichier.</summary>
        private static readonly Queue<string> recentes = new Queue<string>();
        private static string chemin;

        /// <summary>Les dernières lignes du journal (au plus 60), pour le rapport d'erreur.</summary>
        public static string[] Recentes() { lock (sync) return recentes.ToArray(); }

        /// <summary>Chemin du journal (null tant qu'il n'est pas configuré).</summary>
        public static string Chemin { get { lock (sync) return chemin; } }

        /// <summary>Fixe le fichier (dossier de la machine) et y vide les lignes gardées en mémoire.</summary>
        public static void Configurer(string machineDir)
        {
            if (string.IsNullOrEmpty(machineDir)) return;
            lock (sync)
            {
                chemin = Path.Combine(machineDir, NomFichier);
                var version = "?";
                try { version = typeof(TraceAppli).Assembly.GetName().Version.ToString(3); } catch (Exception) { }
                tampon.Insert(0, Ligne("démarrage GamingTweaker " + version + ", Windows " + Environment.OSVersion.Version + ", " + (Environment.Is64BitProcess ? "x64" : "x86")));
                Vider();
            }
        }

        /// <summary>Tests : oublie le fichier configuré et les lignes en attente.</summary>
        internal static void Reinitialiser() { lock (sync) { chemin = null; tampon.Clear(); } }

        public static void Ecrire(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (sync)
            {
                var l = Ligne(message);
                recentes.Enqueue(l);
                while (recentes.Count > 60) recentes.Dequeue();
                tampon.Add(l);
                if (chemin != null) Vider();
                else if (tampon.Count > 200) tampon.RemoveAt(0);
            }
        }

        public static void Erreur(string contexte, Exception ex)
        {
            if (ex == null) { Ecrire(contexte); return; }
            Ecrire(contexte + " : " + ex.GetType().Name + " : " + (ex.Message ?? ""));
        }

        private static string Ligne(string message)
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        }

        private static void Vider()
        {
            if (tampon.Count == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(chemin));
                var fi = new FileInfo(chemin);
                if (fi.Exists && fi.Length > TailleMax)
                {
                    var ancien = Path.Combine(Path.GetDirectoryName(chemin), "app.1.log");
                    if (File.Exists(ancien)) File.Delete(ancien);
                    File.Move(chemin, ancien);
                }
                File.AppendAllText(chemin, string.Join("\r\n", tampon) + "\r\n", new UTF8Encoding(false));
                tampon.Clear();
            }
            catch (Exception) { /* jamais bloquant */ }
        }
    }
}
