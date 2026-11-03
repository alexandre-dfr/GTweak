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

namespace GamingTweaker.Ui
{
    public class MenuEntry
    {
        public string Nom { get; set; }
        public bool EstSeparateur { get; set; }
        /// <summary>Vue réservée aux tâches suivantes : panneau neutre avec le seul titre du menu.</summary>
        public bool EstEmplacement { get; set; }
    }

    public class PreviewRow
    {
        public string Titre { get; set; }
        public string Risque { get; set; }
        public string Reversibilite { get; set; }
        public string Redemarrage { get; set; }
        /// <summary>Estimation de taille ; « — » tant qu'aucun script d'estimation n'existe.</summary>
        public string Taille { get; set; }
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
    }

    public class MainViewModel : ObservableObject
    {
        public static readonly string[] MenusOptions = { "Mises à jour", "Télémétrie", "Nettoyage", "Optimisation", "Bloatware" };

        private readonly IReadOnlyList<CatalogItem> items;
        private readonly MachineFacts facts;
        private readonly DataPaths paths;
        private readonly Func<IEnumerable<CatalogItem>, Task<Dictionary<string, DetectState>>> detect;
        private readonly Func<RunRequest, IProgress<string>, CancellationToken, Task<SessionReport>> run;
        private readonly Dictionary<string, List<string>> listesChargees = new Dictionary<string, List<string>>();
        private ProfilListes profilListes;
        private string menuCourant = MenusOptions[0];
        private string recherche = "";
        private string profilSelectionne;
        private string message = "";
        private string bannière = "";
        private bool chargementProfil;

        public MainViewModel(IReadOnlyList<CatalogItem> items, MachineFacts facts, DataPaths paths,
            Func<IEnumerable<CatalogItem>, Task<Dictionary<string, DetectState>>> detect,
            Func<RunRequest, IProgress<string>, CancellationToken, Task<SessionReport>> run,
            Func<OptionViewModel, bool> confirmerRisque)
        {
            this.items = items; this.facts = facts; this.paths = paths; this.detect = detect; this.run = run;

            Options = items.Where(i => Compatibilite.IsCompatible(i.Compatibilite ?? new CompatRule(), facts))
                           .Select(i => new OptionViewModel(i, confirmerRisque)).ToList();
            foreach (var o in Options) o.CocheChanged += () => { Raise("SelectedCount"); Raise("AUneSelection"); Raise("ResumeSelection"); };

            var menus = MenusOptions.Concat(new[] { "Informations" }).Select(n => new MenuEntry { Nom = n, EstEmplacement = n == "Informations" }).ToList();
            menus.Add(new MenuEntry { EstSeparateur = true });
            menus.Add(new MenuEntry { Nom = "Mode Jeu", EstEmplacement = true });
            menus.Add(new MenuEntry { Nom = "Historique", EstEmplacement = true });
            Menus = menus;

            RecommanderCommand = new RelayCommand(_ => ApplyRecommended(MenuCourant), _ => !EstEmplacement && !EnRecherche && Visibles.Any(o => o.NiveauRisque == RiskLevel.Sur));
            RafraichirProfils();
            Filtrer();
        }

        public IReadOnlyList<CatalogItem> Catalogue { get { return items; } }
        public IReadOnlyList<OptionViewModel> Options { get; private set; }
        public IReadOnlyList<MenuEntry> Menus { get; private set; }
        public ObservableCollection<OptionViewModel> Visibles { get; } = new ObservableCollection<OptionViewModel>();
        public ObservableCollection<string> ProfilsDisponibles { get; } = new ObservableCollection<string>();
        public ICommand RecommanderCommand { get; private set; }

        /// <summary>Appelée par la vue pour confirmer l'absence de point de restauration (peut venir d'un autre thread).</summary>
        public Func<string, bool> ConfirmSansPointDeRestauration { get; set; }
        /// <summary>Appelée avant d'enregistrer un profil contenant une option Risqué.</summary>
        public Func<IReadOnlyList<OptionViewModel>, bool> ConfirmerProfilRisque { get; set; }

        public int SelectedCount { get { return Options.Count(o => o.IsChecked); } }
        public bool AUneSelection { get { return SelectedCount > 0; } }
        public string ResumeSelection { get { var n = SelectedCount; return n + (n > 1 ? " cochés" : " coché"); } }

        public string MenuCourant
        {
            get { return menuCourant; }
            set { if (Set(ref menuCourant, value)) { Raise("EstEmplacement"); Raise("EstMenuTelemetrie"); Raise("TitreAffiche"); Filtrer(); } }
        }

