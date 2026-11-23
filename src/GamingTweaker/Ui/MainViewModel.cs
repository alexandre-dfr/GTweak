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
using GamingTweaker.Lists;

namespace GamingTweaker.Ui
{
    /// <summary>
    /// Vue-modèle de la fenêtre principale. Réparti en fichiers partiels : navigation, filtre, sélection et détection ici ;
    /// profils (MainViewModel.Profils.cs), « Tout restaurer » (MainViewModel.Restauration.cs), aperçu et exécution (MainViewModel.Execution.cs).
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        public static readonly string[] MenusOptions = { "Mises à jour", "Télémétrie", "Nettoyage", "Optimisation", "Bloatware" };

        private readonly IReadOnlyList<CatalogItem> items;
        private readonly MachineFacts facts;
        private readonly DataPaths paths;
        private readonly Func<IEnumerable<CatalogItem>, Task<Dictionary<string, DetectState>>> detect;
        private readonly Func<RunRequest, IProgress<string>, CancellationToken, Task<SessionReport>> run;
        private readonly Func<IEnumerable<CatalogItem>, Task<Dictionary<string, long?>>> estimate;
        private readonly Dictionary<string, List<string>> listesChargees = new Dictionary<string, List<string>>();
        private ProfilListes profilListes;
        public const string MenuAccueil = "Accueil";
        public const string MenuDiagnostic = "Diagnostic";
        private string menuCourant = MenuAccueil;
        private string recherche = "";
        private string profilSelectionne;
        private string message = "";
        private string bannière = "";
        private bool chargementProfil;
        private string avertissementProfil = "";

        public MainViewModel(IReadOnlyList<CatalogItem> items, MachineFacts facts, DataPaths paths,
            Func<IEnumerable<CatalogItem>, Task<Dictionary<string, DetectState>>> detect,
            Func<RunRequest, IProgress<string>, CancellationToken, Task<SessionReport>> run,
            Func<OptionViewModel, bool> confirmerRisque, ListProviderRegistry registre = null,
            Func<IEnumerable<CatalogItem>, Task<Dictionary<string, long?>>> estimate = null)
        {
            this.estimate = estimate ?? Estimation.RunAsync;
            this.items = items; this.facts = facts; this.paths = paths; this.detect = detect; this.run = run;

            Options = items.Where(i => Compatibilite.IsCompatible(i.Compatibilite ?? new CompatRule(), facts))
                           .Select(i => new OptionViewModel(i, confirmerRisque, string.IsNullOrEmpty(i.Liste) || registre == null ? null : registre.Get(i.Liste))).ToList();
            foreach (var o in Options) o.CocheChanged += () => { Raise("SelectedCount"); Raise("AUneSelection"); Raise("ResumeSelection"); MajCompteurs(); };

            // Ordre de la barre latérale : accueil et diagnostic, menus d'options, Mode Jeu ; puis Historique, Informations et Paramètres en dernier.
            var menus = new[] { MenuAccueil, MenuDiagnostic }.Concat(MenusOptions).Concat(new[] { MenuModeJeu })
                .Select(n => new MenuEntry { Nom = n, EstEmplacement = Array.IndexOf(MenusOptions, n) < 0 }).ToList();
            menus.Add(new MenuEntry { EstSeparateur = true });
            foreach (var n in new[] { MenuHistorique, MenuInformations, MenuParametres }) menus.Add(new MenuEntry { Nom = n, EstEmplacement = true });
            Menus = menus;

            RecommanderCommand = new RelayCommand(_ => ApplyRecommended(MenuCourant), _ => !EstEmplacement && !EnRecherche && Visibles.Any(o => o.Recommandee));
            AllerCommand = new RelayCommand(m => { var nom = m as string; if (!string.IsNullOrEmpty(nom)) { Recherche = ""; MenuCourant = nom; } });
            ProfilRapideCommand = new RelayCommand(n => ChargerProfilRapide(n as string));
            MajAccueil();
            DecocherCommand = new RelayCommand(_ => Decocher(), _ => Visibles.Any(o => o.IsChecked));
            RafraichirProfils();
            Filtrer();
        }

