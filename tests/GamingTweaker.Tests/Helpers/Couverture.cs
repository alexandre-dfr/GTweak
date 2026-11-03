using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GamingTweaker.Core;

namespace GamingTweaker.Tests.Helpers
{
    /// <summary>Une ligne du tableau de docs/catalogue-couverture.md.</summary>
    public sealed class LigneCouverture
    {
        public string Source { get; set; }
        public string Entree { get; set; }
        public string Niveau { get; set; }
        public string Decision { get; set; }
        public string Raison { get; set; }
        public override string ToString() => Source + " | " + Entree + " → " + (Decision ?? "");
    }

    /// <summary>Une ligne du « Récapitulatif par menu » de docs/catalogue-couverture.md.</summary>
    public sealed class LigneRecapitulatif
    {
        public string Id { get; set; }
        public string Groupe { get; set; }
        public string Titre { get; set; }
        public RiskLevel Risque { get; set; }
        /// <summary>Texte brut de la colonne « réversible » : « oui », « non » ou « partiel : … ».</summary>
        public string Reversible { get; set; }
        /// <summary>Vrai si l'option a un script revert (réversible différent de « non »).</summary>
        public bool EstReversible { get { return Reversible != "non"; } }
    }

    /// <summary>Lit le tableau de couverture des sources (Task 7B).</summary>
    public static class Couverture
    {
        public const string Exclu = "exclu";
        private const string Entete = "| source | entrée | niveau d'origine | décision | raison |";
        private static readonly Regex IdValide = new Regex(@"^(tel|opt|cln|blt|upd)\.[a-z0-9][a-z0-9.\-]*$");

