using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    public static class ScriptBuilder
    {
        public static string Build(IEnumerable<CatalogItem> items, Selection s, string journalPath)
        {
            AssemblyLoader.Register();
            return BuildCore(items, s, journalPath);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string BuildCore(IEnumerable<CatalogItem> items, Selection s, string journalPath)
        {
            var sb = new StringBuilder();
            sb.Append("param([string[]]$SkipIds = @())\r\n");
            // -File transmet « a,b » comme une seule chaîne : on la redécoupe.
            sb.Append("$SkipIds = @($SkipIds | ForEach-Object { $_ -split ',' } | Where-Object { $_ })\r\n");
            sb.Append("[Console]::OutputEncoding = [Text.Encoding]::UTF8\r\n");
            sb.Append("$ErrorActionPreference = 'Stop'\r\n");
            sb.Append("$GT_JournalPath = ").Append(Quote(journalPath ?? "")).Append("\r\n");
            sb.Append(ReadCommon()).Append("\r\n");

            foreach (var it in items.Where(i => s.OptionIds.Contains(i.Id)))
            {
                var sel = new List<string>();
                List<string> l;
                if (s.Listes != null && s.Listes.TryGetValue(it.Id, out l) && l != null) sel = l;
                // Pas de saut de ligne : un « '@ » en début de ligne ne peut pas apparaître.
                var json = JsonConvert.SerializeObject(sel, Formatting.None).Replace("\r", "").Replace("\n", "");

                sb.Append("if ($SkipIds -notcontains ").Append(Quote(it.Id)).Append(") {\r\n");
                sb.Append("$Selection = [string[]]@(ConvertFrom-Json -InputObject @'\r\n").Append(json).Append("\r\n'@ | ForEach-Object { $_ })\r\n");
                sb.Append("Invoke-GTItem ").Append(Quote(it.Id)).Append(" {\r\n");
                sb.Append(it.Apply ?? "").Append("\r\n}\r\n}\r\n");
            }
            return sb.ToString();
        }

        private static string Quote(string v) => "'" + v.Replace("'", "''") + "'";

        private static string ReadCommon()
        {
            var asm = typeof(ScriptBuilder).Assembly;
            using (var st = asm.GetManifestResourceStream("runtime/Common.ps1"))
            {
                if (st == null) throw new InvalidOperationException("Ressource runtime/Common.ps1 introuvable.");
                using (var r = new StreamReader(st, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }
}
