using System;
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
        /// Un marqueur avec « target » (fichier restauré ou supprimé depuis la quarantaine) n'annule que les entrées antérieures
        /// de la même option et de la même cible. L'ordre du journal est conservé.
        /// </summary>
        public static List<JournalEntry> Active(IEnumerable<JournalEntry> entries)
        {
            var all = entries.ToList();
            var lastMarker = new Dictionary<string, int>();
            var lastTargeted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Kind != UndoneKind) continue;
                if (string.IsNullOrEmpty(all[i].Target)) lastMarker[all[i].Item] = i;
                else lastTargeted[all[i].Item + "\u0001" + all[i].Target] = i;
            }
            var res = new List<JournalEntry>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Kind == UndoneKind) continue;
                int m;
                if (lastMarker.TryGetValue(all[i].Item, out m) && i < m) continue;
                if (all[i].Target != null && lastTargeted.TryGetValue(all[i].Item + "\u0001" + all[i].Target, out m) && i < m) continue;
                res.Add(all[i]);
            }
            return res;
        }

        /// <summary>
        /// Ajoute un marqueur « undone » ciblé (option + cible) à la fin du journal, comme Add-GTJournalLine : un fragment final
        /// sans saut de ligne (écriture interrompue) est d'abord tronqué, puis la ligne est forcée sur disque.
        /// </summary>
        public static void MarquerAnnule(string path, string item, string target)
        {
            AssemblyLoader.Register();
            MarquerCore(path, item, target);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MarquerCore(string path, string item, string target)
        {
            var ligne = JsonConvert.SerializeObject(new JournalEntry { Item = item, Kind = UndoneKind, Target = target },
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }) + "\n";
            var octets = new UTF8Encoding(false).GetBytes(ligne);
            using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                if (fs.Length > 0)
                {
                    var tout = new byte[fs.Length];
                    int lu = 0;
                    while (lu < tout.Length) { int n = fs.Read(tout, lu, tout.Length - lu); if (n <= 0) break; lu += n; }
                    if (tout[tout.Length - 1] != (byte)'\n') fs.SetLength(Array.LastIndexOf(tout, (byte)'\n') + 1);
                }
                fs.Position = fs.Length;
                fs.Write(octets, 0, octets.Length);
                fs.Flush(true);
            }
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
