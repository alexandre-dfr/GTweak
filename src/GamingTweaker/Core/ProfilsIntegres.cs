using System;
using System.Collections.Generic;
using System.Linq;

namespace GamingTweaker.Core
{
    /// <summary>Profil fourni avec l'application : en lecture seule, calculé à partir du catalogue (jamais de fichier).</summary>
    public class ProfilIntegre
    {
        public string Nom { get; set; }
        /// <summary>Nom affiché dans la liste des profils (avec le suffixe « (intégré) »).</summary>
        public string NomAffiche { get { return Nom + ProfilsIntegres.Suffixe; } }
        public string Description { get; set; }
        /// <summary>Phrase courte affichée sur la tuile de l'écran d'accueil.</summary>
        public string Accroche { get; set; }
        internal Func<CatalogItem, bool> Retient { get; set; }
    }

    /// <summary>
    /// Profils intégrés. Règles communes : jamais une option Risqué, jamais une option à liste (son contenu se choisit sur ce PC),
    /// jamais une option que « Recommandé » ne coche pas (« recommande »: false), seulement les options compatibles avec ce PC.
    /// Pour les modifier : « Enregistrer sous » un autre nom, ils deviennent alors des profils de l'utilisateur.
    /// </summary>
    public static class ProfilsIntegres
    {
        public const string Suffixe = " (intégré)";
        public const string MessageNomReserve = "Ce nom est réservé à un profil intégré : choisissez un autre nom.";
        public const string MessageRoutine = "Un profil intégré ne peut pas être planifié tel quel : enregistrez-le d'abord sous votre propre nom (« Sauver »), puis planifiez ce profil.";

        private const string Opt = "Optimisation", Tel = "Télémétrie", Net = "Nettoyage";

        private static bool Est(CatalogItem i, string menu, params RiskLevel[] niveaux)
        {
            return i.Menu == menu && i.Risque.HasValue && niveaux.Contains(i.Risque.Value);
        }

        public static readonly IReadOnlyList<ProfilIntegre> Tous = new List<ProfilIntegre>
        {
            new ProfilIntegre
            {
                Nom = "Gaming léger",
                Accroche = "Le plus sûr : rien que des réglages sans risque.",
                Description = "Optimisation, Télémétrie et Nettoyage : options Sûr uniquement.",
                Retient = i => Est(i, Opt, RiskLevel.Sur) || Est(i, Tel, RiskLevel.Sur) || Est(i, Net, RiskLevel.Sur),
            },
            new ProfilIntegre
            {
                Nom = "Gaming max",
                Accroche = "Le plus de performances en jeu.",
                Description = "Optimisation : options Sûr et Avancé ; Télémétrie et Nettoyage : options Sûr.",
                Retient = i => Est(i, Opt, RiskLevel.Sur, RiskLevel.Avance) || Est(i, Tel, RiskLevel.Sur) || Est(i, Net, RiskLevel.Sur),
            },
            new ProfilIntegre
            {
                Nom = "Confidentialité",
                Accroche = "Le moins de données envoyées à Microsoft.",
                Description = "Télémétrie : options Sûr et Avancé.",
                Retient = i => Est(i, Tel, RiskLevel.Sur, RiskLevel.Avance),
            },
        };

        /// <summary>Profil intégré dont le nom affiché est <paramref name="nomAffiche"/>, ou null.</summary>
        public static ProfilIntegre Trouver(string nomAffiche)
        {
            if (string.IsNullOrEmpty(nomAffiche)) return null;
            return Tous.FirstOrDefault(p => string.Equals(p.NomAffiche, nomAffiche.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>Vrai si un profil de l'utilisateur ne peut pas porter ce nom (il masquerait un profil intégré).</summary>
        public static bool EstReserve(string nom) { return Trouver(nom) != null; }

        /// <summary>Options retenues, dans l'ordre du catalogue.</summary>
        public static Profil Construire(ProfilIntegre p, IEnumerable<CatalogItem> catalogue, MachineFacts facts)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            var ids = (catalogue ?? Enumerable.Empty<CatalogItem>())
                .Where(i => !string.IsNullOrEmpty(i.Id) && string.IsNullOrEmpty(i.Liste) && i.Recommande
                            && i.Risque.HasValue && i.Risque.Value != RiskLevel.Risque
                            && (facts == null || Compatibilite.IsCompatible(i.Compatibilite ?? new CompatRule(), facts))
                            && p.Retient(i))
                .Select(i => i.Id).Distinct().ToList();
            return new Profil { Nom = p.Nom, Options = ids, Listes = new ProfilListes() };
        }
    }
}
