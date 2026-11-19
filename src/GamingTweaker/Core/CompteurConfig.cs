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
        [JsonProperty("fps")] public bool Fps { get; set; } = true;
        /// <summary>Temps d'image (ms) et 1 % low à côté des FPS.</summary>
        [JsonProperty("details")] public bool Details { get; set; } = true;
        [JsonProperty("courbe")] public bool Courbe { get; set; } = true;
        /// <summary>Durée affichée par la courbe des FPS, en secondes (10, 30 ou 60).</summary>
        [JsonProperty("dureeCourbe")] public int DureeCourbe { get; set; } = 30;
        [JsonProperty("cpu")] public bool Cpu { get; set; } = true;
        [JsonProperty("gpu")] public bool Gpu { get; set; } = true;
        [JsonProperty("ram")] public bool Ram { get; set; } = true;

        public static bool CouleurValide(string c) { return c != null && Hex.IsMatch(c.Trim()); }

        /// <summary>Ramène chaque valeur hors bornes (fichier modifié à la main, version plus ancienne) à une valeur sûre.</summary>
        public CompteurConfig Normaliser()
        {
            Couleur = CouleurValide(Couleur) ? Couleur.Trim().ToUpperInvariant() : CouleurParDefaut;
            if (!Enum.IsDefined(typeof(CoinEcran), Position)) Position = CoinEcran.HautDroite;
            if (!Enum.IsDefined(typeof(TailleCompteur), Taille)) Taille = TailleCompteur.Normale;
            if (double.IsNaN(Echelle) || double.IsInfinity(Echelle)) Echelle = 1.0;
            Echelle = Math.Round(Math.Max(EchelleMin, Math.Min(EchelleMax, Echelle)), 2);
            if (double.IsNaN(OpaciteFond) || double.IsInfinity(OpaciteFond)) OpaciteFond = 0.55;
            OpaciteFond = Math.Max(0.2, Math.Min(1.0, OpaciteFond));
            if (Array.IndexOf(DureesCourbe, DureeCourbe) < 0) DureeCourbe = 30;
            if (double.IsNaN(X) || double.IsInfinity(X) || Math.Abs(X) > 100000) X = 0;
            if (double.IsNaN(Y) || double.IsInfinity(Y) || Math.Abs(Y) > 100000) Y = 0;
            return this;
        }

        public CompteurConfig Cloner()
        {
            return (CompteurConfig)MemberwiseClone();
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