        public IReadOnlyList<CatalogItem> Catalogue { get { return items; } }
        public IReadOnlyList<OptionViewModel> Options { get; private set; }
        public IReadOnlyList<MenuEntry> Menus { get; private set; }
        public ObservableCollection<OptionViewModel> Visibles { get; } = new ObservableCollection<OptionViewModel>();
        public ObservableCollection<string> ProfilsDisponibles { get; } = new ObservableCollection<string>();
        public ICommand RecommanderCommand { get; private set; }

        // ---------- Écran d'accueil ----------

        /// <summary>Ouvre le menu passé en paramètre.</summary>
        public ICommand AllerCommand { get; private set; }
        /// <summary>Charge le profil intégré dont le nom (sans suffixe) est passé en paramètre.</summary>
        public ICommand ProfilRapideCommand { get; private set; }
        public bool EstAccueil { get { return !EnRecherche && menuCourant == MenuAccueil; } }

        // ---------- Diagnostic jeu ----------

        private DiagnosticViewModel diagnosticVm;
        /// <summary>Diagnostic jeu (null tant que l'application ne l'a pas branché).</summary>
        public DiagnosticViewModel Diagnostic
        {
            get { return diagnosticVm; }
            set { if (Set(ref diagnosticVm, value) && value != null) value.Executer = ExecuterActionDiagnostic; }
        }
        public bool EstDiagnostic { get { return !EnRecherche && menuCourant == MenuDiagnostic; } }
        /// <summary>Ouvre une page des Paramètres de Windows (« ms-settings:… ») ; branché par l'application.</summary>
        public Action<string> OuvrirParametres { get; set; }

        /// <summary>« option:id » : ouvre son menu et la coche ; « menu:nom » : ouvre le menu ; « ms-settings:… » : page des Paramètres.</summary>
        public void ExecuterActionDiagnostic(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            if (action.StartsWith("option:", StringComparison.Ordinal))
            {
                var o = Options.FirstOrDefault(x => x.Id == action.Substring(7));
                if (o == null) { Message = "Cette option n'est pas disponible sur ce PC."; return; }
                Recherche = ""; MenuCourant = o.Menu;
                o.IsChecked = true;
                Message = o.IsChecked ? "« " + o.Titre + " » est cochée. Vérifiez avec « Aperçu », puis « LANCER »." : "";
            }
            else if (action.StartsWith("menu:", StringComparison.Ordinal)) { Recherche = ""; MenuCourant = action.Substring(5); }
            else if (action.StartsWith("ms-settings:", StringComparison.Ordinal) && OuvrirParametres != null)
            {
                try { OuvrirParametres(action); } catch (Exception ex) { Message = "Impossible d'ouvrir les Paramètres : " + ex.Message; }
            }
        }
        public ObservableCollection<LigneAccueil> EtatMenus { get; } = new ObservableCollection<LigneAccueil>();
        public IReadOnlyList<TuileProfil> ProfilsRapides
        {
            get
            {
                return ProfilsIntegres.Tous.Select((p, i) => new TuileProfil
                {
                    Nom = p.Nom, Accroche = p.Accroche, Description = p.Description, Rang = i,
                    Nombre = ProfilsIntegres.Construire(p, items, facts).Options.Count,
                }).ToList();
            }
        }

        private int totalAppliquees, totalOptions;
        public int TotalAppliquees { get { return totalAppliquees; } }
        /// <summary>Part des réglages déjà en place (0 à 1), pour la jauge de l'accueil.</summary>
        public double ProportionAppliquees { get { return totalOptions == 0 ? 0 : (double)totalAppliquees / totalOptions; } }
        public int TotalOptions { get { return totalOptions; } }
        /// <summary>Phrase de synthèse de l'accueil (avant la détection : « Analyse de ce PC… »).</summary>
        public string SyntheseAccueil
        {
            get
            {
                if (Options.Any(o => o.Etat == "Détection…")) return "Analyse de ce PC en cours…";
                if (Options.Count > 0 && Options.All(o => o.Etat == "Inconnu")) return "L'état de ce PC n'a pas pu être lu.";
                return totalAppliquees + " réglage" + (totalAppliquees > 1 ? "s" : "") + " sur " + totalOptions + " déjà en place sur ce PC.";
            }
        }

