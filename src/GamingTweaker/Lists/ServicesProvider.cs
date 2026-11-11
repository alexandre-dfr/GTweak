using System;
using System.Collections.Generic;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using Microsoft.Win32;

namespace GamingTweaker.Lists
{
    public class ServiceInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        /// <summary>Type de démarrage en français (« Manuel », « Automatique »...).</summary>
        public string StartType { get; set; }
    }

    /// <summary>Lecture des services présents (injectable pour les tests). Seuls les noms demandés sont lus ; un service absent n'est pas renvoyé.</summary>
    public interface IServiceSource
    {
        IReadOnlyDictionary<string, ServiceInfo> Lire(IEnumerable<string> noms);
    }

    public static class ServiceStartLabel
    {
        /// <summary>Valeur « Start » du registre (0 à 4) et démarrage différé, en français.</summary>
        public static string From(int start, bool differe)
        {
            switch (start)
            {
                case 0: return "Démarrage";
                case 1: return "Système";
                case 2: return differe ? "Automatique (début différé)" : "Automatique";
                case 3: return "Manuel";
                case 4: return "Désactivé";
                default: return "Inconnu";
            }
        }
    }

    /// <summary>Lit le type de démarrage dans le registre (comme Get-GTServiceStart) et le nom d'affichage par le gestionnaire de services.</summary>
    public class RegistryServiceSource : IServiceSource
    {
        public IReadOnlyDictionary<string, ServiceInfo> Lire(IEnumerable<string> noms)
        {
            var res = new Dictionary<string, ServiceInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in noms)
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + n))
                    {
                        if (k == null) continue;
                        var start = k.GetValue("Start") as int?;
                        if (start == null) continue;
                        var differe = (k.GetValue("DelayedAutostart") as int? ?? 0) == 1;
                        string affichage = n;
                        try { using (var sc = new ServiceController(n)) affichage = sc.DisplayName; } catch (Exception) { }
                        res[n] = new ServiceInfo { Name = n, DisplayName = affichage, StartType = ServiceStartLabel.From(start.Value, differe) };
                    }
                }
                catch (Exception) { /* service illisible : non proposé */ }
            }
            return res;
        }
    }

    /// <summary>
    /// Liste fixe de services candidats, filtrée sur ceux qui existent sur la machine. Les scripts apply portent la même liste
    /// (test de cohérence) et refusent tout nom qui n'en fait pas partie.
    /// </summary>
    public class ServicesProvider : IListProvider
    {
        /// <summary>Spooler (impression) et StorSvc (stockage, Store) ne sont pas proposés : en manuel, ils ne redémarrent pas à la demande.</summary>
        public static readonly string[] Services =
            { "Fax", "RemoteRegistry", "MapsBroker", "PhoneSvc", "RetailDemo", "WpcMonSvc", "lfsvc", "CscService", "SharedAccess" };

        public static readonly string[] XboxServices = { "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc" };

        private readonly IReadOnlyList<string> candidats;
        private readonly RiskLevel risque;
        private readonly IServiceSource source;

        public string ListId { get; private set; }

        public ServicesProvider(string listId, IReadOnlyList<string> candidats, RiskLevel risque, IServiceSource source = null)
        {
            ListId = listId; this.candidats = candidats; this.risque = risque; this.source = source ?? new RegistryServiceSource();
        }

        public static ServicesProvider Standard(IServiceSource source = null) => new ServicesProvider("services", Services, RiskLevel.Avance, source);
        public static ServicesProvider Xbox(IServiceSource source = null) => new ServicesProvider("xbox-services", XboxServices, RiskLevel.Risque, source);

        public Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
        {
            return Task.Run<IReadOnlyList<ListEntry>>(() =>
            {
                var presents = source.Lire(candidats);
                var res = new List<ListEntry>();
                foreach (var n in candidats)
                {
                    ct.ThrowIfCancellationRequested();
                    ServiceInfo info;
                    if (!presents.TryGetValue(n, out info) || info == null) continue;
                    var affichage = string.IsNullOrWhiteSpace(info.DisplayName) ? n : info.DisplayName;
                    res.Add(new ListEntry
                    {
                        Key = n,
                        Titre = affichage + " (" + n + ")",
                        Detail = "Démarrage actuel : " + (info.StartType ?? "Inconnu"),
                        Risque = risque,
                    });
                }
                return res;
            }, ct);
        }
    }
}
