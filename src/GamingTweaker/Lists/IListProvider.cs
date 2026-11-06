using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;

namespace GamingTweaker.Lists
{
    /// <summary>Un élément d'une liste dynamique (mises à jour, applications, périphériques...).</summary>
    public class ListEntry
    {
        public string Key { get; set; }
        public string Titre { get; set; }
        public string Detail { get; set; }
        public RiskLevel Risque { get; set; }
    }

    /// <summary>Fournisseur d'une liste détectée au premier affichage du menu concerné (Tasks 11 et suivantes).</summary>
    public interface IListProvider
    {
        string ListId { get; }
        Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct);
    }

    /// <summary>Description d'un élément à partir de sa clé, sans charger la liste (null si inconnu) : permet de connaître le niveau de risque d'une sélection de profil non encore chargée.</summary>
    public interface IListLookup
    {
        ListEntry Decrire(string key);
    }

    /// <summary>Texte et lien affichés sous la liste (repli de l'interface).</summary>
    public interface IListInfo
    {
        string Message { get; }
        /// <summary>Adresse https à ouvrir par « Ouvrir la page officielle » ; vide si aucun lien.</summary>
        string Lien { get; }
    }
}
