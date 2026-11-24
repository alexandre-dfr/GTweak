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
    public class HistoriqueViewModel : ObservableObject
    {
        public const string DureeReleveTexte = "30 s";
        public static readonly TimeSpan DureeReleve = TimeSpan.FromSeconds(30);
        public const string AvertissementMesures = "Ces mesures (temps DPC, interruptions, services, processus, mémoire libre) ne disent rien de l'input lag : elles ne mesurent pas la latence entre un clic et l'écran.";

        private readonly DataPaths paths;
        private readonly IRestauration restauration;
        private readonly Dictionary<string, CatalogItem> catalogue;
        private readonly QuarantineService quarantaine;
        private readonly Func<TimeSpan, IProgress<double>, CancellationToken, Task<MesureSnapshot>> prendre;
        private string section = "Sessions";
        private string message = "";
        private bool enCours, enReleve;
        private double progression;
        private SessionViewModel sessionSelectionnee;
        private string rapportTexte = "";
        private string comparaison = "";

        /// <param name="paths">Racine unique des sessions (sous %ProgramFiles%\GamingTweaker : interface et routine).</param>
        public HistoriqueViewModel(DataPaths paths, IRestauration restauration, IReadOnlyList<CatalogItem> catalogue, QuarantineService quarantaine,
            Func<TimeSpan, IProgress<double>, CancellationToken, Task<MesureSnapshot>> prendreReleve = null)
        {
            this.paths = paths; this.restauration = restauration; this.quarantaine = quarantaine;
            this.catalogue = (catalogue ?? new List<CatalogItem>()).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            prendre = prendreReleve ?? ((d, p, ct) => Mesures.TakeAsync(d, p, ct));

            ChoisirSectionCommand = new RelayCommand(p => Section = (string)p);
            ActualiserCommand = new RelayCommand(_ => Charger());
            AnnulerSessionCommand = new RelayCommand(p => { var t = AnnulerSessionAsync((SessionViewModel)p); }, p => !enCours && p is SessionViewModel && ((SessionViewModel)p).Annulable);
            RestaurerQuarantaineCommand = new RelayCommand(p => RestaurerQuarantaine((QuarantaineLigne)p), p => p is QuarantaineLigne && ((QuarantaineLigne)p).OrigineConnue);
            PurgerQuarantaineCommand = new RelayCommand(p => PurgerQuarantaine((QuarantaineLigne)p), p => p is QuarantaineLigne);
            ReleverCommand = new RelayCommand(_ => { var t = ReleverAsync(); }, _ => !enReleve);
            ComparerCommand = new RelayCommand(_ => AvantApres(), _ => Releves.Count >= 2);
            DesinstallerCommand = new RelayCommand(_ => { var t = DesinstallerAsync(); }, _ => !enCours && !enReleve);
            Charger();
        }

        public ObservableCollection<SessionViewModel> Sessions { get; } = new ObservableCollection<SessionViewModel>();
        public ObservableCollection<QuarantaineLigne> Quarantaine { get; } = new ObservableCollection<QuarantaineLigne>();
        public ObservableCollection<ReleveLigne> Releves { get; } = new ObservableCollection<ReleveLigne>();

        public ICommand ChoisirSectionCommand { get; private set; }
        public ICommand ActualiserCommand { get; private set; }
        public ICommand AnnulerSessionCommand { get; private set; }
        public ICommand RestaurerQuarantaineCommand { get; private set; }
        public ICommand PurgerQuarantaineCommand { get; private set; }
        public ICommand ReleverCommand { get; private set; }
        /// <summary>Bouton unique « Avant / après » : les deux relevés cochés s'il y en a exactement deux, sinon les deux plus récents.</summary>
        public ICommand ComparerCommand { get; private set; }
        public ICommand DesinstallerCommand { get; private set; }

        /// <summary>Désinstallation (aperçu si faux, exécution si vrai) ; remplaçable dans les tests. Par défaut : Desinstallation.Executer.</summary>
        public Func<bool, BilanDesinstallation> Desinstaller { get; set; } = confirme => Desinstallation.Executer(confirme);
        /// <summary>Appelée quand la désinstallation a été exécutée (l'application se ferme).</summary>
        public Action ApresDesinstallation { get; set; }

        /// <summary>
        /// Aperçu (routine, sessions, quarantaine) puis confirmation explicite ; les réglages appliqués ne sont pas annulés par la désinstallation,
        /// le texte le rappelle. Sous le verrou d'instance (pris dans Desinstallation.Executer).
        /// </summary>
        public async Task DesinstallerAsync()
        {
            if (enCours) return;
            EnCours = true;
            try
            {
                var apercu = await Task.Run(() => Desinstaller(false));
                var texte = string.Join("\r\n", apercu.Actions.Concat(apercu.Avertissements.Select(a => "Attention : " + a)))
                    + "\r\n\r\nL'application se fermera ; GamingTweaker.exe lui-même reste à supprimer à la main.\r\n\r\nDésinstaller maintenant ?";
                if (apercu.Avertissements.Any(a => a.Contains("autre exécution"))) { Message = apercu.Avertissements[0]; return; }
                if (Confirmer == null || !Confirmer(texte)) { Message = "Désinstallation annulée."; return; }
                var bilan = await Task.Run(() => Desinstaller(true));
                Message = string.Join(" ", bilan.Actions.Concat(bilan.Avertissements.Select(a => "Attention : " + a)));
                if (bilan.Code != 2 && ApresDesinstallation != null) ApresDesinstallation();
            }
            catch (Exception ex) { Message = "Désinstallation impossible : " + ex.Message; }
            finally { EnCours = false; }
        }

        /// <summary>Confirmation (texte, vrai pour continuer) : conflits d'annulation, suppression définitive.</summary>
        public Func<string, bool> Confirmer { get; set; }
        /// <summary>Appelée après une annulation (redétection des options par exemple).</summary>
        public Action ApresAnnulation { get; set; }
        /// <summary>Valeurs modifiées depuis l'application (journal, options) ; appelée sous le verrou d'instance.</summary>
        public Func<string, List<string>, List<Conflict>> TrouverConflits { get; set; } = (j, ids) => UndoService.FindConflicts(j, ids);

        public string Section { get { return section; } set { if (Set(ref section, value)) { Raise("EstSessions"); Raise("EstQuarantaine"); Raise("EstMesures"); } } }
        public bool EstSessions { get { return section == "Sessions"; } }
        public bool EstQuarantaine { get { return section == "Quarantaine"; } }
        public bool EstMesures { get { return section == "Mesures"; } }

        public string Message { get { return message; } set { Set(ref message, value ?? ""); } }
        public bool EnCours { get { return enCours; } private set { if (Set(ref enCours, value)) CommandManager.InvalidateRequerySuggested(); } }
        public bool EnReleve { get { return enReleve; } private set { if (Set(ref enReleve, value)) CommandManager.InvalidateRequerySuggested(); } }
        public double Progression { get { return progression; } private set { Set(ref progression, value); } }
        public string RapportTexte { get { return rapportTexte; } private set { Set(ref rapportTexte, value ?? ""); } }
        public string Comparaison { get { return comparaison; } private set { Set(ref comparaison, value ?? ""); } }
        public string AvertissementInputLag { get { return AvertissementMesures; } }

        public SessionViewModel SessionSelectionnee
        {
            get { return sessionSelectionnee; }
            set { if (Set(ref sessionSelectionnee, value)) { RapportTexte = value == null ? "" : LireRapport(value); CommandManager.InvalidateRequerySuggested(); } }
        }

        // ---------- Chargement ----------

        private class EtatSession { [JsonProperty("etat")] public string Etat { get; set; } [JsonProperty("debut")] public string Debut { get; set; } }

        public void Charger()
        {
            var dossier = sessionSelectionnee == null ? null : sessionSelectionnee.Dossier;
            Sessions.Clear();
            var liste = LireSessions();
            // Verrou d'instance tenu par une autre exécution (routine, autre instance) : la session « Running » la plus récente est en cours.
            bool verrouPris = false;
            if (restauration != null)
            {
                try { using (var v = restauration.PrendreVerrou()) verrouPris = v == null; } catch (Exception) { }
            }
            if (verrouPris)
            {
                var enCoursExec = liste.FirstOrDefault(x => x.Etat == "Running");
                if (enCoursExec != null)
                {
                    enCoursExec.EnCoursExecution = true;
                    enCoursExec.Anormale = false;
                    enCoursExec.EtatLibelle = "en cours";
                    foreach (var o in enCoursExec.Options) o.Annulable = false;
                }
            }
            foreach (var s in liste) Sessions.Add(s);
            SessionSelectionnee = dossier == null ? null : Sessions.FirstOrDefault(s => s.Dossier == dossier);

            Quarantaine.Clear();
            try { foreach (var e in quarantaine.List()) Quarantaine.Add(new QuarantaineLigne { Entree = e }); }
            catch (Exception ex) { Message = "Quarantaine illisible : " + ex.Message; }

            Releves.Clear();
            try
            {
                foreach (var m in Mesures.List(paths.MachineDir)) { var r = new ReleveLigne { Mesure = m }; r.PropertyChanged += (a, b) => CommandManager.InvalidateRequerySuggested(); Releves.Add(r); }
            }
            catch (Exception ex) { Message = "Mesures illisibles : " + ex.Message; }
        }

        /// <summary>Sessions de l'interface et de la routine (une seule racine), du plus récent au plus ancien (date de début).</summary>
        private List<SessionViewModel> LireSessions()
        {
            var res = new List<SessionViewModel>();
            var dir = paths == null ? null : paths.SessionsDir;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    try { res.Add(LireSession(d)); }
                    catch (Exception) { /* dossier illisible : ignoré */ }
                }
            }
            return res.OrderByDescending(s => s.Debut).ThenByDescending(s => s.Id, StringComparer.Ordinal).ToList();
        }

        private SessionViewModel LireSession(string d)
        {
            var s = new SessionViewModel { Id = Path.GetFileName(d), Dossier = d, Etat = "Inconnu" };
            s.Debut = Directory.GetCreationTime(d);
            var f = Path.Combine(d, "state.json");
            if (File.Exists(f))
            {
                try
                {
                    var st = JsonConvert.DeserializeObject<EtatSession>(File.ReadAllText(f, Encoding.UTF8), new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                    if (st != null)
                    {
                        s.Etat = st.Etat ?? "Inconnu";
                        DateTime debut;
                        if (DateTime.TryParseExact(st.Debut ?? "", "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out debut)) s.Debut = debut;
                    }
                }
                catch (Exception) { }
            }
            s.Anormale = s.Etat == "Running" || s.Etat == "Failed";
            s.EtatLibelle = s.Etat == "Running" ? "interrompue (arrêt avant la fin)" : s.Etat == "Failed" ? "en échec (erreur interne)" : s.Etat == "Done" ? "terminée" : "état inconnu";

            SessionReport r = null;
            var rf = Path.Combine(d, "report.json");
            if (File.Exists(rf))
            {
                try { r = JsonConvert.DeserializeObject<SessionReport>(File.ReadAllText(rf, Encoding.UTF8), new StringEnumConverter()); } catch (Exception) { }
            }
            HashSet<string> annulables = new HashSet<string>();
            string erreurJournal = null;
            var jf = Path.Combine(d, "journal.jsonl");
            if (File.Exists(jf))
            {
                try { annulables = new HashSet<string>(Journal.Active(Journal.Read(jf)).Select(e => e.Item)); }
                catch (InvalidDataException ex) { erreurJournal = ex.Message; }
            }
            var ids = new List<string>();
            if (r != null)
            {
                s.Appliquees = r.Items.Count(i => i.Status == OutcomeStatus.Ok);
                s.Echecs = r.Items.Count(i => i.Status == OutcomeStatus.Failed);
                s.Ignorees = r.Items.Count(i => i.Status == OutcomeStatus.Skipped || i.Status == OutcomeStatus.AlreadyApplied);
                foreach (var i in r.Items) { ids.Add(i.Id); AjouterOption(s, i.Id, Libelle(i.Status), i.Message, annulables.Contains(i.Id)); }
            }
            // Options modifiées d'après le journal mais absentes du rapport (session interrompue).
            foreach (var id in annulables.Where(a => !ids.Contains(a)))
                AjouterOption(s, id, "interrompue", null, true);
            s.Resume = r == null ? (s.Anormale ? "pas de rapport" : "aucun rapport")
                                 : s.Appliquees + " appliquée(s), " + s.Echecs + " échec(s), " + s.Ignorees + " ignorée(s)";
            if (erreurJournal != null) s.Resume += " (journal d'annulation illisible)";
            return s;
        }

        private void AjouterOption(SessionViewModel s, string id, string statut, string message, bool annulable)
        {
            CatalogItem c;
            var o = new SessionOption { Id = id, Titre = catalogue.TryGetValue(id ?? "", out c) ? c.Titre : id, Statut = statut, Message = message, Annulable = annulable };
            o.AnnulerCommand = new RelayCommand(_ => { var t = AnnulerOptionAsync(s, id); }, _ => !enCours && o.Annulable);
            s.Options.Add(o);
        }

        private static string Libelle(OutcomeStatus st)
        {
            switch (st)
            {
                case OutcomeStatus.Ok: return "Appliquée";
                case OutcomeStatus.Failed: return "Échec";
                case OutcomeStatus.Skipped: return "Ignorée";
                default: return "Déjà appliquée";
            }
        }

        private static string LireRapport(SessionViewModel s)
        {
            try
            {
                var f = Path.Combine(s.Dossier, "report.txt");
                if (File.Exists(f)) return File.ReadAllText(f, Encoding.UTF8);
            }
            catch (Exception ex) { return "Rapport illisible : " + ex.Message; }
            return "Aucun rapport pour cette session" + (s.Anormale ? " (elle s'est arrêtée avant la fin ; ses modifications restent annulables)." : ".");
        }

        // ---------- Annulation ----------

        public Task AnnulerSessionAsync(SessionViewModel s)
        {
            return AnnulerAsync(s, s.Options.Where(o => o.Annulable).Select(o => o.Id).ToList(), true);
        }

        public Task AnnulerOptionAsync(SessionViewModel s, string id)
        {
            return AnnulerAsync(s, new List<string> { id }, false);
        }

        /// <summary>
        /// Annule par le journal de la session, avec le verrou d'instance de l'exécution et la règle HKCU (autre compte : options HKCU ignorées).
        /// Les valeurs modifiées depuis sont listées dans une confirmation ; les options annulées sortent de last-applied.json.
        /// </summary>
        private async Task AnnulerAsync(SessionViewModel s, List<string> demandes, bool confirmerToujours)
        {
            if (enCours || s == null) return;
            if (s.EnCoursExecution) { Message = "Cette session est en cours d'exécution : attendez sa fin pour l'annuler."; return; }
            var journal = Path.Combine(s.Dossier, "journal.jsonl");
            EnCours = true;
            bool annule = false;
            try
            {
                // Verrou pris avant de lire le journal et de calculer les conflits : rien ne change entre la vérification et l'annulation.
                using (var verrou = restauration.PrendreVerrou())
                {
                    if (verrou == null) { Message = "Une autre exécution de GamingTweaker est en cours : rien n'a été annulé."; return; }
                    List<string> ids;
                    try { ids = Journal.Active(Journal.Read(journal)).Select(e => e.Item).Distinct().Where(demandes.Contains).ToList(); }
                    catch (Exception ex) { Message = ex.Message; return; }
                    if (ids.Count == 0) { Message = "Rien à annuler : le journal de cette session n'a plus de modification active."; return; }

                    List<Conflict> conflits;
                    var trouver = TrouverConflits;
                    try { conflits = await Task.Run(() => trouver(journal, ids)); }
                    catch (Exception ex) { Message = "Vérification des conflits impossible, rien n'a été annulé : " + ex.Message; return; }
                    if (conflits.Count > 0)
                    {
                        var txt = "Ces valeurs ont été modifiées depuis l'application. L'annulation les remplacerait par l'état d'origine :\r\n\r\n"
                                + string.Join("\r\n", conflits.Select(c => "- " + c.ItemId + " : " + c.Target + " (attendu " + c.Attendu + ", actuel " + c.Actuel + ")"))
                                + "\r\n\r\nAnnuler quand même ?";
                        if (Confirmer == null || !Confirmer(txt)) { Message = "Annulation abandonnée."; return; }
                    }
                    else if (confirmerToujours && Confirmer != null && !Confirmer("Annuler " + ids.Count + " option(s) de la session du " + s.DebutTexte + " ?")) return;

                    annule = true;
                    bool memeUtilisateur = restauration.MemeUtilisateur();
                    var reussies = new List<string>(); var echecs = new List<string>(); var ignorees = new List<string>(); var notes = new List<string>();
                    var nonAnnulees = new List<string>();
                    foreach (var id in ids)
                    {
                        CatalogItem c;
                        if (!memeUtilisateur && catalogue.TryGetValue(id, out c) && c.Hkcu) { ignorees.Add(id); continue; }
                        try
                        {
                            var o = await restauration.AnnulerParJournalAsync(journal, id);
                            if (o == null || o.Status == OutcomeStatus.Failed) echecs.Add(id + " : " + (o == null || string.IsNullOrWhiteSpace(o.Message) ? "échec de l'annulation" : o.Message.Trim()));
                            // Ignorée : rien n'a été restauré (ou restauration incomplète) ; ni comptée comme annulée ni retirée de last-applied.
                            else if (o.Status == OutcomeStatus.Skipped) nonAnnulees.Add(id + (string.IsNullOrWhiteSpace(o.Message) ? "" : " : " + o.Message.Trim()));
                            else reussies.Add(id);
                        }
                        catch (Exception ex) { echecs.Add(id + " : " + ex.Message); }
                    }
                    if (reussies.Count > 0) restauration.OublierDernieresApplications(reussies);
                    var sb = new StringBuilder(reussies.Count + " option(s) annulée(s)");
                    if (echecs.Count > 0) sb.Append(", ").Append(echecs.Count).Append(" échec(s) (").Append(string.Join(" ; ", echecs)).Append(')');
                    if (nonAnnulees.Count > 0) sb.Append(", ").Append(nonAnnulees.Count).Append(" non annulée(s) (").Append(string.Join(" ; ", nonAnnulees)).Append(')');
                    if (ignorees.Count > 0) sb.Append(", ").Append(ignorees.Count).Append(" ignorée(s) car l'application tourne sous un autre compte que le bureau (").Append(string.Join(", ", ignorees)).Append(')');
                    sb.Append('.');
                    if (notes.Count > 0) sb.Append(' ').Append(string.Join(" ", notes));
                    Message = sb.ToString();
                }
            }
            finally { EnCours = false; }
            if (annule)
            {
                Charger();
                if (ApresAnnulation != null) ApresAnnulation();
            }
        }

        // ---------- Quarantaine ----------

        /// <summary>Sous le verrou d'instance : le journal (marqueur) n'est pas modifié pendant une exécution.</summary>
        private void SousVerrou(Action a, string refus)
        {
            using (var v = restauration == null ? new Rien() : restauration.PrendreVerrou())
            {
                if (v == null) { Message = refus; return; }
                a();
            }
        }

        private class Rien : IDisposable { public void Dispose() { } }

        public void RestaurerQuarantaine(QuarantaineLigne l)
        {
            if (l == null) return;
            SousVerrou(() =>
            {
                try { quarantaine.Restore(l.Entree.Id); Message = "Fichier restauré : " + l.Entree.Origine; }
                catch (Exception ex) { Message = "Restauration impossible : " + ex.Message; }
            }, "Une autre exécution de GamingTweaker est en cours : rien n'a été restauré.");
            Charger();
        }

        public void PurgerQuarantaine(QuarantaineLigne l)
        {
            if (l == null) return;
            if (Confirmer == null || !Confirmer("Supprimer définitivement « " + l.Nom + " » ?\r\nCette suppression est irréversible.")) return;
            SousVerrou(() =>
            {
                try { quarantaine.Purge(l.Entree.Id); Message = "Fichier supprimé définitivement : " + l.Nom; }
                catch (Exception ex) { Message = "Suppression impossible : " + ex.Message; }
            }, "Une autre exécution de GamingTweaker est en cours : rien n'a été supprimé.");
            Charger();
        }

        // ---------- Mesures ----------

        public async Task ReleverAsync()
        {
            if (enReleve) return;
            EnReleve = true; Progression = 0;
            Message = "Relevé en cours (" + DureeReleveTexte + ") : ne lancez rien d'autre pour une mesure représentative.";
            try
            {
                var p = new Progress<double>(v => Progression = v);
                var s = await prendre(DureeReleve, p, CancellationToken.None);
                Mesures.Save(s, paths.MachineDir);
                Message = "Relevé enregistré." + (string.IsNullOrEmpty(s.Note) ? "" : " " + s.Note);
                Charger();
            }
            catch (Exception ex) { Message = "Relevé impossible : " + ex.Message; }
            finally { EnReleve = false; }
        }

        public void Comparer()
        {
            var deux = Releves.Where(r => r.IsChecked).Select(r => r.Mesure).OrderBy(m => m.Date, StringComparer.Ordinal).ToList();
            if (deux.Count != 2) { Comparaison = "Cochez exactement deux relevés."; return; }
            Comparaison = FormaterComparaison(deux[0], deux[1]);
        }

        public static string FormaterComparaison(MesureSnapshot a, MesureSnapshot b)
        {
            var d = Mesures.Compare(a, b);
            Func<double?, string> ec = v => v.HasValue ? (v.Value > 0 ? "+" : "") + v.Value.ToString("0.00", CultureInfo.CurrentCulture) + " point(s)" : "n/d";
            int mieux = 0, moins = 0;
            // Verdict d'une ligne : « moinsEstMieux » vrai si une baisse est un progrès ; en dessous du seuil, stable.
            Func<double?, double, bool, string> verdict = (delta, seuil, moinsEstMieux) =>
            {
                if (!delta.HasValue || Math.Abs(delta.Value) < seuil) return "stable";
                bool progres = moinsEstMieux ? delta.Value < 0 : delta.Value > 0;
                if (progres) mieux++; else moins++;
                return progres ? "mieux" : "moins bien";
            };
            var sb = new StringBuilder();
            sb.AppendLine("Du relevé du " + a.Date + " à celui du " + b.Date + " :");
            sb.AppendLine("Temps DPC : " + ReleveLigne.Pct(a.DpcTime) + " → " + ReleveLigne.Pct(b.DpcTime) + " (" + ec(d.DpcTime) + ") : " + verdict(d.DpcTime, 0.05, true));
            sb.AppendLine("Temps d'interruptions : " + ReleveLigne.Pct(a.InterruptTime) + " → " + ReleveLigne.Pct(b.InterruptTime) + " (" + ec(d.InterruptTime) + ") : " + verdict(d.InterruptTime, 0.05, true));
            if (a.Cpu.HasValue || b.Cpu.HasValue)
                sb.AppendLine("Processeur au repos : " + ReleveLigne.Pct(a.Cpu) + " → " + ReleveLigne.Pct(b.Cpu) + " (" + ec(d.Cpu) + ") : " + verdict(d.Cpu, 0.5, true));
            sb.AppendLine("Services en cours : " + a.Services + " → " + b.Services + " (" + Signe(d.Services) + ") : " + verdict(d.Services, 1, true));
            sb.AppendLine("Processus : " + a.Processus + " → " + b.Processus + " (" + Signe(d.Processus) + ") : " + verdict(d.Processus, 3, true));
            if (a.Demarrage.HasValue || b.Demarrage.HasValue)
                sb.AppendLine("Programmes au démarrage : " + (a.Demarrage.HasValue ? a.Demarrage.ToString() : "n/d") + " → " + (b.Demarrage.HasValue ? b.Demarrage.ToString() : "n/d") + " : " + verdict(d.Demarrage, 1, true));
            sb.AppendLine("Mémoire disponible : " + a.RamDisponibleMo + " Mo → " + b.RamDisponibleMo + " Mo (" + Signe(d.RamDisponibleMo) + " Mo) : " + verdict(d.RamDisponibleMo, 200, false));
            sb.Append("Bilan : " + mieux + " mesure(s) en progrès, " + moins + " en recul. Relevés à faire dans les mêmes conditions (même programmes ouverts, PC au repos).");
            return sb.ToString();
        }

        /// <summary>« Avant / après » : les deux relevés cochés s'il y en a exactement deux, sinon les deux plus récents.</summary>
        public void AvantApres()
        {
            if (Releves.Count(r => r.IsChecked) == 2) Comparer();
            else ComparerDerniers();
        }

        /// <summary>Compare les deux relevés les plus récents (avant / après une optimisation), sans avoir à les cocher.</summary>
        public void ComparerDerniers()
        {
            var deux = Releves.Select(r => r.Mesure).OrderByDescending(m => m.Date, StringComparer.Ordinal).Take(2).OrderBy(m => m.Date, StringComparer.Ordinal).ToList();
            if (deux.Count < 2) { Comparaison = "Faites un relevé avant d'optimiser, puis un autre après : la comparaison montre ce qui a changé."; return; }
            Comparaison = FormaterComparaison(deux[0], deux[1]);
        }

        private static string Signe(long v) { return (v > 0 ? "+" : "") + v.ToString(CultureInfo.CurrentCulture); }
    }
}
