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

        /// <summary>Lit le premier tableau dont l'en-tête est celui de la couverture.</summary>
        public static List<LigneCouverture> Parse(string markdown)
        {
            var lignes = markdown.Replace("\r\n", "\n").Split('\n');
            int i = Array.FindIndex(lignes, l => l.Trim() == Entete);
            if (i < 0) throw new InvalidDataException("En-tête du tableau de couverture introuvable.");
            var res = new List<LigneCouverture>();
            for (i += 2; i < lignes.Length && lignes[i].TrimStart().StartsWith("|"); i++)
            {
                var c = lignes[i].Trim().Trim('|').Split('|').Select(s => s.Trim()).ToArray();
                if (c.Length != 5) throw new InvalidDataException("Ligne mal formée (" + c.Length + " colonnes) : " + lignes[i]);
                res.Add(new LigneCouverture { Source = c[0], Entree = c[1], Niveau = c[2], Decision = c[3], Raison = c[4] });
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
