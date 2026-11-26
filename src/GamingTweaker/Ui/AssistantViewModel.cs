using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    /// <summary>Une question de l'assistant : les options d'un menu, ou les applications d'une option du menu Installation.</summary>
    public class EtapeAssistant : ObservableObject
    {
        public string Court { get; set; }
        public string Question { get; set; }
        public string Explication { get; set; }
        public string Menu { get; set; }
        /// <summary>Options affichées (menu d'options) ; une seule, dont on affiche les éléments, pour une étape d'applications.</summary>
        public IReadOnlyList<OptionViewModel> Options { get; set; } = new List<OptionViewModel>();
        /// <summary>Étape d'applications : les éléments de la liste de l'option sont affichés directement.</summary>
        public OptionViewModel OptionApplis { get; set; }
        public bool EstApplis { get { return OptionApplis != null; } }
        public bool EstOptions { get { return OptionApplis == null && !EstRecap; } }
        public bool EstRecap { get; set; }
        internal bool Visitee { get; set; }

        private string numero = "";
        public string Numero { get { return numero; } set { Set(ref numero, value ?? ""); } }
    }

    /// <summary>Ligne du récapitulatif : une question et ce qui sera fait.</summary>
    public class LigneRecap
    {
        public string Etape { get; set; }
        public string Choix { get; set; }
        public bool Vide { get; set; }
    }

    /// <summary>
    /// Assistant après une installation de Windows : une question par écran, dans l'ordre d'exécution (mises à jour, bloatware, installations,
    /// télémétrie, optimisation, nettoyage en dernier). Il ne fait que cocher les options de la fenêtre principale (mêmes objets) :
    /// rien ne change avant « Aperçu » puis « LANCER ». Les options Risqué n'y figurent pas (elles restent dans leur menu).
    /// </summary>
    public class AssistantViewModel : ObservableObject
    {
        private readonly MainViewModel main;
        private int index;

        public AssistantViewModel(MainViewModel main)
        {
            this.main = main;
            var etapes = new List<EtapeAssistant>();
            Ajouter(etapes, "Mises à jour", "Mises à jour", "Mettre Windows, vos applications et votre pilote graphique à jour ?",
                "En premier : une mise à jour peut rétablir des réglages, elle passe donc avant tout le reste. Tout est installé en silence ; un redémarrage peut être demandé à la fin.");
            Ajouter(etapes, "Bloatware", "Bloatware", "Retirer les applications préinstallées inutiles ?",
                "Cochez l'option puis les applications à retirer dans sa liste. Les applications retirées du Store se réinstallent depuis l'Historique.");
            AjouterApplis(etapes, "ins.composants", "Composants", "Installer les composants dont les jeux ont besoin ?",
                "Visual C++ et DirectX manquent souvent sur un Windows neuf : sans eux, beaucoup de jeux ne démarrent pas. Les composants déjà présents ne sont pas proposés.");
            AjouterApplis(etapes, "ins.launchers", "Launchers", "Quels launchers de jeux installer ?",
                "Par winget quand il est présent, sinon téléchargés depuis le site de l'éditeur (signature vérifiée). Ceux déjà installés ne sont pas proposés.");
            AjouterApplis(etapes, "ins.marques", "Logiciels des marques", "Quels logiciels pour votre matériel ?",
                "Ceux de votre matériel (carte mère, carte graphique, périphériques USB branchés) sont cochés d'office. Les logiciels sans paquet winget sont signalés à la fin avec leur page officielle.");
            AjouterApplis(etapes, "ins.applis", "Applications", "Quelles applications installer ?",
                "Communication, navigateurs, streaming et musique, outils, jeu à distance.");
            Ajouter(etapes, "Télémétrie", "Télémétrie", "Que couper dans la télémétrie et la collecte de données ?",
                "« Recommandé » ne coche que des réglages Sûr, tous annulables depuis l'Historique.");
            Ajouter(etapes, "Optimisation", "Optimisation", "Quelles optimisations pour le jeu ?",
                "Après les installations : les services et programmes au démarrage ajoutés par les logiciels installés existent alors. « Recommandé » ne coche que des réglages Sûr.");
            Ajouter(etapes, "Nettoyage", "Nettoyage", "Que nettoyer à la fin ?",
                "En dernier : le nettoyage supprime aussi les fichiers temporaires et les installateurs laissés par les étapes précédentes.");
            var n = etapes.Count;
            for (int i = 0; i < n; i++) etapes[i].Numero = "Question " + (i + 1) + " sur " + n;
            etapes.Add(new EtapeAssistant { Court = "Récapitulatif", Question = "Récapitulatif", EstRecap = true, Numero = "Dernière étape",
                Explication = "Rien n'a encore été modifié. « Voir l'aperçu » montre exactement ce qui sera fait, puis « LANCER » l'exécute dans cet ordre. Pour refaire la même chose sur un autre PC : Profil ▾ > Enregistrer la sélection sous…" });
            Etapes = etapes;

            SuivantCommand = new RelayCommand(_ => Aller(index + 1), _ => index < Etapes.Count - 1);
            PrecedentCommand = new RelayCommand(_ => Aller(index - 1), _ => index > 0);
            RecommanderCommand = new RelayCommand(_ => Recommander(), _ => Courante.EstOptions && Courante.Options.Any(o => o.Recommandee));
            RienCommand = new RelayCommand(_ => Rien(), _ => !Courante.EstRecap);
            foreach (var o in etapes.SelectMany(e => e.Options)) o.PropertyChanged += OptionChangee;
            Entrer(Courante);
        }

        public IReadOnlyList<EtapeAssistant> Etapes { get; private set; }
        public EtapeAssistant Courante { get { return Etapes[index]; } }
        public int Index { get { return index; } }
        public bool EstDerniere { get { return index == Etapes.Count - 1; } }
        public bool EstPremiere { get { return index == 0; } }
        public ObservableCollection<LigneRecap> Recap { get; } = new ObservableCollection<LigneRecap>();

        public ICommand SuivantCommand { get; private set; }
        public ICommand PrecedentCommand { get; private set; }
        public ICommand RecommanderCommand { get; private set; }
        public ICommand RienCommand { get; private set; }

        /// <summary>Vrai si au moins une option sera exécutée (bouton « Voir l'aperçu »).</summary>
        public bool AUneSelection { get { return main.AUneSelection; } }

        private void Ajouter(List<EtapeAssistant> etapes, string menu, string court, string question, string explication)
        {
            var opts = main.Options.Where(o => o.Menu == menu && o.NiveauRisque != RiskLevel.Risque).ToList();
            if (opts.Count == 0) return;
            etapes.Add(new EtapeAssistant { Menu = menu, Court = court, Question = question, Explication = explication, Options = opts });
        }

        private void AjouterApplis(List<EtapeAssistant> etapes, string id, string court, string question, string explication)
        {
            var o = main.Options.FirstOrDefault(x => x.Id == id);
            if (o == null || o.Liste == null) return;
            etapes.Add(new EtapeAssistant { Menu = o.Menu, Court = court, Question = question, Explication = explication, Options = new[] { o }, OptionApplis = o });
        }

        public void Aller(int i)
        {
            if (i < 0 || i >= Etapes.Count || i == index) return;
            Quitter(Courante);
            index = i;
            Entrer(Courante);
            Raise("Courante"); Raise("Index"); Raise("EstDerniere"); Raise("EstPremiere"); Raise("AUneSelection");
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Première visite d'une étape : « Recommandé » si rien n'est coché dans ce menu ; applications : sélection par défaut (composants, matériel détecté).</summary>
        private void Entrer(EtapeAssistant e)
        {
            if (e.EstRecap) { MajRecap(); return; }
            var premiere = !e.Visitee;
            e.Visitee = true;
            if (e.EstApplis)
            {
                var o = e.OptionApplis;
                if (!o.IsChecked) o.IsChecked = true;
                var _ = ChargerApplisAsync(o, premiere);
                return;
            }
            if (premiere && !e.Options.Any(o => o.IsChecked)) foreach (var o in e.Options.Where(o => o.Recommandee)) o.IsChecked = true;
            foreach (var o in e.Options.Where(o => o.IsChecked && o.Liste != null)) PreparerListe(o);
        }

        private static async Task ChargerApplisAsync(OptionViewModel o, bool premiere)
        {
            await o.Liste.ChargerAsync();
            if (premiere && o.Liste.EstVide) o.Liste.AppliquerDefaut();
        }

        /// <summary>Option à liste cochée dans l'assistant : sa liste est chargée ; vide, elle prend « Tout » si sa clé le permet, sinon sa sélection par défaut.</summary>
        private static void PreparerListe(OptionViewModel o)
        {
            o.ListeOuverte = true;
            if (!o.Liste.EstVide) return;
            if (o.Liste.SupporteTous) o.Liste.Tous = true; else o.Liste.AppliquerDefaut();
        }

        private void OptionChangee(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "IsChecked") return;
            var o = (OptionViewModel)sender;
            if (o.IsChecked && o.Liste != null && o.Item.Menu != "Installation") PreparerListe(o);
            Raise("AUneSelection");
        }

        /// <summary>Étape d'applications quittée sans rien choisir : l'option est décochée (elle ne compterait pas dans la sélection).</summary>
        private static void Quitter(EtapeAssistant e)
        {
            if (e.EstApplis && e.OptionApplis.IsChecked && e.OptionApplis.Liste.EstVide) e.OptionApplis.IsChecked = false;
        }

        private void Recommander()
        {
            foreach (var o in Courante.Options.Where(o => o.Recommandee)) o.IsChecked = true;
        }

        private void Rien()
        {
            var e = Courante;
            if (e.EstApplis) { e.OptionApplis.Liste.DepuisSelection(new string[0]); return; }
            foreach (var o in e.Options.Where(o => o.IsChecked)) o.IsChecked = false;
        }

        private void MajRecap()
        {
            Recap.Clear();
            foreach (var e in Etapes.Where(x => !x.EstRecap))
            {
                List<string> choix;
                if (e.EstApplis)
                    choix = e.OptionApplis.IsChecked ? e.OptionApplis.Liste.Elements.Where(x => x.IsChecked && !x.Absent).Select(x => x.Titre).ToList() : new List<string>();
                else
                    choix = e.Options.Where(o => o.IsChecked).Select(o => o.Liste == null ? o.Titre : o.Liste.EstVide ? o.Titre + " (aucun élément choisi : non exécutée)" : o.Titre + " (" + o.Liste.ResumeCourt + ")").ToList();
                Recap.Add(new LigneRecap { Etape = e.Court, Choix = choix.Count == 0 ? "Rien" : string.Join(" · ", choix), Vide = choix.Count == 0 });
            }
            Raise("AUneSelection");
        }

        /// <summary>Fin de l'assistant (fenêtre fermée par « Voir l'aperçu ») : les étapes d'applications vides sont décochées.</summary>
        public void Terminer()
        {
            foreach (var e in Etapes) Quitter(e);
        }
    }
}
