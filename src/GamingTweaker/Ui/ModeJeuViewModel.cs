using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    public class ChoixModeJeu : ObservableObject
    {
        private bool coche;
        public string Nom { get; set; }
        public string Affichage { get; set; }
        public string Detail { get; set; }
        public string Chemin { get; set; }
        public bool IsChecked { get { return coche; } set { Set(ref coche, value); } }
    }

    public class ModeJeuViewModel : ObservableObject
    {
        private readonly ModeJeu modeJeu;
        private readonly Func<List<ServiceChoix>> servicesSource;
        private readonly Func<List<ProcessusChoix>> processusSource;
        private bool actif, enCours, utiliserPlan = true;
        private string message = "";

        public ModeJeuViewModel(ModeJeu modeJeu, Func<List<ServiceChoix>> services, Func<List<ProcessusChoix>> processus)
        {
            this.modeJeu = modeJeu; servicesSource = services; processusSource = processus;
            actif = modeJeu.IsActive;
            ActiverCommand = new RelayCommand(_ => { var t = ActiverAsync(); }, _ => !enCours && !actif);
            RetablirCommand = new RelayCommand(_ => { var t = RetablirAsync(); }, _ => !enCours && actif);
            ActualiserCommand = new RelayCommand(_ => Actualiser(), _ => !enCours && !actif);
        }

        public ObservableCollection<ChoixModeJeu> Services { get; } = new ObservableCollection<ChoixModeJeu>();
        public ObservableCollection<ChoixModeJeu> Processus { get; } = new ObservableCollection<ChoixModeJeu>();
        public ICommand ActiverCommand { get; private set; }
        public ICommand RetablirCommand { get; private set; }
        public ICommand ActualiserCommand { get; private set; }

        /// <summary>Confirmation avant d'activer (texte, renvoie vrai pour continuer).</summary>
        public Func<string, bool> Confirmer { get; set; }

        public bool EstActif { get { return actif; } private set { if (Set(ref actif, value)) { Raise("PeutModifier"); CommandManager.InvalidateRequerySuggested(); } } }
        public bool EnCours { get { return enCours; } private set { if (Set(ref enCours, value)) { Raise("PeutModifier"); CommandManager.InvalidateRequerySuggested(); } } }
        public bool PeutModifier { get { return !enCours && !actif; } }
        public bool UtiliserPlan { get { return utiliserPlan; } set { Set(ref utiliserPlan, value); } }
        public string Message { get { return message; } set { Set(ref message, value ?? ""); } }
        public int NombreCoches { get { return Services.Count(s => s.IsChecked) + Processus.Count(p => p.IsChecked); } }

        private bool listesChargees;
        /// <summary>Charge les listes une seule fois (appelé à la première ouverture du menu).</summary>
        public void ChargerSiBesoin() { if (!listesChargees && !actif) Actualiser(); }

        public void Actualiser()
        {
            listesChargees = true;
            var t = ActualiserAsync();
        }

        /// <summary>Énumère services et processus hors du thread de l'interface.</summary>
        public async Task ActualiserAsync()
        {
            listesChargees = true;
            var res = await Task.Run(() => Tuple.Create(servicesSource(), processusSource()));
            Services.Clear(); Processus.Clear();
            foreach (var s in res.Item1) Services.Add(Suivre(new ChoixModeJeu { Nom = s.Nom, Affichage = s.Affichage, Detail = s.Nom }));
            foreach (var p in res.Item2) Processus.Add(Suivre(new ChoixModeJeu { Nom = p.Nom, Affichage = p.Nom, Detail = p.Chemin, Chemin = p.Chemin }));
            Raise("NombreCoches");
        }

        private ChoixModeJeu Suivre(ChoixModeJeu c) { c.PropertyChanged += (s, e) => { if (e.PropertyName == "IsChecked") Raise("NombreCoches"); }; return c; }

        public ModeJeuConfig ConstruireConfig()
        {
            var c = new ModeJeuConfig { PlanGuid = utiliserPlan ? ModeJeu.PlanPerformances : null };
            foreach (var s in Services.Where(x => x.IsChecked)) c.Services.Add(s.Nom);
            foreach (var p in Processus.Where(x => x.IsChecked)) c.Processus.Add(new ProcessusChoix { Nom = p.Nom, Chemin = p.Chemin });
            return c;
        }

        public async Task ActiverAsync()
        {
            if (enCours || actif) return;
            var cfg = ConstruireConfig();
            if (cfg.Services.Count + cfg.Processus.Count == 0 && cfg.PlanGuid == null) { Message = "Rien n'est coché : choisissez des services, des processus ou le plan d'alimentation."; return; }
            var texte = "Le Mode Jeu va arrêter " + cfg.Services.Count + " service(s) et fermer " + cfg.Processus.Count + " processus (travail non enregistré perdu)"
                      + (cfg.PlanGuid != null ? ", et passer au plan Performances maximales" : "") + ".\r\nTout sera rétabli avec « Rétablir ».\r\n\r\nContinuer ?";
            if (Confirmer != null && !Confirmer(texte)) return;
            EnCours = true; Message = "Activation en cours…";
            try
            {
                var b = await Task.Run(() => modeJeu.Activate(cfg));
                EstActif = modeJeu.IsActive;
                Message = Resume("Mode Jeu activé", b);
            }
            catch (Exception ex) { EstActif = modeJeu.IsActive; Message = "Le Mode Jeu a échoué : " + ex.Message; }
            finally { EnCours = false; }
        }

        public async Task RetablirAsync()
        {
            if (enCours) return;
            EnCours = true; Message = "Rétablissement en cours…";
            try
            {
                var b = await Task.Run(() => modeJeu.Restore());
                EstActif = modeJeu.IsActive;
                Message = Resume("Mode Jeu rétabli", b);
            }
            catch (Exception ex) { EstActif = modeJeu.IsActive; Message = "Le rétablissement a échoué : " + ex.Message; }
            finally { EnCours = false; }
        }

        private static string Resume(string titre, ModeJeuBilan b)
        {
            if (b.Verrouille) return b.Avertissements.FirstOrDefault() ?? "";
            var t = titre + " (" + b.Actions.Count + " action(s)).";
            if (b.Avertissements.Count > 0) t += " " + string.Join(" ", b.Avertissements);
            return t;
        }
    }
}
