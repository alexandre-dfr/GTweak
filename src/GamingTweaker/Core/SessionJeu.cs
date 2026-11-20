using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    /// <summary>Bilan d'une session de jeu enregistrée avec le compteur (fichier .json à côté du rapport .txt).</summary>
    public class ResumeSession
    {
        [JsonProperty("jeu")] public string Jeu { get; set; } = "";
        [JsonProperty("debut")] public DateTime Debut { get; set; }
        [JsonProperty("dureeSec")] public double DureeSec { get; set; }
        [JsonProperty("source")] public string Source { get; set; } = "";
        [JsonProperty("images")] public int Images { get; set; }
        [JsonProperty("fpsMoyen")] public double FpsMoyen { get; set; }
        /// <summary>FPS du 99e centile du temps d'image sur toute la session.</summary>
        [JsonProperty("unPourcentBas")] public double UnPourcentBas { get; set; }
        [JsonProperty("unPourMilleBas")] public double UnPourMilleBas { get; set; }
        [JsonProperty("tempsImageMedianMs")] public double TempsImageMedianMs { get; set; }
        [JsonProperty("tempsImageP99Ms")] public double TempsImageP99Ms { get; set; }
        [JsonProperty("tempsImageMaxMs")] public double TempsImageMaxMs { get; set; }
        /// <summary>Images au moins deux fois et demie plus longues que la médiane, et d'au moins 25 ms : saccades visibles.</summary>
        [JsonProperty("saccades")] public int Saccades { get; set; }
        [JsonProperty("cpuMoyen")] public double? CpuMoyen { get; set; }
        [JsonProperty("gpuMoyen")] public double? GpuMoyen { get; set; }
        [JsonProperty("ramMoyenne")] public double? RamMoyenne { get; set; }
        [JsonProperty("tempCpuMax")] public double? TempCpuMax { get; set; }
        [JsonProperty("tempGpuMax")] public double? TempGpuMax { get; set; }
        /// <summary>Version de GamingTweaker, pour relier la session aux optimisations faites entre deux parties.</summary>
        [JsonProperty("version")] public string Version { get; set; } = "";

        [JsonIgnore] public string Fichier { get; set; }
    }

    /// <summary>
    /// Enregistrement d'une partie : tous les écarts entre images du jeu suivi (pas seulement les relevés de l'affichage), plus la charge
    /// relevée deux fois par seconde. Le jeu suivi est le premier processus au premier plan qui affiche des images ; tant qu'un autre
    /// est au premier plan (bureau, Alt+Tab), rien n'est compté et la pause n'est pas prise pour une saccade.
    /// </summary>
    public class EnregistreurSession
    {
        /// <summary>Un écart plus long est une pause (chargement, menu, fenêtre réduite), pas une image.</summary>
        public const double PauseSecondes = 1.0;
        /// <summary>Au-delà, l'enregistrement s'arrête seul (mémoire : quelques Mo par heure).</summary>
        public const double DureeMaxSecondes = 4 * 3600;

        private readonly List<double> ecarts = new List<double>();
        private readonly List<double> cpu = new List<double>(), gpu = new List<double>(), ram = new List<double>();
        private double? tempCpuMax, tempGpuMax;
        private double dernierInstant = double.NaN;
        private string source = "";

        public DateTime Debut { get; private set; }
        public string Jeu { get; private set; } = "";
        public int Images { get { return ecarts.Count; } }
        public double DureeImagesSec { get { return ecarts.Sum(); } }

        public EnregistreurSession(DateTime debut) { Debut = debut; }

        public void Ajouter(ReleveCompteur r)
        {
            if (r == null) return;
            var m = r.Images;
            var auPremierPlan = m != null && !string.IsNullOrEmpty(r.Processus) && (Jeu.Length == 0 || string.Equals(r.Processus, Jeu, StringComparison.OrdinalIgnoreCase));
            if (!auPremierPlan) { dernierInstant = double.NaN; return; }
            if (Jeu.Length == 0) Jeu = r.Processus;
            if (!string.IsNullOrEmpty(m.Source) && m.Source != source) { if (source.Length > 0) dernierInstant = double.NaN; source = m.Source; }
            if (m.Instants != null)
                foreach (var t in m.Instants)
                {
                    if (!double.IsNaN(dernierInstant) && t <= dernierInstant) continue;
                    if (!double.IsNaN(dernierInstant))
                    {
                        var e = t - dernierInstant;
                        if (e > 0 && e < PauseSecondes) ecarts.Add(e);
                    }
                    dernierInstant = t;
                }
            var c = r.Charge;
            if (c != null)
            {
                if (c.Cpu.HasValue) cpu.Add(c.Cpu.Value);
                if (c.Gpu.HasValue) gpu.Add(c.Gpu.Value);
                if (c.Ram.HasValue) ram.Add(c.Ram.Value);
                if (c.TempCpu.HasValue) tempCpuMax = Math.Max(tempCpuMax ?? double.MinValue, c.TempCpu.Value);
                if (c.TempGpu.HasValue) tempGpuMax = Math.Max(tempGpuMax ?? double.MinValue, c.TempGpu.Value);
            }
        }

        public bool Plein { get { return DureeImagesSec >= DureeMaxSecondes; } }

        /// <summary>Bilan de la session (null : moins de 100 images, rien à comparer).</summary>
        public ResumeSession Terminer(DateTime fin)
        {
            var r = SessionsJeu.Calculer(ecarts);
            if (r == null) return null;
            r.Jeu = Jeu; r.Debut = Debut; r.DureeSec = Math.Max(0, (fin - Debut).TotalSeconds); r.Source = source;
            r.CpuMoyen = cpu.Count == 0 ? (double?)null : cpu.Average();
            r.GpuMoyen = gpu.Count == 0 ? (double?)null : gpu.Average();
            r.RamMoyenne = ram.Count == 0 ? (double?)null : ram.Average();
            r.TempCpuMax = tempCpuMax; r.TempGpuMax = tempGpuMax;
            return r;
        }

        /// <summary>Temps d'image en millisecondes, dans l'ordre (fichier .csv).</summary>
        public IReadOnlyList<double> TempsImagesMs { get { return ecarts.Select(e => e * 1000).ToList(); } }
    }

    /// <summary>Calculs, rapports et dossier des sessions de jeu (Documents\GamingTweaker\Sessions de jeu).</summary>
    public static class SessionsJeu
    {
        public const int MinimumImages = 100;
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static string DossierParDefaut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GamingTweaker", "Sessions de jeu"); }
        }

        /// <summary>Statistiques sur les écarts entre images (secondes) ; null sous <see cref="MinimumImages"/> images.</summary>
        public static ResumeSession Calculer(IReadOnlyList<double> ecartsSec)
        {
            if (ecartsSec == null || ecartsSec.Count < MinimumImages) return null;
            var tries = ecartsSec.Where(e => e > 0).OrderBy(e => e).ToArray();
            if (tries.Length < MinimumImages) return null;
            Func<double, double> centile = p => tries[Math.Min(tries.Length - 1, Math.Max(0, (int)Math.Ceiling(p * tries.Length) - 1))];
            var total = tries.Sum();
            var mediane = centile(0.5);
            var seuilSaccade = Math.Max(0.025, 2.5 * mediane);
            return new ResumeSession
            {
                Images = tries.Length,
                FpsMoyen = tries.Length / total,
                UnPourcentBas = 1.0 / centile(0.99),
                UnPourMilleBas = 1.0 / centile(0.999),
                TempsImageMedianMs = mediane * 1000,
                TempsImageP99Ms = centile(0.99) * 1000,
                TempsImageMaxMs = tries[tries.Length - 1] * 1000,
                Saccades = tries.Count(e => e >= seuilSaccade),
                Version = VersionAppli.Texte,
            };
        }

        /// <summary>« +7,7 % » ; null si la référence est nulle.</summary>
        public static string Ecart(double avant, double apres)
        {
            if (avant <= 0) return null;
            var p = (apres - avant) / avant * 100;
            return (p >= 0 ? "+" : "") + p.ToString("0.0", Fr) + " %";
        }

        /// <summary>Lignes de comparaison avec la session précédente du même jeu (pour le rapport et la notification).</summary>
        public static List<string> Comparer(ResumeSession avant, ResumeSession apres)
        {
            var l = new List<string>();
            if (avant == null || apres == null) return l;
            Action<string, double, double, string> ligne = (nom, a, b, format) =>
                l.Add(nom.PadRight(22) + a.ToString(format, Fr).PadLeft(8) + "  →  " + b.ToString(format, Fr).PadLeft(8) + "   " + (Ecart(a, b) ?? ""));
            ligne("FPS moyen", avant.FpsMoyen, apres.FpsMoyen, "0.0");
            ligne("1 % bas (FPS)", avant.UnPourcentBas, apres.UnPourcentBas, "0.0");
            ligne("0,1 % bas (FPS)", avant.UnPourMilleBas, apres.UnPourMilleBas, "0.0");
            ligne("Temps d'image médian", avant.TempsImageMedianMs, apres.TempsImageMedianMs, "0.00");
            ligne("Temps d'image 99 %", avant.TempsImageP99Ms, apres.TempsImageP99Ms, "0.00");
            l.Add("Saccades".PadRight(22) + avant.Saccades.ToString(Fr).PadLeft(8) + "  →  " + apres.Saccades.ToString(Fr).PadLeft(8));
            return l;
        }

        /// <summary>Phrase courte pour la notification de fin d'enregistrement.</summary>
        public static string Synthese(ResumeSession r, ResumeSession precedente)
        {
            var s = Math.Round(r.FpsMoyen).ToString("0", Fr) + " FPS en moyenne, 1 % bas " + Math.Round(r.UnPourcentBas).ToString("0", Fr);
            if (precedente != null)
                s += " (" + Ecart(precedente.FpsMoyen, r.FpsMoyen) + " de FPS moyen, " + Ecart(precedente.UnPourcentBas, r.UnPourcentBas) + " de 1 % bas par rapport à la partie précédente)";
            return s + ".";
        }

        public static string Rapport(ResumeSession r, ResumeSession precedente)
        {
            var sb = new StringBuilder();
            Func<double?, string, string> pc = (v, u) => v.HasValue ? v.Value.ToString("0", Fr) + u : "—";
            sb.AppendLine("GamingTweaker : session de jeu");
            sb.AppendLine(new string('=', 40));
            sb.AppendLine("Jeu                    " + r.Jeu + (string.IsNullOrEmpty(r.Source) ? "" : " (" + r.Source + ")"));
            sb.AppendLine("Début                  " + r.Debut.ToString("dddd d MMMM yyyy HH:mm", Fr));
            sb.AppendLine("Durée                  " + TimeSpan.FromSeconds(Math.Round(r.DureeSec)).ToString(@"h\:mm\:ss"));
            sb.AppendLine("Images                 " + r.Images.ToString("N0", Fr));
            sb.AppendLine();
            sb.AppendLine("FPS moyen              " + r.FpsMoyen.ToString("0.0", Fr));
            sb.AppendLine("1 % bas                " + r.UnPourcentBas.ToString("0.0", Fr) + " FPS");
            sb.AppendLine("0,1 % bas              " + r.UnPourMilleBas.ToString("0.0", Fr) + " FPS");
            sb.AppendLine("Temps d'image médian   " + r.TempsImageMedianMs.ToString("0.00", Fr) + " ms");
            sb.AppendLine("Temps d'image 99 %     " + r.TempsImageP99Ms.ToString("0.00", Fr) + " ms");
            sb.AppendLine("Temps d'image max      " + r.TempsImageMaxMs.ToString("0.0", Fr) + " ms");
            sb.AppendLine("Saccades               " + r.Saccades.ToString(Fr) + " (images d'au moins 25 ms et 2,5 fois la médiane)");
            sb.AppendLine();
            sb.AppendLine("Processeur (moyenne)   " + pc(r.CpuMoyen, " %") + (r.TempCpuMax.HasValue ? "   max " + pc(r.TempCpuMax, " °C") : ""));
            sb.AppendLine("Carte graph. (moyenne) " + pc(r.GpuMoyen, " %") + (r.TempGpuMax.HasValue ? "   max " + pc(r.TempGpuMax, " °C") : ""));
            sb.AppendLine("Mémoire (moyenne)      " + pc(r.RamMoyenne, " %"));
            sb.AppendLine();
            if (precedente != null)
            {
                sb.AppendLine("Comparaison avec la partie précédente (" + precedente.Debut.ToString("d MMMM yyyy HH:mm", Fr) + ", " + TimeSpan.FromSeconds(Math.Round(precedente.DureeSec)).ToString(@"h\:mm\:ss") + ")");
                sb.AppendLine(new string('-', 40));
                foreach (var l in Comparer(precedente, r)) sb.AppendLine(l);
                sb.AppendLine();
            }
            else sb.AppendLine("Première partie enregistrée pour ce jeu : la prochaine sera comparée à celle-ci.");
            sb.AppendLine("Pour comparer, jouez la même scène, à la même résolution et avec les mêmes réglages graphiques.");
            sb.AppendLine("Le détail image par image est dans le fichier .csv de même nom (ouvrable dans Excel). L'évolution de toutes les parties est dans historique.csv.");
            return sb.ToString();
        }

        /// <summary>Nom de fichier sûr : « 2026-11-20 21h05 - eldenring ».</summary>
        public static string NomFichier(ResumeSession r)
        {
            var jeu = new string((r.Jeu ?? "jeu").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            if (jeu.Length == 0) jeu = "jeu";
            if (jeu.Length > 60) jeu = jeu.Substring(0, 60);
            return r.Debut.ToString("yyyy-MM-dd HH'h'mm", CultureInfo.InvariantCulture) + " - " + jeu;
        }

        /// <summary>Sessions enregistrées, de la plus récente à la plus ancienne.</summary>
        public static List<ResumeSession> Lister(string dossier = null)
        {
            dossier = dossier ?? DossierParDefaut;
            var res = new List<ResumeSession>();
            if (!Directory.Exists(dossier)) return res;
            foreach (var f in Directory.GetFiles(dossier, "*.json"))
                try
                {
                    var r = JsonConvert.DeserializeObject<ResumeSession>(File.ReadAllText(f, Encoding.UTF8));
                    if (r == null || r.Images <= 0) continue;
                    r.Fichier = f; res.Add(r);
                }
                catch (Exception ex) { TraceAppli.Erreur("session de jeu illisible : " + f, ex); }
            return res.OrderByDescending(r => r.Debut).ToList();
        }

        /// <summary>Dernière session du même jeu avant celle-ci.</summary>
        public static ResumeSession Precedente(IEnumerable<ResumeSession> sessions, ResumeSession r)
        {
            return (sessions ?? Enumerable.Empty<ResumeSession>())
                .Where(s => s != r && string.Equals(s.Jeu, r.Jeu, StringComparison.OrdinalIgnoreCase) && s.Debut < r.Debut)
                .OrderByDescending(s => s.Debut).FirstOrDefault();
        }

        public const string EnteteHistorique = "Date;Jeu;Durée (min);Images;FPS moyen;1 % bas;0,1 % bas;Temps d'image médian (ms);Temps d'image 99 % (ms);Saccades;CPU moyen (%);GPU moyen (%);RAM moyenne (%);Version";

        /// <summary>Ligne de historique.csv (séparateur « ; » et virgule décimale : ouvert tel quel par Excel en français).</summary>
        public static string LigneHistorique(ResumeSession r)
        {
            Func<double?, string> n = v => v.HasValue ? v.Value.ToString("0.0", Fr) : "";
            return string.Join(";", new[]
            {
                r.Debut.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), (r.Jeu ?? "").Replace(";", ","), (r.DureeSec / 60).ToString("0.0", Fr),
                r.Images.ToString(CultureInfo.InvariantCulture), n(r.FpsMoyen), n(r.UnPourcentBas), n(r.UnPourMilleBas), r.TempsImageMedianMs.ToString("0.00", Fr),
                r.TempsImageP99Ms.ToString("0.00", Fr), r.Saccades.ToString(CultureInfo.InvariantCulture), n(r.CpuMoyen), n(r.GpuMoyen), n(r.RamMoyenne), r.Version ?? "",
            });
        }

        /// <summary>Écrit le bilan (.json), le rapport (.txt), les temps d'image (.csv) et une ligne de historique.csv ; renvoie le chemin du rapport.</summary>
        public static string Enregistrer(ResumeSession r, IReadOnlyList<double> tempsImagesMs, ResumeSession precedente, string dossier = null)
        {
            dossier = dossier ?? DossierParDefaut;
            Directory.CreateDirectory(dossier);
            var nom = NomFichier(r);
            var baseChemin = Path.Combine(dossier, nom);
            for (int i = 2; File.Exists(baseChemin + ".json"); i++) baseChemin = Path.Combine(dossier, nom + " (" + i + ")");
            var utf8Bom = new UTF8Encoding(true);
            File.WriteAllText(baseChemin + ".json", JsonConvert.SerializeObject(r, Formatting.Indented), new UTF8Encoding(false));
            File.WriteAllText(baseChemin + ".txt", Rapport(r, precedente), utf8Bom);
            var csv = new StringBuilder("Image;Temps d'image (ms)\r\n");
            if (tempsImagesMs != null)
                for (int i = 0; i < tempsImagesMs.Count; i++) csv.Append(i + 1).Append(';').Append(tempsImagesMs[i].ToString("0.000", Fr)).Append("\r\n");
            File.WriteAllText(baseChemin + ".csv", csv.ToString(), utf8Bom);
            var hist = Path.Combine(dossier, "historique.csv");
            if (!File.Exists(hist)) File.WriteAllText(hist, EnteteHistorique + "\r\n", utf8Bom);
            File.AppendAllText(hist, LigneHistorique(r) + "\r\n", Encoding.UTF8);
            r.Fichier = baseChemin + ".json";
            TraceAppli.Ecrire("session de jeu enregistrée : " + Path.GetFileName(baseChemin));
            return baseChemin + ".txt";
        }
    }
}