        private IReadOnlyList<TuileMateriel> tuilesMateriel = MaterielPc.EnLecture();
        /// <summary>Processeur, carte graphique, mémoire et disque système (tuiles « Lecture… » tant que l'application ne les a pas lues).</summary>
        public IReadOnlyList<TuileMateriel> TuilesMateriel
        {
            get { return tuilesMateriel; }
            set { Set(ref tuilesMateriel, value ?? MaterielPc.EnLecture()); }
        }

        public string ResumePc
        {
            get
            {
                if (facts == null) return "";
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(facts.WindowsMajor)) parts.Add(("Windows " + facts.WindowsMajor + " " + facts.Edition).Trim());
                if (!string.IsNullOrEmpty(facts.CpuVendor)) parts.Add("processeur " + facts.CpuVendor);
                if (facts.GpuVendors != null && facts.GpuVendors.Count > 0) parts.Add("carte graphique " + string.Join(" / ", facts.GpuVendors));
                if (facts.EstPortable) parts.Add("portable");
                return string.Join(" · ", parts);
            }
        }

        /// <summary>Recalcule l'état par menu (après la détection).</summary>
        private void MajAccueil()
        {
            EtatMenus.Clear();
            foreach (var m in MenusOptions)
            {
                var opts = Options.Where(o => o.Menu == m).ToList();
                if (opts.Count == 0) continue;
                EtatMenus.Add(new LigneAccueil { Menu = m, Total = opts.Count, Appliquees = opts.Count(o => o.Etat == EtatDejaApplique) });
            }
            totalAppliquees = EtatMenus.Sum(l => l.Appliquees); totalOptions = EtatMenus.Sum(l => l.Total);
            Raise("TotalAppliquees"); Raise("TotalOptions"); Raise("SyntheseAccueil"); Raise("ProportionAppliquees");
            Raise("AvancementMenu"); Raise("ProportionMenu");
        }

        private LigneAccueil LigneMenuCourant { get { return EtatMenus.FirstOrDefault(l => l.Menu == menuCourant); } }

        /// <summary>« 12 / 32 appliquées » pour le menu d'options affiché ; vide ailleurs ou pendant la détection.</summary>
        public string AvancementMenu
        {
            get
            {
                var l = LigneMenuCourant;
                if (l == null) return "";
                if (Options.Any(o => o.Menu == menuCourant && o.Etat == "Détection…")) return "Analyse…";
                return l.Appliquees + " / " + l.Total + " appliquée" + (l.Appliquees > 1 ? "s" : "");
            }
        }

        public double ProportionMenu { get { var l = LigneMenuCourant; return l == null ? 0 : l.Proportion; } }

        public void ChargerProfilRapide(string nom)
        {
            var pi = ProfilsIntegres.Tous.FirstOrDefault(p => p.Nom == nom);
            if (pi == null) return;
            if (ProfilSelectionne == pi.NomAffiche) RechargerProfil(); else ProfilSelectionne = pi.NomAffiche;
            var n = SelectedCount;
            Message = pi.NomAffiche + " : " + n + " option" + (n > 1 ? "s" : "") + " cochée" + (n > 1 ? "s" : "") + " (" + pi.Description.TrimEnd('.')
                    + "). Vérifiez avec « Aperçu », puis « LANCER ».";
        }
        /// <summary>Décoche les options affichées (le menu courant, ou les résultats de la recherche).</summary>
        public ICommand DecocherCommand { get; private set; }

        public void Decocher()
        {
            foreach (var o in Visibles.ToList()) if (o.IsChecked) o.IsChecked = false;
        }

        private bool masquerAppliquees;
        /// <summary>Masque les options que la détection donne « Déjà appliqué » (une option cochée reste toujours visible).</summary>
        public bool MasquerAppliquees { get { return masquerAppliquees; } set { if (Set(ref masquerAppliquees, value)) Filtrer(); } }

        private void MajCompteurs()
        {
            foreach (var m in Menus.Where(m => !m.EstSeparateur && !m.EstEmplacement))
                m.Coches = Options.Count(o => o.IsChecked && o.Menu == m.Nom);
        }

        /// <summary>Appelée par la vue pour confirmer l'absence de point de restauration (peut venir d'un autre thread).</summary>
        public Func<string, bool> ConfirmSansPointDeRestauration { get; set; }
        /// <summary>Appelée avant d'enregistrer un profil contenant une option Risqué.</summary>
        public Func<IReadOnlyList<OptionViewModel>, bool> ConfirmerProfilRisque { get; set; }

        private Func<OptionViewModel, string, string, bool> confirmerElementRisque;
        /// <summary>Confirmation d'un élément Risqué d'une liste (option, titre, détail) ; à défaut, celle de l'option est réutilisée.</summary>
        public Func<OptionViewModel, string, string, bool> ConfirmerElementRisque
        {
            get { return confirmerElementRisque; }
            set { confirmerElementRisque = value; foreach (var o in Options) o.ConfirmerElement = value; }
        }

        public int SelectedCount { get { return Options.Count(o => o.IsChecked); } }
        /// <summary>Vrai si au moins une option sera réellement exécutée (une option à liste sans élément choisi ne compte pas).</summary>
        public bool AUneSelection { get { return BuildSelection().OptionIds.Count > 0; } }
        public string ResumeSelection { get { var n = SelectedCount; return n + (n > 1 ? " cochés" : " coché"); } }

        public string MenuCourant
        {
            get { return menuCourant; }
            set
            {
                var precedent = menuCourant;
                if (!Set(ref menuCourant, value)) return;
                // En quittant les Paramètres : fin du placement du compteur, réglages enregistrés.
                if (precedent == MenuParametres && compteurVm != null) { compteurVm.ModePlacement = false; compteurVm.Enregistrer(); }
                if (value == MenuDiagnostic && diagnosticVm != null) diagnosticVm.AnalyserSiBesoin();
                MajVueAffichee();
                if (value == MenuModeJeu && modeJeuVm != null) modeJeuVm.ChargerSiBesoin();
                if (value == MenuHistorique && historiqueVm != null) historiqueVm.Charger();
            }
        }

        public string Recherche
        {
            get { return recherche; }
            set
            {
                if (!Set(ref recherche, value ?? "")) return;
                Raise("EnRecherche");
                CommandManager.InvalidateRequerySuggested();
                MajVueAffichee();
            }
        }

        /// <summary>Le menu courant ou la recherche a changé : signale toutes les propriétés « quelle vue est affichée », puis refiltre.</summary>
        private void MajVueAffichee()
        {
            foreach (var p in new[] { "AvancementMenu", "ProportionMenu", "EstAccueil", "EstDiagnostic", "EstEmplacement", "EstMenuOptions", "EstModeJeu", "EstHistorique", "EstMenuInformations", "EstParametres", "TitreAffiche" })
                Raise(p);
            MajInfo();
            Filtrer();
        }

        public bool EnRecherche { get { return Normaliser(recherche).Length > 0; } }
        public bool EstEmplacement { get { return !EnRecherche && Menus.Any(m => m.Nom == menuCourant && m.EstEmplacement); } }
        public const string MenuInformations = "Informations";
        /// <summary>Menu Informations (fiche matériel et capteurs) : null tant que l'application ne l'a pas branché.</summary>
        private InfoViewModel info;
        public InfoViewModel Info { get { return info; } set { if (Set(ref info, value)) MajInfo(); } }
        public bool EstMenuInformations { get { return !EnRecherche && menuCourant == MenuInformations; } }
        private void MajInfo() { if (Info != null) Info.SetVisible(EstMenuInformations); }
        public const string MenuTelemetrie = "Télémétrie";
        /// <summary>Vrai sur un menu d'options (bandeau « Tout restaurer », qui porte sur tous les menus).</summary>
        public bool EstMenuOptions { get { return !EnRecherche && Array.IndexOf(MenusOptions, menuCourant) >= 0; } }
        public const string MenuModeJeu = "Mode Jeu";
        public const string MenuHistorique = "Historique";
        public const string MenuParametres = "Paramètres";
        /// <summary>Menu Paramètres : réglages de l'application et du compteur en jeu.</summary>
        public bool EstParametres { get { return !EnRecherche && menuCourant == MenuParametres; } }
        public bool EstModeJeu { get { return !EnRecherche && menuCourant == MenuModeJeu; } }
        public bool EstHistorique { get { return !EnRecherche && menuCourant == MenuHistorique; } }

        private ModeJeuViewModel modeJeuVm;
        /// <summary>Vue-modèle du Mode Jeu (null tant qu'elle n'est pas configurée par l'application).</summary>
        public ModeJeuViewModel ModeJeu { get { return modeJeuVm; } set { Set(ref modeJeuVm, value); } }

        private RoutineViewModel routineVm;
        /// <summary>Planification de la routine (null tant qu'elle n'est pas configurée par l'application).</summary>
        public RoutineViewModel Routine { get { return routineVm; } set { Set(ref routineVm, value); } }

        private HistoriqueViewModel historiqueVm;
        /// <summary>Vue-modèle de l'historique (null tant qu'elle n'est pas configurée par l'application).</summary>
        public HistoriqueViewModel Historique { get { return historiqueVm; } set { Set(ref historiqueVm, value); } }

        public string TitreAffiche { get { return EnRecherche ? "Résultats de la recherche" : menuCourant == MenuAccueil ? "Bienvenue" : menuCourant; } }

        public string Message { get { return message; } set { Set(ref message, value ?? ""); } }

        // ---------- Guide du premier lancement, version ----------

        public const string FichierGuideVu = "guide-vu";
        private bool guideMasque;
        /// <summary>Le guide de l'accueil s'affiche tant qu'il n'a pas été masqué sur ce PC (fichier témoin dans le dossier de la machine).</summary>
        public bool AfficherGuide
        {
            get
            {
                if (guideMasque) return false;
                try { return paths == null || string.IsNullOrEmpty(paths.MachineDir) || !File.Exists(Path.Combine(paths.MachineDir, FichierGuideVu)); }
                catch (Exception) { return true; }
            }
        }
        private ICommand masquerGuide;
        public ICommand MasquerGuideCommand { get { return masquerGuide ?? (masquerGuide = new RelayCommand(_ => MasquerGuide())); } }
        public void MasquerGuide()
        {
            guideMasque = true;
            try { if (paths != null && !string.IsNullOrEmpty(paths.MachineDir)) { Directory.CreateDirectory(paths.MachineDir); File.WriteAllText(Path.Combine(paths.MachineDir, FichierGuideVu), DateTime.Now.ToString("o")); } }
            catch (Exception ex) { TraceAppli.Erreur("guide de l'accueil : témoin non écrit", ex); }
            Raise("AfficherGuide");
        }

        private ICommand reafficherGuide;
        /// <summary>Paramètres : le guide de l'accueil s'affiche de nouveau (fichier témoin supprimé).</summary>
        public ICommand ReafficherGuideCommand { get { return reafficherGuide ?? (reafficherGuide = new RelayCommand(_ => ReafficherGuide())); } }
        public void ReafficherGuide()
        {
            guideMasque = false;
            try { if (paths != null && !string.IsNullOrEmpty(paths.MachineDir)) File.Delete(Path.Combine(paths.MachineDir, FichierGuideVu)); }
            catch (Exception ex) { TraceAppli.Erreur("guide de l'accueil : témoin non supprimé", ex); }
            Raise("AfficherGuide");
            Message = "Le guide de démarrage s'affiche de nouveau sur l'accueil.";
        }

        public string TexteVersion { get { return "GamingTweaker " + VersionAppli.Texte; } }

        private CompteurViewModel compteurVm;
        /// <summary>Compteur en jeu (null tant que l'application ne l'a pas branché).</summary>
        public CompteurViewModel Compteur { get { return compteurVm; } set { Set(ref compteurVm, value); } }

        // ---------- Nouvelle version ----------

        private VersionDisponible nouvelleVersion;
        private bool nouvelleVersionMasquee;
        /// <summary>Release plus récente trouvée sur GitHub au démarrage (null : à jour, hors ligne ou vérification impossible).</summary>
        public VersionDisponible NouvelleVersion
        {
            get { return nouvelleVersion; }
            set { if (Set(ref nouvelleVersion, value)) { Raise("NouvelleVersionVisible"); Raise("TexteNouvelleVersion"); Raise("PeutMettreAJour"); Raise("TexteBoutonMiseAJour"); } }
        }
        /// <summary>Bandeau fermé par l'utilisateur (jusqu'au prochain lancement).</summary>
        public bool NouvelleVersionMasquee
        {
            get { return nouvelleVersionMasquee; }
            set { if (Set(ref nouvelleVersionMasquee, value)) Raise("NouvelleVersionVisible"); }
        }
        public bool NouvelleVersionVisible { get { return nouvelleVersion != null && !nouvelleVersionMasquee; } }

        private bool miseAJourEnCours;
        /// <summary>Téléchargement et vérification de la nouvelle version en cours (bouton « Mettre à jour » désactivé).</summary>
        public bool MiseAJourEnCours
        {
            get { return miseAJourEnCours; }
            set { if (Set(ref miseAJourEnCours, value)) { Raise("TexteNouvelleVersion"); Raise("PeutMettreAJour"); } }
        }
        public bool PeutMettreAJour { get { return nouvelleVersion != null && !miseAJourEnCours; } }
        public string TexteBoutonMiseAJour { get { return nouvelleVersion != null && nouvelleVersion.PeutInstaller ? "Mettre à jour" : "Télécharger"; } }

        /// <summary>Ferme vraiment l'application (fenêtre, icône de la zone de notification) ; branché par l'application.</summary>
        public Action Quitter { get; set; }
        public string TexteNouvelleVersion
        {
            get
            {
                if (nouvelleVersion == null) return "";
                if (miseAJourEnCours) return "Téléchargement de GamingTweaker " + nouvelleVersion.Version + " et vérification de son empreinte…";
                return "GamingTweaker " + nouvelleVersion.Version + " est disponible (vous avez la version " + MiseAJour.VersionCourante + ").";
            }
        }
        /// <summary>Bandeau d'avertissement (compte d'élévation différent du bureau).</summary>
        public string Bandeau { get { return bannière; } set { Set(ref bannière, value ?? ""); } }


        // ---------- Filtre ----------

        public const string EtatDejaApplique = "Déjà appliqué";

        public static string Normaliser(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().ToLowerInvariant().Trim();
        }

        private void Filtrer()
        {
            var q = Normaliser(recherche);
            IEnumerable<OptionViewModel> src;
            if (q.Length > 0) src = Options.Where(o => o.SearchText.Contains(q));
            else if (EstEmplacement) src = Enumerable.Empty<OptionViewModel>();
            else src = Options.Where(o => o.Menu == menuCourant);
            if (masquerAppliquees) src = src.Where(o => o.IsChecked || o.Etat != EtatDejaApplique);
            var liste = src.ToList();
            Visibles.Clear();
            foreach (var o in liste) Visibles.Add(o);
        }

        // ---------- Sélection ----------

        /// <summary>Coche les options Sûr du menu que le catalogue n'exclut pas de la recommandation (« recommande »: false).</summary>
        public void ApplyRecommended(string menu)
        {
            foreach (var o in Options.Where(o => o.Menu == menu && o.Recommandee)) o.IsChecked = true;
        }

        public Selection BuildSelection()
        {
            var s = new Selection();
            foreach (var o in Options.Where(o => o.IsChecked))
            {
                if (o.Liste != null)
                {
                    // Option à liste : sans élément choisi, elle n'est pas exécutée (signalé dans l'aperçu).
                    var sel = o.Liste.VersSelection();
                    if (sel.Count == 0) continue;
                    s.OptionIds.Add(o.Id);
                    s.Listes[o.Id] = sel;
                    continue;
                }
                s.OptionIds.Add(o.Id);
                List<string> l;
                if (listesChargees.TryGetValue(o.Id, out l)) s.Listes[o.Id] = new List<string>(l);
            }
            return s;
        }

        // ---------- Détection ----------

        /// <summary>État d'une option dès qu'il est connu (détection progressive) ; à appeler sur le thread de l'interface.</summary>
        public void AppliquerEtat(string id, DetectState s)
        {
            var o = Options.FirstOrDefault(x => x.Id == id);
            if (o != null) { o.SetEtat(s); MajAccueil(); }
        }

        public async Task DetectAsync()
        {
            foreach (var o in Options) o.SetEtat(DetectState.Detecting);
            Raise("AvancementMenu");
            Dictionary<string, DetectState> res = null;
            try { res = await detect(Options.Select(o => o.Item).ToList()); }
            catch (Exception ex) { TraceAppli.Erreur("détection impossible, tout reste Inconnu", ex); }
            foreach (var o in Options)
            {
                DetectState s;
                o.SetEtat(res != null && res.TryGetValue(o.Id, out s) ? s : DetectState.Unknown);
            }
            if (masquerAppliquees) Filtrer();
            MajAccueil();
        }
    }
}
