using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GamingTweaker.Core
{
    public class BilanDesinstallation
    {
        public List<string> Actions { get; } = new List<string>();
        public List<string> Avertissements { get; } = new List<string>();
        /// <summary>0 : terminé ; 1 : terminé avec des éléments non supprimés ; 2 : rien fait (aperçu, verrou, refus).</summary>
        public int Code { get; set; }
    }

    /// <summary>
    /// « GamingTweaker.exe --uninstall [--confirm] » : retire la routine planifiée (tâche et dossier du Planificateur), la copie de l'exe
    /// et le dossier de données protégé (%ProgramFiles%\GamingTweaker : sessions, journaux, quarantaine, téléchargements, scripts).
    /// Sans --confirm : aperçu seulement. Les réglages appliqués ne sont PAS annulés : lancer « Tout restaurer » ou l'annulation des sessions avant,
    /// car les journaux d'annulation disparaissent avec le dossier de données.
    /// </summary>
    public static class Desinstallation
    {
        public static BilanDesinstallation Executer(bool confirme, string racine = null, string lockName = null)
        {
            var b = new BilanDesinstallation();
            racine = racine ?? DataPaths.RacineProtegeeParDefaut;
            using (var verrou = SingleInstance.TryAcquire(lockName))
            {
                if (verrou == null) { b.Avertissements.Add("Une autre exécution de GamingTweaker est en cours : rien n'a été désinstallé."); b.Code = 2; return b; }

                var quarantaine = Path.Combine(racine, "Quarantaine");
                int fichiersQuarantaine = Directory.Exists(quarantaine) ? Directory.GetFiles(quarantaine).Length : 0;
                int sessions = 0;
                var machines = Path.Combine(racine, "Machines");
                if (Directory.Exists(machines))
                    foreach (var m in Directory.GetDirectories(machines))
                    {
                        var s = Path.Combine(m, "Sessions");
                        if (Directory.Exists(s)) sessions += Directory.GetDirectories(s).Length;
                    }
                var routine = Planification.Query();

                if (!confirme)
                {
                    b.Actions.Add("Aperçu (rien n'est supprimé sans --confirm) :");
                    b.Actions.Add(routine.Existe ? "- la routine planifiée " + Planification.NomTache + " serait supprimée" : "- aucune routine planifiée");
                    b.Actions.Add("- le démarrage avec Windows (" + DemarrageWindows.NomTache + ") serait supprimé s'il existe");
                    b.Actions.Add("- le dossier " + racine + " serait supprimé (" + sessions + " session(s) avec leurs journaux d'annulation, "
                        + fichiersQuarantaine + " fichier(s) en quarantaine)");
                    if (sessions > 0) b.Avertissements.Add("Les réglages appliqués ne sont pas annulés par la désinstallation : utilisez d'abord « Tout restaurer » ou l'annulation des sessions dans l'historique.");
                    if (fichiersQuarantaine > 0) b.Avertissements.Add("Les fichiers en quarantaine seront supprimés définitivement : restaurez-les d'abord depuis l'historique si besoin.");
                    b.Code = 2;
                    return b;
                }

                // 1. Démarrage avec Windows puis routine : tâches + dossier du Planificateur (une tâche absente vaut succès).
                try { Planification.SupprimerTacheEtDossier(DemarrageWindows.NomTache); b.Actions.Add("Démarrage avec Windows supprimé (ou déjà absent)."); }
                catch (Exception ex) { b.Avertissements.Add("Démarrage avec Windows non supprimé : " + ex.Message); }
                try { Planification.SupprimerTacheEtDossier(); b.Actions.Add("Routine planifiée supprimée (ou déjà absente)."); }
                catch (Exception ex) { b.Avertissements.Add("Routine planifiée non supprimée : " + ex.Message); }

                // 2. Dossier de données. L'exe en cours, s'il est la copie de la routine, ne peut pas être supprimé : tout le reste l'est.
                string moi = null;
                try { moi = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName); } catch (Exception) { }
                if (Directory.Exists(racine))
                {
                    var restes = new List<string>();
                    SupprimerSauf(racine, moi, restes);
                    if (restes.Count == 0) b.Actions.Add("Dossier de données supprimé : " + racine);
                    else
                    {
                        b.Actions.Add("Dossier de données vidé : " + racine);
                        foreach (var r in restes) b.Avertissements.Add("Non supprimé : " + r);
                    }
                }
                else b.Actions.Add("Aucun dossier de données à supprimer (" + racine + ").");

                b.Code = b.Avertissements.Count == 0 ? 0 : 1;
                return b;
            }
        }

        /// <summary>Supprime récursivement, sauf le fichier « garder » (exe en cours) et ses dossiers parents ; les échecs sont listés sans interrompre.</summary>
        private static void SupprimerSauf(string dossier, string garder, List<string> restes)
        {
            foreach (var f in Directory.GetFiles(dossier))
            {
                if (garder != null && string.Equals(Path.GetFullPath(f), garder, StringComparison.OrdinalIgnoreCase)) { restes.Add(f + " (programme en cours d'exécution : supprimez-le à la main)"); continue; }
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch (Exception ex) { restes.Add(f + " : " + ex.Message); }
            }
            foreach (var d in Directory.GetDirectories(dossier))
            {
                // Jonction ou lien : retiré sans être suivi (le dossier est protégé, mais par prudence).
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) { try { Directory.Delete(d, false); } catch (Exception ex) { restes.Add(d + " : " + ex.Message); } continue; }
                SupprimerSauf(d, garder, restes);
            }
            if (!Directory.EnumerateFileSystemEntries(dossier).Any())
            {
                try { Directory.Delete(dossier, false); } catch (Exception ex) { restes.Add(dossier + " : " + ex.Message); }
            }
        }
    }
}
