using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;

namespace GamingTweaker.Lists
{
    /// <summary>
    /// Mises à jour logicielles de Windows Update non installées et non masquées (pilotes exclus), cherchées par l'API COM
    /// Microsoft.Update.Session. Chaque élément porte son numéro KB. Lecture seule : rien n'est téléchargé ni installé ici.
    /// </summary>
    public class WindowsUpdateProvider : IListProvider, IListInfo
    {
        private static readonly Regex Kb = new Regex(@"^KB\d{4,8}$", RegexOptions.CultureInvariant);

        // Même requête et même filtre que upd.windows.apply.ps1. Sortie : KB|titre|taille en Mo.
        private const string Script = @"
$ErrorActionPreference = 'Stop'
$s = New-Object -ComObject Microsoft.Update.Session
$found = $s.CreateUpdateSearcher().Search(""IsInstalled=0 and Type='Software' and IsHidden=0"")
foreach ($u in $found.Updates) {
    if ($u.InstallationBehavior.CanRequestUserInput) { continue }
    $kbs = @($u.KBArticleIDs)
    if ($kbs.Count -eq 0) { continue }
    $mb = [int][Math]::Round([double]$u.MaxDownloadSize / 1MB)
    [Console]::Out.WriteLine('KB' + $kbs[0] + '|' + (($u.Title -replace '\|', '/') -replace '\s+', ' ') + '|' + $mb)
}
";

        private readonly Func<CancellationToken, Task<string>> executer;

        public WindowsUpdateProvider(Func<CancellationToken, Task<string>> executer = null)
        {
            this.executer = executer ?? (ct => PowerShellQuery.RunAsync(Script, ct, 300000));
        }

        public string ListId { get { return "windowsUpdate"; } }
        public string Message { get; private set; } = "";
        public string Lien { get { return ""; } }

        /// <summary>Sortie « KB|titre|Mo » → entrées (lignes mal formées ignorées, doublons de KB écartés).</summary>
        public static IReadOnlyList<ListEntry> Parse(string output)
        {
            var res = new List<ListEntry>();
            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = l.Trim().Split('|');
                if (p.Length < 2 || !Kb.IsMatch(p[0]) || !vus.Add(p[0])) continue;
                int mo;
                var taille = p.Length > 2 && int.TryParse(p[2], out mo) && mo > 0 ? " — " + mo + " Mo" : "";
                res.Add(new ListEntry { Key = p[0], Titre = p[1].Trim().Length == 0 ? p[0] : p[1].Trim(), Detail = p[0] + taille, Risque = RiskLevel.Sur });
            }
            return res;
        }

        public async Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
        {
            var liste = Parse(await executer(ct));
            Message = liste.Count == 0 ? "Aucune mise à jour logicielle de Windows en attente." : "";
            return liste;
        }
    }
}
