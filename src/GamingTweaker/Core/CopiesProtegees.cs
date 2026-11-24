using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Copies de l'exe dans %ProgramFiles%\GamingTweaker, lancées élevées par le Planificateur : App\ (routine hebdomadaire) et Demarrage\
    /// (démarrage avec Windows). Elles sont faites une fois, à la planification : sans ce contrôle, elles resteraient à l'ancienne version
    /// (ancien catalogue, anciens scripts) après une mise à jour intégrée ou le lancement d'une version plus récente téléchargée à la main.
    /// Au démarrage de l'interface, une copie plus ancienne que l'exe en cours est remplacée ; jamais par une version plus ancienne.
    /// </summary>
    public static class CopiesProtegees
    {
        /// <summary>Décision pure : la copie est d'une version strictement plus ancienne que l'exe en cours.</summary>
        public static bool AMettreAJour(Version copie, Version courante)
        {
            return copie != null && courante != null && copie < courante;
        }

        /// <summary>Version de fichier d'un exe (0.9.2.58), null si illisible.</summary>
        public static Version VersionFichier(string exe)
        {
            try
            {
                if (!File.Exists(exe)) return null;
                Version v;
                return Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "", out v) ? v : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Version complète de l'exe en cours (avec la révision de la CI).</summary>
        public static Version VersionCourante { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        /// <summary>
        /// Remplace les copies plus anciennes que l'exe en cours. Hors du thread de l'interface ; ne lève jamais (erreurs dans la trace).
        /// Une copie en cours d'exécution (routine en train de tourner) est laissée : elle sera remplacée au prochain lancement.
        /// </summary>
        public static void ActualiserSiAnciennes()
        {
            string source;
            try { using (var p = Process.GetCurrentProcess()) source = p.MainModule.FileName; }
            catch (Exception) { return; }
            var courante = VersionCourante;

            // Routine : seul l'exe est remplacé ; la copie du profil et la tâche restent telles quelles.
            try
            {
                if (!Planification.DepuisLaCopie(source, Planification.ExeCopie)
                    && AMettreAJour(VersionFichier(Planification.ExeCopie), courante)
                    && Planification.Query().Existe)
                {
                    Planification.ActualiserExe();
                    TraceAppli.Ecrire("copie de la routine mise à jour vers " + courante);
                }
            }
            catch (Exception ex) { TraceAppli.Erreur("mise à jour de la copie de la routine", ex); }

            // Démarrage avec Windows : la copie est refaite (même chemin que l'activation).
            try
            {
                if (!Planification.DepuisLaCopie(source, DemarrageWindows.ExeCopie)
                    && AMettreAJour(VersionFichier(DemarrageWindows.ExeCopie), courante)
                    && DemarrageWindows.EstInstalle())
                {
                    DemarrageWindows.Installer();
                    TraceAppli.Ecrire("copie lancée avec Windows mise à jour vers " + courante);
                }
            }
            catch (Exception ex) { TraceAppli.Erreur("mise à jour de la copie lancée avec Windows", ex); }
        }
    }
}
