using System;
using System.IO;
using System.Text;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Rapport d'erreur inattendue : écrit dans %LOCALAPPDATA%\GamingTweaker\erreurs.log (lisible sans droits administrateur, contrairement
    /// au journal applicatif du dossier protégé) avec la pile d'appels et les dernières lignes du journal, et noté dans app.log.
    /// Ne lève jamais d'exception : un rapport impossible à écrire renvoie null.
    /// </summary>
    public static class RapportErreur
    {
        public const string NomFichier = "erreurs.log";
        private const long TailleMax = 512 * 1024;
        private static readonly object sync = new object();

        public static string Dossier
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamingTweaker"); }
        }

        /// <summary>Texte du rapport (public pour les tests).</summary>
        public static string Texte(string contexte, Exception ex)
        {
            var sb = new StringBuilder();
            string version = "?";
            try { version = typeof(RapportErreur).Assembly.GetName().Version.ToString(3); } catch (Exception) { }
            sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · GamingTweaker " + version + " · Windows " + Environment.OSVersion.Version + " ====");
            sb.AppendLine("Contexte : " + (contexte ?? "inconnu"));
            sb.AppendLine(ex == null ? "(aucune exception)" : ex.ToString());
            var recentes = TraceAppli.Recentes();
            if (recentes.Length > 0)
            {
                sb.AppendLine("-- Dernières lignes du journal --");
                foreach (var l in recentes) sb.AppendLine(l);
            }
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Enregistre le rapport ; renvoie le chemin du fichier, ou null s'il n'a pas pu être écrit.</summary>
        public static string Ecrire(string contexte, Exception ex)
        {
            try { TraceAppli.Erreur("ERREUR INATTENDUE (" + contexte + ")", ex); } catch (Exception) { }
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(Dossier);
                    var f = Path.Combine(Dossier, NomFichier);
                    var fi = new FileInfo(f);
                    if (fi.Exists && fi.Length > TailleMax) File.Delete(f);
                    File.AppendAllText(f, Texte(contexte, ex), new UTF8Encoding(false));
                    return f;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Message affiché à l'utilisateur.</summary>
        public static string Message(Exception ex, string chemin, bool continuer)
        {
            var msg = (ex == null ? "" : ex.Message ?? "").Trim();
            return "GamingTweaker a rencontré une erreur inattendue" + (msg.Length > 0 ? " :\r\n" + msg : ".")
                 + "\r\n\r\n" + (chemin != null ? "Un rapport a été enregistré dans :\r\n" + chemin + "\r\nEnvoyez-le pour que le problème soit corrigé." : "Le rapport n'a pas pu être enregistré.")
                 + "\r\n\r\n" + (continuer ? "L'application continue ; si quelque chose ne répond plus, relancez-la." : "L'application va se fermer.");
        }
    }
}