        public string Recherche
        {
            get { return recherche; }
            set { if (Set(ref recherche, value ?? "")) { Raise("EnRecherche"); CommandManager.InvalidateRequerySuggested(); Raise("TitreAffiche"); Raise("EstMenuTelemetrie"); Filtrer(); } }
        }

        public bool EnRecherche { get { return Normaliser(recherche).Length > 0; } }
        public bool EstEmplacement { get { return !EnRecherche && Menus.Any(m => m.Nom == menuCourant && m.EstEmplacement); } }
        public const string MenuTelemetrie = "Télémétrie";
        public const string UrlShutUp10 = "https://www.oo-software.com/fr/shutup10";
        /// <summary>Vrai quand le bandeau du menu Télémétrie (Tout restaurer, lien O&amp;O ShutUp10++) doit s'afficher.</summary>
        public bool EstMenuTelemetrie { get { return !EnRecherche && menuCourant == MenuTelemetrie; } }
        public string TitreAffiche { get { return EnRecherche ? "Résultats de la recherche" : menuCourant; } }

        public string Message { get { return message; } set { Set(ref message, value ?? ""); } }
        /// <summary>Bandeau d'avertissement (compte d'élévation différent du bureau).</summary>
        public string Bandeau { get { return bannière; } set { Set(ref bannière, value ?? ""); } }

        public string ProfilSelectionne
        {
            get { return profilSelectionne; }
            set
            {
                if (!Set(ref profilSelectionne, value)) return;
                if (!chargementProfil && !string.IsNullOrEmpty(value)) ChargerProfilNomme(value);
            }
        }

        // ---------- Filtre ----------

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
            var liste = src.ToList();
            Visibles.Clear();
            foreach (var o in liste) Visibles.Add(o);
        }

        // ---------- Sélection ----------

        public void ApplyRecommended(string menu)
        {
            foreach (var o in Options.Where(o => o.Menu == menu && o.NiveauRisque == RiskLevel.Sur)) o.IsChecked = true;
        }

        public Selection BuildSelection()
        {
            var s = new Selection();
            foreach (var o in Options.Where(o => o.IsChecked))
            {
                s.OptionIds.Add(o.Id);
                List<string> l;
                if (listesChargees.TryGetValue(o.Id, out l)) s.Listes[o.Id] = new List<string>(l);
            }
            return s;
        }

        public Profil ToProfil(string nom)
        {
            var p = new Profil { Nom = nom, Options = Options.Where(o => o.IsChecked).Select(o => o.Id).ToList() };
            if (profilListes != null) p.Listes = profilListes;
            return p;
        }

        public void LoadProfil(Profil p)
        {
            foreach (var o in Options) o.ForceUnchecked();
            listesChargees.Clear();
            profilListes = p.Listes;
            var res = Profils.Resolve(p, items, facts);
            foreach (var kv in res.Selection.Listes) listesChargees[kv.Key] = kv.Value;
            var refuses = new List<string>();
            foreach (var o in Options.Where(o => res.Selection.OptionIds.Contains(o.Id)))
            {
                o.IsChecked = true;
                if (!o.IsChecked) refuses.Add(o.Titre);
            }
            var infos = new List<string>();
            if (res.Inconnues.Count > 0) infos.Add(res.Inconnues.Count + " option(s) inconnue(s) ignorée(s).");
            if (res.Incompatibles.Count > 0) infos.Add(res.Incompatibles.Count + " option(s) incompatible(s) avec ce PC ignorée(s).");
            if (refuses.Count > 0) infos.Add("Non cochée(s) faute de confirmation : " + string.Join(", ", refuses) + ".");
            Message = string.Join(" ", infos);
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
            foreach (var n in noms) ProfilsDisponibles.Add(n);
        }

        /// <summary>Recharge le profil actuellement sélectionné (annule les modifications de cases depuis son chargement).</summary>
        public void RechargerProfil()
        {
            if (!string.IsNullOrEmpty(profilSelectionne)) ChargerProfilNomme(profilSelectionne);
        }

        private string CheminProfil(string nom) { return Path.Combine(paths.ProfilsDir, nom + ".json"); }

        private void ChargerProfilNomme(string nom)
        {
            var load = Profils.Load(CheminProfil(nom));
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
            var risques = Options.Where(o => o.IsChecked && o.NiveauRisque == RiskLevel.Risque).ToList();
            if (risques.Count > 0 && (ConfirmerProfilRisque == null || !ConfirmerProfilRisque(risques))) return null;
            try { Profils.Save(ToProfil(nom), CheminProfil(nom)); }
            catch (Exception ex) { return "Le profil n'a pas pu être enregistré : " + ex.Message; }
            RafraichirProfils();
            chargementProfil = true;
            try { ProfilSelectionne = nom; } finally { chargementProfil = false; }
            Message = "Profil « " + nom + " » enregistré.";
            return null;
        }

