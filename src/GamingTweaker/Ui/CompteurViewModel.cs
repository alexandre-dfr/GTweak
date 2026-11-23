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
        /// <summary>Texte d'une combinaison (« Ctrl+Maj+F11 »).</summary>
        public static string TexteCombinaison(uint modificateurs, uint touche)
        {
            var p = new List<string>();
            if ((modificateurs & 2) != 0) p.Add("Ctrl");
            if ((modificateurs & 1) != 0) p.Add("Alt");
            if ((modificateurs & 4) != 0) p.Add("Maj");
            p.Add(NomTouche(touche));
            return string.Join("+", p);
        }

        public static string NomTouche(uint vk)
        {
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "Pavé " + (vk - 0x60);
            switch (vk)
            {
                case 0x20: return "Espace";
                case 0x2D: return "Inser";
                case 0x2E: return "Suppr";
                case 0x24: return "Origine";
                case 0x23: return "Fin";
                case 0x21: return "Page préc.";
                case 0x22: return "Page suiv.";
                case 0x13: return "Pause";
                case 0x91: return "Arrêt défil.";
            }
            try { return System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)vk).ToString(); }
            catch (Exception) { return "Touche " + vk; }
        }

        /// <summary>Raccourci actuel (« Ctrl+Maj+F11 »).</summary>
        public string Raccourci { get { return TexteCombinaison(config.RaccourciModificateurs, config.RaccourciTouche); } }
        public uint RaccourciModificateurs { get { return config.RaccourciModificateurs; } }
        public uint RaccourciTouche { get { return config.RaccourciTouche; } }

        private bool raccourciDisponible = true;
        /// <summary>Faux si Windows a refusé la combinaison (déjà prise par une autre application) ; renseigné par l'application.</summary>
        public bool RaccourciDisponible { get { return raccourciDisponible; } set { if (Set(ref raccourciDisponible, value)) { Raise("TexteRaccourci"); Raise("EtatBouton"); } } }
        public string TexteRaccourci { get { return Raccourci + (raccourciDisponible ? "" : " (indisponible : déjà utilisé par une autre application, choisissez-en un autre)"); } }

        /// <summary>La combinaison a changé : l'application la réenregistre auprès de Windows.</summary>
        public event Action RaccourciChange;

        /// <summary>Change le raccourci ; renvoie la raison d'un refus, ou null.</summary>
        public string DefinirRaccourci(uint modificateurs, uint touche)
        {
            var refus = CompteurConfig.RefusRaccourci(modificateurs, touche);
            if (refus != null) return refus;
            if (config.RaccourciModificateurs == modificateurs && config.RaccourciTouche == touche) return null;
            config.RaccourciModificateurs = modificateurs; config.RaccourciTouche = touche;
            Raise("Raccourci"); Raise("TexteRaccourci"); Raise("EtatBouton");
            Modifie(false);
            var h = RaccourciChange; if (h != null) h();
            return null;
        }
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
            // Réglages lus du fichier : le compteur démarre toujours masqué (bouton ou raccourci pour l'afficher pendant la session).
            if (config == null) { config = CompteurConfig.Charger(machineDir); config.Actif = false; }
            this.config = config.Normaliser();
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

        private ElementCompteur elementCouleur = ElementCompteur.Tout;
        /// <summary>Élément dont on règle la couleur (réglages) ; « Tout » : la même couleur partout.</summary>
        public ElementCompteur ElementCouleur { get { return elementCouleur; } set { if (Set(ref elementCouleur, value)) Raise("Couleur"); } }

        public IReadOnlyList<ChoixListe<ElementCompteur>> ElementsCouleur { get; } = new[]
        {
            new ChoixListe<ElementCompteur> { Valeur = ElementCompteur.Tout, Nom = "Tout le compteur" },
            new ChoixListe<ElementCompteur> { Valeur = ElementCompteur.Fps, Nom = "FPS" },
            new ChoixListe<ElementCompteur> { Valeur = ElementCompteur.Details, Nom = "Temps d'image et 1 % bas" },
            new ChoixListe<ElementCompteur> { Valeur = ElementCompteur.Courbe, Nom = "Courbe" },
            new ChoixListe<ElementCompteur> { Valeur = ElementCompteur.Cpu, Nom = "Processeur (CPU)" },
            new ChoixListe<ElementCompteur> { Valeur = ElementCompteur.Gpu, Nom = "Carte graphique (GPU)" },
            new ChoixListe<ElementCompteur> { Valeur = ElementCompteur.Ram, Nom = "Mémoire (RAM)" },
        };

        /// <summary>Couleur de l'élément choisi (<see cref="ElementCouleur"/>). Sur « Tout », elle s'applique partout.</summary>
        public string Couleur
        {
            get { return config.CouleurDe(elementCouleur); }
            set
            {
                if (!CompteurConfig.CouleurValide(value)) return;
                if (config.CouleurDe(elementCouleur) == value.Trim().ToUpperInvariant() && elementCouleur != ElementCompteur.Tout) return;
                config.DefinirCouleur(elementCouleur, value);
                Raise();
                foreach (var b in new[] { "BrosseTexte", "BrosseFps", "BrosseDetails", "BrosseCourbe", "BrosseCpu", "BrosseGpu", "BrosseRam" }) Raise(b);
                Modifie(false);
            }
        }

        /// <summary>Couleur effective d'un élément (tests).</summary>
        public string CouleurDe(ElementCompteur e) { return config.CouleurDe(e); }

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
        public bool TempCpu { get { return config.TempCpu; } set { if (config.TempCpu != value) { config.TempCpu = value; Raise(); MajChoixSource(); Modifie(false); } } }
        public bool TempGpu { get { return config.TempGpu; } set { if (config.TempGpu != value) { config.TempGpu = value; Raise(); MajChoixSource(); Modifie(false); } } }
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

        public Brush BrosseTexte { get { return Brosse(ElementCompteur.Tout); } }
        public Brush BrosseFps { get { return Brosse(ElementCompteur.Fps); } }
        public Brush BrosseDetails { get { return Brosse(ElementCompteur.Details); } }
        public Brush BrosseCourbe { get { return Brosse(ElementCompteur.Courbe); } }
        public Brush BrosseCpu { get { return Brosse(ElementCompteur.Cpu); } }
        public Brush BrosseGpu { get { return Brosse(ElementCompteur.Gpu); } }
        public Brush BrosseRam { get { return Brosse(ElementCompteur.Ram); } }

        private Brush Brosse(ElementCompteur e)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(config.CouleurDe(e)));
            b.Freeze(); return b;
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
            TexteCpu = Pourcent("CPU", c.Cpu) + (config.TempCpu ? Temperature(c.TempCpu) : "");
            TexteGpu = Pourcent("GPU", c.Gpu) + (config.TempGpu ? Temperature(c.TempGpu) : "");
            TexteRam = Pourcent("RAM", c.Ram) + (c.Ram.HasValue && c.RamTotaleGo > 0 ? "  " + c.RamUtiliseeGo.ToString("0.0", Fr) + "/" + c.RamTotaleGo.ToString("0", Fr) + " Go" : "");

            historique.Add(m == null ? double.NaN : m.Fps);
            var max = config.DureeCourbe * RelevesParSeconde;
            if (historique.Count > CompteurConfig.DureesCourbe.Max() * RelevesParSeconde) historique.RemoveAt(0);
            PointsCourbe = CalculerCourbe(historique.Skip(Math.Max(0, historique.Count - max)).ToList(), max);
            Raise("Erreur");
        }

        private static string Temperature(double? t) { return "  " + (t.HasValue ? Math.Round(t.Value).ToString("0", Fr).PadLeft(3) + " °C" : "  — °C"); }

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

        private void MajChoixSource()
        {
            source.Choisir(config.Cpu, config.Gpu, config.Ram);
            source.ChoisirTemperatures(config.Cpu && config.TempCpu, config.Gpu && config.TempGpu);
        }

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
