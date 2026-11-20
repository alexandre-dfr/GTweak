using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GamingTweaker.Core
{
    /// <summary>Coin de l'écran principal, ou position libre (déplacée à la souris, sur n'importe quel écran).</summary>
    public enum CoinEcran { HautGauche, HautDroite, BasGauche, BasDroite, Libre }

    /// <summary>Taille prédéfinie, ou personnalisée (redimensionnée à la souris : voir <see cref="CompteurConfig.Echelle"/>).</summary>
    /// <summary>Éléments du compteur dont la couleur se règle séparément (« Tout » : la couleur commune).</summary>
    public enum ElementCompteur { Tout, Fps, Details, Courbe, Cpu, Gpu, Ram }

    public enum TailleCompteur { Petite, Normale, Grande, Personnalisee }

    /// <summary>
    /// Réglages du compteur en jeu (FPS, processeur, carte graphique, mémoire), enregistrés dans le dossier de la machine.
    /// Désactivé par défaut. Toute valeur invalide du fichier est ramenée à sa valeur par défaut (<see cref="Normaliser"/>).
    /// </summary>
    public class CompteurConfig
    {
        public const string NomFichier = "compteur.json";
        public const string CouleurParDefaut = "#4CE07A";
        public static readonly int[] DureesCourbe = { 10, 30, 60 };
        private static readonly Regex Hex = new Regex(@"^#[0-9A-Fa-f]{6}\z", RegexOptions.CultureInvariant);

        [JsonProperty("actif")] public bool Actif { get; set; }
        [JsonProperty("position")] [JsonConverter(typeof(StringEnumConverter))] public CoinEcran Position { get; set; } = CoinEcran.HautDroite;
        /// <summary>Position libre : coin haut-gauche du compteur, en pixels logiques du bureau (tous écrans).</summary>
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("couleur")] public string Couleur { get; set; } = CouleurParDefaut;
        /// <summary>Couleur propre à un élément (#RRVVBB) ; absente ou vide : la couleur commune <see cref="Couleur"/>.</summary>
        [JsonProperty("couleurs")] public System.Collections.Generic.Dictionary<string, string> Couleurs { get; set; } = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>Couleur effective d'un élément.</summary>
        public string CouleurDe(ElementCompteur e)
        {
            string c;
            if (e != ElementCompteur.Tout && Couleurs != null && Couleurs.TryGetValue(e.ToString(), out c) && CouleurValide(c)) return c.Trim().ToUpperInvariant();
            return Couleur;
        }

        /// <summary>Fixe la couleur d'un élément ; « Tout » fixe la couleur commune et retire les couleurs propres.</summary>
        public void DefinirCouleur(ElementCompteur e, string c)
        {
            if (!CouleurValide(c)) return;
            c = c.Trim().ToUpperInvariant();
            if (Couleurs == null) Couleurs = new System.Collections.Generic.Dictionary<string, string>();
            if (e == ElementCompteur.Tout) { Couleur = c; Couleurs.Clear(); }
            else if (c == Couleur) Couleurs.Remove(e.ToString());
            else Couleurs[e.ToString()] = c;
        }
        [JsonProperty("taille")] [JsonConverter(typeof(StringEnumConverter))] public TailleCompteur Taille { get; set; } = TailleCompteur.Normale;
        /// <summary>Échelle personnalisée (0,6 à 3), utilisée quand <see cref="Taille"/> vaut Personnalisee.</summary>
        [JsonProperty("echelle")] public double Echelle { get; set; } = 1.0;

        public const double EchelleMin = 0.6, EchelleMax = 3.0;

        /// <summary>Échelle effective : celle de la taille prédéfinie, ou l'échelle personnalisée.</summary>
        public double EchelleEffective
        {
            get
            {
                switch (Taille)
                {
                    case TailleCompteur.Petite: return 0.85;
                    case TailleCompteur.Grande: return 1.3;
                    case TailleCompteur.Personnalisee: return Echelle;
                    default: return 1.0;
                }
            }
        }
        /// <summary>Opacité du fond (0,2 à 1).</summary>
        [JsonProperty("opaciteFond")] public double OpaciteFond { get; set; } = 0.55;
        /// <summary>Raccourci global : modificateurs (Alt = 1, Ctrl = 2, Maj = 4) et touche (code virtuel Windows). Par défaut Ctrl+Maj+F11.</summary>
        [JsonProperty("raccourciModificateurs")] public uint RaccourciModificateurs { get; set; } = RaccourciModificateursParDefaut;
        [JsonProperty("raccourciTouche")] public uint RaccourciTouche { get; set; } = RaccourciToucheParDefaut;
        public const uint RaccourciModificateursParDefaut = 2 | 4, RaccourciToucheParDefaut = 0x7A;
        /// <summary>Raccourci global qui démarre ou arrête l'enregistrement d'une partie. Par défaut Ctrl+Maj+F10.</summary>
        [JsonProperty("raccourciEnregistrementModificateurs")] public uint RaccourciEnregistrementModificateurs { get; set; } = RaccourciModificateursParDefaut;
        [JsonProperty("raccourciEnregistrementTouche")] public uint RaccourciEnregistrementTouche { get; set; } = RaccourciEnregistrementToucheParDefaut;
        public const uint RaccourciEnregistrementToucheParDefaut = 0x79;

        /// <summary>
        /// Combinaison acceptée : au moins Ctrl ou Alt (une touche seule ou Maj+touche gênerait le jeu), une touche qui n'est pas un modificateur,
        /// ni la touche Windows, ni F12 (réservée au débogueur par Windows). Renvoie la raison du refus, ou null.
        /// </summary>
        public static string RefusRaccourci(uint modificateurs, uint touche)
        {
            if ((modificateurs & ~7u) != 0) return "Modificateurs inconnus.";
            if ((modificateurs & 3u) == 0) return "La combinaison doit contenir Ctrl ou Alt, pour ne pas gêner le jeu.";
            if (touche < 0x08 || touche > 0xFE) return "Touche non prise en charge.";
            if (touche == 0x10 || touche == 0x11 || touche == 0x12 || (touche >= 0xA0 && touche <= 0xA5)) return "Ajoutez une touche en plus de Ctrl, Alt ou Maj.";
            if (touche == 0x5B || touche == 0x5C) return "La touche Windows n'est pas utilisable.";
            if (touche == 0x7B) return "F12 est réservée par Windows : choisissez une autre touche.";
            return null;
        }

        [JsonProperty("fps")] public bool Fps { get; set; } = true;
        /// <summary>Temps d'image (ms) et 1 % low à côté des FPS.</summary>
        [JsonProperty("details")] public bool Details { get; set; } = true;
        [JsonProperty("courbe")] public bool Courbe { get; set; } = true;
        /// <summary>Durée affichée par la courbe des FPS, en secondes (10, 30 ou 60).</summary>
        [JsonProperty("dureeCourbe")] public int DureeCourbe { get; set; } = 30;
        [JsonProperty("cpu")] public bool Cpu { get; set; } = true;
        [JsonProperty("gpu")] public bool Gpu { get; set; } = true;
        [JsonProperty("ram")] public bool Ram { get; set; } = true;
        /// <summary>Températures affichées sur les lignes CPU et GPU (désactivées par défaut).</summary>
        [JsonProperty("tempCpu")] public bool TempCpu { get; set; }
        [JsonProperty("tempGpu")] public bool TempGpu { get; set; }

        public static bool CouleurValide(string c) { return c != null && Hex.IsMatch(c.Trim()); }

        /// <summary>Ramène chaque valeur hors bornes (fichier modifié à la main, version plus ancienne) à une valeur sûre.</summary>
        public CompteurConfig Normaliser()
        {
            Couleur = CouleurValide(Couleur) ? Couleur.Trim().ToUpperInvariant() : CouleurParDefaut;
            var propres = new System.Collections.Generic.Dictionary<string, string>();
            if (Couleurs != null)
                foreach (var kv in Couleurs)
                {
                    ElementCompteur el;
                    if (kv.Key != null && Enum.TryParse(kv.Key, false, out el) && el != ElementCompteur.Tout && CouleurValide(kv.Value))
                        propres[el.ToString()] = kv.Value.Trim().ToUpperInvariant();
                }
            Couleurs = propres;
            if (!Enum.IsDefined(typeof(CoinEcran), Position)) Position = CoinEcran.HautDroite;
            if (!Enum.IsDefined(typeof(TailleCompteur), Taille)) Taille = TailleCompteur.Normale;
            if (double.IsNaN(Echelle) || double.IsInfinity(Echelle)) Echelle = 1.0;
            Echelle = Math.Round(Math.Max(EchelleMin, Math.Min(EchelleMax, Echelle)), 2);
            if (double.IsNaN(OpaciteFond) || double.IsInfinity(OpaciteFond)) OpaciteFond = 0.55;
            OpaciteFond = Math.Max(0.2, Math.Min(1.0, OpaciteFond));
            if (Array.IndexOf(DureesCourbe, DureeCourbe) < 0) DureeCourbe = 30;
            if (RefusRaccourci(RaccourciModificateurs, RaccourciTouche) != null) { RaccourciModificateurs = RaccourciModificateursParDefaut; RaccourciTouche = RaccourciToucheParDefaut; }
            if (RefusRaccourci(RaccourciEnregistrementModificateurs, RaccourciEnregistrementTouche) != null
                || (RaccourciEnregistrementModificateurs == RaccourciModificateurs && RaccourciEnregistrementTouche == RaccourciTouche))
            {
                RaccourciEnregistrementModificateurs = RaccourciModificateursParDefaut; RaccourciEnregistrementTouche = RaccourciEnregistrementToucheParDefaut;
                // Le raccourci du compteur occupe déjà Ctrl+Maj+F10 : F9 à la place.
                if (RaccourciModificateurs == RaccourciModificateursParDefaut && RaccourciTouche == RaccourciEnregistrementToucheParDefaut) RaccourciEnregistrementTouche = 0x78;
            }
            if (double.IsNaN(X) || double.IsInfinity(X) || Math.Abs(X) > 100000) X = 0;
            if (double.IsNaN(Y) || double.IsInfinity(Y) || Math.Abs(Y) > 100000) Y = 0;
            return this;
        }

        public CompteurConfig Cloner()
        {
            var c = (CompteurConfig)MemberwiseClone();
            c.Couleurs = new System.Collections.Generic.Dictionary<string, string>(Couleurs ?? new System.Collections.Generic.Dictionary<string, string>());
            return c;
        }

        public static CompteurConfig Charger(string machineDir)
        {
            try
            {
                var f = Path.Combine(machineDir ?? "", NomFichier);
                if (string.IsNullOrEmpty(machineDir) || !File.Exists(f)) return new CompteurConfig();
                var c = JsonConvert.DeserializeObject<CompteurConfig>(File.ReadAllText(f, Encoding.UTF8)) ?? new CompteurConfig();
                return c.Normaliser();
            }
            catch (Exception ex)
            {
                TraceAppli.Erreur("réglages du compteur en jeu illisibles, valeurs par défaut", ex);
                return new CompteurConfig();
            }
        }

        /// <summary>Écriture atomique (fichier temporaire puis remplacement).</summary>
        public void Enregistrer(string machineDir)
        {
            if (string.IsNullOrEmpty(machineDir)) return;
            Directory.CreateDirectory(machineDir);
            var f = Path.Combine(machineDir, NomFichier);
            var tmp = f + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(Normaliser(), Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
        }
    }
}
