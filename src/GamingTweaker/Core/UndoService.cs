using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace GamingTweaker.Core
{
    public class Conflict
    {
        public string ItemId { get; set; }
        public string Target { get; set; }
        /// <summary>Valeur écrite par l'application (dernière entrée du journal).</summary>
        public string Attendu { get; set; }
        /// <summary>Valeur actuelle sur la machine.</summary>
        public string Actuel { get; set; }
    }

    public static class UndoService
    {
        public const string Absent = "(absent)";

        /// <summary>
        /// Script d'annulation exécutable par Runner : une option synthétique par id, dans l'ordre inverse de première apparition dans le journal.
        /// Les ids sans entrée de journal arrivent en dernier (Restore-GTItem les signale en « ignoré »).
        /// </summary>
        public static string BuildScript(string journalPath, IEnumerable<string> itemIds)
        {
            AssemblyLoader.Register();
            return BuildScriptCore(journalPath, itemIds);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string BuildScriptCore(string journalPath, IEnumerable<string> itemIds)
        {
            var ids = itemIds.ToList();
            var wanted = new HashSet<string>(ids);
            var order = Journal.Active(Journal.Read(journalPath)).Select(e => e.Item).Distinct().Where(wanted.Contains).Reverse().ToList();
            order.AddRange(ids.Where(i => !order.Contains(i)).Distinct());
            foreach (var id in order)
                if (!Regex.IsMatch(id ?? "", @"^[a-z0-9][a-z0-9.\-]*$"))
                    throw new ArgumentException("Identifiant d'option invalide : " + id);
            var items = order.Select(id => new CatalogItem
            {
                Id = id, Menu = "Annulation", Groupe = "Annulation", Titre = id, Risque = RiskLevel.Sur,
                Reversible = false, Detect = "", Apply = "Restore-GTItem -Item '" + id + "'",
            }).ToList();
            return ScriptBuilder.Build(items, new Selection { OptionIds = new HashSet<string>(order) }, journalPath);
        }

        /// <summary>
        /// Valeurs modifiées depuis l'application. Géré : registre (valeur actuelle vs dernier « after » de la cible) et services (type de démarrage).
        /// Les autres types d'entrée (tâches, énergie, BCD, hosts, applications, quarantaine) ne produisent jamais de conflit.
        /// </summary>
        public static List<Conflict> FindConflicts(string journalPath, IEnumerable<string> itemIds)
        {
            AssemblyLoader.Register();
            return FindConflictsCore(journalPath, itemIds);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<Conflict> FindConflictsCore(string journalPath, IEnumerable<string> itemIds)
        {
            var wanted = new HashSet<string>(itemIds);
            var res = new List<Conflict>();
            foreach (var g in Journal.Active(Journal.Read(journalPath)).Where(e => wanted.Contains(e.Item)).GroupBy(e => new { e.Item, e.Kind, e.Target }))
            {
                var last = g.Last();
                string attendu, actuel;
                if (last.Kind == "registry")
                {
                    attendu = Norm(last.After);
                    actuel = ReadRegistry(last.Target);
                }
                else if (last.Kind == "service")
                {
                    attendu = Norm(last.After);
                    actuel = ReadService(last.Target);
                    if (actuel == null) continue;
                    if (attendu == "AutomaticDelayed" && actuel == "Automatic") continue; // ServiceController ne distingue pas le démarrage différé
                }
                else continue;
                if (attendu != actuel)
                    res.Add(new Conflict { ItemId = last.Item, Target = last.Target, Attendu = attendu, Actuel = actuel });
            }
            return res;
        }

        internal static string Norm(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return Absent;
            if (t.Type == JTokenType.Array) return string.Join("\n", t.Select(x => x.ToString()));
            return t.ToString();
        }

        private static string ReadService(string name)
        {
            try { using (var sc = new ServiceController(name)) return sc.StartType.ToString(); }
            catch (Exception) { return null; }
        }

        internal static string ReadRegistry(string target)
        {
            int p = target.IndexOf('|');
            if (p < 0) return Absent;
            string path = target.Substring(0, p), name = target.Substring(p + 1);
            int c = path.IndexOf(":\\", StringComparison.Ordinal);
            if (c < 0) return Absent;
            RegistryKey root;
            switch (path.Substring(0, c).ToUpperInvariant())
            {
                case "HKCU": root = Registry.CurrentUser; break;
                case "HKLM": root = Registry.LocalMachine; break;
                case "HKCR": root = Registry.ClassesRoot; break;
                case "HKU": root = Registry.Users; break;
                default: return Absent;
            }
            using (var k = root.OpenSubKey(path.Substring(c + 2)))
            {
                if (k == null || Array.IndexOf(k.GetValueNames(), name) < 0) return Absent;
                var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (v is int) return ((uint)(int)v).ToString();
                if (v is string[]) return string.Join("\n", (string[])v);
                if (v is byte[]) return Convert.ToBase64String((byte[])v);
                return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
