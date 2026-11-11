using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Opérations de « Tout restaurer » : annulation par le journal le plus récent qui contient l'option,
    /// ou, faute de journal, exécution du script revert de l'option.
    /// </summary>
    public interface IRestauration
    {
        /// <summary>Journaux qui ont encore des entrées à annuler pour l'option, du plus récent au plus ancien.</summary>
        IReadOnlyList<string> TrouverJournauxActifs(string itemId);
        Task<ItemOutcome> AnnulerParJournalAsync(string journalPath, string itemId);
        Task<ItemOutcome> ExecuterRevertAsync(CatalogItem item);
        /// <summary>Verrou d'instance (le même que l'exécution des options) ; null si une autre exécution est en cours.</summary>
        IDisposable PrendreVerrou();
        /// <summary>Faux si l'application tourne sous un autre compte que le bureau (HKCU n'est alors pas celui de l'utilisateur).</summary>
        bool MemeUtilisateur();
        /// <summary>Retire ces options de last-applied.json (elles ne sont plus « appliquées par l'outil »).</summary>
        void OublierDernieresApplications(IEnumerable<string> ids);
    }

    public class RestaurationService : IRestauration
    {
        private readonly DataPaths paths;
        private readonly List<DataPaths> racines;
        private readonly Func<bool> sameUser;
        private readonly string lockName;

        /// <param name="paths">Racine unique des données (sous %ProgramFiles%\GamingTweaker : l'interface et la routine y écrivent leurs sessions).</param>
        public RestaurationService(DataPaths paths, Func<bool> sameUser = null, string lockName = null)
        {
            this.paths = paths; this.sameUser = sameUser; this.lockName = lockName;
            racines = new List<DataPaths>();
            if (paths != null) racines.Add(paths);
        }

        public IDisposable PrendreVerrou() { return SingleInstance.TryAcquire(lockName); }

        public bool MemeUtilisateur() { return sameUser == null || sameUser(); }

        public void OublierDernieresApplications(IEnumerable<string> ids)
        {
            AssemblyLoader.Register();
            try { Oublier(ids.ToList()); }
            catch (Exception) { /* non bloquant */ }
        }

        /// <summary>Chaque racine garde son last-applied.json ; une option annulée n'est plus appliquée, elle est retirée de chacun.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void Oublier(List<string> ids)
        {
            foreach (var r in racines)
            {
                try
                {
                    if (string.IsNullOrEmpty(r.MachineDir)) continue;
                    var f = Path.Combine(r.MachineDir, "last-applied.json");
                    if (!File.Exists(f)) continue;
                    var liste = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(f, Encoding.UTF8)) ?? new List<string>();
                    var reste = liste.Except(ids, StringComparer.Ordinal).ToList();
                    File.WriteAllText(f, JsonConvert.SerializeObject(reste, Formatting.Indented), new UTF8Encoding(false));
                }
                catch (Exception) { /* non bloquant, racine suivante */ }
            }
        }

        /// <summary>Dossiers de session de toutes les racines, du plus récent au plus ancien (noms datés yyyyMMdd-HHmmss).</summary>
        private IEnumerable<string> DossiersSessions()
        {
            var tous = new List<string>();
            foreach (var r in racines)
            {
                var dir = r.SessionsDir;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                tous.AddRange(Directory.GetDirectories(dir));
            }
            return tous.OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal);
        }

        public IReadOnlyList<string> TrouverJournauxActifs(string itemId)
        {
            var res = new List<string>();
            foreach (var d in DossiersSessions())
            {
                var j = Path.Combine(d, "journal.jsonl");
                if (!File.Exists(j)) continue;
                try { if (Journal.Active(Journal.Read(j)).Any(e => e.Item == itemId)) res.Add(j); }
                catch (InvalidDataException) { /* journal douteux : on ne s'en sert pas pour annuler */ }
            }
            return res;
        }

        public async Task<ItemOutcome> AnnulerParJournalAsync(string journalPath, string itemId)
        {
            var script = UndoService.BuildScript(journalPath, new[] { itemId });
            var r = await Runner.RunAsync(script, null, CancellationToken.None, _ => 300);
            return r.Items.FirstOrDefault(i => i.Id == itemId)
                ?? new ItemOutcome { Id = itemId, Status = OutcomeStatus.Failed, Message = "aucun résultat de l'annulation" };
        }

        /// <summary>Exécute le script revert (valeurs par défaut de Windows), qui n'utilise pas le journal.</summary>
        public async Task<ItemOutcome> ExecuterRevertAsync(CatalogItem item)
        {
            var synthetique = new CatalogItem
            {
                Id = item.Id, Menu = item.Menu, Groupe = item.Groupe, Titre = item.Titre, Risque = item.Risque,
                Reversible = false, Detect = "", Apply = item.Revert,
            };
            var script = ScriptBuilder.Build(new[] { synthetique }, new Selection { OptionIds = new System.Collections.Generic.HashSet<string> { item.Id } }, "");
            var r = await Runner.RunAsync(script, null, CancellationToken.None, _ => item.DelaiSec > 0 ? item.DelaiSec : 300);
            return r.Items.FirstOrDefault(i => i.Id == item.Id)
                ?? new ItemOutcome { Id = item.Id, Status = OutcomeStatus.Failed, Message = "aucun résultat du script de retour aux valeurs par défaut" };
        }
    }
}
