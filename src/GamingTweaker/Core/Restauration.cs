using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Opérations de « Tout restaurer » : annulation par le journal le plus récent qui contient l'option,
    /// ou, faute de journal, exécution du script revert de l'option.
    /// </summary>
    public interface IRestauration
    {
        /// <summary>Chemin du journal le plus récent qui contient encore des entrées à annuler pour l'option, ou null.</summary>
        string TrouverJournalActif(string itemId);
        Task<ItemOutcome> AnnulerParJournalAsync(string journalPath, string itemId);
        Task<ItemOutcome> ExecuterRevertAsync(CatalogItem item);
    }

    public class RestaurationService : IRestauration
    {
        private readonly DataPaths paths;

        public RestaurationService(DataPaths paths) { this.paths = paths; }

        public string TrouverJournalActif(string itemId)
        {
            var dir = paths == null ? null : paths.SessionsDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            foreach (var d in Directory.GetDirectories(dir).OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal))
            {
                var j = Path.Combine(d, "journal.jsonl");
                if (!File.Exists(j)) continue;
                try { if (Journal.Active(Journal.Read(j)).Any(e => e.Item == itemId)) return j; }
                catch (InvalidDataException) { /* journal douteux : on ne s'en sert pas pour annuler */ }
            }
            return null;
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
