using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GamingTweaker.Core;
using GamingTweaker.Lists;

namespace GamingTweaker.Ui
{
    public partial class MainViewModel
    {
        // ---------- Profils ----------

        public string ProfilSelectionne
        {
            get { return profilSelectionne; }
            set
            {
                if (!Set(ref profilSelectionne, value)) return;
                Raise("ProfilIntegreSelectionne");
                if (!chargementProfil && !string.IsNullOrEmpty(value)) ChargerProfilNomme(value);
            }
        }

        public Profil ToProfil(string nom)
        {
            var p = new Profil { Nom = nom, Options = Options.Where(o => o.IsChecked).Select(o => o.Id).ToList() };
            p.Listes = profilListes == null ? new ProfilListes() : profilListes.Cloner();
            avertissementProfil = "";
            var perdues = new List<string>();
            // Le profil reflète exactement ce qui est coché : la clé de chaque option à liste est retirée puis réécrite si elle est cochée et non vide.
            foreach (var o in Options.Where(o => o.Liste != null)) Profils.RetirerListe(p.Listes, o.Item.Liste);
            foreach (var o in Options.Where(o => o.IsChecked && o.Liste != null))
            {
                var ecartes = new List<string>();
                var sel = o.Liste.VersSelection();
                if (sel.Count == 0) continue;
                if (!Profils.AjouterListe(p.Listes, o.Item.Liste, sel, ecartes)) perdues.Add(o.Titre);
                else if (ecartes.Count > 0) perdues.Add(o.Titre + " (" + string.Join(", ", ecartes) + ")");
            }
            if (perdues.Count > 0)
                avertissementProfil = "Éléments non enregistrés dans le profil (nom non pris en charge) : " + string.Join(" ; ", perdues) + ".";
            return p;
        }

        public void LoadProfil(Profil p)
        {
            foreach (var o in Options) o.ForceUnchecked();
            foreach (var o in Options.Where(o => o.Liste != null)) o.Liste.DepuisSelection(null);
            listesChargees.Clear();
            profilListes = p.Listes;
            var res = Profils.Resolve(p, items, facts);
            foreach (var kv in res.Selection.Listes) listesChargees[kv.Key] = kv.Value;
            var refuses = new List<string>();
            foreach (var o in Options.Where(o => res.Selection.OptionIds.Contains(o.Id)))
            {
                List<string> sel;
                if (o.Liste != null && res.Selection.Listes.TryGetValue(o.Id, out sel)) o.Liste.DepuisSelection(sel);
                o.IsChecked = true;
                if (!o.IsChecked) { refuses.Add(o.Titre); continue; }
                // Éléments Risqué du profil : même confirmation que pour une option Risqué ; les refusés sont retirés de la sélection.
                if (o.Liste != null) refuses.AddRange(o.Liste.ConfirmerSelectionProfil().Select(x => o.Titre + " : " + x));
            }
            var infos = new List<string>();
            if (res.Inconnues.Count > 0) infos.Add(res.Inconnues.Count + " option(s) inconnue(s) ignorée(s).");
            if (res.Incompatibles.Count > 0) infos.Add(res.Incompatibles.Count + " option(s) incompatible(s) avec ce PC ignorée(s).");
            if (refuses.Count > 0) infos.Add("Non cochée(s) faute de confirmation : " + string.Join(", ", refuses) + ".");
            Message = string.Join(" ", infos);
            MajCompteurs();
        }

