using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GamingTweaker.Core;
using GamingTweaker.Lists;

namespace GamingTweaker.Ui
{
    public partial class MainViewModel
    {
        // ---------- Aperçu et exécution ----------

        public IReadOnlyList<PreviewRow> BuildPreview()
        {
            return Options.Where(o => o.IsChecked).OrderBy(o => Core.Catalogue.RangMenu(o.Menu)).ThenBy(o => o.Item.Ordre).Select(o => new PreviewRow
            {
                Id = o.Id, Menu = o.Menu,
                Titre = o.Titre + (o.Liste == null ? "" : o.Liste.EstVide ? " : aucun élément choisi, non exécutée" : " (" + o.Liste.ResumeCourt + ")"), Risque = o.Risque,
                Reversibilite = o.Reversible ? (string.IsNullOrWhiteSpace(o.Item.AnnulationPartielle) ? "Annulable" : "Annulable en partie : " + o.Item.AnnulationPartielle) : "définitif",
                Redemarrage = o.Redemarrage ? "Redémarrage requis" : "",
                Taille = "—",
            }).ToList();
        }

        /// <summary>
        /// Note de l'aperçu pour le menu Installation : les applications choisies qui ne s'installeront pas seules (sans lien direct),
        /// pour que l'utilisateur le sache avant LANCER et non à la lecture du rapport. Chaîne vide si toutes s'installent seules.
        /// </summary>
        public static string NoteInstallation(IEnumerable<string> ids, IEnumerable<AppInstallable> catalogue)
        {
            var sel = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var sansLien = (catalogue ?? Enumerable.Empty<AppInstallable>()).Where(a => a != null && sel.Contains(a.Id) && string.IsNullOrEmpty(a.Url)).ToList();
            var manuelles = sansLien.Where(a => string.IsNullOrEmpty(a.Winget)).Select(a => a.Nom).ToList();
            var parWinget = sansLien.Where(a => !string.IsNullOrEmpty(a.Winget)).Select(a => a.Nom).ToList();
            var parts = new List<string>();
            if (manuelles.Count > 0)
                parts.Add("À installer vous-même depuis leur page officielle (indiquée dans le rapport) : " + string.Join(", ", manuelles) + ".");
            if (parWinget.Count > 0)
                parts.Add("Installées par winget uniquement ; si winget est absent, depuis leur page officielle : " + string.Join(", ", parWinget) + ".");
            return string.Join(" ", parts);
        }

        /// <summary><see cref="NoteInstallation"/> pour les applications cochées dans les options du menu Installation.</summary>
        public string NoteInstallationApercu()
        {
            var ids = Options.Where(o => o.IsChecked && o.Menu == "Installation" && o.Liste != null).SelectMany(o => o.Liste.VersSelection()).ToList();
            if (ids.Count == 0) return "";
            try { return NoteInstallation(ids, AppsCatalogue.LoadEmbedded()); }
            catch (Exception ex) { TraceAppli.Erreur("aperçu : note du menu Installation", ex); return ""; }
        }

        /// <summary>Synthèse de l'aperçu : options annulables, définitives, et celles qui demandent un redémarrage.</summary>
        public static string ResumeApercu(IReadOnlyList<PreviewRow> lignes)
        {
            int annulables = lignes.Count(l => l.Reversibilite != "définitif");
            int definitives = lignes.Count(l => l.Reversibilite == "définitif");
            int redemarrage = lignes.Count(l => !string.IsNullOrEmpty(l.Redemarrage));
            return annulables + " annulable" + (annulables > 1 ? "s" : "") + " · " + definitives + " définitive" + (definitives > 1 ? "s" : "")
                 + " · " + redemarrage + " nécessit" + (redemarrage > 1 ? "ent" : "e") + " un redémarrage";
        }

        /// <summary>
        /// Lance les estimations de taille des options de l'aperçu (scripts en lecture seule) et renseigne la colonne Taille.
        /// Renvoie le texte du total (« Total estimé : 1,2 Go »), ou une chaîne vide si aucune estimation n'est disponible.
        /// </summary>
        public async Task<string> EstimerAsync(IReadOnlyList<PreviewRow> lignes)
        {
            var parId = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            var concernes = lignes.Where(l => l.Id != null && parId.ContainsKey(l.Id) && !string.IsNullOrWhiteSpace(parId[l.Id].Estimate))
                                  .Select(l => parId[l.Id]).ToList();
            if (concernes.Count == 0) return "";
            Dictionary<string, long?> res;
            try { res = await estimate(concernes); }
            catch (Exception) { return ""; }
            if (res == null) return "";
            long total = 0; int connues = 0;
            foreach (var l in lignes)
            {
                long? v;
                if (l.Id == null || !res.TryGetValue(l.Id, out v) || v == null) continue;
                l.Taille = Estimation.FormatTaille(v.Value);
                total += v.Value; connues++;
            }
            if (connues == 0) return "";
            return "Total estimé : " + Estimation.FormatTaille(total) + (connues < lignes.Count ? " (options sans estimation exclues)" : "");
        }

