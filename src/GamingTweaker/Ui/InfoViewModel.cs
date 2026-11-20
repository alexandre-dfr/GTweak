using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using GamingTweaker.Info;

namespace GamingTweaker.Ui
{
    public class SensorLine : ObservableObject
    {
        private string valeur = "--";
        public string Nom { get; set; }
        public string Valeur { get { return valeur; } set { Set(ref valeur, value); } }
    }

    public class SensorGroup
    {
        public string Titre { get; set; }
        public ObservableCollection<SensorLine> Lignes { get; } = new ObservableCollection<SensorLine>();
    }

    /// <summary>Menu Informations : fiche matériel (collectée une fois) et capteurs en direct (sur demande, toutes les 2 s tant que le menu est visible).</summary>
    public class InfoViewModel : ObservableObject, IDisposable
    {
        private static readonly string[] TypesAffiches = { "Temperature", "Load", "Clock", "Power", "Fan" };

        private readonly Func<InfoReport> collect;
        private readonly Func<ICapteurs> creerCapteurs;
        private readonly DispatcherTimer timer;
        private ICapteurs capteurs;
        private bool visible, collecte, lecture;
        private string statut = "", statutCapteurs = "Les capteurs sont désactivés.";
        private bool enCours;
        private readonly Dictionary<string, SensorLine> lignesParCle = new Dictionary<string, SensorLine>();
        private readonly Dictionary<string, SensorGroup> groupes = new Dictionary<string, SensorGroup>();

        public InfoViewModel(Func<InfoReport> collect, Func<ICapteurs> creerCapteurs)
        {
            this.collect = collect; this.creerCapteurs = creerCapteurs;
            // Les reprises après await doivent revenir sur le thread de l'interface (sans objet dans l'application, utile hors Application.Run).
            if (System.Threading.SynchronizationContext.Current == null)
                System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += async (s, e) => await LireAsync();
            ActiverCapteursCommand = new RelayCommand(async _ => await ActiverCapteursAsync(), _ => !CapteursActifs && !activation);
        }
        private bool activation;

        public ObservableCollection<InfoSection> Sections { get; } = new ObservableCollection<InfoSection>();
        public ObservableCollection<SensorGroup> Capteurs { get; } = new ObservableCollection<SensorGroup>();
        public ICommand ActiverCapteursCommand { get; private set; }

        /// <summary>Demande d'accord avant d'activer les capteurs (texte). Renvoie vrai si l'utilisateur accepte.</summary>
        public Func<string, bool> ConfirmerCapteurs { get; set; }
        /// <summary>Demande d'accord avant d'installer PawnIO. Renvoie vrai si l'utilisateur accepte.</summary>
        public Func<string, bool> ConfirmerPawnIo { get; set; }
        /// <summary>Installation de PawnIO (appelée seulement après accord). Renvoie un message.</summary>
        public Func<string> InstallerPawnIo { get; set; }
        public Func<bool> PawnIoInstalle { get; set; }

        public string Statut { get { return statut; } private set { Set(ref statut, value); } }
        public string StatutCapteurs { get { return statutCapteurs; } private set { Set(ref statutCapteurs, value); } }
        public bool EnCours { get { return enCours; } private set { Set(ref enCours, value); } }
        public bool CapteursActifs { get { return capteurs != null; } }
        public bool Visible { get { return visible; } }

        private string sectionDemandee;
        /// <summary>Section à faire défiler en tête de la vue (« CPU », « GPU »…), demandée depuis l'accueil ; la vue la remet à null une fois affichée.</summary>
        public string SectionDemandee { get { return sectionDemandee; } set { sectionDemandee = value; Raise(); } }

        /// <summary>Appelé par le view-model principal selon le menu affiché.</summary>
        public void SetVisible(bool v)
        {
            if (visible == v) return;
            visible = v; Raise("Visible");
            if (v)
            {
                if (!collecte) { collecte = true; var _ = ChargerAsync(); }
                if (capteurs != null) { timer.Start(); var _2 = LireAsync(); }
            }
            else timer.Stop();
        }

        public async Task ChargerAsync()
        {
            EnCours = true; Statut = "Collecte des informations en cours…";
            try
            {
                var rapport = await Task.Run(collect);
                Sections.Clear();
                foreach (var s in rapport.Sections) Sections.Add(s);
                Statut = "";
            }
            catch (Exception ex) { Statut = "Collecte impossible : " + ex.Message; }
            finally { EnCours = false; }
        }

