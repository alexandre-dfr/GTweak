using System.Collections.Generic;
using System.Linq;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    /// <summary>Une ligne du résumé de fin : icône (police Segoe Fluent/MDL2), genre (ok, attention, erreur, info) et texte.</summary>
    public class LigneResume
    {
        public string Icone { get; set; }
        /// <summary>« ok », « attention », « erreur » ou « info » (couleur de l'icône).</summary>
        public string Genre { get; set; }
        public string Texte { get; set; }
        /// <summary>Détail sous la ligne (raisons, titres) ; vide si aucun.</summary>
        public string Detail { get; set; } = "";
        public bool ADetail { get { return !string.IsNullOrEmpty(Detail); } }
    }

    /// <summary>
    /// Résumé lisible d'une exécution, affiché après « LANCER » à la place du journal brut :
    /// appliquées, ignorées (avec la raison), en échec, et redémarrage nécessaire ou non.
    /// </summary>
    public static class ResumeExecution
    {
        public const string IconeOk = "", IconeAttention = "", IconeErreur = "", IconeRedemarrage = "", IconeInfo = "";
        /// <summary>Nombre de raisons détaillées par ligne ; au-delà, « et N autres ».</summary>
        public const int DetailsMax = 4;

        public static string Titre(SessionReport r)
        {
            if (r == null || r.SessionDir == null) return "Rien n'a été exécuté";
            if (r.Items.Any(i => i.Status == OutcomeStatus.Failed) || r.ExitCode != 0) return "Terminé avec des erreurs";
            return r.RedemarrageRequis ? "Terminé : redémarrez le PC" : "Terminé";
        }

        public static List<LigneResume> Construire(SessionReport r, IReadOnlyList<CatalogItem> catalogue)
        {
            var res = new List<LigneResume>();
            if (r == null || r.SessionDir == null)
            {
                res.Add(new LigneResume { Icone = IconeInfo, Genre = "info", Texte = "Aucune option n'a été exécutée.", Detail = Joindre(r == null ? new List<string>() : r.Avertissements, 99) });
                return res;
            }
            var titres = (catalogue ?? new List<CatalogItem>()).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Titre);
            System.Func<string, string> titre = id => { string t; return titres.TryGetValue(id, out t) && !string.IsNullOrWhiteSpace(t) ? t : id; };

            var ok = r.Items.Where(i => i.Status == OutcomeStatus.Ok).ToList();
            var deja = r.Items.Where(i => i.Status == OutcomeStatus.AlreadyApplied).ToList();
            var ignorees = r.Items.Where(i => i.Status == OutcomeStatus.Skipped).ToList();
            var echecs = r.Items.Where(i => i.Status == OutcomeStatus.Failed).ToList();

            var nbOk = ok.Count + deja.Count;
            res.Add(new LigneResume
            {
                Icone = IconeOk, Genre = nbOk > 0 ? "ok" : "info",
                Texte = nbOk == 0 ? "Aucune option appliquée"
                      : nbOk + " option" + (nbOk > 1 ? "s" : "") + " appliquée" + (nbOk > 1 ? "s" : "")
                        + (deja.Count > 0 ? " (dont " + deja.Count + " déjà en place)" : ""),
            });
            if (ignorees.Count > 0)
                res.Add(new LigneResume
                {
                    Icone = IconeAttention, Genre = "attention",
                    Texte = ignorees.Count + " option" + (ignorees.Count > 1 ? "s" : "") + " ignorée" + (ignorees.Count > 1 ? "s" : "") + " (rien n'a été modifié)",
                    Detail = Joindre(ignorees.Select(i => titre(i.Id) + Raison(i.Message)).ToList(), DetailsMax),
                });
            if (echecs.Count > 0)
                res.Add(new LigneResume
                {
                    Icone = IconeErreur, Genre = "erreur",
                    Texte = echecs.Count + " option" + (echecs.Count > 1 ? "s" : "") + " en échec : l'Historique permet d'annuler ce qui a été fait",
                    Detail = Joindre(echecs.Select(i => titre(i.Id) + Raison(i.Message)).ToList(), DetailsMax),
                });
            res.Add(r.RedemarrageRequis
                ? new LigneResume { Icone = IconeRedemarrage, Genre = "attention", Texte = "Redémarrage nécessaire pour terminer (jamais lancé automatiquement)" }
                : new LigneResume { Icone = IconeRedemarrage, Genre = "info", Texte = "Aucun redémarrage nécessaire" });
            var autres = new List<string>(r.Avertissements);
            if (r.Retablies.Count > 0) autres.Add("Rétablies automatiquement : " + string.Join(", ", r.Retablies.Select(titre)));
            if (autres.Count > 0)
                res.Add(new LigneResume { Icone = IconeInfo, Genre = "info", Texte = "À savoir", Detail = Joindre(autres, 99) });
            return res;
        }

        private static string Raison(string message)
        {
            var m = (message ?? "").Trim();
            return m.Length == 0 ? "" : " : " + m;
        }

        private static string Joindre(IList<string> lignes, int max)
        {
            if (lignes == null || lignes.Count == 0) return "";
            var vues = lignes.Take(max).Select(l => "• " + l).ToList();
            if (lignes.Count > max) vues.Add("… et " + (lignes.Count - max) + " autre" + (lignes.Count - max > 1 ? "s" : "") + " (voir le journal détaillé)");
            return string.Join("\n", vues);
        }
    }
}