        public void RafraichirProfils()
        {
            var noms = new List<string>();
            try
            {
                if (paths != null && Directory.Exists(paths.ProfilsDir))
                    noms = Directory.GetFiles(paths.ProfilsDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (Exception) { /* dossier illisible : liste vide */ }
            ProfilsDisponibles.Clear();
            // Profils intégrés d'abord ; un fichier qui porterait leur nom affiché n'est pas listé (il serait masqué).
            foreach (var pi in ProfilsIntegres.Tous) ProfilsDisponibles.Add(pi.NomAffiche);
            foreach (var n in noms.Where(n => !ProfilsIntegres.EstReserve(n))) ProfilsDisponibles.Add(n);
        }

        /// <summary>Recharge le profil actuellement sélectionné (annule les modifications de cases depuis son chargement).</summary>
        public void RechargerProfil()
        {
            if (!string.IsNullOrEmpty(profilSelectionne)) ChargerProfilNomme(profilSelectionne);
        }

        public string CheminProfil(string nom) { return Path.Combine(paths.ProfilsDir, nom + ".json"); }

        /// <summary>Vrai si le profil sélectionné est un profil intégré (lecture seule).</summary>
        public bool ProfilIntegreSelectionne { get { return ProfilsIntegres.Trouver(profilSelectionne) != null; } }

        /// <summary>Nom proposé par « Sauver » : celui du profil sélectionné, sans le suffixe « (intégré) ».</summary>
        public string NomPourEnregistrer
        {
            get
            {
                var pi = ProfilsIntegres.Trouver(profilSelectionne);
                return pi != null ? pi.Nom : profilSelectionne ?? "";
            }
        }

        private void ChargerProfilNomme(string nom)
        {
            var integre = ProfilsIntegres.Trouver(nom);
            if (integre != null)
            {
                LoadProfil(ProfilsIntegres.Construire(integre, items, facts));
                Message = (integre.NomAffiche + " : " + integre.Description + " " + Message).Trim();
                return;
            }
            var load = Profils.Load(CheminProfil(nom), ClesListes);
            if (load.Erreur != null) { Message = load.Erreur; return; }
            LoadProfil(load.Profil);
            if (load.Avertissements.Count > 0) Message = (Message + " " + string.Join(" ", load.Avertissements)).Trim();
        }

        /// <summary>Enregistre la sélection sous ce nom. Renvoie un message d'erreur, ou null si tout va bien (ou si l'utilisateur annule).</summary>
        public string SauverProfil(string nom)
        {
            nom = NettoyerNom(nom);
            if (nom.Length == 0) return "Donnez un nom au profil (sans caractères spéciaux).";
            if (ProfilsIntegres.EstReserve(nom)) return ProfilsIntegres.MessageNomReserve;
            var risques = Options.Where(o => o.IsChecked && (o.NiveauRisque == RiskLevel.Risque || (o.Liste != null && o.Liste.ElementsRisques().Count > 0))).ToList();
            if (risques.Count > 0 && (ConfirmerProfilRisque == null || !ConfirmerProfilRisque(risques))) return null;
            try { Profils.Save(ToProfil(nom), CheminProfil(nom)); }
            catch (Exception ex) { return "Le profil n'a pas pu être enregistré : " + ex.Message; }
            RafraichirProfils();
            chargementProfil = true;
            try { ProfilSelectionne = nom; } finally { chargementProfil = false; }
            Message = ("Profil « " + nom + " » enregistré. " + avertissementProfil).Trim();
            return null;
        }

        /// <summary>Nom de fichier de profil : sans caractères interdits dans un nom de fichier ni point final.</summary>
        public static string NettoyerNom(string nom)
        {
            var invalides = Path.GetInvalidFileNameChars();
            return new string((nom ?? "").Trim().Where(c => !invalides.Contains(c)).ToArray()).Trim().TrimEnd('.').Trim();
        }

        private IEnumerable<string> ClesListes { get { return items.Where(c => !string.IsNullOrEmpty(c.Liste)).Select(c => c.Liste).Distinct(); } }

        /// <summary>
        /// Exporte la sélection actuelle (ce qui est coché) vers un fichier choisi par l'utilisateur, pour la reprendre sur un autre PC.
        /// Même contenu qu'un profil enregistré : des identifiants d'options et des noms d'éléments, jamais de commande.
        /// Renvoie un message d'erreur, ou null si tout va bien.
        /// </summary>
        public string ExporterProfil(string chemin)
        {
            if (string.IsNullOrWhiteSpace(chemin)) return null;
            if (SelectedCount == 0) return "Rien n'est coché : cochez les options à exporter (ou chargez un profil), puis recommencez.";
            var nom = NettoyerNom(Path.GetFileNameWithoutExtension(chemin));
            if (nom.Length == 0) nom = "Profil exporté";
            try { Profils.Save(ToProfil(nom), chemin); }
            catch (Exception ex) { return "Le profil n'a pas pu être exporté : " + ex.Message; }
            Message = ("Profil exporté : " + chemin + ". Sur l'autre PC : « Importer… ». " + avertissementProfil).Trim();
            return null;
        }

        /// <summary>
        /// Importe un profil exporté : il est validé comme un profil enregistré (version, identifiants, éléments de liste), puis réécrit
        /// dans le dossier des profils (jamais copié tel quel) et chargé. Les options Risqué demandent la même confirmation qu'au chargement.
        /// <paramref name="confirmerEcrasement"/> reçoit le nom d'un profil existant du même nom. Renvoie un message d'erreur, ou null.
        /// </summary>
        public string ImporterProfil(string chemin, Func<string, bool> confirmerEcrasement)
        {
            if (string.IsNullOrWhiteSpace(chemin) || paths == null) return null;
            var load = Profils.Load(chemin, ClesListes);
            if (load.Erreur != null) return load.Erreur;
            var nom = NettoyerNom(load.Profil.Nom);
            if (nom.Length == 0) nom = NettoyerNom(Path.GetFileNameWithoutExtension(chemin));
            if (nom.Length == 0) nom = "Profil importé";
            if (ProfilsIntegres.EstReserve(nom)) nom += " (importé)";
            if (File.Exists(CheminProfil(nom)) && (confirmerEcrasement == null || !confirmerEcrasement(nom))) return null;
            load.Profil.Nom = nom;
            try { Profils.Save(load.Profil, CheminProfil(nom)); }
            catch (Exception ex) { return "Le profil n'a pas pu être importé : " + ex.Message; }
            RafraichirProfils();
            if (ProfilSelectionne == nom) RechargerProfil(); else ProfilSelectionne = nom;
            var avert = load.Avertissements.Count > 0 ? " " + string.Join(" ", load.Avertissements) : "";
            Message = ("Profil « " + nom + " » importé. " + Message + avert + " Vérifiez avec « Aperçu », puis « LANCER ».").Trim();
            return null;
        }
    }
}
