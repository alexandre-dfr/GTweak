using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Lists;

namespace GamingTweaker.Ui
{
    public class ListeElementViewModel : ObservableObject
    {
        private readonly Action changed;
        private bool isChecked;

        public ListeElementViewModel(string key, string titre, string detail, RiskLevel risque, bool absent, bool coche, Action changed)
        {
            Key = key; Titre = titre; Detail = detail; Absent = absent; isChecked = coche; this.changed = changed;
            Risque = risque == RiskLevel.Sur ? "Sûr" : risque == RiskLevel.Avance ? "Avancé" : "Risqué";
        }

        public string Key { get; private set; }
        public string Titre { get; private set; }
        public string Detail { get; private set; }
        public string Risque { get; private set; }
        /// <summary>Présent dans la sélection (profil) mais introuvable sur ce PC.</summary>
        public bool Absent { get; private set; }

        public bool IsChecked
        {
            get { return isChecked; }
            set { if (Set(ref isChecked, value) && changed != null) changed(); }
        }

        internal void SetSilencieux(bool v) { if (isChecked == v) return; isChecked = v; Raise("IsChecked"); }
    }

    /// <summary>Choix des éléments d'une option à liste (convention de Task 5 : « * » = tous, « !x » = exclusion, sinon liste explicite).</summary>
    public class ListeSelectionViewModel : ObservableObject
    {
        private readonly IListProvider provider;
        private readonly Action changed;
        private List<string> brut = new List<string>();
        private bool tous;
        private bool charge;
        private bool enCours;
        private string erreur = "";
        private IReadOnlyList<ListEntry> disponibles;
        private Task chargement;
        private bool interne;

        public ListeSelectionViewModel(IListProvider provider, Action changed = null)
        {
            this.provider = provider; this.changed = changed;
        }

        public string ListId { get { return provider.ListId; } }
        public ObservableCollection<ListeElementViewModel> Elements { get; } = new ObservableCollection<ListeElementViewModel>();
        public bool Charge { get { return charge; } private set { Set(ref charge, value); } }
        public bool EnChargement { get { return enCours; } private set { Set(ref enCours, value); } }
        public string Erreur { get { return erreur; } private set { Set(ref erreur, value ?? ""); } }
        /// <summary>Message technique de l'erreur (secondaire).</summary>
        public string ErreurDetail { get { return erreurDetail; } private set { Set(ref erreurDetail, value ?? ""); } }
        private string erreurDetail = "";

        /// <summary>Texte d'information du fournisseur (winget absent, recherche NVIDIA en échec...), affiché sous la liste.</summary>
        public string Message { get { return message; } private set { Set(ref message, value ?? ""); } }
        private string message = "";
        /// <summary>Adresse https de la page officielle (repli) ; vide si aucun lien.</summary>
        public string Lien { get { return lien; } private set { Set(ref lien, value ?? ""); } }
        private string lien = "";

        /// <summary>Faux quand la clé de profil de cette liste ne peut pas stocker « Tout » : la case n'est alors pas proposée.</summary>
        public bool SupporteTous { get { return Profils.CleSupporteTous(provider.ListId); } }

        public bool Tous
        {
            get { return tous; }
            set
            {
                if (tous == value) return;
                if (value && !SupporteTous) { Raise("Tous"); return; }
                tous = value;
                if (charge)
                {
                    // Cocher « Tout » coche tous les éléments présents ; le décocher conserve les cases telles quelles.
                    if (value)
                    {
                        interne = true;
                        try { foreach (var e in Elements.Where(x => !x.Absent)) e.SetSilencieux(true); }
                        finally { interne = false; }
                    }
                }
                else
                {
                    brut = value ? new List<string> { "*" } : brut.Where(x => x != "*" && !x.StartsWith("!", StringComparison.Ordinal)).ToList();
                }
                Raise("Tous"); Notifier();
            }
        }

        /// <summary>Vrai quand aucun élément n'est choisi.</summary>
        public bool EstVide { get { return VersSelection().Count == 0; } }

        public string Resume { get { return "Choisir… (" + ResumeCourt + ")"; } }

