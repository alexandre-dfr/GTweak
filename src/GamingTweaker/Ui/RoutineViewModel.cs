using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    public class JourChoix
    {
        public DayOfWeek Jour { get; set; }
        public string Nom { get; set; }
    }

    /// <summary>Routine hebdomadaire : jour, heure, profil courant. Les effets système (tâche planifiée) sont injectés.</summary>
    public class RoutineViewModel : ObservableObject
    {
        public static readonly IReadOnlyList<JourChoix> JoursSemaine = new List<JourChoix>
        {
            new JourChoix { Jour = DayOfWeek.Monday, Nom = "lundi" }, new JourChoix { Jour = DayOfWeek.Tuesday, Nom = "mardi" },
            new JourChoix { Jour = DayOfWeek.Wednesday, Nom = "mercredi" }, new JourChoix { Jour = DayOfWeek.Thursday, Nom = "jeudi" },
            new JourChoix { Jour = DayOfWeek.Friday, Nom = "vendredi" }, new JourChoix { Jour = DayOfWeek.Saturday, Nom = "samedi" },
            new JourChoix { Jour = DayOfWeek.Sunday, Nom = "dimanche" },
        };

        private readonly MainViewModel main;
        private readonly Action<RoutineConfig> install;
        private readonly Action remove;
        private readonly Func<RoutineEtat> query;
        private DayOfWeek jour = DayOfWeek.Sunday;
        private string heure = "20:00";
        private string etatTexte = "Routine : état en cours de lecture…";
        private string message = "";
        private bool occupe;

        public RoutineViewModel(MainViewModel main, Action<RoutineConfig> install, Action remove, Func<RoutineEtat> query)
        {
            this.main = main; this.install = install; this.remove = remove; this.query = query;
            PlanifierCommand = new RelayCommand(_ => { var t = PlanifierAsync(); }, _ => !occupe);
            SupprimerCommand = new RelayCommand(_ => { var t = SupprimerAsync(); }, _ => !occupe);
        }

        public IReadOnlyList<JourChoix> Jours { get { return JoursSemaine; } }
        public ICommand PlanifierCommand { get; private set; }
        public ICommand SupprimerCommand { get; private set; }

        /// <summary>Confirmation d'un profil contenant des options Risqué (titres) : renvoie vrai pour planifier. Sans elle, la planification est refusée.</summary>
        public Func<IReadOnlyList<string>, bool> ConfirmerRisque { get; set; }
        /// <summary>Faux si l'appli est élevée sous un autre compte que celui du bureau : la planification est alors refusée. Null : non vérifié.</summary>
        public Func<bool> MemeUtilisateur { get; set; }

        public DayOfWeek Jour { get { return jour; } set { Set(ref jour, value); } }
        public string Heure { get { return heure; } set { Set(ref heure, value ?? ""); } }
        public string EtatTexte { get { return etatTexte; } private set { Set(ref etatTexte, value); } }
        public string Message { get { return message; } private set { Set(ref message, value ?? ""); } }

        public static bool TryParseHeure(string texte, out TimeSpan t)
        {
            return TimeSpan.TryParseExact((texte ?? "").Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out t) && t >= TimeSpan.Zero && t < TimeSpan.FromHours(24);
        }

        public static string NomJour(DayOfWeek j) { return JoursSemaine.First(x => x.Jour == j).Nom; }

        public static string DecrireEtat(RoutineEtat e)
        {
            if (e == null || !e.Existe) return "Aucune routine planifiée." + (e != null && !string.IsNullOrEmpty(e.Message) ? " " + e.Message : "");
            var profil = string.IsNullOrEmpty(e.ProfilChemin) ? "profil inconnu" : "profil « " + Path.GetFileNameWithoutExtension(e.ProfilChemin) + " »";
            return "Routine active : " + (e.Jour.HasValue ? NomJour(e.Jour.Value) : "jour inconnu") + (e.Heure.HasValue ? " à " + e.Heure.Value.ToString(@"hh\:mm") : "") + ", " + profil + ".";
        }

        public async Task ActualiserAsync()
        {
            RoutineEtat e;
            try { e = await Task.Run(query); } catch (Exception ex) { e = new RoutineEtat { Existe = false, Message = ex.Message }; }
            EtatTexte = DecrireEtat(e);
            if (e.Existe && e.Jour.HasValue) Jour = e.Jour.Value;
            if (e.Existe && e.Heure.HasValue) Heure = e.Heure.Value.ToString(@"hh\:mm");
        }

        private void Occupe(bool v) { occupe = v; CommandManager.InvalidateRequerySuggested(); }

        public async Task PlanifierAsync()
        {
            if (occupe) return;
            TimeSpan h;
            if (!TryParseHeure(heure, out h)) { Message = "Heure invalide : saisissez par exemple 20:00."; return; }
            var nom = main.ProfilSelectionne;
            if (string.IsNullOrEmpty(nom)) { Message = "Choisissez d'abord un profil dans la barre du bas (ou enregistrez la sélection avec « Sauver »)."; return; }
            var chemin = main.CheminProfil(nom);
            if (!Planification.NomProfilAccepte(nom) || !Planification.NomProfilAccepte(Path.GetFileName(chemin))) { Message = Planification.MessagePourcent; return; }
            if (MemeUtilisateur != null)
            {
                bool meme;
                try { meme = await Task.Run(MemeUtilisateur); } catch (Exception) { meme = false; }
                if (!meme) { Message = Planification.MessageAutreCompte; return; }
            }
            var load = Profils.Load(chemin);
            if (load.Erreur != null) { Message = load.Erreur; return; }
            if (Profils.ContientRisque(load.Profil, main.Catalogue))
            {
                var ids = new HashSet<string>(load.Profil.Options ?? new List<string>());
                var risques = main.Catalogue.Where(c => ids.Contains(c.Id) && c.Risque == RiskLevel.Risque).Select(c => c.Titre ?? c.Id).ToList();
                if (ConfirmerRisque == null || !ConfirmerRisque(risques)) { Message = "Routine non planifiée : le profil contient des options Risqué et n'a pas été confirmé."; return; }
            }
            Occupe(true);
            try
            {
                var cfg = new RoutineConfig { Jour = jour, Heure = h, Profil = chemin };
                await Task.Run(() => install(cfg));
                Message = "Routine planifiée.";
            }
            catch (Exception ex) { Message = "La routine n'a pas pu être planifiée : " + ex.Message; }
            finally { Occupe(false); }
            await ActualiserAsync();
        }

        public async Task SupprimerAsync()
        {
            if (occupe) return;
            Occupe(true);
            try { await Task.Run(remove); Message = "Routine supprimée (les sessions, journaux et mesures sont conservés)."; }
            catch (Exception ex) { Message = "Suppression de la routine : " + ex.Message; }
            finally { Occupe(false); }
            await ActualiserAsync();
        }
    }
}
