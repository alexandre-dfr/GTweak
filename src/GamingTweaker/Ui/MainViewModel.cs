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
    public class MenuEntry : ObservableObject
    {
        private int coches;
        public string Nom { get; set; }
        /// <summary>Options cochées dans ce menu (affiché à côté du nom, 0 : rien).</summary>
        public int Coches { get { return coches; } set { if (Set(ref coches, value)) Raise("Libelle"); } }
        public string Libelle { get { return coches > 0 ? Nom + "  ·  " + coches : Nom; } }
        public bool EstSeparateur { get; set; }
        /// <summary>Vue réservée aux tâches suivantes : panneau neutre avec le seul titre du menu.</summary>
        public bool EstEmplacement { get; set; }
    }

    /// <summary>Ligne « état de ce PC » de l'écran d'accueil : options déjà appliquées sur le total du menu.</summary>
    public class LigneAccueil
    {
        public string Menu { get; set; }
        public int Appliquees { get; set; }
        public int Total { get; set; }
        public string Texte { get { return Appliquees + " / " + Total + " déjà appliquée" + (Appliquees > 1 ? "s" : ""); } }
        public double Proportion { get { return Total == 0 ? 0 : (double)Appliquees / Total; } }
    }

    /// <summary>Tuile de profil intégré de l'écran d'accueil.</summary>
    public class TuileProfil
    {
        public string Nom { get; set; }
        public string Accroche { get; set; }
        public string Description { get; set; }
        public int Nombre { get; set; }
        public string NombreTexte { get { return Nombre + " option" + (Nombre > 1 ? "s" : ""); } }
        /// <summary>0 : Gaming léger, 1 : Gaming max, 2 : Confidentialité (couleur de la tuile).</summary>
        public int Rang { get; set; }
    }

    public class PreviewRow : ObservableObject
    {
        private string taille = "—";
        public string Id { get; set; }
        public string Titre { get; set; }
        public string Risque { get; set; }
        public string Reversibilite { get; set; }
        public string Redemarrage { get; set; }
        /// <summary>Estimation de taille ; « — » tant qu'elle n'est pas connue ou si l'option n'en a pas.</summary>
        public string Taille { get { return taille; } set { Set(ref taille, value); } }
    }

    /// <summary>Bilan de « Tout restaurer ».</summary>
    public class ResultatRestauration
    {
        public int AnnuleesParJournal { get; set; }
        public int AnnuleesParRevert { get; set; }
        /// <summary>Une ligne « id : message » par option dont l'annulation a échoué.</summary>
        public List<string> Echecs { get; } = new List<string>();
        /// <summary>Options appliquées mais impossibles à restaurer (ni journal, ni script revert).</summary>
        public List<string> Ignorees { get; } = new List<string>();
        /// <summary>Annulations réussies avec une réserve (par exemple une réinstallation manuelle).</summary>
        public List<string> Remarques { get; } = new List<string>();
        /// <summary>Options HKCU ignorées parce que l'application tourne sous un autre compte que le bureau.</summary>
        public List<string> IgnoreesAutreCompte { get; } = new List<string>();
    }

    public class MainViewModel : ObservableObject
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

            var menus = new[] { MenuAccueil, MenuDiagnostic }.Concat(MenusOptions).Concat(new[] { "Informations" })
                .Select(n => new MenuEntry { Nom = n, EstEmplacement = n == "Informations" || n == MenuAccueil || n == MenuDiagnostic }).ToList();
            menus.Add(new MenuEntry { EstSeparateur = true });
            menus.Add(new MenuEntry { Nom = "Mode Jeu", EstEmplacement = true });
            menus.Add(new MenuEntry { Nom = "Historique", EstEmplacement = true });
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
            Raise("TotalAppliquees"); Raise("TotalOptions"); Raise("SyntheseAccueil");
        }

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
            set { if (Set(ref menuCourant, value)) { Raise("EstDiagnostic"); if (value == MenuDiagnostic && diagnosticVm != null) diagnosticVm.AnalyserSiBesoin(); Raise("EstAccueil"); Raise("EstEmplacement"); Raise("EstMenuTelemetrie"); Raise("EstMenuOptions"); Raise("EstModeJeu"); Raise("EstHistorique"); Raise("TitreAffiche"); Raise("EstMenuInformations"); MajInfo(); Filtrer(); if (value == MenuModeJeu && modeJeuVm != null) modeJeuVm.ChargerSiBesoin(); if (value == MenuHistorique && historiqueVm != null) historiqueVm.Charger(); } }
        }

        public string Recherche
        {
            get { return recherche; }
            set { if (Set(ref recherche, value ?? "")) { Raise("EnRecherche"); Raise("EstAccueil"); Raise("EstDiagnostic"); CommandManager.InvalidateRequerySuggested(); Raise("TitreAffiche"); Raise("EstMenuTelemetrie"); Raise("EstMenuOptions"); Raise("EstModeJeu"); Raise("EstHistorique"); Raise("EstMenuInformations"); MajInfo(); Filtrer(); } }
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
        public const string UrlShutUp10 = "https://www.oo-software.com/fr/shutup10";
        /// <summary>Vrai sur le menu Télémétrie (lien O&amp;O ShutUp10++).</summary>
        public bool EstMenuTelemetrie { get { return !EnRecherche && menuCourant == MenuTelemetrie; } }
        /// <summary>Vrai sur un menu d'options (bandeau « Tout restaurer », qui porte sur tous les menus).</summary>
        public bool EstMenuOptions { get { return !EnRecherche && Array.IndexOf(MenusOptions, menuCourant) >= 0; } }
        public const string MenuModeJeu = "Mode Jeu";
        public const string MenuHistorique = "Historique";
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
        /// <summary>Bandeau d'avertissement (compte d'élévation différent du bureau).</summary>
        public string Bandeau { get { return bannière; } set { Set(ref bannière, value ?? ""); } }

        public string ProfilSelectionne
        {
            get { return profilSelectionne; }
            set
            {
                if (!Set(ref profilSelectionne, value)) return;
                Raise("ProfilIntegreSelectionne");
                if (!chargementProfil && !string.IsNullOrEmpty(value)) ChargerProfilNomme(value);
            }
        }

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

        public Profil ToProfil(string nom)
        {
            var p = new Profil { Nom = nom, Options = Options.Where(o => o.IsChecked).Select(o => o.Id).ToList() };
            p.Listes = profilListes == null ? new ProfilListes() : profilListes.Cloner();
            avertissementProfil = "";
            var perdues = new List<string>();
            // Le profil reflète exactement ce qui est coché : la clé de chaque option à liste est retirée puis réécrite si elle est cochée et non vide.
            foreach (var o in Options.Where(o => o.Liste != null)) Profils.RetirerListe(p.Listes, o.Item.Liste);
            foreach (var o in Options.Where(o => o.IsChecked && o.Liste != null))
            {
                var ecartes = new List<string>();
                var sel = o.Liste.VersSelection();
                if (sel.Count == 0) continue;
                if (!Profils.AjouterListe(p.Listes, o.Item.Liste, sel, ecartes)) perdues.Add(o.Titre);
                else if (ecartes.Count > 0) perdues.Add(o.Titre + " (" + string.Join(", ", ecartes) + ")");
            }
            if (perdues.Count > 0)
                avertissementProfil = "Éléments non enregistrés dans le profil (nom non pris en charge) : " + string.Join(" ; ", perdues) + ".";
            return p;
        }

        public void LoadProfil(Profil p)
        {
            foreach (var o in Options) o.ForceUnchecked();
            foreach (var o in Options.Where(o => o.Liste != null)) o.Liste.DepuisSelection(null);
            listesChargees.Clear();
            profilListes = p.Listes;
            var res = Profils.Resolve(p, items, facts);
            foreach (var kv in res.Selection.Listes) listesChargees[kv.Key] = kv.Value;
            var refuses = new List<string>();
            foreach (var o in Options.Where(o => res.Selection.OptionIds.Contains(o.Id)))
            {
                List<string> sel;
                if (o.Liste != null && res.Selection.Listes.TryGetValue(o.Id, out sel)) o.Liste.DepuisSelection(sel);
                o.IsChecked = true;
                if (!o.IsChecked) { refuses.Add(o.Titre); continue; }
                // Éléments Risqué du profil : même confirmation que pour une option Risqué ; les refusés sont retirés de la sélection.
                if (o.Liste != null) refuses.AddRange(o.Liste.ConfirmerSelectionProfil().Select(x => o.Titre + " : " + x));
            }
            var infos = new List<string>();
            if (res.Inconnues.Count > 0) infos.Add(res.Inconnues.Count + " option(s) inconnue(s) ignorée(s).");
            if (res.Incompatibles.Count > 0) infos.Add(res.Incompatibles.Count + " option(s) incompatible(s) avec ce PC ignorée(s).");
            if (refuses.Count > 0) infos.Add("Non cochée(s) faute de confirmation : " + string.Join(", ", refuses) + ".");
            Message = string.Join(" ", infos);
            MajCompteurs();
        }

        // ---------- Profils ----------

        public void RafraichirProfils()
        {
            var noms = new List<string>();
            try
            {
                if (paths != null && Directory.Exists(paths.ProfilsDir))
                    noms = Directory.GetFiles(paths.ProfilsDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (Exception) { /* dossier illisible : liste vide */ }
            ProfilsDisponibles.Clear();
            // Profils intégrés d'abord ; un fichier qui porterait leur nom affiché n'est pas listé (il serait masqué).
            foreach (var pi in ProfilsIntegres.Tous) ProfilsDisponibles.Add(pi.NomAffiche);
            foreach (var n in noms.Where(n => !ProfilsIntegres.EstReserve(n))) ProfilsDisponibles.Add(n);
        }

        /// <summary>Recharge le profil actuellement sélectionné (annule les modifications de cases depuis son chargement).</summary>
        public void RechargerProfil()
        {
            if (!string.IsNullOrEmpty(profilSelectionne)) ChargerProfilNomme(profilSelectionne);
        }

        public string CheminProfil(string nom) { return Path.Combine(paths.ProfilsDir, nom + ".json"); }

        /// <summary>Vrai si le profil sélectionné est un profil intégré (lecture seule).</summary>
        public bool ProfilIntegreSelectionne { get { return ProfilsIntegres.Trouver(profilSelectionne) != null; } }

        /// <summary>Nom proposé par « Sauver » : celui du profil sélectionné, sans le suffixe « (intégré) ».</summary>
        public string NomPourEnregistrer
        {
            get
            {
                var pi = ProfilsIntegres.Trouver(profilSelectionne);
                return pi != null ? pi.Nom : profilSelectionne ?? "";
            }
        }

        private void ChargerProfilNomme(string nom)
        {
            var integre = ProfilsIntegres.Trouver(nom);
            if (integre != null)
            {
                LoadProfil(ProfilsIntegres.Construire(integre, items, facts));
                Message = (integre.NomAffiche + " : " + integre.Description + " " + Message).Trim();
                return;
            }
            var load = Profils.Load(CheminProfil(nom), items.Where(c => !string.IsNullOrEmpty(c.Liste)).Select(c => c.Liste).Distinct());
            if (load.Erreur != null) { Message = load.Erreur; return; }
            LoadProfil(load.Profil);
            if (load.Avertissements.Count > 0) Message = (Message + " " + string.Join(" ", load.Avertissements)).Trim();
        }

        /// <summary>Enregistre la sélection sous ce nom. Renvoie un message d'erreur, ou null si tout va bien (ou si l'utilisateur annule).</summary>
        public string SauverProfil(string nom)
        {
            nom = (nom ?? "").Trim();
            var invalides = Path.GetInvalidFileNameChars();
            nom = new string(nom.Where(c => !invalides.Contains(c)).ToArray()).Trim().TrimEnd('.');
            if (nom.Length == 0) return "Donnez un nom au profil (sans caractères spéciaux).";
            if (ProfilsIntegres.EstReserve(nom)) return ProfilsIntegres.MessageNomReserve;
            var risques = Options.Where(o => o.IsChecked && (o.NiveauRisque == RiskLevel.Risque || (o.Liste != null && o.Liste.ElementsRisques().Count > 0))).ToList();
            if (risques.Count > 0 && (ConfirmerProfilRisque == null || !ConfirmerProfilRisque(risques))) return null;
            try { Profils.Save(ToProfil(nom), CheminProfil(nom)); }
            catch (Exception ex) { return "Le profil n'a pas pu être enregistré : " + ex.Message; }
            RafraichirProfils();
            chargementProfil = true;
            try { ProfilSelectionne = nom; } finally { chargementProfil = false; }
            Message = ("Profil « " + nom + " » enregistré. " + avertissementProfil).Trim();
            return null;
        }

        // ---------- Détection ----------

        public async Task DetectAsync()
        {
            foreach (var o in Options) o.SetEtat(DetectState.Detecting);
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

        // ---------- Tout restaurer (tous les menus) ----------

        /// <summary>Service d'annulation utilisé par « Tout restaurer » (journal, sinon script revert).</summary>
        public IRestauration Restauration { get; set; }

        private bool restaurationEnCours;
        public bool RestaurationEnCours { get { return restaurationEnCours; } private set { if (Set(ref restaurationEnCours, value)) Raise("PeutToutRestaurer"); } }
        public bool PeutToutRestaurer { get { return !restaurationEnCours; } }

        /// <summary>
        /// Annule les options réversibles de tous les menus : toute option qui a des entrées actives dans un journal est annulée par TOUS ses journaux
        /// (du plus récent au plus ancien), quel que soit son état de détection ; une option détectée « appliquée » sans journal passe par son
        /// script revert (valeurs par défaut de Windows). Une annulation par journal en échec n'est jamais remplacée par le script revert.
        /// Même verrou d'instance et même règle de compte que l'exécution : les options HKCU sont ignorées sous un autre compte.
        /// Les valeurs modifiées depuis par l'utilisateur ne sont pas comparées. Redétecte ensuite.
        /// </summary>
        public async Task<ResultatRestauration> ToutRestaurerAsync()
        {
            var res = new ResultatRestauration();
            var svc = Restauration;
            if (svc == null) { Message = "Tout restaurer est indisponible : aucun service d'annulation n'est configuré."; return res; }
            if (RestaurationEnCours) return res;
            RestaurationEnCours = true;
            try
            {
                using (var verrou = svc.PrendreVerrou())
                {
                    if (verrou == null) { Message = "Une autre exécution de GamingTweaker est en cours : rien n'a été restauré."; return res; }

                    var tel = Options.Where(o => Array.IndexOf(MenusOptions, o.Menu) >= 0).Select(o => o.Item).ToList();
                    bool memeUtilisateur = svc.MemeUtilisateur();
                    Dictionary<string, DetectState> etats = null;
                    string erreurDetection = null;
                    try { etats = await detect(tel); }
                    catch (Exception ex) { erreurDetection = ex.Message; }
                    Func<CatalogItem, bool> appliquee = i => { DetectState s; return etats != null && etats.TryGetValue(i.Id, out s) && s == DetectState.Applied; };

                    var restaures = new List<string>();
                    int candidats = 0;
                    foreach (var it in tel)
                    {
                        try
                        {
                            var journaux = svc.TrouverJournauxActifs(it.Id);
                            if (journaux.Count == 0 && !appliquee(it)) continue;
                            candidats++;
                            if (!memeUtilisateur && it.Hkcu) { res.IgnoreesAutreCompte.Add(it.Id); continue; }

                            ItemOutcome o = null;
                            bool parJournal = journaux.Count > 0;
                            if (parJournal)
                            {
                                foreach (var j in journaux)
                                {
                                    o = await svc.AnnulerParJournalAsync(j, it.Id);
                                    if (o == null || o.Status == OutcomeStatus.Failed) break;
                                    if (o.Status == OutcomeStatus.Skipped && !string.IsNullOrWhiteSpace(o.Message)) res.Remarques.Add(it.Id + " : " + o.Message.Trim());
                                }
                            }
                            else if (it.Reversible && !string.IsNullOrWhiteSpace(it.Revert)) o = await svc.ExecuterRevertAsync(it);
                            else { res.Ignorees.Add(it.Id); continue; }

                            if (o == null || o.Status == OutcomeStatus.Failed)
                                res.Echecs.Add(it.Id + " : " + (o == null || string.IsNullOrWhiteSpace(o.Message) ? "échec de l'annulation" : o.Message.Trim()));
                            else if (!parJournal && o.Status == OutcomeStatus.Skipped)
                            {
                                // Le script revert n'a rien restauré (ex. état d'origine connu du seul journal) : ni compté ni oublié.
                                res.Ignorees.Add(it.Id);
                                if (!string.IsNullOrWhiteSpace(o.Message)) res.Remarques.Add(it.Id + " : " + o.Message.Trim());
                            }
                            else
                            {
                                if (parJournal) res.AnnuleesParJournal++; else res.AnnuleesParRevert++;
                                restaures.Add(it.Id);
                            }
                        }
                        catch (Exception ex) { res.Echecs.Add(it.Id + " : " + ex.Message); }
                    }

                    if (restaures.Count > 0) svc.OublierDernieresApplications(restaures);

                    if (candidats == 0)
                    {
                        Message = erreurDetection != null ? "Détection impossible, rien n'a été restauré : " + erreurDetection
                                                          : "Rien à restaurer : aucune option appliquée par GamingTweaker.";
                        return res;
                    }

                    var sb = new StringBuilder("Tout restaurer : ");
                    sb.Append(res.AnnuleesParJournal).Append(" annulée(s) par le journal, ").Append(res.AnnuleesParRevert).Append(" remise(s) aux valeurs par défaut de Windows");
                    if (res.Echecs.Count > 0) sb.Append(", ").Append(res.Echecs.Count).Append(" échec(s) (").Append(string.Join(" ; ", res.Echecs)).Append(')');
                    if (res.Ignorees.Count > 0) sb.Append(", ").Append(res.Ignorees.Count).Append(" sans moyen de restauration (").Append(string.Join(", ", res.Ignorees)).Append(')');
                    if (res.IgnoreesAutreCompte.Count > 0) sb.Append(", ").Append(res.IgnoreesAutreCompte.Count).Append(" ignorée(s) car l'application tourne sous un autre compte que le bureau (").Append(string.Join(", ", res.IgnoreesAutreCompte)).Append(')');
                    sb.Append('.');
                    if (res.Remarques.Count > 0) sb.Append(' ').Append(string.Join(" ", res.Remarques));
                    Message = sb.ToString();
                }
                await DetectAsync();
                return res;
            }
            finally { RestaurationEnCours = false; }
        }

        // ---------- Aperçu et exécution ----------

        public IReadOnlyList<PreviewRow> BuildPreview()
        {
            return Options.Where(o => o.IsChecked).Select(o => new PreviewRow
            {
                Id = o.Id,
                Titre = o.Titre + (o.Liste == null ? "" : o.Liste.EstVide ? " : aucun élément choisi, non exécutée" : " (" + o.Liste.ResumeCourt + ")"), Risque = o.Risque,
                Reversibilite = o.Reversible ? (string.IsNullOrWhiteSpace(o.Item.AnnulationPartielle) ? "Annulable" : "Annulable en partie : " + o.Item.AnnulationPartielle) : "définitif",
                Redemarrage = o.Redemarrage ? "Redémarrage requis" : "",
                Taille = "—",
            }).ToList();
        }

        /// <summary>
        /// Lance les estimations de taille des options de l'aperçu (scripts en lecture seule) et renseigne la colonne Taille.
        /// Renvoie le texte du total (« Total estimé : 1,2 Go »), ou une chaîne vide si aucune estimation n'est disponible.
        /// </summary>
        public async Task<string> EstimerAsync(IReadOnlyList<PreviewRow> lignes)
        {
            var parId = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            var concernes = lignes.Where(l => l.Id != null && parId.ContainsKey(l.Id) && !string.IsNullOrWhiteSpace(parId[l.Id].Estimate))
                                  .Select(l => parId[l.Id]).ToList();
            if (concernes.Count == 0) return "";
            Dictionary<string, long?> res;
            try { res = await estimate(concernes); }
            catch (Exception) { return ""; }
            if (res == null) return "";
            long total = 0; int connues = 0;
            foreach (var l in lignes)
            {
                long? v;
                if (l.Id == null || !res.TryGetValue(l.Id, out v) || v == null) continue;
                l.Taille = Estimation.FormatTaille(v.Value);
                total += v.Value; connues++;
            }
            if (connues == 0) return "";
            return "Total estimé : " + Estimation.FormatTaille(total) + (connues < lignes.Count ? " (options sans estimation exclues)" : "");
        }

        /// <summary>Le script exact qui sera exécuté (le dossier de session réel n'existe qu'au lancement).</summary>
        public string BuildPreviewScript()
        {
            return ScriptBuilder.Build(items, BuildSelection(), @"<dossier de la session>\journal.jsonl", paths == null ? null : paths.QuarantineDir);
        }

        public const string NoteApercu = "Le dossier de session est créé au lancement ; les options déjà appliquées ou ignorées ne seront pas exécutées.";

        /// <summary>Script à exporter : le script de l'aperçu précédé d'un en-tête explicatif.</summary>
        public string BuildExportScript()
        {
            var script = BuildPreviewScript();
            return "# GamingTweaker : script exporté depuis l'aperçu.\r\n# " + NoteApercu + "\r\n\r\n" + script;
        }

        public Task<SessionReport> LancerAsync(IProgress<string> log, CancellationToken ct)
        {
            var req = new RunRequest
            {
                Selection = BuildSelection(), CatalogItems = items, Facts = facts, Paths = paths,
                ConfirmSansPointDeRestauration = ConfirmSansPointDeRestauration,
            };
            if (run == null) throw new InvalidOperationException("Aucun exécuteur configuré.");
            // Le pipeline prend le verrou et crée le point de restauration avant son premier await : hors du thread de l'interface.
            return Task.Run(() => run(req, log, ct));
        }

        /// <summary>Ligne lisible pour le journal en direct ; null pour les lignes techniques à masquer.</summary>
        public string FormaterLigne(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            if (!line.StartsWith("##GT|", StringComparison.Ordinal)) return line;
            var p = line.Split(new[] { '|' }, 4);
            if (p.Length < 3) return null;
            var o = Options.FirstOrDefault(x => x.Id == p[2]);
            var t = o == null ? p[2] : o.Titre;
            var msg = p.Length > 3 && !string.IsNullOrWhiteSpace(p[3]) ? " (" + p[3].Trim() + ")" : "";
            switch (p[1])
            {
                case "start": return "En cours : " + t;
                case "ok": return "Appliqué : " + t + msg;
                case "fail": return "Échec : " + t + msg;
                case "skip": return "Ignoré : " + t + msg;
                default: return null;
            }
        }

        public static string RapportTexte(SessionReport r, IReadOnlyList<CatalogItem> catalogue)
        {
            var titres = catalogue.ToDictionary(c => c.Id, c => c.Titre);
            var sb = new StringBuilder();
            if (r.SessionDir == null)
            {
                sb.AppendLine("Rien n'a été exécuté.");
            }
            else
            {
                foreach (var it in r.Items)
                {
                    string t; if (!titres.TryGetValue(it.Id, out t)) t = it.Id;
                    var st = it.Status == OutcomeStatus.Ok ? "Appliqué" : it.Status == OutcomeStatus.AlreadyApplied ? "Déjà appliqué"
                           : it.Status == OutcomeStatus.Skipped ? "Ignoré" : "Échec";
                    sb.Append(st).Append(" : ").Append(t);
                    if (!string.IsNullOrWhiteSpace(it.Message)) sb.Append(" (").Append(it.Message.Trim()).Append(')');
                    sb.AppendLine();
                }
                if (r.Retablies.Count > 0) sb.AppendLine().AppendLine("Rétablies automatiquement : " + string.Join(", ", r.Retablies));
            }
            if (r.Avertissements.Count > 0)
            {
                sb.AppendLine().AppendLine("Avertissements :");
                foreach (var w in r.Avertissements) sb.AppendLine("- " + w);
            }
            if (r.RedemarrageRequis) sb.AppendLine().AppendLine("Un redémarrage est nécessaire pour terminer. Il n'est jamais lancé automatiquement.");
            return sb.ToString().TrimEnd();
        }
    }
}
