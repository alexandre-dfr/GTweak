using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    /// <summary>Diagnostic jeu : mesure (hors du thread de l'interface), points classés, action proposée pour chacun.</summary>
    public class DiagnosticViewModel : ObservableObject
    {
        private readonly Func<MesuresDiag> mesurer;
        private bool enCours, analyse;
        private string resume = "Analyse non lancée.";

        public DiagnosticViewModel(Func<MesuresDiag> mesurer)
        {
            this.mesurer = mesurer;
            AnalyserCommand = new RelayCommand(_ => { var t = AnalyserAsync(); }, _ => !enCours);
            ActionCommand = new RelayCommand(p => { var pt = p as PointDiag; if (pt != null && Executer != null) Executer(pt.Action); });
        }

        public ObservableCollection<PointDiag> Points { get; } = new ObservableCollection<PointDiag>();
        public ICommand AnalyserCommand { get; private set; }
        public ICommand ActionCommand { get; private set; }
        /// <summary>Exécute l'action d'un point (« ms-settings:… », « option:id », « menu:nom »).</summary>
        public Action<string> Executer { get; set; }

        public bool EnCours { get { return enCours; } private set { if (Set(ref enCours, value)) CommandManager.InvalidateRequerySuggested(); } }
        public bool Analyse { get { return analyse; } private set { Set(ref analyse, value); } }
        public string Resume { get { return resume; } private set { Set(ref resume, value); } }
        public int NombreAttention { get { return Points.Count(p => p.Niveau == NiveauDiag.Attention); } }

        /// <summary>Analyse une seule fois (première ouverture) ; « Relancer » force une nouvelle mesure.</summary>
        public void AnalyserSiBesoin() { if (!analyse && !enCours) { var t = AnalyserAsync(); } }

        public async Task AnalyserAsync()
        {
            if (enCours) return;
            EnCours = true; Resume = "Analyse de ce PC…";
            try
            {
                var m = await Task.Run(mesurer);
                var pts = DiagnosticJeu.Analyser(m, DateTime.Now);
                Points.Clear();
                foreach (var p in pts) Points.Add(p);
                var a = pts.Count(p => p.Niveau == NiveauDiag.Attention);
                var i = pts.Count(p => p.Niveau == NiveauDiag.Info);
                Resume = a == 0 && i == 0 ? "Rien à signaler : ce PC est bien réglé pour le jeu."
                       : (a > 0 ? a + " point" + (a > 1 ? "s" : "") + " à corriger" : "Aucun point bloquant") + (i > 0 ? ", " + i + " suggestion" + (i > 1 ? "s" : "") : "") + ".";
                Analyse = true;
                Raise("NombreAttention");
            }
            catch (Exception ex) { Resume = "Analyse impossible : " + ex.Message; }
            finally { EnCours = false; }
        }
    }
}
