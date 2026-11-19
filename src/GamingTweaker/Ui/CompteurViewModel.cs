using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    /// <summary>Choix affiché dans une liste déroulante (valeur et libellé).</summary>
    public class ChoixListe<T>
    {
        public T Valeur { get; set; }
        public string Nom { get; set; }
    }

    /// <summary>
    /// Compteur en jeu : réglages (enregistrés dans compteur.json, appliqués tout de suite) et valeurs affichées par la surimpression
    /// (<see cref="CompteurWindow"/>), rafraîchies 2 fois par seconde tant que le compteur est actif (assez pour suivre, assez peu pour rester lisible).
    /// </summary>
    public class CompteurViewModel : ObservableObject, IDisposable
    {
        public const double LargeurCourbe = 200, HauteurCourbe = 36;
        public const int RelevesParSeconde = 2;
        public const string Raccourci = "Ctrl+Maj+F11";
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly string machineDir;
        private readonly ISourceCompteur source;
        private readonly CompteurConfig config;
        private readonly DispatcherTimer minuteur;
        private readonly DispatcherTimer enregistrement;
        private readonly List<double> historique = new List<double>();
        private bool sourceDemarree;

        public CompteurViewModel(string machineDir, ISourceCompteur source, CompteurConfig config = null)
        {
            this.machineDir = machineDir;
            this.source = source;
            this.config = (config ?? CompteurConfig.Charger(machineDir)).Normaliser();
            minuteur = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / RelevesParSeconde) };
            minuteur.Tick += (s, e) => Rafraichir(source.Lire());
            // Enregistrement différé : un curseur déplacé n'écrit pas le fichier à chaque pas.
            enregistrement = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            enregistrement.Tick += (s, e) => { enregistrement.Stop(); Enregistrer(); };
            if (this.config.Actif) DemarrerSource();
        }

        /// <summary>L'affichage doit être montré, caché ou replacé (actif, position, taille, éléments affichés).</summary>
        public event Action AffichageChange;

        // ---------- Réglages ----------

        public bool Actif
        {
            get { return config.Actif; }
            set
            {
                if (config.Actif == value) return;
                config.Actif = value;
                if (value) DemarrerSource(); else if (!modePlacement) ArreterSource();
                Raise(); Raise("EtatBouton"); Modifie(true);
            }
        }

        /// <summary>Bascule le compteur (raccourci clavier, bouton).</summary>
        public void Basculer() { Actif = !Actif; }

        public CoinEcran Position { get { return config.Position; } set { if (config.Position != value) { config.Position = value; Raise(); Modifie(true); } } }

        /// <summary>Position libre (pixels logiques du bureau), utilisée quand <see cref="Position"/> vaut Libre.</summary>
        public double X { get { return config.X; } }
        public double Y { get { return config.Y; } }

        /// <summary>Le compteur a été déplacé à la souris : il garde cette position (n'importe quel écran).</summary>
        public void DeplaceA(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return;
            config.X = Math.Round(x); config.Y = Math.Round(y);
            if (config.Position != CoinEcran.Libre) { config.Position = CoinEcran.Libre; Raise("Position"); }
            Modifie(false);
        }

        private bool modePlacement;
        /// <summary>
        /// Placement (fenêtre des réglages ouverte) : le compteur est affiché même désactivé, entouré d'un cadre, et se déplace à la souris.
        /// Hors placement, il laisse passer les clics.
        /// </summary>
        public bool ModePlacement
        {
            get { return modePlacement; }
            set
            {
                if (!Set(ref modePlacement, value)) return;
                if (value) DemarrerSource(); else if (!config.Actif) ArreterSource();
                var h = AffichageChange; if (h != null) h();
            }
        }

        /// <summary>La surimpression doit être visible (compteur actif, ou réglages ouverts).</summary>
        public bool Visible { get { return config.Actif || modePlacement; } }
        public TailleCompteur Taille
        {
            get { return config.Taille; }
            set
            {
                if (config.Taille == value) return;
                if (value == TailleCompteur.Personnalisee) config.Echelle = config.EchelleEffective;
                config.Taille = value; Raise(); Raise("Echelle"); Raise("TexteEchelle"); Modifie(true);
            }
        }

        /// <summary>Redimensionnement à la souris (poignée du cadre, Ctrl+molette) : passe en taille personnalisée.</summary>
        public void Redimensionner(double echelle)
        {
            if (double.IsNaN(echelle) || double.IsInfinity(echelle)) return;
            var e = Math.Round(Math.Max(CompteurConfig.EchelleMin, Math.Min(CompteurConfig.EchelleMax, echelle)), 2);
            if (config.Taille == TailleCompteur.Personnalisee && Math.Abs(config.Echelle - e) < 0.005) return;
            config.Echelle = e;
            if (config.Taille != TailleCompteur.Personnalisee) { config.Taille = TailleCompteur.Personnalisee; Raise("Taille"); }
            Raise("Echelle"); Raise("TexteEchelle"); Modifie(false);
        }

        /// <summary>Échelle en pourcentage (réglages).</summary>
        public string TexteEchelle { get { return Math.Round(Echelle * 100).ToString("0", Fr) + " %"; } }

        public string Couleur
        {
            get { return config.Couleur; }
            set
            {
                if (!CompteurConfig.CouleurValide(value)) return;
                var c = value.Trim().ToUpperInvariant();
                if (config.Couleur == c) return;
                config.Couleur = c; Raise(); Raise("BrosseTexte"); Modifie(false);
            }
        }

        public double OpaciteFond
        {
            get { return config.OpaciteFond; }
            set
            {
                var v = Math.Max(0.2, Math.Min(1.0, value));
                if (Math.Abs(config.OpaciteFond - v) < 0.001) return;
                config.OpaciteFond = v; Raise(); Raise("BrosseFond"); Modifie(false);
            }
        }

        public bool Fps { get { return config.Fps; } set { if (config.Fps != value) { config.Fps = value; Raise(); Raise("AfficherCourbe"); Modifie(true); } } }
        public bool Details { get { return config.Details; } set { if (config.Details != value) { config.Details = value; Raise(); Modifie(true); } } }
        public bool Courbe { get { return config.Courbe; } set { if (config.Courbe != value) { config.Courbe = value; Raise(); Raise("AfficherCourbe"); Modifie(true); } } }
        public int DureeCourbe
        {
            get { return config.DureeCourbe; }
            set { if (config.DureeCourbe != value && Array.IndexOf(CompteurConfig.DureesCourbe, value) >= 0) { config.DureeCourbe = value; Raise(); Modifie(false); } }
        }
        public bool Cpu { get { return config.Cpu; } set { if (config.Cpu != value) { config.Cpu = value; Raise(); Raise("AfficherCharge"); MajChoixSource(); Modifie(true); } } }
        public bool Gpu { get { return config.Gpu; } set { if (config.Gpu != value) { config.Gpu = value; Raise(); Raise("AfficherCharge"); MajChoixSource(); Modifie(true); } } }
        public bool Ram { get { return config.Ram; } set { if (config.Ram != value) { config.Ram = value; Raise(); Raise("AfficherCharge"); MajChoixSource(); Modifie(true); } } }

        public IReadOnlyList<ChoixListe<CoinEcran>> Positions { get; } = new[]
        {
            new ChoixListe<CoinEcran> { Valeur = CoinEcran.HautGauche, Nom = "En haut à gauche" },
            new ChoixListe<CoinEcran> { Valeur = CoinEcran.HautDroite, Nom = "En haut à droite" },
            new ChoixListe<CoinEcran> { Valeur = CoinEcran.BasGauche, Nom = "En bas à gauche" },
            new ChoixListe<CoinEcran> { Valeur = CoinEcran.BasDroite, Nom = "En bas à droite" },
            new ChoixListe<CoinEcran> { Valeur = CoinEcran.Libre, Nom = "Libre (déplacé à la souris)" },
        };

        public IReadOnlyList<ChoixListe<TailleCompteur>> Tailles { get; } = new[]
        {
            new ChoixListe<TailleCompteur> { Valeur = TailleCompteur.Petite, Nom = "Petite" },
            new ChoixListe<TailleCompteur> { Valeur = TailleCompteur.Normale, Nom = "Normale" },
            new ChoixListe<TailleCompteur> { Valeur = TailleCompteur.Grande, Nom = "Grande" },
            new ChoixListe<TailleCompteur> { Valeur = TailleCompteur.Personnalisee, Nom = "Personnalisée (à la souris)" },
        };

        public IReadOnlyList<ChoixListe<int>> Durees { get; } = CompteurConfig.DureesCourbe.Select(d => new ChoixListe<int> { Valeur = d, Nom = d + " s" }).ToList();

        /// <summary>Couleurs proposées (une couleur libre s'écrit au format #RRVVBB).</summary>
        public IReadOnlyList<ChoixListe<string>> Couleurs { get; } = new[]
        {
            new ChoixListe<string> { Valeur = "#4CE07A", Nom = "Vert" },
            new ChoixListe<string> { Valeur = "#FFFFFF", Nom = "Blanc" },
            new ChoixListe<string> { Valeur = "#FFD43B", Nom = "Jaune" },
            new ChoixListe<string> { Valeur = "#FF9F1C", Nom = "Orange" },
            new ChoixListe<string> { Valeur = "#FF4D6D", Nom = "Rouge" },
            new ChoixListe<string> { Valeur = "#C77DFF", Nom = "Violet" },
            new ChoixListe<string> { Valeur = "#4CC9F0", Nom = "Cyan" },
        };

        /// <summary>Infobulle du bouton de la barre du haut.</summary>
        public string EtatBouton { get { return "Compteur FPS en jeu : " + (Actif ? "affiché" : "masqué") + " (" + Raccourci + " pour l'afficher ou le masquer)"; } }

        /// <summary>Message d'erreur de la lecture des FPS (vide si tout va bien).</summary>
        public string Erreur { get { return Actif ? source.Erreur ?? "" : ""; } }

        // ---------- Valeurs affichées ----------

        private string texteFps = "—", texteDetails = "", texteProcessus = "", texteCpu = "", texteGpu = "", texteRam = "";
        private PointCollection points = new PointCollection();

        public string TexteFps { get { return texteFps; } private set { Set(ref texteFps, value); } }
        public string TexteDetails { get { return texteDetails; } private set { Set(ref texteDetails, value); } }
        public string TexteProcessus { get { return texteProcessus; } private set { Set(ref texteProcessus, value); } }
        public string TexteCpu { get { return texteCpu; } private set { Set(ref texteCpu, value); } }
        public string TexteGpu { get { return texteGpu; } private set { Set(ref texteGpu, value); } }
        public string TexteRam { get { return texteRam; } private set { Set(ref texteRam, value); } }
        public PointCollection PointsCourbe { get { return points; } private set { points = value; Raise(); } }
        public bool AfficherCourbe { get { return config.Fps && config.Courbe; } }
        public bool AfficherCharge { get { return config.Cpu || config.Gpu || config.Ram; } }
        public double Echelle { get { return config.EchelleEffective; } }

        public Brush BrosseTexte
        {
            get
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(config.Couleur));
                b.Freeze(); return b;
            }
        }

        public Brush BrosseFond
        {
            get
            {
                var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(config.OpaciteFond * 255), 0x10, 0x10, 0x14));
                b.Freeze(); return b;
            }
        }

        /// <summary>Met à jour les textes et la courbe à partir d'un relevé (appelé par le minuteur ; public pour les tests).</summary>
        public void Rafraichir(ReleveCompteur r)
        {
            if (r == null) return;
            var m = r.Images;
            TexteFps = m == null ? "—" : Math.Round(m.Fps).ToString("0", Fr);
            // Largeurs fixes (police à chasse fixe) : le compteur ne « danse » pas quand les chiffres changent.
            TexteDetails = m == null ? "" : m.TempsImageMs.ToString("0.0", Fr).PadLeft(5) + " ms" + (m.UnPourcentBas.HasValue ? "  ·  1% bas " + Math.Round(m.UnPourcentBas.Value).ToString("0", Fr).PadLeft(3) : "");
            TexteProcessus = m == null ? (string.IsNullOrEmpty(r.Processus) ? "" : r.Processus + " : aucune image")
                           : r.Processus + (string.IsNullOrEmpty(m.Source) ? "" : " · " + m.Source);
            var c = r.Charge ?? new ChargeSysteme();
            TexteCpu = Pourcent("CPU", c.Cpu);
            TexteGpu = Pourcent("GPU", c.Gpu);
            TexteRam = Pourcent("RAM", c.Ram) + (c.Ram.HasValue && c.RamTotaleGo > 0 ? "  " + c.RamUtiliseeGo.ToString("0.0", Fr) + "/" + c.RamTotaleGo.ToString("0", Fr) + " Go" : "");

            historique.Add(m == null ? double.NaN : m.Fps);
            var max = config.DureeCourbe * RelevesParSeconde;
            if (historique.Count > CompteurConfig.DureesCourbe.Max() * RelevesParSeconde) historique.RemoveAt(0);
            PointsCourbe = CalculerCourbe(historique.Skip(Math.Max(0, historique.Count - max)).ToList(), max);
            Raise("Erreur");
        }

        private static string Pourcent(string nom, double? v) { return nom + " " + (v.HasValue ? Math.Round(v.Value).ToString("0", Fr).PadLeft(3) + " %" : "  —  "); }

        /// <summary>
        /// Points de la courbe des FPS : les valeurs les plus récentes à droite, sur <paramref name="capacite"/> relevés ;
        /// l'échelle va de 0 au plus haut FPS de la période (au moins 60). Un relevé sans image vaut 0.
        /// </summary>
        public static PointCollection CalculerCourbe(IReadOnlyList<double> valeurs, int capacite)
        {
            var pc = new PointCollection();
            if (valeurs.Count == 0 || capacite < 2) { pc.Freeze(); return pc; }
            var haut = Math.Max(60, valeurs.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max() * 1.1);
            var pas = LargeurCourbe / (capacite - 1);
            var debut = capacite - valeurs.Count;
            for (int i = 0; i < valeurs.Count; i++)
            {
                var v = double.IsNaN(valeurs[i]) ? 0 : valeurs[i];
                pc.Add(new Point((debut + i) * pas, HauteurCourbe - Math.Min(1, v / haut) * HauteurCourbe));
            }
            pc.Freeze();
            return pc;
        }

        // ---------- Démarrage, arrêt, enregistrement ----------

        private void DemarrerSource()
        {
            if (sourceDemarree) return;
            MajChoixSource();
            source.Demarrer();
            sourceDemarree = true;
            historique.Clear();
            minuteur.Start();
            Raise("Erreur");
        }

        private void ArreterSource()
        {
            if (!sourceDemarree) return;
            minuteur.Stop();
            source.Arreter();
            sourceDemarree = false;
            historique.Clear();
            Raise("Erreur");
        }

        private void MajChoixSource() { source.Choisir(config.Cpu, config.Gpu, config.Ram); }

        private void Modifie(bool disposition)
        {
            enregistrement.Stop(); enregistrement.Start();
            if (disposition) { var h = AffichageChange; if (h != null) h(); }
        }

        /// <summary>Enregistre les réglages tout de suite (fermeture de l'application).</summary>
        public void Enregistrer()
        {
            enregistrement.Stop();
            try { config.Cloner().Enregistrer(machineDir); }
            catch (Exception ex) { TraceAppli.Erreur("réglages du compteur en jeu non enregistrés", ex); }
        }

        public void Dispose()
        {
            Enregistrer();
            ArreterSource();
        }
    }
}
