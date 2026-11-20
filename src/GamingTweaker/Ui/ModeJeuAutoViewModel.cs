using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    /// <summary>
    /// Mode Jeu automatique : liste des jeux surveillés, réglages utilisés (sélection du Mode Jeu enregistrée), démarrage avec Windows.
    /// Toute modification est enregistrée tout de suite dans le dossier protégé.
    /// </summary>
    public class ModeJeuAutoViewModel : ObservableObject
    {
        private readonly string machineDir;
        private readonly ModeJeuViewModel modeJeu;
        private readonly Func<bool> demarrageInstalle;
        private readonly Action<bool> definirDemarrage;
        private ModeJeuAutoConfig config;
        private JeuSurveille jeuSelectionne;
        private bool demarrage, occupe;
        private string message = "";

        public ModeJeuAutoViewModel(string machineDir, ModeJeuViewModel modeJeu, Func<bool> demarrageInstalle = null, Action<bool> definirDemarrage = null)
        {
            this.machineDir = machineDir; this.modeJeu = modeJeu;
            this.demarrageInstalle = demarrageInstalle; this.definirDemarrage = definirDemarrage;
            config = ModeJeuAutoConfig.Charger(machineDir);
            foreach (var j in config.Jeux) Jeux.Add(j);
            if (modeJeu != null)
            {
                // Le profil NVIDIA et le plan choisis dans le Mode Jeu valent aussi pour le mode automatique, sans bouton à cliquer.
                modeJeu.ProfilNvidia = config.Nvidia;
                modeJeu.UtiliserPlan = config.Plan;
                modeJeu.LibererMemoire = config.LibererMemoire;
                // Services et processus : une seule sélection, enregistrée à chaque case cochée et recochée au chargement des listes.
                modeJeu.SelectionEnregistree = () => Tuple.Create<IReadOnlyList<string>, IReadOnlyList<ProcessusChoix>>(config.Services.ToList(), config.Processus.ToList());
                modeJeu.SelectionChangee += () => { if (modeJeu.ListesChargees) ReprendreSelection(true, null); };
                modeJeu.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "ProfilNvidia" && config.Nvidia != modeJeu.ProfilNvidia)
                    { config.Nvidia = modeJeu.ProfilNvidia; Enregistrer("Profil NVIDIA du mode automatique : " + ProfilsNvidia.Libelle(config.Nvidia) + "."); }
                    else if (e.PropertyName == "UtiliserPlan" && config.Plan != modeJeu.UtiliserPlan)
                    { config.Plan = modeJeu.UtiliserPlan; Enregistrer("Plan d'alimentation du mode automatique mis à jour."); }
                    else if (e.PropertyName == "LibererMemoire" && config.LibererMemoire != modeJeu.LibererMemoire)
                    { config.LibererMemoire = modeJeu.LibererMemoire; Enregistrer("Libération de mémoire du mode automatique mise à jour."); }
                };
            }
            AjouterExeCommand = new RelayCommand(_ => AjouterExe(), _ => !occupe);
            AjouterSteamCommand = new RelayCommand(_ => AjouterDepuis("Steam", () => Core.Jeux.JeuxSteam()), _ => !occupe);
            AjouterEpicCommand = new RelayCommand(_ => AjouterDepuis("Epic Games", () => Core.Jeux.JeuxEpic()), _ => !occupe);
            RetirerCommand = new RelayCommand(_ => Retirer(), _ => !occupe && jeuSelectionne != null);
            ReprendreSelectionCommand = new RelayCommand(_ => ReprendreSelection(), _ => !occupe);
        }

        public ObservableCollection<JeuSurveille> Jeux { get; } = new ObservableCollection<JeuSurveille>();
        public ICommand AjouterExeCommand { get; private set; }
        public ICommand AjouterSteamCommand { get; private set; }
        public ICommand AjouterEpicCommand { get; private set; }
        public ICommand RetirerCommand { get; private set; }
        public ICommand ReprendreSelectionCommand { get; private set; }

        /// <summary>Dialogue de choix d'un .exe ; renvoie le chemin ou null.</summary>
        public Func<string> ChoisirExe { get; set; }
        /// <summary>Dialogue de choix parmi des jeux trouvés (titre, jeux) ; renvoie ceux cochés.</summary>
        public Func<string, IReadOnlyList<JeuSurveille>, IReadOnlyList<JeuSurveille>> ChoisirJeux { get; set; }
        /// <summary>Appelé après chaque enregistrement (zone de notification, surveillance).</summary>
        public event Action ConfigChangee;

        /// <summary>Réglages enregistrés (lus par la surveillance des jeux).</summary>
        public ModeJeuAutoConfig Config { get { return config; } }

        public JeuSurveille JeuSelectionne { get { return jeuSelectionne; } set { if (Set(ref jeuSelectionne, value)) CommandManager.InvalidateRequerySuggested(); } }
        public string Message { get { return message; } set { Set(ref message, value ?? ""); } }

        public bool Actif
        {
            get { return config.Actif; }
            set
            {
                if (config.Actif == value) return;
                config.Actif = value;
                Enregistrer(value ? "Mode Jeu automatique activé : il s'active au lancement d'un jeu de la liste et tout est rétabli quand vous quittez le dernier."
                                  : "Mode Jeu automatique désactivé.");
                Raise(); Raise("RappelDemarrage");
            }
        }

        public bool PrioriteHaute
        {
            get { return config.PrioriteHaute; }
            set
            {
                if (config.PrioriteHaute == value) return;
                config.PrioriteHaute = value;
                Enregistrer(value ? "Le jeu en cours passera en priorité haute (jamais « temps réel »)." : "Priorité du jeu laissée à Windows.");
                Raise();
            }
        }

        public string ResumeReglages
        {
            get
            {
                if (config.Jeux.Count == 0 && Jeux.Count == 0) return "Ajoutez au moins un jeu : le Mode Jeu s'activera quand il démarre.";
                return Jeux.Count + " jeu" + (Jeux.Count > 1 ? "x" : "") + " surveillé" + (Jeux.Count > 1 ? "s" : "")
                     + ". Le mode automatique utilise les mêmes réglages que l'étape 1.";
            }
        }

        /// <summary>Le mode automatique ne marche que si GamingTweaker tourne : rappel quand il est actif sans démarrage avec Windows.</summary>
        public bool RappelDemarrage { get { return config.Actif && !demarrage; } }

        public bool DemarrageAvecWindows
        {
            get { return demarrage; }
            set
            {
                if (demarrage == value || occupe || definirDemarrage == null) return;
                var t = DefinirDemarrageAsync(value);
            }
        }

        public bool Occupe { get { return occupe; } private set { if (Set(ref occupe, value)) CommandManager.InvalidateRequerySuggested(); } }

        /// <summary>Lit l'état de la tâche de démarrage hors du thread de l'interface.</summary>
        public async Task ActualiserDemarrageAsync()
        {
            if (demarrageInstalle == null) return;
            bool installe;
            try { installe = await Task.Run(demarrageInstalle); } catch (Exception) { installe = false; }
            demarrage = installe; Raise("DemarrageAvecWindows"); Raise("RappelDemarrage");
        }

        private async Task DefinirDemarrageAsync(bool valeur)
        {
            Occupe = true;
            try
            {
                await Task.Run(() => definirDemarrage(valeur));
                demarrage = valeur;
                Message = valeur ? "GamingTweaker démarrera avec Windows, dans la zone de notification, avec les droits administrateur (copie protégée dans Program Files)."
                                 : "GamingTweaker ne démarrera plus avec Windows.";
            }
            catch (Exception ex) { Message = (valeur ? "Démarrage avec Windows non activé : " : "Démarrage avec Windows non retiré : ") + ex.Message; }
            finally { Occupe = false; Raise("DemarrageAvecWindows"); Raise("RappelDemarrage"); }
        }

        /// <summary>Reprend la sélection actuelle du Mode Jeu (services, processus, plan) pour le mode automatique.</summary>
        public void ReprendreSelection() { ReprendreSelection(true, "Sélection du Mode Jeu enregistrée pour le mode automatique."); }

        private void ReprendreSelection(bool enregistrer, string messageOk)
        {
            if (modeJeu == null) return;
            var c = modeJeu.ConstruireConfig();
            config.Services = c.Services.ToList();
            config.Processus = c.Processus.Select(p => new ProcessusChoix { Nom = p.Nom, Chemin = p.Chemin }).ToList();
            config.Plan = c.PlanGuid != null;
            config.Nvidia = c.Nvidia;
            config.LibererMemoire = c.LibererMemoire;
            Raise("ResumeReglages");
            if (enregistrer) Enregistrer(messageOk);
        }

        public void AjouterExe()
        {
            var chemin = ChoisirExe == null ? null : ChoisirExe();
            if (string.IsNullOrEmpty(chemin)) return;
            try { Ajouter(new[] { Core.Jeux.DepuisExe(chemin) }); }
            catch (Exception ex) { Message = ex.Message; }
        }

        private async void AjouterDepuis(string bibliotheque, Func<List<JeuSurveille>> trouver)
        {
            Occupe = true;
            List<JeuSurveille> trouves;
            try { trouves = await Task.Run(trouver); }
            catch (Exception ex) { Message = "Bibliothèque " + bibliotheque + " illisible : " + ex.Message; return; }
            finally { Occupe = false; }
            var nouveaux = trouves.Where(j => !Jeux.Any(x => string.Equals(x.Chemin, j.Chemin, StringComparison.OrdinalIgnoreCase))).ToList();
            if (trouves.Count == 0) { Message = "Aucun jeu " + bibliotheque + " trouvé sur ce PC."; return; }
            if (nouveaux.Count == 0) { Message = "Tous les jeux " + bibliotheque + " trouvés sont déjà dans la liste."; return; }
            var choisis = ChoisirJeux == null ? nouveaux : ChoisirJeux("Jeux " + bibliotheque, nouveaux);
            if (choisis != null && choisis.Count > 0) Ajouter(choisis);
        }

        public void Ajouter(IEnumerable<JeuSurveille> jeux)
        {
            int n = 0;
            foreach (var j in jeux)
            {
                if (j == null || Jeux.Any(x => string.Equals(x.Chemin, j.Chemin, StringComparison.OrdinalIgnoreCase))) continue;
                Jeux.Add(j); n++;
            }
            if (n > 0) Enregistrer(n + " jeu(x) ajouté(s) à la liste.");
        }

        public void Retirer()
        {
            if (jeuSelectionne == null) return;
            var nom = jeuSelectionne.Nom;
            Jeux.Remove(jeuSelectionne);
            JeuSelectionne = null;
            Enregistrer(nom + " retiré de la liste.");
        }

        private void Enregistrer(string messageOk)
        {
            config.Jeux = Jeux.ToList();
            try { config.Enregistrer(machineDir); if (messageOk != null) Message = messageOk; }
            catch (Exception ex) { Message = "Réglages du Mode Jeu automatique non enregistrés : " + ex.Message; }
            Raise("ResumeReglages");
            var h = ConfigChangee;
            if (h != null) h();
        }
    }
}

namespace GamingTweaker.Ui
{
    /// <summary>Visible si la valeur n'est pas null (section masquée tant que le mode automatique n'est pas configuré).</summary>
    public class NonNulVersVisibilite : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
