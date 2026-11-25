using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    /// <summary>Mesure d'une partie : FPS et 1 % bas du jeu au premier plan, relevés par le compteur en jeu (ETW, rien d'injecté).</summary>
    public class MesureJeu
    {
        /// <summary>Début de la mesure, « yyyy-MM-ddTHH:mm:ss ».</summary>
        [JsonProperty("date")] public string Date { get; set; }
        /// <summary>Processus mesuré (sans « .exe ») ; vide si aucun jeu n'a affiché d'image.</summary>
        [JsonProperty("jeu")] public string Jeu { get; set; } = "";
        [JsonProperty("dureeSec")] public double DureeSec { get; set; }
        /// <summary>Part de la mesure (0 à 1) pendant laquelle le jeu était au premier plan et affichait des images.</summary>
        [JsonProperty("couverture")] public double Couverture { get; set; }
        [JsonProperty("fpsMoyen", NullValueHandling = NullValueHandling.Ignore)] public double? FpsMoyen { get; set; }
        /// <summary>Moyenne des « 1 % bas » (99e centile du temps d'image sur 10 s) relevés pendant la partie.</summary>
        [JsonProperty("unPourcentBas", NullValueHandling = NullValueHandling.Ignore)] public double? UnPourcentBas { get; set; }
        /// <summary>Pire « 1 % bas » relevé (la plus grosse saccade de la partie).</summary>
        [JsonProperty("pireBas", NullValueHandling = NullValueHandling.Ignore)] public double? PireBas { get; set; }
        [JsonProperty("cpu", NullValueHandling = NullValueHandling.Ignore)] public double? Cpu { get; set; }
        [JsonProperty("gpu", NullValueHandling = NullValueHandling.Ignore)] public double? Gpu { get; set; }
        /// <summary>« DirectX » ou « Vulkan/OpenGL ».</summary>
        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)] public string Source { get; set; }
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string Note { get; set; }

        public string Libelle
        {
            get
            {
                DateTime d;
                var date = DateTime.TryParseExact(Date ?? "", "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) : Date;
                if (!FpsMoyen.HasValue) return date + " : aucune image mesurée" + (string.IsNullOrEmpty(Note) ? "" : " (" + Note + ")");
                return date + " · " + Jeu + " : " + MesuresJeu.Nombre(FpsMoyen) + " FPS, 1 % bas " + MesuresJeu.Nombre(UnPourcentBas)
                     + (Couverture < MesuresJeu.CouvertureMinimale ? " (incomplète)" : "");
            }
        }
    }

    /// <summary>
    /// Mesure avant / après en jeu : la même scène, mesurée avant puis après l'optimisation. Relevé une fois par seconde du compteur en jeu
    /// (fenêtre au premier plan), après un délai pour revenir dans le jeu. Rangée dans MesuresJeu\ du dossier de la machine.
    /// </summary>
    public static class MesuresJeu
    {
        public const string Dossier = "MesuresJeu";
        public static readonly TimeSpan Attente = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan Duree = TimeSpan.FromSeconds(60);
        /// <summary>En dessous, la mesure est signalée incomplète (jeu quitté, menu, chargement).</summary>
        public const double CouvertureMinimale = 0.8;
        /// <summary>Écart relatif en dessous duquel deux mesures sont jugées identiques (bruit d'une partie à l'autre).</summary>
        public const double SeuilStable = 0.03;

        internal static string Nombre(double? v) { return v.HasValue ? Math.Round(v.Value).ToString("0", CultureInfo.CurrentCulture) : "n/d"; }

        /// <summary>
        /// Résume les relevés (un par seconde) : le jeu est le processus qui a affiché des images le plus souvent ; seuls ses relevés comptent.
        /// </summary>
        public static MesureJeu Agreger(IList<ReleveCompteur> releves, string date, double dureeSec)
        {
            var m = new MesureJeu { Date = date, DureeSec = dureeSec };
            var avecImages = (releves ?? new List<ReleveCompteur>()).Where(r => r != null && r.Images != null && r.Images.Fps > 0 && !string.IsNullOrEmpty(r.Processus)).ToList();
            int total = releves == null ? 0 : releves.Count;
            if (avecImages.Count == 0 || total == 0)
            {
                m.Note = "aucun jeu n'a affiché d'image au premier plan pendant la mesure";
                return m;
            }
            var jeu = avecImages.GroupBy(r => r.Processus, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).First();
            var l = jeu.ToList();
            m.Jeu = l[0].Processus;
            m.Couverture = Math.Round((double)l.Count / total, 2);
            m.FpsMoyen = Math.Round(l.Average(r => r.Images.Fps), 1);
            var bas = l.Where(r => r.Images.UnPourcentBas.HasValue).Select(r => r.Images.UnPourcentBas.Value).ToList();
            if (bas.Count > 0) { m.UnPourcentBas = Math.Round(bas.Average(), 1); m.PireBas = Math.Round(bas.Min(), 1); }
            var cpu = l.Where(r => r.Charge != null && r.Charge.Cpu.HasValue).Select(r => r.Charge.Cpu.Value).ToList();
            if (cpu.Count > 0) m.Cpu = Math.Round(cpu.Average(), 1);
            var gpu = l.Where(r => r.Charge != null && r.Charge.Gpu.HasValue).Select(r => r.Charge.Gpu.Value).ToList();
            if (gpu.Count > 0) m.Gpu = Math.Round(gpu.Average(), 1);
            m.Source = l.Select(r => r.Images.Source).Where(s => !string.IsNullOrEmpty(s)).GroupBy(s => s).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (m.Couverture < CouvertureMinimale)
                m.Note = "le jeu n'a été au premier plan que " + Math.Round(m.Couverture * 100) + " % du temps (menu, chargement ou autre fenêtre)";
            return m;
        }

        /// <summary>
        /// Attend <paramref name="attente"/> (le temps de revenir dans le jeu) puis relève le compteur une fois par seconde pendant
        /// <paramref name="duree"/>. La source est partagée avec la surimpression : elle n'est arrêtée que si elle ne tournait pas pour elle.
        /// <paramref name="etat"/> reçoit les secondes restantes (négatives pendant l'attente).
        /// </summary>
        public static Task<MesureJeu> EnregistrerAsync(ISourceCompteur source, TimeSpan attente, TimeSpan duree, IProgress<int> etat, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var date = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                var releves = new List<ReleveCompteur>();
                source.Demarrer();
                try
                {
                    var finAttente = DateTime.UtcNow + attente;
                    while (DateTime.UtcNow < finAttente)
                    {
                        if (etat != null) etat.Report(-(int)Math.Ceiling((finAttente - DateTime.UtcNow).TotalSeconds));
                        if (ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250))) ct.ThrowIfCancellationRequested();
                    }
                    date = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                    var fin = DateTime.UtcNow + duree;
                    var prochain = DateTime.UtcNow;
                    while (DateTime.UtcNow < fin)
                    {
                        var dodo = prochain - DateTime.UtcNow;
                        if (dodo > TimeSpan.Zero && ct.WaitHandle.WaitOne(dodo)) ct.ThrowIfCancellationRequested();
                        ct.ThrowIfCancellationRequested();
                        try { releves.Add(source.Lire()); } catch (Exception ex) { TraceAppli.Erreur("mesure en jeu : relevé", ex); releves.Add(null); }
                        prochain = prochain.AddSeconds(1);
                        if (etat != null) etat.Report(Math.Max(0, (int)Math.Ceiling((fin - DateTime.UtcNow).TotalSeconds)));
                    }
                }
                finally { source.Arreter(); }
                var m = Agreger(releves, date, duree.TotalSeconds);
                if (!m.FpsMoyen.HasValue && !string.IsNullOrEmpty(source.Erreur)) m.Note = source.Erreur;
                TraceAppli.Ecrire("mesure en jeu : " + (m.FpsMoyen.HasValue ? m.Jeu + " " + m.FpsMoyen + " FPS, couverture " + m.Couverture : "aucune image"));
                return m;
            }, ct);
        }

        // ---------- Comparaison ----------

        /// <summary>Texte « avant / après » de deux mesures (a la plus ancienne).</summary>
        public static string Comparer(MesureJeu a, MesureJeu b)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Partie du " + a.Date + " (" + Vide(a.Jeu) + ") → partie du " + b.Date + " (" + Vide(b.Jeu) + ") :");
            if (!string.Equals(a.Jeu, b.Jeu, StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("Attention : ce ne sont pas les mêmes jeux, la comparaison n'a pas de sens.");
            int mieux = 0, moins = 0;
            Func<string, double?, double?, bool, string> ligne = (nom, x, y, plusEstMieux) =>
            {
                string v = "n/d";
                if (x.HasValue && y.HasValue && x.Value > 0)
                {
                    var rel = (y.Value - x.Value) / x.Value;
                    if (Math.Abs(rel) < SeuilStable) v = "stable";
                    else
                    {
                        bool progres = plusEstMieux ? rel > 0 : rel < 0;
                        if (progres) mieux++; else moins++;
                        v = (progres ? "mieux" : "moins bien");
                    }
                    v = (rel > 0 ? "+" : "") + (rel * 100).ToString("0", CultureInfo.CurrentCulture) + " % : " + v;
                }
                return nom + " : " + Nombre(x) + " → " + Nombre(y) + " (" + v + ")";
            };
            sb.AppendLine(ligne("FPS moyens", a.FpsMoyen, b.FpsMoyen, true));
            sb.AppendLine(ligne("1 % bas (fluidité)", a.UnPourcentBas, b.UnPourcentBas, true));
            sb.AppendLine(ligne("Pire 1 % bas (saccades)", a.PireBas, b.PireBas, true));
            if (a.Cpu.HasValue || b.Cpu.HasValue) sb.AppendLine("Charge processeur : " + Nombre(a.Cpu) + " % → " + Nombre(b.Cpu) + " %");
            if (a.Gpu.HasValue || b.Gpu.HasValue) sb.AppendLine("Charge carte graphique : " + Nombre(a.Gpu) + " % → " + Nombre(b.Gpu) + " %");
            if (a.Couverture < CouvertureMinimale || b.Couverture < CouvertureMinimale)
                sb.AppendLine("Attention : au moins une mesure est incomplète (jeu pas toujours au premier plan).");
            sb.Append("Bilan : " + mieux + " mesure(s) en progrès, " + moins + " en recul. Un écart de moins de "
                + (SeuilStable * 100).ToString("0", CultureInfo.CurrentCulture) + " % est du bruit : refaites la même scène, mêmes réglages graphiques.");
            return sb.ToString();
        }

        private static string Vide(string s) { return string.IsNullOrEmpty(s) ? "aucun jeu" : s; }

        // ---------- Stockage ----------

        public static string Save(MesureJeu m, string machineDir)
        {
            var dir = Path.Combine(machineDir, Dossier);
            Directory.CreateDirectory(dir);
            var baseName = (m.Date ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Replace(":", "").Replace("-", "").Replace("T", "-");
            var path = Path.Combine(dir, baseName + ".json");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, baseName + "-" + n + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(m, Formatting.Indented), new UTF8Encoding(false));
            return path;
        }

        /// <summary>Mesures enregistrées, de la plus récente à la plus ancienne ; les fichiers illisibles sont ignorés.</summary>
        public static List<MesureJeu> List(string machineDir)
        {
            var res = new List<MesureJeu>();
            var dir = Path.Combine(machineDir ?? "", Dossier);
            if (!Directory.Exists(dir)) return res;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var m = JsonConvert.DeserializeObject<MesureJeu>(File.ReadAllText(f, Encoding.UTF8), new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                    if (m != null) res.Add(m);
                }
                catch (Exception) { }
            }
            return res.OrderByDescending(m => m.Date, StringComparer.Ordinal).ToList();
        }
    }
}
