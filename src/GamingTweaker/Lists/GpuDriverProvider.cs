using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;

namespace GamingTweaker.Lists
{
    public class GpuInfo
    {
        public string Name { get; set; }
        /// <summary>Version Windows du pilote (« 32.0.16.2101 »).</summary>
        public string DriverVersion { get; set; }
    }

    public interface IGpuSource
    {
        IReadOnlyList<GpuInfo> Lire();
    }

    public class WmiGpuSource : IGpuSource
    {
        public IReadOnlyList<GpuInfo> Lire()
        {
            var res = new List<GpuInfo>();
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                    foreach (System.Management.ManagementObject o in s.Get())
                        res.Add(new GpuInfo { Name = Convert.ToString(o["Name"]), DriverVersion = Convert.ToString(o["DriverVersion"]) });
            }
            catch (Exception) { /* WMI indisponible : aucun GPU connu */ }
            return res;
        }
    }

    /// <summary>
    /// Branches du pilote NVIDIA (Game Ready, Studio) avec la dernière version trouvée. Si la recherche échoue : message et lien vers
    /// la page officielle. GPU AMD ou Intel : version installée et lien officiel, rien d'installable par l'application.
    /// </summary>
    public class GpuDriverProvider : IListProvider, IListInfo, IListDefaults
    {
        public const string LienAmd = "https://www.amd.com/fr/support/download/drivers.html";
        public const string LienIntel = "https://www.intel.fr/content/www/fr/fr/download-center/home.html";

        private readonly IGpuSource source;
        private readonly Func<string, string, Task<NvidiaDriverInfo>> chercher;

        public GpuDriverProvider(IGpuSource source = null, Func<string, string, Task<NvidiaDriverInfo>> chercher = null)
        {
            this.source = source ?? new WmiGpuSource();
            this.chercher = chercher ?? NvidiaLookup.FindLatestAsync;
        }

        public string ListId { get { return "pilote"; } }
        public string Message { get; private set; } = "";
        public string Lien { get; private set; } = "";
        public IReadOnlyList<string> SelectionParDefaut { get { return new[] { "GameReady" }; } }

        public async Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
        {
            Message = ""; Lien = "";
            var gpus = source.Lire().Where(g => !string.IsNullOrWhiteSpace(g.Name)).ToList();
            var nv = gpus.FirstOrDefault(g => g.Name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0);
            var autres = gpus.Where(g => g != nv).ToList();
            var notes = new List<string>();
            var res = new List<ListEntry>();

            if (nv != null)
            {
                var installee = NvidiaLookup.VersionNvidiaDepuisWindows(nv.DriverVersion) ?? nv.DriverVersion ?? "inconnue";
                var gr = await chercher(nv.Name, "GameReady");
                var st = await chercher(nv.Name, "Studio");
                res.Add(Entree("GameReady", "Game Ready (recommandé pour les jeux)", gr, installee));
                res.Add(Entree("Studio", "Studio (création de contenu, plus stable)", st, installee));
                notes.Add(nv.Name + " : pilote installé " + installee + ".");
                if (gr == null && st == null)
                    notes.Add("La recherche automatique de la dernière version a échoué (accès NVIDIA indisponible ou modifié). Téléchargez le pilote depuis la page officielle.");
                Lien = NvidiaLookup.PageOfficielle;
            }
            else if (gpus.Count == 0) notes.Add("Aucun GPU détecté.");

            foreach (var g in autres)
            {
                var amd = g.Name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || g.Name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0;
                var intel = g.Name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!amd && !intel) continue;
                notes.Add(g.Name + " : pilote installé " + (g.DriverVersion ?? "inconnu") + ". Mise à jour à faire depuis la page officielle " + (amd ? "AMD" : "Intel") + " (non automatique).");
                if (Lien.Length == 0) Lien = amd ? LienAmd : LienIntel;
            }
            Message = string.Join(" ", notes);
            return res;
        }

        private static ListEntry Entree(string cle, string titre, NvidiaDriverInfo info, string installee)
        {
            string detail;
            if (info == null) detail = "Dernière version introuvable pour ce GPU";
            else
            {
                var etat = Compare(installee, info.Version) >= 0 ? "à jour" : "mise à jour disponible";
                detail = "Dernière version : " + info.Version + " — installée : " + installee + " (" + etat + ")";
            }
            return new ListEntry { Key = cle, Titre = titre, Detail = detail, Risque = RiskLevel.Avance };
        }

        /// <summary>Compare deux versions « 621.01 » / « 621.14 » ; -1, 0 ou 1.</summary>
        public static int Compare(string a, string b)
        {
            Func<string, long[]> parts = s => (s ?? "").Split('.').Select(x => { long n; return long.TryParse(new string(x.Where(char.IsDigit).ToArray()), out n) ? n : 0; }).ToArray();
            var pa = parts(a); var pb = parts(b);
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                long x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }
    }
}
