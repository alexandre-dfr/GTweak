using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Une entrée de journal.jsonl. kind : registry, defender, service, task, power, bcd, hosts, app, quarantine, regkey (clé supprimée, export .reg), regkeynew (clé créée).
    /// Registry : target = « chemin|nom », valueType = DWord/QWord/String/ExpandString/MultiString/Binary (Binary en base64).
    /// </summary>
    public class JournalEntry
    {
        [JsonProperty("item")] public string Item { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("target")] public string Target { get; set; }
        [JsonProperty("existed")] public bool Existed { get; set; }
        [JsonProperty("before")] public JToken Before { get; set; }
        [JsonProperty("after")] public JToken After { get; set; }
        [JsonProperty("valueType")] public string ValueType { get; set; }
    }

    public static class Journal
    {
        /// <summary>
        /// Lit le journal (marqueurs « undone » inclus). Fichier absent : liste vide. Dernière ligne non vide illisible (écriture interrompue) : ignorée.
        /// Ligne illisible au milieu : InvalidDataException en français (on refuse d'annuler à partir d'un journal douteux).
        /// </summary>
        public const string UndoneKind = "undone";

        /// <summary>
        /// Entrées encore à annuler : pour chaque option, celles écrites après son dernier marqueur « undone » (marqueurs exclus).
        /// L'ordre du journal est conservé.
        /// </summary>
        public static List<JournalEntry> Active(IEnumerable<JournalEntry> entries)
        {
            var all = entries.ToList();
            var lastMarker = new Dictionary<string, int>();
            for (int i = 0; i < all.Count; i++) if (all[i].Kind == UndoneKind) lastMarker[all[i].Item] = i;
            var res = new List<JournalEntry>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Kind == UndoneKind) continue;
                int m;
                if (lastMarker.TryGetValue(all[i].Item, out m) && i < m) continue;
                res.Add(all[i]);
            }
            return res;
        }

        public static IReadOnlyList<JournalEntry> Read(string path)
        {
            AssemblyLoader.Register();
            return ReadCore(path);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IReadOnlyList<JournalEntry> ReadCore(string path)
        {
            var res = new List<JournalEntry>();
            if (!File.Exists(path)) return res;
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var r = new StreamReader(fs, new UTF8Encoding(false), true)) text = r.ReadToEnd();
            var lines = text.Split('\n');
            int last = -1;
            for (int i = 0; i < lines.Length; i++) if (lines[i].Trim().Length > 0) last = i;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].Trim();
                if (l.Length == 0) continue;
                JournalEntry e = null;
                try { e = JsonConvert.DeserializeObject<JournalEntry>(l); } catch (JsonException) { }
                if (e == null || string.IsNullOrEmpty(e.Item) || string.IsNullOrEmpty(e.Kind))
                {
                    if (i == last) continue;
                    throw new InvalidDataException("Le journal d'annulation est corrompu à la ligne " + (i + 1) + " (" + path + "). Aucune annulation n'a été faite.");
                }
                res.Add(e);
            }
            return res;
        }
    }
}
