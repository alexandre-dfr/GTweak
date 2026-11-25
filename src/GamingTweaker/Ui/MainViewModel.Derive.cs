using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using GamingTweaker.Core;
using Newtonsoft.Json;

namespace GamingTweaker.Ui
{
    public partial class MainViewModel
    {
        // ---------- Réglages rétablis depuis leur application (mise à jour de Windows, pilote, réglage à la main) ----------

        /// <summary>Options appliquées par GamingTweaker sur ce PC, tenues à jour par l'exécution et l'annulation.</summary>
        public const string FichierDernieresApplications = "last-applied.json";

        private IReadOnlyList<OptionViewModel> derives = new List<OptionViewModel>();

        /// <summary>
        /// Options appliquées par GamingTweaker (et jamais annulées depuis l'historique) que la détection donne maintenant « Non appliqué » :
        /// une mise à jour de Windows ou d'un pilote, ou un réglage à la main, les a rétablies. Vide pendant la détection.
        /// </summary>
        public IReadOnlyList<OptionViewModel> Derives { get { return derives; } }
        public bool ADerive { get { return derives.Count > 0; } }

        public string TexteDerive
        {
            get
            {
                int n = derives.Count;
                if (n == 0) return "";
                var noms = string.Join(", ", derives.Take(3).Select(o => "« " + o.Titre + " »")) + (n > 3 ? " et " + (n - 3) + " autre" + (n - 3 > 1 ? "s" : "") : "");
                return n + (n > 1 ? " réglages appliqués par GamingTweaker ne sont plus en place" : " réglage appliqué par GamingTweaker n'est plus en place")
                     + " (rétabli" + (n > 1 ? "s" : "") + " par une mise à jour de Windows ou d'un pilote, ou à la main) : " + noms + ".";
            }
        }

        private ICommand reappliquerDeriveCommand;
        /// <summary>Recoche les options rétablies ; rien ne s'exécute avant « Aperçu » puis « LANCER ».</summary>
        public ICommand ReappliquerDeriveCommand
        {
            get { return reappliquerDeriveCommand ?? (reappliquerDeriveCommand = new RelayCommand(_ => ReappliquerDerive(), _ => ADerive)); }
        }

        public void ReappliquerDerive()
        {
            int n = 0;
            foreach (var o in derives.ToList())
            {
                if (!o.IsChecked) o.IsChecked = true;   // une option Risqué redemande sa confirmation
                if (o.IsChecked) n++;
            }
            Message = n == 0 ? "Aucun réglage n'a été recoché."
                : n + " réglage" + (n > 1 ? "s" : "") + " recoché" + (n > 1 ? "s" : "") + ". Vérifiez avec « Aperçu », puis « LANCER ».";
        }

        /// <summary>Identifiants de last-applied.json ; vide si absent ou illisible.</summary>
        private HashSet<string> LireDernieresApplications()
        {
            try
            {
                if (paths == null || string.IsNullOrEmpty(paths.MachineDir)) return new HashSet<string>(StringComparer.Ordinal);
                var f = Path.Combine(paths.MachineDir, FichierDernieresApplications);
                if (!File.Exists(f)) return new HashSet<string>(StringComparer.Ordinal);
                var l = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(f, Encoding.UTF8)) ?? new List<string>();
                return new HashSet<string>(l.Where(x => !string.IsNullOrEmpty(x)), StringComparer.Ordinal);
            }
            catch (Exception) { return new HashSet<string>(StringComparer.Ordinal); }
        }

        /// <summary>Recalcule <see cref="Derives"/> (appelée avec l'accueil ; rien tant que la détection n'est pas terminée).</summary>
        private void MajDerive()
        {
            List<OptionViewModel> d;
            if (Options.Any(o => o.Etat == "Détection…")) d = new List<OptionViewModel>();
            else
            {
                var appliquees = LireDernieresApplications();
                d = appliquees.Count == 0 ? new List<OptionViewModel>()
                  : Options.Where(o => appliquees.Contains(o.Id) && o.Etat == EtatNonApplique).ToList();
            }
            if (d.Select(o => o.Id).SequenceEqual(derives.Select(o => o.Id))) return;
            derives = d;
            Raise("Derives"); Raise("ADerive"); Raise("TexteDerive");
            if (d.Count > 0) TraceAppli.Ecrire("accueil : " + d.Count + " réglage(s) rétabli(s) depuis leur application : " + string.Join(", ", d.Select(o => o.Id)));
        }
    }
}
