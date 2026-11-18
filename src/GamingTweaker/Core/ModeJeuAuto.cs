using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Réglages du Mode Jeu automatique, dans le dossier protégé de la machine (modejeu-auto.json) : l'outil élevé ne les lit
    /// que là où un utilisateur standard ne peut pas écrire.
    /// </summary>
    public class ModeJeuAutoConfig
    {
        [JsonProperty("actif")] public bool Actif { get; set; }
        [JsonProperty("jeux")] public List<JeuSurveille> Jeux { get; set; } = new List<JeuSurveille>();
        [JsonProperty("services")] public List<string> Services { get; set; } = new List<string>();
        [JsonProperty("processus")] public List<ProcessusChoix> Processus { get; set; } = new List<ProcessusChoix>();
        [JsonProperty("plan")] public bool Plan { get; set; } = true;
        [JsonProperty("nvidia")] [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))] public ProfilNvidia Nvidia { get; set; }

        public const string NomFichier = "modejeu-auto.json";

        public ModeJeuConfig VersModeJeu()
        {
            var c = new ModeJeuConfig { PlanGuid = Plan ? ModeJeu.PlanPerformances : null, Nvidia = Nvidia };
            c.Services.AddRange((Services ?? new List<string>()).Where(s => !ModeJeu.EstServiceProtege(s)));
            // Jamais fermés par le mode automatique : un jeu de la liste (il serait fermé puis relancé en boucle) et les lanceurs
            // (Steam, Epic : les fermer peut fermer le jeu en cours).
            c.Processus.AddRange((Processus ?? new List<ProcessusChoix>()).Where(p => p != null && !ModeJeu.EstProcessusProtege(p.Nom) && !EstJeuOuLanceur(p)));
            return c;
        }

        public static readonly string[] Lanceurs = { "steam", "steamwebhelper", "steamservice", "EpicGamesLauncher", "EpicWebHelper", "EpicOnlineServicesHost" };

        private bool EstJeuOuLanceur(ProcessusChoix p)
        {
            var nom = (p.Nom ?? "").Trim();
            if (nom.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) nom = nom.Substring(0, nom.Length - 4);
            if (Lanceurs.Any(l => string.Equals(l, nom, StringComparison.OrdinalIgnoreCase))) return true;
            if (string.IsNullOrEmpty(p.Chemin)) return false;
            return (Jeux ?? new List<JeuSurveille>()).Any(j => j != null && (Core.Jeux.Correspond(p.Chemin, j)
                || (j.EstDossier && p.Chemin.StartsWith(j.Chemin.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))));
        }

        public static ModeJeuAutoConfig Charger(string machineDir)
        {
            try
            {
                var f = Path.Combine(machineDir ?? "", NomFichier);
                if (!File.Exists(f)) return new ModeJeuAutoConfig();
                var c = JsonConvert.DeserializeObject<ModeJeuAutoConfig>(File.ReadAllText(f, Encoding.UTF8)) ?? new ModeJeuAutoConfig();
                c.Jeux = (c.Jeux ?? new List<JeuSurveille>()).Where(j => j != null && !string.IsNullOrWhiteSpace(j.Chemin)).ToList();
                c.Services = c.Services ?? new List<string>();
                c.Processus = c.Processus ?? new List<ProcessusChoix>();
                return c;
            }
            catch (Exception ex)
            {
                TraceAppli.Erreur("réglages du Mode Jeu automatique illisibles, mode désactivé", ex);
                return new ModeJeuAutoConfig();
            }
        }

        /// <summary>Écriture atomique (fichier temporaire puis remplacement).</summary>
        public void Enregistrer(string machineDir)
        {
            Directory.CreateDirectory(machineDir);
            var f = Path.Combine(machineDir, NomFichier);
            var tmp = f + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
        }
    }

    public enum ActionAuto { Rien, Activer, Retablir }

    /// <summary>
    /// Décision pure du Mode Jeu automatique, appelée à chaque relevé des processus.
    /// - Un jeu de la liste démarre et le Mode Jeu n'est pas actif : Activer (le Mode Jeu est alors « à nous »).
    /// - Plus aucun jeu pendant <see cref="RelevesAvantRetablir"/> relevés de suite, et le Mode Jeu est à nous : Rétablir.
    ///   (un jeu qui se relance, ou un lanceur qui enchaîne deux exe, ne fait pas rétablir puis réactiver).
    /// - Un Mode Jeu activé à la main n'est jamais rétabli automatiquement.
    /// - Un Mode Jeu rétabli à la main pendant une partie n'est pas réactivé avant que tous les jeux soient fermés.
    /// </summary>
    public class AutomateModeJeu
    {
        public const int RelevesAvantRetablir = 2;
        private int relevesSansJeu;
        private bool suspendu;

        /// <summary>Vrai si le Mode Jeu actif a été activé par l'automate.</summary>
        public bool ActiveParAuto { get; private set; }

        public ActionAuto Decider(bool jeuEnCours, bool modeJeuActif)
        {
            if (jeuEnCours)
            {
                relevesSansJeu = 0;
                if (modeJeuActif) return ActionAuto.Rien;
                if (ActiveParAuto) { ActiveParAuto = false; suspendu = true; }   // rétabli à la main pendant la partie
                return suspendu ? ActionAuto.Rien : ActionAuto.Activer;
            }
            suspendu = false;
            if (!modeJeuActif) { ActiveParAuto = false; relevesSansJeu = 0; return ActionAuto.Rien; }
            if (!ActiveParAuto) return ActionAuto.Rien;
            relevesSansJeu++;
            return relevesSansJeu >= RelevesAvantRetablir ? ActionAuto.Retablir : ActionAuto.Rien;
        }

        /// <summary>Résultat de l'action demandée : l'automate ne se considère propriétaire que d'une activation réussie.</summary>
        public void Resultat(ActionAuto a, bool modeJeuActifApres)
        {
            if (a == ActionAuto.Activer) ActiveParAuto = modeJeuActifApres;
            else if (a == ActionAuto.Retablir) { if (!modeJeuActifApres) ActiveParAuto = false; relevesSansJeu = 0; }
        }
    }

    /// <summary>
    /// Surveillance périodique des processus : applique les décisions de l'automate au Mode Jeu et signale chaque changement (notification).
    /// </summary>
    public sealed class SurveillanceJeux : IDisposable
    {
        public static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(5);

        private readonly ModeJeu modeJeu;
        private readonly Func<ModeJeuAutoConfig> config;
        private readonly Func<IEnumerable<string>> processus;
        private readonly AutomateModeJeu automate = new AutomateModeJeu();
        private readonly object verrou = new object();
        private Timer minuteur;
        private bool enCours;

        /// <summary>(titre, texte) : appelé hors du thread de l'interface.</summary>
        public event Action<string, string> Notification;

        public SurveillanceJeux(ModeJeu modeJeu, Func<ModeJeuAutoConfig> config, Func<IEnumerable<string>> processus = null)
        {
            this.modeJeu = modeJeu; this.config = config; this.processus = processus ?? Jeux.CheminsProcessus;
        }

        public bool Demarree { get { return minuteur != null; } }

        public void Demarrer()
        {
            lock (verrou) { if (minuteur == null) minuteur = new Timer(_ => Releve(), null, TimeSpan.Zero, Intervalle); }
        }

        public void Arreter()
        {
            lock (verrou) { if (minuteur != null) { minuteur.Dispose(); minuteur = null; } }
        }

        /// <summary>Un relevé (public pour les tests) ; jamais deux en même temps.</summary>
        public void Releve()
        {
            lock (verrou) { if (enCours) return; enCours = true; }
            try
            {
                var c = config();
                if (c == null || !c.Actif || c.Jeux.Count == 0) return;
                var jeux = Jeux.EnCours(processus(), c.Jeux);
                var action = automate.Decider(jeux.Count > 0, modeJeu.IsActive);
                if (action == ActionAuto.Activer)
                {
                    var b = modeJeu.Activate(c.VersModeJeu());
                    automate.Resultat(action, modeJeu.IsActive);
                    if (b.Verrouille) return;   // une autre exécution est en cours : nouvel essai au prochain relevé
                    Notifier("Mode Jeu activé", jeux[0].Nom + " a démarré : " + b.Actions.Count + " action(s)." + Avertissements(b));
                }
                else if (action == ActionAuto.Retablir)
                {
                    var b = modeJeu.Restore();
                    automate.Resultat(action, modeJeu.IsActive);
                    if (b.Verrouille) return;
                    Notifier("Mode Jeu rétabli", "Plus aucun jeu en cours : tout est rétabli." + Avertissements(b));
                }
            }
            catch (Exception ex)
            {
                TraceAppli.Erreur("Mode Jeu automatique", ex);
                Notifier("Mode Jeu automatique", "Erreur : " + ex.Message);
            }
            finally { lock (verrou) enCours = false; }
        }

        /// <summary>Fermeture de l'application : rétablit le Mode Jeu s'il a été activé par la surveillance.</summary>
        public void RetablirSiActiveParAuto()
        {
            Arreter();
            if (automate.ActiveParAuto && modeJeu.IsActive) modeJeu.Restore();
        }

        private static string Avertissements(ModeJeuBilan b)
        {
            return b.Avertissements.Count == 0 ? "" : " " + string.Join(" ", b.Avertissements);
        }

        private void Notifier(string titre, string texte)
        {
            TraceAppli.Ecrire("Mode Jeu automatique : " + titre + " - " + texte);
            var n = Notification;
            if (n != null) n(titre, texte);
        }

        public void Dispose() { Arreter(); }
    }
}
