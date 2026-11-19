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
        // ---------- Tout restaurer (tous les menus) ----------

        /// <summary>Service d'annulation utilisé par « Tout restaurer » (journal, sinon script revert).</summary>
        public IRestauration Restauration { get; set; }

        private bool restaurationEnCours;
        public bool RestaurationEnCours { get { return restaurationEnCours; } private set { if (Set(ref restaurationEnCours, value)) Raise("PeutToutRestaurer"); } }
        public bool PeutToutRestaurer { get { return !restaurationEnCours; } }

        /// <summary>
        /// Annule les options réversibles de tous les menus : toute option qui a des entrées actives dans un journal est annulée par TOUS ses journaux
        /// (du plus récent au plus ancien), quel que soit son état de détection ; une option détectée « appliquée » sans journal passe par son
        /// script revert (valeurs par défaut de Windows). Une annulation par journal en échec n'est jamais remplacée par le script revert.
        /// Même verrou d'instance et même règle de compte que l'exécution : les options HKCU sont ignorées sous un autre compte.
        /// Les valeurs modifiées depuis par l'utilisateur ne sont pas comparées. Redétecte ensuite.
        /// </summary>
        public async Task<ResultatRestauration> ToutRestaurerAsync()
        {
            var res = new ResultatRestauration();
            var svc = Restauration;
            if (svc == null) { Message = "Tout restaurer est indisponible : aucun service d'annulation n'est configuré."; return res; }
            if (RestaurationEnCours) return res;
            RestaurationEnCours = true;
            try
            {
                using (var verrou = svc.PrendreVerrou())
                {
                    if (verrou == null) { Message = "Une autre exécution de GamingTweaker est en cours : rien n'a été restauré."; return res; }

                    var tel = Options.Where(o => Array.IndexOf(MenusOptions, o.Menu) >= 0).Select(o => o.Item).ToList();
                    bool memeUtilisateur = svc.MemeUtilisateur();
                    Dictionary<string, DetectState> etats = null;
                    string erreurDetection = null;
                    try { etats = await detect(tel); }
                    catch (Exception ex) { erreurDetection = ex.Message; }
                    Func<CatalogItem, bool> appliquee = i => { DetectState s; return etats != null && etats.TryGetValue(i.Id, out s) && s == DetectState.Applied; };

                    var restaures = new List<string>();
                    int candidats = 0;
                    foreach (var it in tel)
                    {
                        try
                        {
                            var journaux = svc.TrouverJournauxActifs(it.Id);
                            if (journaux.Count == 0 && !appliquee(it)) continue;
                            candidats++;
                            if (!memeUtilisateur && it.Hkcu) { res.IgnoreesAutreCompte.Add(it.Id); continue; }

                            ItemOutcome o = null;
                            bool parJournal = journaux.Count > 0;
                            if (parJournal)
                            {
                                foreach (var j in journaux)
                                {
                                    o = await svc.AnnulerParJournalAsync(j, it.Id);
                                    if (o == null || o.Status == OutcomeStatus.Failed) break;
                                    if (o.Status == OutcomeStatus.Skipped && !string.IsNullOrWhiteSpace(o.Message)) res.Remarques.Add(it.Id + " : " + o.Message.Trim());
                                }
                            }
                            else if (it.Reversible && !string.IsNullOrWhiteSpace(it.Revert)) o = await svc.ExecuterRevertAsync(it);
                            else { res.Ignorees.Add(it.Id); continue; }

                            if (o == null || o.Status == OutcomeStatus.Failed)
                                res.Echecs.Add(it.Id + " : " + (o == null || string.IsNullOrWhiteSpace(o.Message) ? "échec de l'annulation" : o.Message.Trim()));
                            else if (!parJournal && o.Status == OutcomeStatus.Skipped)
                            {
                                // Le script revert n'a rien restauré (ex. état d'origine connu du seul journal) : ni compté ni oublié.
                                res.Ignorees.Add(it.Id);
                                if (!string.IsNullOrWhiteSpace(o.Message)) res.Remarques.Add(it.Id + " : " + o.Message.Trim());
                            }
                            else
                            {
                                if (parJournal) res.AnnuleesParJournal++; else res.AnnuleesParRevert++;
                                restaures.Add(it.Id);
                            }
                        }
                        catch (Exception ex) { res.Echecs.Add(it.Id + " : " + ex.Message); }
                    }

                    if (restaures.Count > 0) svc.OublierDernieresApplications(restaures);

                    if (candidats == 0)
                    {
                        Message = erreurDetection != null ? "Détection impossible, rien n'a été restauré : " + erreurDetection
                                                          : "Rien à restaurer : aucune option appliquée par GamingTweaker.";
                        return res;
                    }

                    var sb = new StringBuilder("Tout restaurer : ");
                    sb.Append(res.AnnuleesParJournal).Append(" annulée(s) par le journal, ").Append(res.AnnuleesParRevert).Append(" remise(s) aux valeurs par défaut de Windows");
                    if (res.Echecs.Count > 0) sb.Append(", ").Append(res.Echecs.Count).Append(" échec(s) (").Append(string.Join(" ; ", res.Echecs)).Append(')');
                    if (res.Ignorees.Count > 0) sb.Append(", ").Append(res.Ignorees.Count).Append(" sans moyen de restauration (").Append(string.Join(", ", res.Ignorees)).Append(')');
                    if (res.IgnoreesAutreCompte.Count > 0) sb.Append(", ").Append(res.IgnoreesAutreCompte.Count).Append(" ignorée(s) car l'application tourne sous un autre compte que le bureau (").Append(string.Join(", ", res.IgnoreesAutreCompte)).Append(')');
                    sb.Append('.');
                    if (res.Remarques.Count > 0) sb.Append(' ').Append(string.Join(" ", res.Remarques));
                    Message = sb.ToString();
                }
                await DetectAsync();
                return res;
            }
            finally { RestaurationEnCours = false; }
        }
    }
}
