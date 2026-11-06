using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace GamingTweaker.Core
{
    public class NvidiaDriverInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
    }

    /// <summary>Un produit NVIDIA de la recherche manuelle : série (psid) et produit (pfid).</summary>
    public class NvidiaProduct
    {
        public string Psid { get; set; }
        public string Pfid { get; set; }
    }

    /// <summary>
    /// Recherche de la dernière version du pilote GeForce par l'accès NVIDIA non documenté (lookupValueSearch.aspx puis
    /// AjaxDriverService.php). Toute erreur renvoie null : l'interface propose alors la page officielle.
    /// </summary>
    public static class NvidiaLookup
    {
        public const string PageOfficielle = "https://www.nvidia.com/fr-fr/drivers/";
        public const string UrlProduits = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";
        private static readonly Regex UrlValide = new Regex(@"^https://([a-z0-9\-]+\.)*nvidia\.com/\S+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool UrlAcceptee(string url) { return url != null && UrlValide.IsMatch(url); }

        /// <summary>Réponse JSON de DriverManualLookup → version et adresse ; null si la réponse est inconnue, en échec ou si l'adresse n'est pas en https sur nvidia.com.</summary>
        public static NvidiaDriverInfo Parse(string json)
        {
            AssemblyLoader.Register();
            return ParseCore(json);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static NvidiaDriverInfo ParseCore(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return null;
                var o = JObject.Parse(json);
                if ((string)o["Success"] != "1") return null;
                var ids = o["IDS"] as JArray;
                if (ids == null || ids.Count == 0) return null;
                var d = ids[0]["downloadInfo"];
                if (d == null || (string)d["Success"] != "1") return null;
                var version = ((string)d["Version"] ?? "").Trim();
                var url = ((string)d["DownloadURL"] ?? "").Trim();
                if (version.Length == 0 || !UrlAcceptee(url)) return null;
                return new NvidiaDriverInfo { Version = version, Url = url };
            }
            catch (Exception) { return null; }
        }

        /// <summary>Produits dont le nom est celui du GPU (« NVIDIA GeForce RTX 2050 » devient « GeForce RTX 2050 »), dans l'ordre de la réponse XML de lookupValueSearch.</summary>
        public static List<NvidiaProduct> ParseProduits(string xml, string gpuName)
        {
            var res = new List<NvidiaProduct>();
            try
            {
                var nom = Regex.Replace(gpuName ?? "", @"^\s*NVIDIA\s+", "", RegexOptions.IgnoreCase).Trim();
                if (nom.Length == 0) return res;
                foreach (var v in XDocument.Parse(xml).Descendants("LookupValue"))
                {
                    var n = (string)v.Element("Name");
                    var pfid = (string)v.Element("Value");
                    var psid = (string)v.Attribute("ParentID");
                    if (!string.Equals(n == null ? null : n.Trim(), nom, StringComparison.OrdinalIgnoreCase)) continue;
                    if (psid == null || pfid == null || !Regex.IsMatch(psid, @"^\d+$") || !Regex.IsMatch(pfid, @"^\d+$")) continue;
                    res.Add(new NvidiaProduct { Psid = psid, Pfid = pfid });
                }
            }
            catch (Exception) { /* XML illisible : aucun produit */ }
            return res;
        }

        /// <summary>« 32.0.16.2101 » (version Windows du pilote) devient « 621.01 » (numérotation NVIDIA) ; null si le format est inattendu.</summary>
        public static string VersionNvidiaDepuisWindows(string windowsVersion)
        {
            var m = Regex.Match(windowsVersion ?? "", @"^\d+\.\d+\.(\d+)\.(\d+)$");
            if (!m.Success) return null;
            var chiffres = m.Groups[1].Value + m.Groups[2].Value.PadLeft(4, '0');
            if (chiffres.Length < 5) return null;
            chiffres = chiffres.Substring(chiffres.Length - 5);
            return chiffres.Substring(0, 3) + "." + chiffres.Substring(3);
        }

        /// <summary>branche : « GameReady » ou « Studio ». Renvoie null en cas d'échec (jamais d'exception).</summary>
        public static Task<NvidiaDriverInfo> FindLatestAsync(string gpuName, string branche)
        {
            return FindLatestAsync(gpuName, branche, HttpGetAsync, SystemeEstWindows11() ? 135 : 57);
        }

        /// <summary>osId : 135 = Windows 11, 57 = Windows 10 64 bits.</summary>
        public static async Task<NvidiaDriverInfo> FindLatestAsync(string gpuName, string branche, Func<string, Task<string>> get, int osId)
        {
            try
            {
                var crd = branche == "Studio" ? 1 : 0;
                var produits = ParseProduits(await get(UrlProduits), gpuName);
                foreach (var p in produits)
                {
                    var json = await get("https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid="
                        + p.Psid + "&pfid=" + p.Pfid + "&osID=" + osId + "&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=" + crd + "&qnf=0&sort1=1&numberOfResults=1");
                    var info = Parse(json);
                    if (info != null) return info;
                }
            }
            catch (Exception) { /* réseau ou format modifié : repli sur la page officielle */ }
            return null;
        }

        private static bool SystemeEstWindows11()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build;
                    return k != null && int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber")), out build) && build >= 22000;
                }
            }
            catch (Exception) { return false; }
        }

        private static async Task<string> HttpGetAsync(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                return await c.GetStringAsync(url);
        }
    }
}
