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
            try { version = VersionAppli.Texte; } catch (Exception) { }
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

        // ---------- Journal de démarrage ----------

        public const string NomJournalDemarrage = "demarrage.log";
        public const string JalonFin = "fermeture normale";
        private static string precedent;

        /// <summary>
        /// Début d'un lancement de l'interface : garde la dernière étape du lancement précédent (s'il ne s'est pas terminé normalement),
        /// puis recommence le journal. Écrit sur le disque à chaque étape : si le processus est tué (antivirus, plantage natif),
        /// la dernière ligne indique où il s'est arrêté, ce que le rapport d'erreur ne peut pas voir.
        /// </summary>
        public static void DebutDemarrage()
        {
            try
            {
                var f = Path.Combine(Dossier, NomJournalDemarrage);
                precedent = null;
                if (File.Exists(f))
                {
                    var lignes = File.ReadAllLines(f);
                    precedent = DerniereEtapeInachevee(lignes);
                }
                Directory.CreateDirectory(Dossier);
                File.WriteAllText(f, "", new UTF8Encoding(false));
            }
            catch (Exception) { }
            Jalon("lancement");
        }

        /// <summary>Dernière étape d'un journal de démarrage qui ne se termine pas par la fermeture normale (null sinon ; public pour les tests).</summary>
        public static string DerniereEtapeInachevee(string[] lignes)
        {
            if (lignes == null || lignes.Length == 0) return null;
            var derniere = lignes[lignes.Length - 1];
            if (derniere.EndsWith(JalonFin, StringComparison.Ordinal)) return null;
            var i = derniere.IndexOf(' ', 20);
            return i > 0 && i < derniere.Length - 1 ? derniere.Substring(i + 1) : derniere;
        }

        /// <summary>Étape où s'est arrêté le lancement précédent, s'il ne s'est pas fermé normalement (null sinon).</summary>
        public static string LancementPrecedentInterrompu { get { return precedent; } }

        /// <summary>Note une étape du démarrage (et la fermeture normale, avec <see cref="JalonFin"/>). Ne lève jamais.</summary>
        public static void Jalon(string etape)
        {
            try
            {
                lock (sync)
                    File.AppendAllText(Path.Combine(Dossier, NomJournalDemarrage),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + etape + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception) { }
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
