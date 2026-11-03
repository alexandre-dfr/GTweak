using System;
using System.Text;
using System.Threading;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    public class OptionViewModel : ObservableObject
    {
        private readonly Func<OptionViewModel, bool> confirmer;
        private bool isChecked;
        private string etat = "Détection…";

        public OptionViewModel(CatalogItem item, Func<OptionViewModel, bool> confirmer)
        {
            Item = item; this.confirmer = confirmer;
            NiveauRisque = item.Risque ?? RiskLevel.Sur;
            Risque = NiveauRisque == RiskLevel.Sur ? "Sûr" : NiveauRisque == RiskLevel.Avance ? "Avancé" : "Risqué";
            SearchText = MainViewModel.Normaliser(item.Titre + " " + item.Description + " " + item.Groupe);
        }

        public CatalogItem Item { get; private set; }
        public string Id { get { return Item.Id; } }
        public string Titre { get { return Item.Titre; } }
        public string Description { get { return Item.Description; } }
        public string Groupe { get { return Item.Groupe; } }
        public string Menu { get { return Item.Menu; } }
        public RiskLevel NiveauRisque { get; private set; }
        public string Risque { get; private set; }
        public bool Reversible { get { return Item.Reversible; } }
        public bool Redemarrage { get { return Item.Redemarrage; } }
        internal string SearchText { get; private set; }
        internal event Action CocheChanged;
        internal bool Confirme { get; set; }

        public string Etat { get { return etat; } private set { Set(ref etat, value); } }

        public bool IsChecked
        {
            get { return isChecked; }
            set
            {
                if (value == isChecked) return;
                if (value && NiveauRisque == RiskLevel.Risque && !Confirme)
                {
                    if (confirmer == null || !confirmer(this))
                    {
                        // La case a déjà basculé côté interface : on republie l'ancienne valeur après la mise à jour de la liaison.
                        var ctx = SynchronizationContext.Current;
                        if (ctx != null) ctx.Post(_ => Raise("IsChecked"), null); else Raise("IsChecked");
                        return;
                    }
                    Confirme = true;
                }
                isChecked = value; Raise();
                var h = CocheChanged; if (h != null) h();
            }
        }

        /// <summary>Décoche sans confirmation (réinitialisation avant le chargement d'un profil).</summary>
        internal void ForceUnchecked()
        {
            if (!isChecked) return;
            isChecked = false; Raise("IsChecked");
            var h = CocheChanged; if (h != null) h();
        }

        internal void SetEtat(DetectState s)
        {
            Etat = s == DetectState.Applied ? "Déjà appliqué" : s == DetectState.NotApplied ? "Non appliqué"
                 : s == DetectState.Detecting ? "Détection…" : "Inconnu";
        }

        public string TexteConfirmation
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(Titre).Append("\r\n\r\n").Append(Description).Append("\r\n\r\n");
                sb.Append("Cette option est classée Risqué : ");
                sb.Append(Reversible
                    ? "elle modifie des réglages sensibles du système. Elle peut être annulée depuis l'historique."
                    : "son effet est définitif et ne peut pas être annulé.");
                sb.Append("\r\n\r\nLa cocher quand même ?");
                return sb.ToString();
            }
        }
    }
}