        public InfoReport Rapport()
        {
            var r = new InfoReport();
            foreach (var s in Sections) r.Sections.Add(s);
            return r;
        }

        /// <summary>Écrit le rapport en UTF-8 avec BOM.</summary>
        public void Exporter(string chemin)
        {
            File.WriteAllText(chemin, Rapport().ToText(), new UTF8Encoding(true));
        }

        public async Task ActiverCapteursAsync()
        {
            if (capteurs != null || activation) return;
            var accord = ConfirmerCapteurs == null || ConfirmerCapteurs(
                "Les capteurs en direct utilisent la bibliothèque LibreHardwareMonitor pour lire les températures, charges et fréquences. "
                + "Aucune donnée n'est envoyée ailleurs. Les températures du processeur demandent en plus le pilote PawnIO.\r\n\r\nActiver les capteurs ?");
            if (!accord) return;
            activation = true; CommandManager.InvalidateRequerySuggested();
            try
            {
                var note = "";
                var installe = PawnIoInstalle != null ? PawnIoInstalle() : PawnIoInstaller.IsInstalled();
                if (!installe)
                {
                    var veut = ConfirmerPawnIo != null && ConfirmerPawnIo(
                        "PawnIO (pilote signé, projet libre) n'est pas installé. Sans lui, les températures et la consommation du processeur ne sont pas lisibles ; "
                        + "la carte graphique et le reste fonctionnent.\r\n\r\nL'installer maintenant (winget, ou page officielle) ?");
                    if (veut && InstallerPawnIo != null)
                    {
                        StatutCapteurs = "Installation de PawnIO…";
                        note = await Task.Run(InstallerPawnIo);
                        installe = PawnIoInstalle != null ? PawnIoInstalle() : PawnIoInstaller.IsInstalled();
                    }
                    if (!installe) note = (note + " Capteurs du processeur limités : PawnIO n'est pas installé.").Trim();
                }
                StatutCapteurs = "Démarrage des capteurs…";
                try { capteurs = await Task.Run(creerCapteurs); }
                catch (Exception ex) { StatutCapteurs = "Capteurs indisponibles : " + ex.Message; return; }
                Raise("CapteursActifs");
                StatutCapteurs = (note.Length > 0 ? note + " " : "") + "Mise à jour toutes les 2 secondes.";
                if (visible) { timer.Start(); await LireAsync(); }
            }
            finally { activation = false; CommandManager.InvalidateRequerySuggested(); }
        }

        public async Task LireAsync()
        {
            var c = capteurs;
            if (c == null || lecture || !visible) return;
            lecture = true;
            try
            {
                var valeurs = await Task.Run(() => c.Read());
                if (capteurs == null) return;
                Appliquer(valeurs);
            }
            catch (Exception ex) { StatutCapteurs = "Lecture des capteurs impossible : " + ex.Message; }
            finally { lecture = false; }
        }

        /// <summary>Met à jour les lignes en place (pas de reconstruction, donc pas de saut de défilement).</summary>
        public void Appliquer(IEnumerable<SensorReading> valeurs)
        {
            foreach (var v in valeurs.Where(x => TypesAffiches.Contains(x.Type) && x.Value.HasValue))
            {
                var cle = v.Materiel + "|" + v.Type + "|" + v.Nom;
                SensorLine ligne;
                if (!lignesParCle.TryGetValue(cle, out ligne))
                {
                    SensorGroup g;
                    if (!groupes.TryGetValue(v.Materiel, out g)) { g = new SensorGroup { Titre = v.Materiel }; groupes[v.Materiel] = g; Capteurs.Add(g); }
                    ligne = new SensorLine { Nom = Libelle(v) };
                    lignesParCle[cle] = ligne; g.Lignes.Add(ligne);
                }
                ligne.Valeur = v.Affichage;
            }
        }

        private static string Libelle(SensorReading v)
        {
            string t = v.Type == "Temperature" ? "Température" : v.Type == "Load" ? "Charge" : v.Type == "Clock" ? "Fréquence" : v.Type == "Power" ? "Puissance" : "Ventilateur";
            return t + " - " + v.Nom;
        }

        public void Dispose()
        {
            timer.Stop();
            var c = capteurs; capteurs = null;
            if (c != null) c.Dispose();
        }
    }
}