        public static string Chemin()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "GamingTweaker.sln"))) dir = dir.Parent;
            if (dir == null) throw new FileNotFoundException("GamingTweaker.sln introuvable au-dessus de " + AppDomain.CurrentDomain.BaseDirectory);
            return Path.Combine(dir.FullName, "docs", "catalogue-couverture.md");
        }

        public static IReadOnlyList<LigneCouverture> Lire() => Parse(File.ReadAllText(Chemin(), Encoding.UTF8));

        /// <summary>
        /// Lit le tableau dont l'en-tête est celui de la couverture. Le tableau s'étend jusqu'au titre
        /// suivant (ligne « # ») ou à la fin du fichier ; une ligne vide suivie d'autres lignes, ou une
        /// ligne qui n'est pas une ligne de tableau, lève une exception au lieu d'arrêter la lecture.
        /// </summary>
        public static List<LigneCouverture> Parse(string markdown)
        {
            var lignes = markdown.Replace("\r\n", "\n").Split('\n');
            int i = Array.FindIndex(lignes, l => l.Trim() == Entete);
            if (i < 0) throw new InvalidDataException("En-tête du tableau de couverture introuvable.");
            if (i + 1 >= lignes.Length || !Regex.IsMatch(lignes[i + 1].Trim(), @"^\|(\s*-+\s*\|){5}$"))
                throw new InvalidDataException("Ligne de séparation du tableau de couverture absente.");
            int fin = i + 2;
            while (fin < lignes.Length && !lignes[fin].TrimStart().StartsWith("#")) fin++;
            int dernier = fin - 1;
            while (dernier > i + 1 && string.IsNullOrWhiteSpace(lignes[dernier])) dernier--;

            var res = new List<LigneCouverture>();
            for (int k = i + 2; k <= dernier; k++)
            {
                var t = lignes[k].Trim();
                if (!t.StartsWith("|") || !t.EndsWith("|"))
                    throw new InvalidDataException("Ligne " + (k + 1) + " : ligne vide ou hors tableau à l'intérieur du tableau de couverture : « " + lignes[k] + " »");
                var c = t.Substring(1, t.Length - 2).Split('|').Select(s => s.Trim()).ToArray();
                if (c.Length != 5) throw new InvalidDataException("Ligne " + (k + 1) + " mal formée (" + c.Length + " colonnes) : " + lignes[k]);
                res.Add(new LigneCouverture { Source = c[0], Entree = c[1], Niveau = c[2], Decision = c[3], Raison = c[4] });
            }
            return res;
        }

        /// <summary>
        /// Compare les entrées du tableau à tools/inventaire/inventaire.json : chaque entrée de
        /// l'inventaire doit figurer exactement une fois, et aucune autre. Renvoie les écarts.
        /// </summary>
        public static List<string> EcartsAvecInventaire()
        {
            var racine = Path.GetDirectoryName(Path.GetDirectoryName(Chemin()));
            var inv = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(racine, "tools", "inventaire", "inventaire.json"), Encoding.UTF8));
            var attendu = new List<string>();
            foreach (var e in inv["winutil"])
                attendu.Add("WinUtil|" + (string)e["entree"] + " (`" + (string)e["cle"] + "`)");
            foreach (var e in inv["privacysexy"])
            {
                var g = (string)e["groupe"];
                attendu.Add("privacy.sexy|" + ((string.IsNullOrEmpty(g) ? "" : g + " > ") + (string)e["entree"]).Replace("|", "/"));
            }
            var lu = Lire().Select(l => l.Source + "|" + l.Entree).ToList();
            var ecarts = new List<string>();
            foreach (var g in attendu.GroupBy(x => x))
            {
                int n = lu.Count(x => x == g.Key);
                if (n != g.Count()) ecarts.Add(g.Key + " : " + n + " ligne(s) au lieu de " + g.Count());
            }
            ecarts.AddRange(lu.Where(x => !attendu.Contains(x)).Select(x => x + " : absente de l'inventaire"));
            return ecarts;
        }

        /// <summary>
        /// Lignes du tableau « ### menu » du Récapitulatif par menu (colonnes : id, groupe, titre, risque, réversible, lignes sources).
        /// Un menu absent, un tableau mal formé ou un risque inconnu lèvent une exception.
        /// </summary>
        public static List<LigneRecapitulatif> Recapitulatif(string menu)
            => ParseRecapitulatif(File.ReadAllText(Chemin(), Encoding.UTF8), menu);

        public static List<LigneRecapitulatif> ParseRecapitulatif(string markdown, string menu)
        {
            var lignes = markdown.Replace("\r\n", "\n").Split('\n');
            int i = Array.FindIndex(lignes, l => l.Trim() == "### " + menu);
            if (i < 0) throw new InvalidDataException("Menu « " + menu + " » absent du récapitulatif.");
            int k = i + 1;
            while (k < lignes.Length && !lignes[k].TrimStart().StartsWith("|"))
            {
                if (lignes[k].TrimStart().StartsWith("#")) throw new InvalidDataException("Tableau du menu « " + menu + " » introuvable.");
                k++;
            }
            if (k + 1 >= lignes.Length || !Regex.IsMatch(lignes[k + 1].Trim(), @"^\|(\s*-+\s*\|){6}$"))
                throw new InvalidDataException("Séparation du tableau du menu « " + menu + " » absente.");
            var res = new List<LigneRecapitulatif>();
            for (int n = k + 2; n < lignes.Length && lignes[n].TrimStart().StartsWith("|"); n++)
            {
                var c = lignes[n].Trim().Trim('|').Split('|').Select(s => s.Trim()).ToArray();
                if (c.Length != 6) throw new InvalidDataException("Ligne mal formée (" + c.Length + " colonnes) : " + lignes[n]);
                RiskLevel r;
                switch (c[3])
                {
                    case "Sûr": r = RiskLevel.Sur; break;
                    case "Avancé": r = RiskLevel.Avance; break;
                    case "Risqué": r = RiskLevel.Risque; break;
                    default: throw new InvalidDataException("Risque inconnu « " + c[3] + " » : " + lignes[n]);
                }
                res.Add(new LigneRecapitulatif { Id = c[0].Trim('`'), Groupe = c[1], Titre = c[2], Risque = r, Reversible = c[4] });
            }
            return res;
        }

        public static List<LigneCouverture> SansDecision() =>
            Lire().Where(l => string.IsNullOrWhiteSpace(l.Decision)).ToList();

        public static List<LigneCouverture> ExclusionsSansRaison() =>
            Lire().Where(l => l.Decision == Exclu && string.IsNullOrWhiteSpace(l.Raison)).ToList();

        /// <summary>Décisions qui ne sont ni « exclu » ni un identifiant de menu valide.</summary>
        public static List<LigneCouverture> DecisionsInvalides() =>
            Lire().Where(l => !string.IsNullOrWhiteSpace(l.Decision) && l.Decision != Exclu && !IdValide.IsMatch(l.Decision)).ToList();

        public static List<string> IdsCites() =>
            Lire().Select(l => l.Decision).Where(d => !string.IsNullOrWhiteSpace(d) && d != Exclu).Distinct().ToList();

        /// <summary>Identifiants cités dans la couverture et absents du catalogue.</summary>
        public static List<string> IdsInconnus(IEnumerable<CatalogItem> catalogue)
        {
            var connus = new HashSet<string>(catalogue.Select(c => c.Id), StringComparer.Ordinal);
            return IdsCites().Where(id => !connus.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }
    }
}
