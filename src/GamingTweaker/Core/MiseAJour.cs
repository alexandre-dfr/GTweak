using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GamingTweaker.Core
{
    /// <summary>Version plus récente publiée sur GitHub.</summary>
    public class VersionDisponible
    {
        public Version Version { get; set; }
        /// <summary>Page de la release (https://github.com/&lt;dépôt&gt;/releases/…), ouverte dans le navigateur ; rien n'est téléchargé par l'appli.</summary>
        public string Url { get; set; }
        /// <summary>Lien de téléchargement de GamingTweaker.exe dans cette release (null : pas de mise à jour en un clic).</summary>
        public string UrlExe { get; set; }
        /// <summary>Empreinte SHA-256 de l'exe publiée par GitHub (64 caractères hexadécimaux en minuscules), ou null.</summary>
        public string Sha256 { get; set; }
        public bool InstallableEnUnClic { get { return UrlExe != null && Sha256 != null; } }
    }

    /// <summary>
    /// Vérifie au démarrage de l'interface si une release plus récente existe (API GitHub « releases/latest », qui ignore les pré-versions).
    /// L'appli affiche un bandeau : lien vers la page de la release et, si GitHub publie l'empreinte de l'exe, mise à jour en un clic (MiseAJourInstallation).
    /// Toute erreur (hors ligne, réponse inattendue, limite de l'API) est silencieuse : aucun bandeau.
    /// </summary>
    public static class MiseAJour
    {
        public const string Depot = "alexandre-dfr/GTweak";
        public const string UrlApi = "https://api.github.com/repos/" + Depot + "/releases/latest";
        private const string PrefixePage = "https://github.com/" + Depot + "/releases/";
        public const string PrefixeTelechargement = "https://github.com/" + Depot + "/releases/download/";
        public const string NomExe = "GamingTweaker.exe";
        private static readonly Regex Empreinte = new Regex(@"^sha256:([0-9a-fA-F]{64})\z", RegexOptions.CultureInvariant);
        private static readonly Regex Tag = new Regex(@"^v?(\d{1,5})\.(\d{1,5})\.(\d{1,5})\z", RegexOptions.CultureInvariant);

        /// <summary>Version de l'exe en cours (Major.Minor.Build, la révision n'est pas publiée).</summary>
        public static Version VersionCourante
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        }

        /// <summary>« v1.2.3 » ou « 1.2.3 » → 1.2.3 ; null sinon.</summary>
        public static Version LireTag(string tag)
        {
            var m = Tag.Match((tag ?? "").Trim());
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        }

        /// <summary>
        /// Réponse JSON de « releases/latest » → version plus récente que <paramref name="courante"/>, ou null (même version, plus ancienne,
        /// brouillon, pré-version, réponse inconnue, ou page qui n'est pas une release de ce dépôt en https).
        /// </summary>
        public static VersionDisponible Analyser(string json, Version courante)
        {
            AssemblyLoader.Register();
            return AnalyserCore(json, courante);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static VersionDisponible AnalyserCore(string json, Version courante)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json) || courante == null) return null;
                var o = JObject.Parse(json);
                if ((bool?)o["draft"] == true || (bool?)o["prerelease"] == true) return null;
                var v = LireTag((string)o["tag_name"]);
                if (v == null) return null;
                var url = ((string)o["html_url"] ?? "").Trim();
                if (!url.StartsWith(PrefixePage, StringComparison.OrdinalIgnoreCase) || !SystemExe.UrlAcceptee(url)) return null;
                var c = new Version(courante.Major, courante.Minor, Math.Max(0, courante.Build));
                if (v <= c) return null;
                var d = new VersionDisponible { Version = v, Url = url };
                // Exe de la release : nom exact, lien de téléchargement de ce dépôt en https, empreinte SHA-256 fournie par GitHub.
                var assets = o["assets"] as JArray;
                if (assets != null)
                    foreach (var a in assets)
                    {
                        if (!string.Equals((string)a["name"], NomExe, StringComparison.Ordinal)) continue;
                        var lien = ((string)a["browser_download_url"] ?? "").Trim();
                        var m = Empreinte.Match(((string)a["digest"] ?? "").Trim());
                        if (lien.StartsWith(PrefixeTelechargement, StringComparison.OrdinalIgnoreCase) && SystemExe.UrlAcceptee(lien) && m.Success)
                        { d.UrlExe = lien; d.Sha256 = m.Groups[1].Value.ToLowerInvariant(); }
                        break;
                    }
                return d;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Interroge GitHub (10 s au plus) ; null si aucune version plus récente ou en cas d'erreur.</summary>
        public static async Task<VersionDisponible> VerifierAsync(Func<string, Task<string>> get = null)
        {
            try
            {
                var json = await (get ?? HttpGetAsync)(UrlApi).ConfigureAwait(false);
                return Analyser(json, VersionCourante);
            }
            catch (Exception ex)
            {
                TraceAppli.Ecrire("vérification de mise à jour impossible : " + ex.Message);
                return null;
            }
        }

        internal static async Task<string> HttpGetAsync(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                // L'API GitHub refuse les requêtes sans User-Agent ; aucun identifiant de la machine ou de l'utilisateur n'est envoyé.
                c.DefaultRequestHeaders.UserAgent.ParseAdd("GamingTweaker/" + VersionCourante);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using (var r = await c.GetAsync(url).ConfigureAwait(false))
                {
                    if (!r.IsSuccessStatusCode) return null;
                    return await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
