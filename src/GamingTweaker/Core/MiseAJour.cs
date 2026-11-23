using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        /// <summary>Page de la release (https://github.com/&lt;dépôt&gt;/releases/…), ouverte par « Nouveautés ».</summary>
        public string Url { get; set; }
        /// <summary>Adresse de GamingTweaker.exe dans la release (https://github.com/&lt;dépôt&gt;/releases/download/…) ; null si absente.</summary>
        public string UrlExe { get; set; }
        /// <summary>Empreinte SHA-256 attendue de l'exe (64 caractères hexadécimaux, minuscules) ; null si la release n'en publie pas.</summary>
        public string Sha256 { get; set; }
        /// <summary>Vrai si l'appli peut se mettre à jour seule (exe et empreinte connus) ; sinon seule la page est proposée.</summary>
        public bool PeutInstaller { get { return UrlExe != null && Sha256 != null; } }
    }

    /// <summary>
    /// Vérifie au démarrage de l'interface si une release plus récente existe (API GitHub « releases/latest », qui ignore les pré-versions)
    /// et affiche un bandeau. Rien n'est téléchargé sans clic sur « Mettre à jour » : l'exe de la release est alors téléchargé,
    /// son empreinte SHA-256 vérifiée, puis il remplace l'exe en cours et l'appli redémarre (<see cref="InstallerAsync"/>).
    /// Toute erreur de vérification (hors ligne, réponse inattendue, limite de l'API) est silencieuse : aucun bandeau.
    /// </summary>
    public static class MiseAJour
    {
        public const string Depot = "alexandre-dfr/GTweak";
        public const string UrlApi = "https://api.github.com/repos/" + Depot + "/releases/latest";
        private const string PrefixePage = "https://github.com/" + Depot + "/releases/";
        private const string PrefixeTelechargement = "https://github.com/" + Depot + "/releases/download/";
        public const string NomExe = "GamingTweaker.exe";
        /// <summary>Argument de l'exe relancé après une mise à jour, suivi du numéro du processus qui s'arrête.</summary>
        public const string ArgumentApres = "--apres-mise-a-jour";
        private static readonly Regex Hex64 = new Regex(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
        private static readonly Regex ShaNotes = new Regex(@"SHA-256 de GamingTweaker\.exe\s*:\s*`?([0-9A-Fa-f]{64})", RegexOptions.CultureInvariant);
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
                if (!(v > c)) return null;
                var res = new VersionDisponible { Version = v, Url = url };
                var exe = (o["assets"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(a => (string)a["name"] == NomExe);
                if (exe != null)
                {
                    var u = ((string)exe["browser_download_url"] ?? "").Trim();
                    if (u.StartsWith(PrefixeTelechargement, StringComparison.Ordinal) && u.EndsWith("/" + NomExe, StringComparison.Ordinal) && SystemExe.UrlAcceptee(u)) res.UrlExe = u;
                    // Empreinte calculée par GitHub (champ « digest »), sinon celle que la CI écrit dans les notes de la release.
                    var d = ((string)exe["digest"] ?? "").Trim();
                    string sha = d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? d.Substring(7).ToLowerInvariant() : null;
                    if (sha == null) { var m = ShaNotes.Match((string)o["body"] ?? ""); if (m.Success) sha = m.Groups[1].Value.ToLowerInvariant(); }
                    if (sha != null && Hex64.IsMatch(sha)) res.Sha256 = sha;
                }
                return res;
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

        // ---------- Installation de la mise à jour ----------

        /// <summary>SHA-256 d'un fichier, en hexadécimal minuscule.</summary>
        public static string Sha256Fichier(string fichier)
        {
            using (var s = File.OpenRead(fichier))
            using (var h = System.Security.Cryptography.SHA256.Create())
                return string.Concat(h.ComputeHash(s).Select(b => b.ToString("x2")));
        }

        /// <summary>
        /// Télécharge l'exe de la release à côté de <paramref name="exe"/>, vérifie son empreinte puis le met à la place de l'exe en cours
        /// (l'ancien est renommé « .ancien », supprimé au prochain lancement). En cas d'erreur, l'exe en cours est laissé intact.
        /// </summary>
        public static async Task InstallerAsync(VersionDisponible v, string exe, Func<string, string, Task> telecharger = null)
        {
            if (v == null || !v.PeutInstaller) throw new InvalidOperationException("Cette version ne publie pas d'exe vérifiable : utilisez « Nouveautés » pour la télécharger.");
            var nouveau = exe + ".nouveau";
            Supprimer(nouveau);
            try
            {
                await (telecharger ?? TelechargerAsync)(v.UrlExe, nouveau).ConfigureAwait(false);
                var h = Sha256Fichier(nouveau);
                if (!string.Equals(h, v.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("le fichier téléchargé ne correspond pas à l'empreinte publiée (téléchargement corrompu ou modifié) ; rien n'a été remplacé.");
                Remplacer(exe, nouveau);
            }
            finally { Supprimer(nouveau); }
            TraceAppli.Ecrire("mise à jour vers " + v.Version + " installée (SHA-256 " + v.Sha256 + ")");
        }

        /// <summary>Met <paramref name="nouveau"/> à la place de <paramref name="exe"/> (en cours d'exécution : renommer reste permis).</summary>
        public static void Remplacer(string exe, string nouveau)
        {
            var ancien = exe + ".ancien";
            Supprimer(ancien);
            File.Move(exe, ancien);
            try { File.Move(nouveau, exe); }
            catch (Exception) { try { File.Move(ancien, exe); } catch (Exception) { } throw; }
        }

        /// <summary>Lance la nouvelle version, qui attend la fin de ce processus avant d'ouvrir son interface.</summary>
        public static void Relancer(string exe)
        {
            int pid;
            using (var p = Process.GetCurrentProcess()) pid = p.Id;
            var psi = new ProcessStartInfo(exe, ArgumentApres + " " + pid) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
            using (Process.Start(psi)) { }
        }

        /// <summary>Au lancement après une mise à jour : attend la fin de l'ancienne instance (20 s au plus), puis supprime l'ancien exe.</summary>
        public static void ApresMiseAJour(string pid, string exe)
        {
            int n;
            if (int.TryParse(pid, out n) && n > 0)
            {
                try { using (var p = Process.GetProcessById(n)) p.WaitForExit(20000); } catch (Exception) { /* déjà terminé */ }
            }
            NettoyerAncien(exe);
        }

        /// <summary>Supprime l'exe remplacé par une mise à jour précédente (quelques essais : l'ancienne instance peut finir de se fermer).</summary>
        public static void NettoyerAncien(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return;
            var ancien = exe + ".ancien";
            for (int i = 0; i < 5 && File.Exists(ancien); i++)
            {
                try { File.Delete(ancien); }
                catch (Exception) { System.Threading.Thread.Sleep(300); }
            }
        }

        private static void Supprimer(string f) { try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { } }

        private static async Task TelechargerAsync(string url, string destination)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd("GamingTweaker/" + VersionCourante);
                using (var r = await c.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    r.EnsureSuccessStatusCode();
                    using (var src = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await src.CopyToAsync(dst).ConfigureAwait(false);
                }
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
