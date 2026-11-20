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
    }

    /// <summary>
    /// Vérifie au démarrage de l'interface si une release plus récente existe (API GitHub « releases/latest », qui ignore les pré-versions).
    /// Rien n'est téléchargé ni installé : l'appli affiche un bandeau avec un lien vers la page de la release.
    /// Toute erreur (hors ligne, réponse inattendue, limite de l'API) est silencieuse : aucun bandeau.
    /// </summary>
    public static class MiseAJour
    {
        public const string Depot = "alexandre-dfr/GTweak";
        public const string UrlApi = "https://api.github.com/repos/" + Depot + "/releases/latest";
        private const string PrefixePage = "https://github.com/" + Depot + "/releases/";
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
                return v > c ? new VersionDisponible { Version = v, Url = url } : null;
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

        private static async Task<string> HttpGetAsync(string url)
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