        /// <summary>« Tous », « Tous sauf 2 », « 3 sélectionnés ».</summary>
        public string ResumeCourt
        {
            get
            {
                var sel = VersSelection();
                string x;
                if (sel.Contains("*"))
                {
                    var n = sel.Count(s => s.StartsWith("!", StringComparison.Ordinal));
                    x = n == 0 ? "Tous" : "Tous sauf " + n;
                }
                else x = sel.Count + (sel.Count > 1 ? " sélectionnés" : " sélectionné");
                return x;
            }
        }

        private void Notifier()
        {
            Raise("Resume"); Raise("ResumeCourt"); Raise("EstVide");
            if (changed != null) changed();
        }

        /// <summary>Charge les éléments du fournisseur une seule fois (les appels simultanés partagent le même chargement ; une erreur permet de réessayer).</summary>
        public Task ChargerAsync()
        {
            if (charge) return Task.FromResult(0);
            if (chargement != null && !chargement.IsCompleted) return chargement;
            return chargement = ChargerInterneAsync();
        }

        private async Task ChargerInterneAsync()
        {
            EnChargement = true; Erreur = ""; ErreurDetail = "";
            try
            {
                var res = await provider.LoadAsync(CancellationToken.None);
                disponibles = res ?? new List<ListEntry>();
                var info = provider as IListInfo;
                if (info != null) { Message = info.Message; Lien = info.Lien; }
                Appliquer(brut);
                Charge = true;
            }
            catch (Exception ex)
            {
                Erreur = "Impossible de charger la liste des éléments. Repliez puis dépliez la liste pour réessayer.";
                ErreurDetail = ex.Message;
            }
            finally { EnChargement = false; }
            Notifier();
        }

        /// <summary>Remplace la sélection (profil, valeur par défaut). Conservée telle quelle avant le chargement, appliquée aux éléments après.</summary>
        public void DepuisSelection(IEnumerable<string> valeurs)
        {
            brut = (valeurs ?? new string[0]).Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (charge) Appliquer(brut);
            else { tous = brut.Contains("*"); Raise("Tous"); }
            Notifier();
        }

        public void AppliquerDefaut()
        {
            var d = provider as IListDefaults;
            if (d != null && d.SelectionParDefaut != null && d.SelectionParDefaut.Count > 0) DepuisSelection(d.SelectionParDefaut);
        }

        private void Appliquer(List<string> sel)
        {
            var estTous = sel.Contains("*");
            var positifs = new HashSet<string>(sel.Where(s => s != "*" && !s.StartsWith("!", StringComparison.Ordinal)), StringComparer.OrdinalIgnoreCase);
            var exclus = new HashSet<string>(sel.Where(s => s.StartsWith("!", StringComparison.Ordinal)).Select(s => s.Substring(1)), StringComparer.OrdinalIgnoreCase);
            interne = true;
            try
            {
                tous = estTous; Raise("Tous");
                Elements.Clear();
                var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in disponibles)
                {
                    if (e == null || e.Key == null || !vus.Add(e.Key)) continue;
                    var coche = estTous ? !exclus.Contains(e.Key) : positifs.Contains(e.Key);
                    Elements.Add(new ListeElementViewModel(e.Key, e.Titre ?? e.Key, e.Detail, e.Risque, false, coche, ElementChange));
                }
                foreach (var k in (estTous ? exclus : positifs).Where(k => !vus.Contains(k)))
                    Elements.Add(new ListeElementViewModel(k, k, "Absent de ce PC (conservé dans le profil)", RiskLevel.Avance, true, !estTous, ElementChange));
            }
            finally { interne = false; }
        }

        private void ElementChange() { if (!interne) Notifier(); }

        public List<string> VersSelection()
        {
            if (!charge) return new List<string>(brut);
            var res = new List<string>();
            if (tous)
            {
                res.Add("*");
                res.AddRange(Elements.Where(e => !e.IsChecked).Select(e => "!" + e.Key));
            }
            else res.AddRange(Elements.Where(e => e.IsChecked).Select(e => e.Key));
            return res;
        }
    }
}
