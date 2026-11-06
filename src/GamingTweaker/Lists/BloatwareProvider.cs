using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using Newtonsoft.Json;

namespace GamingTweaker.Lists
{
    /// <summary>Une application connue de <c>bloatware-list.json</c>. <c>Liste</c> = identifiant du fournisseur (« bloatware » ou « bloatware-tierces »).</summary>
    public class KnownApp
    {
        [JsonProperty("package")] public string Package { get; set; }
        [JsonProperty("titre")] public string Titre { get; set; }
        [JsonProperty("detail")] public string Detail { get; set; }
        [JsonProperty("risque")] public RiskLevel Risque { get; set; }
        /// <summary>Identifiant Microsoft Store pour la réinstallation à l'annulation ; null si inconnu (réinstallation à la main).</summary>
        [JsonProperty("storeId")] public string StoreId { get; set; }
        [JsonProperty("liste")] public string Liste { get; set; }
    }

    public static class KnownApps
    {
        public static IReadOnlyList<KnownApp> LoadEmbedded()
        {
            AssemblyLoader.Register();
            return LoadCore();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IReadOnlyList<KnownApp> LoadCore()
        {
            using (var s = typeof(KnownApps).Assembly.GetManifestResourceStream("bloatware-list.json"))
            {
                if (s == null) throw new InvalidOperationException("Ressource introuvable : bloatware-list.json");
                using (var r = new StreamReader(s, new UTF8Encoding(false), true)) return ParseCore(r.ReadToEnd());
            }
        }

        public static List<KnownApp> Parse(string json)
        {
            AssemblyLoader.Register();
            return ParseCore(json);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<KnownApp> ParseCore(string json)
        {
            var l = JsonConvert.DeserializeObject<List<KnownApp>>(json);
            if (l == null || l.Any(a => a == null || string.IsNullOrWhiteSpace(a.Package))) throw new InvalidDataException("bloatware-list.json : liste invalide.");
            return l;
        }
    }

    /// <summary>Noms des paquets présents (utilisateurs et image provisionnée).</summary>
    public interface IInstalledAppsSource
    {
        Task<IReadOnlyList<string>> LireAsync(CancellationToken ct);
    }

    public class PowerShellInstalledApps : IInstalledAppsSource
    {
        private const string Script = @"
$ErrorActionPreference = 'SilentlyContinue'
Get-AppxPackage -AllUsers | ForEach-Object { $_.Name }
Get-AppxProvisionedPackage -Online | ForEach-Object { $_.DisplayName }
if ((Test-Path (Join-Path $env:SystemRoot 'SysWOW64\OneDriveSetup.exe')) -or (Test-Path (Join-Path $env:SystemRoot 'System32\OneDriveSetup.exe'))) { 'OneDrive' }
";

        public async Task<IReadOnlyList<string>> LireAsync(CancellationToken ct)
        {
            var sortie = await PowerShellQuery.RunAsync(Script, ct);
            return sortie.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Applications préinstallées : liste connue croisée avec ce qui est réellement présent. Le Store, WebView2, winget et les pilotes graphiques n'y figurent jamais.</summary>
    public class BloatwareProvider : IListProvider
    {
        private readonly IReadOnlyList<KnownApp> connues;
        private readonly IInstalledAppsSource source;

        public string ListId { get; private set; }

        public BloatwareProvider(string listId, IReadOnlyList<KnownApp> connues, IInstalledAppsSource source = null)
        {
            ListId = listId;
            this.connues = connues.Where(a => a.Liste == listId).ToList();
            this.source = source ?? new PowerShellInstalledApps();
        }

        public static BloatwareProvider Standard(IInstalledAppsSource source = null) => new BloatwareProvider("bloatware", KnownApps.LoadEmbedded(), source);
        public static BloatwareProvider Tierces(IInstalledAppsSource source = null) => new BloatwareProvider("bloatware-tierces", KnownApps.LoadEmbedded(), source);

        /// <summary>Seules les applications connues ET présentes sont listées (ordre de la liste connue).</summary>
        public static IReadOnlyList<ListEntry> Match(IEnumerable<string> installed, IReadOnlyList<KnownApp> known)
        {
            var presents = new HashSet<string>(installed ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var res = new List<ListEntry>();
            foreach (var a in known)
            {
                if (!presents.Contains(a.Package)) continue;
                var reinstall = string.IsNullOrEmpty(a.StoreId) ? "annulation : réinstallation à faire à la main" : "annulation : réinstallation depuis le Store";
                res.Add(new ListEntry
                {
                    Key = a.Package,
                    Titre = a.Titre + " (" + a.Package + ")",
                    Detail = (string.IsNullOrEmpty(a.Detail) ? "" : a.Detail + " — ") + reinstall,
                    Risque = a.Risque,
                });
            }
            return res;
        }

        public async Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
        {
            var installes = await source.LireAsync(ct);
            return Match(installes, connues);
        }
    }
}
