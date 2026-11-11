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
        public static string Build(IEnumerable<CatalogItem> items, Selection s, string journalPath, string quarantineDir = null)
        {
            AssemblyLoader.Register();
            return BuildCore(items, s, journalPath, quarantineDir);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string BuildCore(IEnumerable<CatalogItem> items, Selection s, string journalPath, string quarantineDir)
        {
            var sb = new StringBuilder();
            sb.Append("param([string[]]$SkipIds = @())\r\n");
            // -File transmet « a,b » comme une seule chaîne : on la redécoupe.
            sb.Append("$SkipIds = @($SkipIds | ForEach-Object { $_ -split ',' } | Where-Object { $_ })\r\n");
            sb.Append(Header(journalPath, quarantineDir));

            // Tri stable par « ordre » puis par position dans le catalogue.
            var selected = items.Where(i => s.OptionIds.Contains(i.Id)).OrderBy(i => i.Ordre).ToList();
            // Options choisies, visibles de tous les scripts (exclusions entre options, ex. deux plans d'alimentation).
            sb.Append("$GT_SelectedIds = [string[]]@(").Append(string.Join(", ", s.OptionIds.OrderBy(x => x, StringComparer.Ordinal).Select(Quote))).Append(")\r\n");
            // Plan : permet au runner de connaître toutes les options avant la première (annulation, arrêt brutal).
            foreach (var it in selected)
                sb.Append("if ($SkipIds -notcontains ").Append(Quote(it.Id)).Append(") { Write-GTEvent 'plan' ").Append(Quote(it.Id)).Append(" }\r\n");

            foreach (var it in selected)
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

        /// <summary>En-tête commun (encodage, journal, quarantaine, fonctions communes), partagé avec Detection.</summary>
        internal static string Header(string journalPath, string quarantineDir) { return Header(journalPath, quarantineDir, true); }

        /// <param name="avecWinget">Faux pour la détection et l'estimation (lecture seule) : winget n'y sert pas et sa résolution lance PowerShell.</param>
        internal static string Header(string journalPath, string quarantineDir, bool avecWinget)
        {
            var sb = new StringBuilder();
            sb.Append("[Console]::OutputEncoding = [Text.Encoding]::UTF8\r\n");
            sb.Append("$ErrorActionPreference = 'Stop'\r\n");
            // Modules chargés uniquement depuis $PSHOME\Modules (jamais Documents, ni un chemin hérité de l'utilisateur).
            sb.Append(PowerShellLanceur.LigneModules);
            sb.Append("$GT_JournalPath = ").Append(Quote(journalPath ?? "")).Append("\r\n");
            sb.Append("$GT_QuarantineDir = ").Append(quarantineDir == null ? "$null" : Quote(quarantineDir)).Append("\r\n");
            // Téléchargements (pilote NVIDIA) : sous la racine protégée, chemin fixé par l'outil et non par $env:ProgramFiles.
            sb.Append("$GT_DownloadRoot = ").Append(Quote(Path.Combine(DataPaths.RacineProtegeeParDefaut, "Telechargements"))).Append("\r\n");
            // winget vérifié par l'outil (paquet DesktopAppInstaller sous Program Files\WindowsApps) : le PATH des scripts est réduit aux dossiers
            // système, l'alias WindowsApps n'y figure pas. $null : winget absent (mises à jour d'applications ignorées, réinstallation à la main).
            var winget = avecWinget ? SystemExe.Winget() : null;
            sb.Append("$GT_WingetPath = ").Append(winget == null ? "$null" : Quote(winget)).Append("\r\n");
            sb.Append(ReadCommon()).Append("\r\n");
            return sb.ToString();
        }

        // PowerShell traite aussi U+2018, U+2019, U+201A et U+201B comme des guillemets simples : on les double tous.
        internal static string Quote(string v)
        {
            var sb = new StringBuilder("'");
            foreach (var c in v)
            {
                sb.Append(c);
                if (c == '\'' || c == '‘' || c == '’' || c == '‚' || c == '‛') sb.Append(c);
            }
            return sb.Append('\'').ToString();
        }

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
