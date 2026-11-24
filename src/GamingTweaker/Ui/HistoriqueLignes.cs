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
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GamingTweaker.Ui
{
    public class SessionOption : ObservableObject
    {
        public string Id { get; set; }
        public string Titre { get; set; }
        public string Statut { get; set; }
        public string Message { get; set; }
        /// <summary>Le journal de la session contient encore des modifications à annuler pour cette option.</summary>
        public bool Annulable { get; set; }
        public ICommand AnnulerCommand { get; set; }
    }

    public class SessionViewModel
    {
        public string Id { get; set; }
        public string Dossier { get; set; }
        public DateTime Debut { get; set; }
        public string DebutTexte { get { return Debut.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture); } }
        public string Etat { get; set; }
        public string EtatLibelle { get; set; }
        /// <summary>Session interrompue (Running) ou en échec interne (Failed) : mise en évidence.</summary>
        public bool Anormale { get; set; }
        /// <summary>Session « Running » alors qu'une exécution tient le verrou : elle est en cours, pas interrompue.</summary>
        public bool EnCoursExecution { get; set; }
        public int Appliquees { get; set; }
        public int Echecs { get; set; }
        public int Ignorees { get; set; }
        public string Resume { get; set; }
        public bool Annulable { get { return Options.Any(o => o.Annulable); } }
        public List<SessionOption> Options { get; } = new List<SessionOption>();
    }

    public class QuarantaineLigne
    {
        public QuarantineEntry Entree { get; set; }
        public string Nom { get { return Entree.Nom; } }
        public string Origine { get { return string.IsNullOrEmpty(Entree.Origine) ? "origine inconnue (journal absent)" : Entree.Origine; } }
        public string TailleTexte { get { return Estimation.FormatTaille(Entree.Taille); } }
        public bool OrigineConnue { get { return !string.IsNullOrEmpty(Entree.Origine); } }
    }

    public class ReleveLigne : ObservableObject
    {
        private bool coche;
        public MesureSnapshot Mesure { get; set; }
        public bool IsChecked { get { return coche; } set { Set(ref coche, value); } }
        public string Libelle
        {
            get
            {
                DateTime d;
                var date = DateTime.TryParseExact(Mesure.Date ?? "", "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) : Mesure.Date;
                return date + " : DPC " + Pct(Mesure.DpcTime) + ", interruptions " + Pct(Mesure.InterruptTime) + (Mesure.Cpu.HasValue ? ", processeur " + Pct(Mesure.Cpu) : "") + ", " + Mesure.Services + " services, " + Mesure.Processus + " processus, " + Mesure.RamDisponibleMo + " Mo libres";
            }
        }
        internal static string Pct(double? v) { return v.HasValue ? v.Value.ToString("0.00", CultureInfo.CurrentCulture) + " %" : "n/d"; }
    }
}
