using System;
using System.Collections.Generic;

namespace GamingTweaker.Lists
{
    /// <summary>Fournisseur qui propose une sélection initiale (identifiants d'éléments) quand l'utilisateur coche l'option sans rien choisir.</summary>
    public interface IListDefaults
    {
        IReadOnlyList<string> SelectionParDefaut { get; }
    }

    /// <summary>Fournisseurs de listes par identifiant (<c>CatalogItem.Liste</c>).</summary>
    public class ListProviderRegistry
    {
        private readonly Dictionary<string, IListProvider> parId = new Dictionary<string, IListProvider>(StringComparer.Ordinal);

        public void Register(IListProvider provider)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            parId[provider.ListId] = provider;
        }

        public IListProvider Get(string listId)
        {
            IListProvider p;
            return listId != null && parId.TryGetValue(listId, out p) ? p : null;
        }
    }
}