        /// <summary>Le script exact qui sera exécuté (le dossier de session réel n'existe qu'au lancement).</summary>
        public string BuildPreviewScript()
        {
            return ScriptBuilder.Build(items, BuildSelection(), @"<dossier de la session>\journal.jsonl", paths == null ? null : paths.QuarantineDir);
        }

        public const string NoteApercu = "Le dossier de session est créé au lancement ; les options déjà appliquées ou ignorées ne seront pas exécutées.";

        /// <summary>Script à exporter : le script de l'aperçu précédé d'un en-tête explicatif.</summary>
        public string BuildExportScript()
        {
            var script = BuildPreviewScript();
            return "# GamingTweaker : script exporté depuis l'aperçu.\r\n# " + NoteApercu + "\r\n\r\n" + script;
        }

        public Task<SessionReport> LancerAsync(IProgress<string> log, CancellationToken ct)
        {
            var req = new RunRequest
            {
                Selection = BuildSelection(), CatalogItems = items, Facts = facts, Paths = paths,
                ConfirmSansPointDeRestauration = ConfirmSansPointDeRestauration,
            };
            if (run == null) throw new InvalidOperationException("Aucun exécuteur configuré.");
            // Le pipeline prend le verrou et crée le point de restauration avant son premier await : hors du thread de l'interface.
            return Task.Run(() => run(req, log, ct));
        }

        /// <summary>Ligne lisible pour le journal en direct ; null pour les lignes techniques à masquer.</summary>
        public string FormaterLigne(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            if (!line.StartsWith("##GT|", StringComparison.Ordinal)) return line;
            var p = line.Split(new[] { '|' }, 4);
            if (p.Length < 3) return null;
            var o = Options.FirstOrDefault(x => x.Id == p[2]);
            var t = o == null ? p[2] : o.Titre;
            var msg = p.Length > 3 && !string.IsNullOrWhiteSpace(p[3]) ? " (" + p[3].Trim() + ")" : "";
            switch (p[1])
            {
                case "start": return "En cours : " + t;
                case "ok": return "Appliqué : " + t + msg;
                case "fail": return "Échec : " + t + msg;
                case "skip": return "Ignoré : " + t + msg;
                default: return null;
            }
        }

        public static string RapportTexte(SessionReport r, IReadOnlyList<CatalogItem> catalogue)
        {
            var titres = catalogue.ToDictionary(c => c.Id, c => c.Titre);
            var sb = new StringBuilder();
            if (r.SessionDir == null)
            {
                sb.AppendLine("Rien n'a été exécuté.");
            }
            else
            {
                foreach (var it in r.Items)
                {
                    string t; if (!titres.TryGetValue(it.Id, out t)) t = it.Id;
                    var st = it.Status == OutcomeStatus.Ok ? "Appliqué" : it.Status == OutcomeStatus.AlreadyApplied ? "Déjà appliqué"
                           : it.Status == OutcomeStatus.Skipped ? "Ignoré" : "Échec";
                    sb.Append(st).Append(" : ").Append(t);
                    if (!string.IsNullOrWhiteSpace(it.Message)) sb.Append(" (").Append(it.Message.Trim()).Append(')');
                    sb.AppendLine();
                }
                if (r.Retablies.Count > 0) sb.AppendLine().AppendLine("Rétablies automatiquement : " + string.Join(", ", r.Retablies));
            }
            if (r.Avertissements.Count > 0)
            {
                sb.AppendLine().AppendLine("Avertissements :");
                foreach (var w in r.Avertissements) sb.AppendLine("- " + w);
            }
            if (r.RedemarrageRequis) sb.AppendLine().AppendLine("Un redémarrage est nécessaire pour terminer. Il n'est jamais lancé automatiquement.");
            return sb.ToString().TrimEnd();
        }
    }
}