        // ---------- Détection ----------

        public async Task DetectAsync()
        {
            foreach (var o in Options) o.SetEtat(DetectState.Detecting);
            Dictionary<string, DetectState> res = null;
            try { res = await detect(Options.Select(o => o.Item).ToList()); }
            catch (Exception) { /* détection impossible : tout reste Inconnu */ }
            foreach (var o in Options)
            {
                DetectState s;
                o.SetEtat(res != null && res.TryGetValue(o.Id, out s) ? s : DetectState.Unknown);
            }
        }

        // ---------- Tout restaurer (menu Télémétrie) ----------

        /// <summary>Service d'annulation utilisé par « Tout restaurer » (journal, sinon script revert).</summary>
        public IRestauration Restauration { get; set; }

        private bool restaurationEnCours;
        public bool RestaurationEnCours { get { return restaurationEnCours; } private set { if (Set(ref restaurationEnCours, value)) Raise("PeutToutRestaurer"); } }
        public bool PeutToutRestaurer { get { return !restaurationEnCours; } }

        /// <summary>
        /// Pour chaque option du menu Télémétrie détectée « appliquée » : annulation par le journal actif le plus récent qui la contient,
        /// sinon exécution de son script revert (valeurs par défaut de Windows). Les valeurs modifiées depuis par l'utilisateur ne sont pas comparées.
        /// Une annulation par journal en échec n'est jamais remplacée par le script revert. Redétecte ensuite.
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
                var tel = Options.Where(o => o.Menu == MenuTelemetrie).Select(o => o.Item).ToList();
                Dictionary<string, DetectState> etats;
                try { etats = await detect(tel); }
                catch (Exception ex) { Message = "Détection impossible, rien n'a été restauré : " + ex.Message; return res; }

                var appliquees = tel.Where(i => { DetectState s; return etats != null && etats.TryGetValue(i.Id, out s) && s == DetectState.Applied; }).ToList();
                if (appliquees.Count == 0) { Message = "Rien à restaurer : aucune option du menu Télémétrie n'est appliquée."; return res; }

                foreach (var it in appliquees)
                {
                    try
                    {
                        var journal = svc.TrouverJournalActif(it.Id);
                        ItemOutcome o;
                        bool parJournal = journal != null;
                        if (parJournal) o = await svc.AnnulerParJournalAsync(journal, it.Id);
                        else if (it.Reversible && !string.IsNullOrWhiteSpace(it.Revert)) o = await svc.ExecuterRevertAsync(it);
                        else { res.Ignorees.Add(it.Id); continue; }

                        if (o == null || o.Status == OutcomeStatus.Failed)
                            res.Echecs.Add(it.Id + " : " + (o == null || string.IsNullOrWhiteSpace(o.Message) ? "échec de l'annulation" : o.Message.Trim()));
                        else
                        {
                            if (parJournal) res.AnnuleesParJournal++; else res.AnnuleesParRevert++;
                            if (o.Status == OutcomeStatus.Skipped && !string.IsNullOrWhiteSpace(o.Message)) res.Remarques.Add(it.Id + " : " + o.Message.Trim());
                        }
                    }
                    catch (Exception ex) { res.Echecs.Add(it.Id + " : " + ex.Message); }
                }

                var sb = new StringBuilder("Tout restaurer : ");
                sb.Append(res.AnnuleesParJournal).Append(" annulée(s) par le journal, ").Append(res.AnnuleesParRevert).Append(" remise(s) aux valeurs par défaut de Windows");
                if (res.Echecs.Count > 0) sb.Append(", ").Append(res.Echecs.Count).Append(" échec(s) (").Append(string.Join(" ; ", res.Echecs)).Append(')');
                if (res.Ignorees.Count > 0) sb.Append(", ").Append(res.Ignorees.Count).Append(" sans moyen de restauration (").Append(string.Join(", ", res.Ignorees)).Append(')');
                sb.Append('.');
                if (res.Remarques.Count > 0) sb.Append(' ').Append(string.Join(" ", res.Remarques));
                Message = sb.ToString();
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
                Titre = o.Titre, Risque = o.Risque,
                Reversibilite = o.Reversible ? "Annulable" : "définitif",
                Redemarrage = o.Redemarrage ? "Redémarrage requis" : "",
                Taille = "—",
            }).ToList();
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
                case "ok": return "Appliqué : " + t;
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
