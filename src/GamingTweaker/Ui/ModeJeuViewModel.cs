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
        private bool actif, enCours, utiliserPlan = true, peutOublier;
        private string message = "";

        public ModeJeuViewModel(ModeJeu modeJeu, Func<List<ServiceChoix>> services, Func<List<ProcessusChoix>> processus)
        {
            this.modeJeu = modeJeu; servicesSource = services; processusSource = processus;
            actif = modeJeu.IsActive;
            ActiverCommand = new RelayCommand(_ => { var t = ActiverAsync(); }, _ => !enCours && !actif);
            RetablirCommand = new RelayCommand(_ => { var t = RetablirAsync(); }, _ => !enCours && actif);
            ActualiserCommand = new RelayCommand(_ => Actualiser(), _ => !enCours && !actif);
            OublierCommand = new RelayCommand(_ => { var t = OublierAsync(); }, _ => !enCours && actif && peutOublier);
        }

        /// <summary>
        /// Sélection enregistrée (services, processus) : cochée à nouveau à chaque chargement des listes, y compris ce qui ne tourne pas en ce moment.
        /// Branchée par le mode automatique, qui enregistre la même sélection : le Mode Jeu manuel et automatique utilisent toujours les mêmes réglages.
        /// </summary>
        public Func<Tuple<IReadOnlyList<string>, IReadOnlyList<ProcessusChoix>>> SelectionEnregistree { get; set; }

        /// <summary>Une case de service ou de processus a changé (pas lors du chargement des listes).</summary>
        public event Action SelectionChangee;

        /// <summary>Vrai une fois les listes chargées : avant, la sélection enregistrée ne doit pas être écrasée par des listes vides.</summary>
        public bool ListesChargees { get { return listesChargees; } }

        private bool servicesOuverts, processusOuverts, precochage;
        /// <summary>Liste des services dépliée.</summary>
        public bool ServicesOuverts { get { return servicesOuverts; } set { Set(ref servicesOuverts, value); } }
        public bool ProcessusOuverts { get { return processusOuverts; } set { Set(ref processusOuverts, value); } }
        private ICommand basculerServices, basculerProcessus;
        public ICommand BasculerServicesCommand { get { return basculerServices ?? (basculerServices = new RelayCommand(_ => ServicesOuverts = !ServicesOuverts)); } }
        public ICommand BasculerProcessusCommand { get { return basculerProcessus ?? (basculerProcessus = new RelayCommand(_ => ProcessusOuverts = !ProcessusOuverts)); } }

        public int NombreServices { get { return Services.Count(s => s.IsChecked); } }
        public int NombreProcessus { get { return Processus.Count(p => p.IsChecked); } }
        public string TexteServices { get { return Pluriel(NombreServices, "service coché", "services cochés"); } }
        public string TexteProcessus { get { return Pluriel(NombreProcessus, "programme coché", "programmes cochés"); } }
        private static string Pluriel(int n, string un, string plusieurs) { return n == 0 ? "aucun " + un.Split(' ')[0] + " coché" : n + " " + (n > 1 ? plusieurs : un); }

        /// <summary>Ce que fait le Mode Jeu avec les réglages actuels, en une phrase.</summary>
        public string ResumeSelection
        {
            get
            {
                var p = new List<string>();
                if (NombreServices > 0) p.Add("arrête " + Pluriel(NombreServices, "service", "services").Replace(" coché", ""));
                if (NombreProcessus > 0) p.Add("ferme " + Pluriel(NombreProcessus, "programme", "programmes").Replace(" coché", ""));
                if (utiliserPlan) p.Add("passe en Performances élevées");
                if (libererMemoire) p.Add("libère la mémoire");
                if (NvidiaDisponible && profilNvidia != ProfilNvidia.Aucun) p.Add("applique le profil NVIDIA " + ProfilsNvidia.Libelle(profilNvidia));
                if (p.Count == 0) return "Rien n'est choisi : cochez au moins un réglage ci-dessous.";
                return "Le Mode Jeu " + string.Join(", ", p) + ". Tout est remis comme avant avec « Rétablir ».";
            }
        }

        /// <summary>Titre de la carte d'état.</summary>
        public string TitreEtat { get { return enCours ? "Un instant…" : actif ? "Mode Jeu actif" : "Mode Jeu inactif"; } }
        public string TexteEtat
        {
            get
            {
                if (actif) return "Votre PC est en configuration jeu. Quand vous avez fini de jouer, cliquez sur « Rétablir » : tout ce qui a été arrêté est relancé.";
                return "Activez-le juste avant de jouer, ou laissez le mode automatique (plus bas) s'en charger au lancement de vos jeux.";
            }
        }

        public ObservableCollection<ChoixModeJeu> Services { get; } = new ObservableCollection<ChoixModeJeu>();
        public ObservableCollection<ChoixModeJeu> Processus { get; } = new ObservableCollection<ChoixModeJeu>();
        public ICommand ActiverCommand { get; private set; }
        public ICommand RetablirCommand { get; private set; }
        public ICommand ActualiserCommand { get; private set; }
        public ICommand OublierCommand { get; private set; }

        /// <summary>Mode Jeu automatique (null tant que l'application ne l'a pas configuré).</summary>
        public ModeJeuAutoViewModel Auto { get; set; }

        /// <summary>Relit l'état du Mode Jeu (activé ou rétabli par le mode automatique, hors de cette vue).</summary>
        public void Synchroniser() { if (!enCours) EstActif = modeJeu.IsActive; }

        /// <summary>Confirmation avant d'activer (texte, renvoie vrai pour continuer).</summary>
        public Func<string, bool> Confirmer { get; set; }

        public bool EstActif { get { return actif; } private set { if (Set(ref actif, value)) { Raise("PeutModifier"); Raise("TitreEtat"); Raise("TexteEtat"); CommandManager.InvalidateRequerySuggested(); } } }
        public bool EnCours { get { return enCours; } private set { if (Set(ref enCours, value)) { Raise("PeutModifier"); Raise("TitreEtat"); CommandManager.InvalidateRequerySuggested(); } } }
        public bool PeutModifier { get { return !enCours && !actif; } }
        /// <summary>Proposé après un « Rétablir » qui n'a pas pu tout rétablir (ou un état illisible).</summary>
        public bool PeutOublier { get { return peutOublier; } private set { if (Set(ref peutOublier, value)) CommandManager.InvalidateRequerySuggested(); } }
        public bool UtiliserPlan { get { return utiliserPlan; } set { if (Set(ref utiliserPlan, value)) Raise("ResumeSelection"); } }

        private bool libererMemoire;
        /// <summary>Vide le jeu de travail des programmes en arrière-plan à l'activation.</summary>
        public bool LibererMemoire { get { return libererMemoire; } set { if (Set(ref libererMemoire, value)) Raise("ResumeSelection"); } }

        private ProfilNvidia profilNvidia;
        /// <summary>Faux sans carte NVIDIA : le choix du profil NVIDIA est masqué.</summary>
        public bool NvidiaDisponible { get; set; }
        public IReadOnlyList<KeyValuePair<ProfilNvidia, string>> ProfilsNvidiaDisponibles { get; } = new[] { ProfilNvidia.Aucun, ProfilNvidia.Performance, ProfilNvidia.Qualite }
            .Select(p => new KeyValuePair<ProfilNvidia, string>(p, "NVIDIA : " + ProfilsNvidia.Libelle(p))).ToList();
        /// <summary>Profil du pilote NVIDIA le temps du Mode Jeu ; rétabli ensuite (valeurs d'avant).</summary>
        public ProfilNvidia ProfilNvidia { get { return profilNvidia; } set { if (Set(ref profilNvidia, value)) Raise("ResumeSelection"); } }

        /// <summary>Accès au pilote NVIDIA pour « Lire les réglages NVIDIA » (null : bouton inactif).</summary>
        public INvidiaReglages Nvidia { get; set; }
        private string etatNvidia = "";
        /// <summary>Réglages lus dans le pilote, une ligne par réglage.</summary>
        public string EtatNvidia { get { return etatNvidia; } private set { Set(ref etatNvidia, value ?? ""); } }
        private ICommand lireNvidiaCommand;
        public ICommand LireNvidiaCommand { get { return lireNvidiaCommand ?? (lireNvidiaCommand = new RelayCommand(_ => { var t = LireNvidiaAsync(); }, _ => Nvidia != null)); } }

        /// <summary>Lit dans le pilote les réglages que le profil NVIDIA modifie (diagnostic : ce qui est réellement en place).</summary>
        public async Task LireNvidiaAsync()
        {
            var nv = Nvidia;
            if (nv == null) return;
            EtatNvidia = "Lecture du pilote…";
            EtatNvidia = await Task.Run(() =>
            {
                if (!nv.Disponible) return "Pilote NVIDIA introuvable (nvapi64.dll absent).";
                var lignes = new List<string>();
                foreach (var id in ProfilsNvidia.Diagnostic)
                {
                    try
                    {
                        var v = nv.Lire(id);
                        lignes.Add(ProfilsNvidia.NomReglage(id) + " : " + ProfilsNvidia.NomValeur(id, v[1]) + (v[0] == 1 ? "" : " (non défini dans le profil global)"));
                    }
                    catch (Exception ex) { lignes.Add(ProfilsNvidia.NomReglage(id) + " : lecture impossible (" + ex.Message + ")"); }
                }
                return string.Join("\r\n", lignes);
            });
        }
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
            precochage = true;
            try
            {
                Services.Clear(); Processus.Clear();
                foreach (var s in res.Item1) Services.Add(Suivre(new ChoixModeJeu { Nom = s.Nom, Affichage = s.Affichage, Detail = s.Nom }));
                foreach (var p in res.Item2) Processus.Add(Suivre(new ChoixModeJeu { Nom = p.Nom, Affichage = p.Nom, Detail = p.Chemin, Chemin = p.Chemin }));
                var sel = SelectionEnregistree == null ? null : SelectionEnregistree();
                if (sel != null) Precocher(sel.Item1, sel.Item2);
            }
            finally { precochage = false; }
            RaiseCompteurs();
        }

        /// <summary>
        /// Coche la sélection enregistrée. Un service ou programme enregistré qui ne tourne pas en ce moment est ajouté en tête de liste, coché,
        /// pour qu'il reste visible et décochable.
        /// </summary>
        public void Precocher(IEnumerable<string> services, IEnumerable<ProcessusChoix> processus)
        {
            int i = 0;
            foreach (var nom in services ?? Enumerable.Empty<string>())
            {
                var c = Services.FirstOrDefault(x => string.Equals(x.Nom, nom, StringComparison.OrdinalIgnoreCase));
                if (c == null) Services.Insert(i++, c = Suivre(new ChoixModeJeu { Nom = nom, Affichage = nom, Detail = "Pas démarré en ce moment" }));
                c.IsChecked = true;
            }
            i = 0;
            foreach (var p in processus ?? Enumerable.Empty<ProcessusChoix>())
            {
                if (p == null) continue;
                var c = Processus.FirstOrDefault(x => string.Equals(x.Nom, p.Nom, StringComparison.OrdinalIgnoreCase)
                                                    && (string.IsNullOrEmpty(p.Chemin) || string.Equals(x.Chemin, p.Chemin, StringComparison.OrdinalIgnoreCase)));
                if (c == null) Processus.Insert(i++, c = Suivre(new ChoixModeJeu { Nom = p.Nom, Affichage = p.Nom, Detail = (p.Chemin ?? "") + " (pas lancé en ce moment)", Chemin = p.Chemin }));
                c.IsChecked = true;
            }
        }

        private void RaiseCompteurs()
        {
            Raise("NombreCoches"); Raise("NombreServices"); Raise("NombreProcessus"); Raise("TexteServices"); Raise("TexteProcessus"); Raise("ResumeSelection");
        }

        private ChoixModeJeu Suivre(ChoixModeJeu c)
        {
            c.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != "IsChecked" || precochage) return;
                RaiseCompteurs();
                var h = SelectionChangee; if (h != null) h();
            };
            return c;
        }

        public ModeJeuConfig ConstruireConfig()
        {
            var c = new ModeJeuConfig { PlanGuid = utiliserPlan ? ModeJeu.PlanPerformances : null, Nvidia = NvidiaDisponible ? profilNvidia : ProfilNvidia.Aucun, LibererMemoire = libererMemoire };
            foreach (var s in Services.Where(x => x.IsChecked)) c.Services.Add(s.Nom);
            foreach (var p in Processus.Where(x => x.IsChecked)) c.Processus.Add(new ProcessusChoix { Nom = p.Nom, Chemin = p.Chemin });
            return c;
        }

        public async Task ActiverAsync()
        {
            if (enCours || actif) return;
            var cfg = ConstruireConfig();
            if (cfg.Services.Count + cfg.Processus.Count == 0 && cfg.PlanGuid == null && cfg.Nvidia == ProfilNvidia.Aucun && !cfg.LibererMemoire) { Message = "Rien n'est choisi : cochez au moins un réglage dans « Ce que fait le Mode Jeu »."; return; }
            var texte = "Le Mode Jeu va arrêter " + cfg.Services.Count + " service(s) et fermer " + cfg.Processus.Count + " processus (travail non enregistré perdu)"
                      + (cfg.PlanGuid != null ? ", et passer au plan Performances élevées" : "")
                      + (cfg.Nvidia != ProfilNvidia.Aucun ? ", et appliquer le profil NVIDIA " + ProfilsNvidia.Libelle(cfg.Nvidia) : "")
                      + (cfg.LibererMemoire ? ", et libérer la mémoire des programmes en arrière-plan" : "")
                      + ".\r\nTout sera rétabli avec « Rétablir ».\r\n\r\nContinuer ?";
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
                PeutOublier = EstActif && !b.Verrouille;
                Message = Resume("Mode Jeu rétabli", b) + (PeutOublier ? " Si le problème persiste, « Oublier l'état » abandonne le rétablissement." : "");
            }
            catch (Exception ex) { EstActif = modeJeu.IsActive; PeutOublier = EstActif; Message = "Le rétablissement a échoué : " + ex.Message; }
            finally { EnCours = false; }
        }

        /// <summary>Après confirmation, supprime l'état : ce qui reste arrêté devra être relancé à la main.</summary>
        public async Task OublierAsync()
        {
            if (enCours || !actif || !peutOublier) return;
            var texte = "Oublier l'état du Mode Jeu ?\r\n\r\nGamingTweaker ne tentera plus de rétablir ce qui a été arrêté : les services et processus encore arrêtés "
                      + "devront être relancés à la main (ou au prochain redémarrage), et le plan d'alimentation vérifié dans les options d'alimentation.";
            if (Confirmer == null || !Confirmer(texte)) return;
            EnCours = true;
            try
            {
                var b = await Task.Run(() => modeJeu.Oublier());
                EstActif = modeJeu.IsActive;
                if (!EstActif) PeutOublier = false;
                Message = b.Verrouille ? (b.Avertissements.FirstOrDefault() ?? "") : "État du Mode Jeu oublié : vérifiez à la main les services et processus arrêtés.";
            }
            catch (Exception ex) { EstActif = modeJeu.IsActive; Message = "L'état n'a pas pu être oublié : " + ex.Message; }
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
