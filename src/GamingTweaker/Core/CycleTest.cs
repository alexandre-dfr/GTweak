using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Cycle de test pour Windows Sandbox : pour chaque option testable et réversible, détecter, appliquer, détecter, annuler, détecter.
    /// Refusé hors du compte WDAGUtilityAccount (jamais sur un vrai PC).
    /// </summary>
    public static class CycleTest
    {
        public const string SandboxUser = "WDAGUtilityAccount";

        public static bool IsSandbox(string user) => string.Equals(user, SandboxUser, StringComparison.OrdinalIgnoreCase);

        private class FauxPointDeRestauration : IRestorePoint
        {
            public RestorePointResult TryCreate(string description) => new RestorePointResult { Created = true, Message = "ignoré (cycle de test)" };
        }

        private class Ligne
        {
            public string id;
            public bool couvert;
            public string avant, apresApply, apresRevert, resultat, raison;
        }

        /// <summary>Code de sortie : 0 = cycle terminé sans échec, 1 = au moins un échec, 2 = refusé ou impossible.</summary>
        public static int Run(string fichierResultat, string user = null)
        {
            if (!IsSandbox(user ?? Environment.UserName))
            {
                Console.Error.WriteLine("--cycle-test n'est accepté que dans Windows Sandbox (compte " + SandboxUser + "). Refusé.");
                return 2;
            }
            if (string.IsNullOrWhiteSpace(fichierResultat)) { Console.Error.WriteLine("Fichier de résultat manquant."); return 2; }
            AssemblyLoader.Register();
            try { return RunCore(fichierResultat); }
            catch (Exception ex) { Console.Error.WriteLine("Erreur inattendue : " + ex); return 2; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunCore(string fichierResultat)
        {
            var catalogue = Catalogue.LoadEmbedded();
            var facts = SystemInfo.Detect();
            var racine = Path.Combine(Path.GetTempPath(), "GamingTweakerCycle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(racine);
            var paths = DataPaths.Resolve(racine, facts, racine);
            var pipeline = new RunPipeline(new FauxPointDeRestauration(), () => true, @"Global\GamingTweaker.Cycle." + Guid.NewGuid().ToString("N"));
            var lignes = new List<Ligne>();

            foreach (var it in catalogue)
            {
                var l = new Ligne { id = it.Id };
                lignes.Add(l);
                if (!Compatibilite.IsCompatible(it.Compatibilite, facts)) { l.resultat = "non couvert"; l.raison = "incompatible avec cette machine (" + facts.WindowsMajor + "/" + facts.Edition + ")"; continue; }
                if (!it.Testable) { l.resultat = "non couvert"; l.raison = "Testable=false (matériel, effet système ou réseau)"; continue; }
                if (!it.Reversible) { l.resultat = "non couvert"; l.raison = "non réversible"; continue; }
                l.couvert = true;
                try { Cycle(it, facts, paths, pipeline, l); }
                catch (Exception ex) { l.resultat = "échec"; l.raison = "exception : " + ex.Message; }
                Console.WriteLine(l.id + " : " + l.resultat + (l.raison != null ? " (" + l.raison + ")" : ""));
                try { Ecrire(Path.ChangeExtension(fichierResultat, ".partiel.json"), Resume(lignes), lignes); } catch (Exception) { }
            }

            var resume = Resume(lignes);
            Ecrire(fichierResultat, resume, lignes);
            try { Directory.Delete(racine, true); } catch (Exception) { }
            return resume.echec > 0 ? 1 : 0;
        }

        private class ResumeCycle { public int total, ok, echec, nonCouvert; }

        private static ResumeCycle Resume(List<Ligne> lignes)
        {
            return new ResumeCycle
            {
                total = lignes.Count,
                ok = lignes.Count(x => x.couvert && x.resultat == "ok"),
                echec = lignes.Count(x => x.couvert && x.resultat != "ok"),
                nonCouvert = lignes.Count(x => !x.couvert),
            };
        }

        private static void Cycle(CatalogItem it, MachineFacts facts, DataPaths paths, RunPipeline pipeline, Ligne l)
        {
            var avant = Etat(it);
            l.avant = avant.ToString();
            if (avant == DetectState.Applied) { l.resultat = "non couvert"; l.couvert = false; l.raison = "déjà appliquée au départ dans le bac à sable"; return; }
            var req = new RunRequest
            {
                Selection = new Selection { OptionIds = new HashSet<string> { it.Id } },
                Silent = true, CatalogItems = new List<CatalogItem> { it }, Facts = facts, Paths = paths,
            };
            var rapport = pipeline.ExecuteAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            var o = rapport.Items.FirstOrDefault(i => i.Id == it.Id);
            var apres = Etat(it);
            l.apresApply = apres.ToString();

            // Problème constaté après l'application (échec, état initial ou final douteux) : noté, mais l'annulation est quand même faite
            // si le journal contient des entrées. Sans elle, l'option resterait appliquée dans le bac à sable et fausserait les suivantes.
            string probleme = null; bool ignoree = false;
            if (o == null || o.Status == OutcomeStatus.Failed) probleme = "application : " + (o == null ? "aucun résultat" : o.Message);
            else if (o.Status == OutcomeStatus.Skipped) ignoree = true;
            else if (avant == DetectState.Unknown) probleme = "état initial indéterminé";
            else if (apres != DetectState.Applied) probleme = "après application, la détection renvoie " + apres;

            var journal = rapport.SessionDir == null ? null : Path.Combine(rapport.SessionDir, "journal.jsonl");
            var entrees = journal != null && File.Exists(journal) ? Journal.Active(Journal.Read(journal)).Where(e => e.Item == it.Id).ToList() : new List<JournalEntry>();
            bool annuler = entrees.Count > 0 || (probleme == null && !ignoree);

            ItemOutcome annulation = null;
            if (annuler)
            {
                var script = UndoService.BuildScript(journal, new[] { it.Id });
                annulation = Runner.RunAsync(script, null, CancellationToken.None, _ => it.DelaiSec > 0 ? it.DelaiSec : 300).GetAwaiter().GetResult()
                    .Items.FirstOrDefault(i => i.Id == it.Id);
            }
            var final = Etat(it);
            l.apresRevert = final.ToString();
            bool annulationEchouee = annuler && (annulation == null || annulation.Status == OutcomeStatus.Failed);
            string noteAnnulation = annulationEchouee ? "annulation : " + (annulation == null ? "aucun résultat" : annulation.Message) : null;

            if (ignoree && !annulationEchouee) { l.resultat = "non couvert"; l.couvert = false; l.raison = "ignorée à l'application : " + o.Message; return; }
            if (ignoree) probleme = "ignorée à l'application (" + o.Message + ") mais des modifications ont été journalisées";
            if (probleme != null)
            {
                l.resultat = "échec";
                l.raison = probleme + (noteAnnulation != null ? " ; " + noteAnnulation : annuler ? " ; modifications annulées" : "");
                return;
            }
            if (noteAnnulation != null) { l.resultat = "échec"; l.raison = noteAnnulation; return; }
            if (final != avant) { l.resultat = "échec"; l.raison = "après annulation, la détection renvoie " + final + " (avant : " + avant + ")"; return; }

            // Instantané registre : chaque valeur journalisée doit être revenue à son état d'origine, c'est-à-dire à celui de la PREMIÈRE
            // entrée de sa cible (une option qui écrit deux fois la même valeur a, en deuxième entrée, la valeur intermédiaire pour « avant »).
            var raison = VerifierRegistre(entrees, UndoService.ReadRegistry);
            if (raison != null) { l.resultat = "échec"; l.raison = raison; return; }
            l.resultat = "ok";
        }

        /// <summary>Première entrée registre de chaque cible comparée à la valeur actuelle ; null si tout est restauré, sinon la raison.</summary>
        internal static string VerifierRegistre(IEnumerable<JournalEntry> entrees, Func<string, string> lire)
        {
            foreach (var e in entrees.Where(x => x.Kind == "registry").GroupBy(x => x.Target, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                var attendu = e.Existed ? UndoService.Norm(e.Before) : UndoService.Absent;
                var actuel = lire(e.Target);
                if (attendu != actuel) return "registre non restauré : " + e.Target + " (attendu " + attendu + ", actuel " + actuel + ")";
            }
            return null;
        }

        private static DetectState Etat(CatalogItem it)
        {
            var d = Detection.RunAsync(new[] { it }).GetAwaiter().GetResult();
            DetectState s;
            return d.TryGetValue(it.Id, out s) ? s : DetectState.Unknown;
        }

        private static void Ecrire(string fichier, object resume, List<Ligne> lignes)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(fichier));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { resume, options = lignes }, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(fichier, json, new UTF8Encoding(false));
        }
    }
}
