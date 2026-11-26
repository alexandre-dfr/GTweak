using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
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
        /// <summary>Adresse de la signature (GamingTweaker.exe.sig) dans la release ; null si absente.</summary>
        public string UrlSignature { get; set; }
        /// <summary>Empreinte SHA-256 publiée par GitHub ou la CI (64 caractères hexadécimaux, minuscules) ; contrôle d'intégrité facultatif.</summary>
        public string Sha256 { get; set; }
        /// <summary>Clés publiques qui doivent avoir signé cette version (celles intégrées à l'exe).</summary>
        public string[] Cles { get; set; }
        /// <summary>Vrai si l'appli peut se mettre à jour seule (exe, signature et clé connus) ; sinon seule la page est proposée.</summary>
        public bool PeutInstaller { get { return UrlExe != null && UrlSignature != null && Cles != null && Cles.Length > 0; } }
    }

    /// <summary>
    /// Nouvelle version vérifiée et déjà à la place de l'exe. Tant que l'objet n'est pas libéré, le fichier reste ouvert sans partage en écriture
    /// ni en suppression : personne ne peut le modifier, le renommer ou le remplacer avant que <see cref="MiseAJour.Relancer"/> l'ait lancé.
    /// </summary>
    public sealed class MiseAJourPrete : IDisposable
    {
        private FileStream fichier;
        internal MiseAJourPrete(FileStream f) { fichier = f; }
        public void Dispose() { var f = fichier; fichier = null; if (f != null) f.Dispose(); }
    }

    /// <summary>
    /// Vérifie au démarrage de l'interface si une release plus récente existe (API GitHub « releases/latest », qui ignore les pré-versions)
    /// et affiche un bandeau. Rien n'est téléchargé sans clic sur « Mettre à jour » : l'exe de la release est alors téléchargé, sa signature
    /// vérifiée avec la clé publique intégrée (<see cref="SignatureRelease"/>), puis il remplace l'exe en cours et l'appli redémarre
    /// (<see cref="InstallerAsync"/>). Toute erreur de vérification (hors ligne, réponse inattendue, limite de l'API) est silencieuse : aucun bandeau.
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

        /// <summary>
        /// Taille maximale acceptée pour l'exe téléchargé (il fait quelques dizaines de Mo) : au-delà, le téléchargement est arrêté et le
        /// fichier partiel supprimé, pour qu'une réponse anormale ne remplisse pas le disque.
        /// </summary>
        public const long TailleMaxExe = 200L * 1024 * 1024;

        public const string MessageSignature = "la signature de cette version est absente ou ne correspond pas à la clé de GamingTweaker ; rien n'a été remplacé.";
        public const string MessageDossier = "le dossier de GamingTweaker est redirigé (lien ou jonction) ou a changé pendant le téléchargement ; rien n'a été remplacé. Téléchargez la version depuis « Nouveautés ».";

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
        /// Avec une clé configurée, une release sans exe ni signature n'est pas annoncée : elle n'est pas encore signée par le mainteneur.
        /// </summary>
        public static VersionDisponible Analyser(string json, Version courante, string[] cles = null)
        {
            AssemblyLoader.Register();
            return AnalyserCore(json, courante, cles ?? SignatureRelease.Cles);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static VersionDisponible AnalyserCore(string json, Version courante, string[] cles)
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
                var res = new VersionDisponible { Version = v, Url = url, Cles = cles };
                var assets = (o["assets"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                var exe = assets.FirstOrDefault(a => (string)a["name"] == NomExe);
                if (exe != null)
                {
                    res.UrlExe = UrlAsset(exe, NomExe);
                    // Empreinte calculée par GitHub (champ « digest »), sinon celle que la CI écrit dans les notes de la release.
                    var d = ((string)exe["digest"] ?? "").Trim();
                    string sha = d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? d.Substring(7).ToLowerInvariant() : null;
                    if (sha == null) { var m = ShaNotes.Match((string)o["body"] ?? ""); if (m.Success) sha = m.Groups[1].Value.ToLowerInvariant(); }
                    if (sha != null && Hex64.IsMatch(sha)) res.Sha256 = sha;
                }
                var sig = assets.FirstOrDefault(a => (string)a["name"] == SignatureRelease.NomFichier);
                if (sig != null) res.UrlSignature = UrlAsset(sig, SignatureRelease.NomFichier);
                if (cles != null && cles.Length > 0 && !res.PeutInstaller) return null;
                return res;
            }
            catch (Exception) { return null; }
        }

        private static string UrlAsset(JObject asset, string nom)
        {
            var u = ((string)asset["browser_download_url"] ?? "").Trim();
            return u.StartsWith(PrefixeTelechargement, StringComparison.Ordinal) && u.EndsWith("/" + nom, StringComparison.Ordinal) && SystemExe.UrlAcceptee(u) ? u : null;
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
            using (var s = File.OpenRead(fichier)) return Sha256Flux(s);
        }

        private static string Sha256Flux(Stream s)
        {
            using (var h = System.Security.Cryptography.SHA256.Create())
                return string.Concat(h.ComputeHash(s).Select(b => b.ToString("x2")));
        }

        /// <summary>
        /// Télécharge l'exe de la release à côté de <paramref name="exe"/>, vérifie sa signature puis le met à la place de l'exe en cours
        /// (l'ancien est renommé « .ancien », supprimé au prochain lancement). En cas d'erreur, l'exe en cours est laissé intact.
        /// Le dossier de l'exe portable est souvent modifiable par l'utilisateur (Téléchargements) alors que l'appli tourne en administrateur
        /// et relance la nouvelle version sans invite UAC : le fichier téléchargé est donc ouvert une seule fois, sans partage en écriture
        /// ni en suppression, et c'est par cette même poignée qu'il est vérifié puis renommé. Le résultat garde cette poignée ouverte :
        /// lancer la nouvelle version (<see cref="Relancer"/>) avant de le libérer.
        /// </summary>
        public static async Task<MiseAJourPrete> InstallerAsync(VersionDisponible v, string exe,
            Func<string, string, Task> telecharger = null, Func<string, Task<string>> lireSignature = null)
        {
            if (v == null || !v.PeutInstaller) throw new InvalidOperationException("Cette version ne publie pas d'exe signé : utilisez « Nouveautés » pour la télécharger.");
            exe = Path.GetFullPath(exe);
            var nouveau = exe + ".nouveau";
            Supprimer(nouveau);
            FileStream fichier = null;
            try
            {
                await (telecharger ?? TelechargerAsync)(v.UrlExe, nouveau).ConfigureAwait(false);
                var signature = await (lireSignature ?? LireSignatureAsync)(v.UrlSignature).ConfigureAwait(false);

                fichier = OuvrirVerrouille(nouveau);
                var h = Sha256Flux(fichier);
                if (v.Sha256 != null && !string.Equals(h, v.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("le fichier téléchargé ne correspond pas à l'empreinte publiée (téléchargement corrompu ou modifié) ; rien n'a été remplacé.");
                if (!SignatureRelease.Verifier(signature, v.Version, h, v.Cles)) throw new InvalidDataException(MessageSignature);

                var ancien = exe + ".ancien";
                Supprimer(ancien);
                File.Move(exe, ancien);   // l'exe en cours peut être renommé, pas modifié
                try { RenommerParPoignee(fichier.SafeFileHandle, CheminNt(exe)); }
                catch (Exception) { try { File.Move(ancien, exe); } catch (Exception exc) { TraceAppli.Erreur("mise à jour : ancien exe non remis en place", exc); } throw; }
                TraceAppli.Ecrire("mise à jour vers " + v.Version + " installée (SHA-256 " + h + ", signature vérifiée)");
                var prete = new MiseAJourPrete(fichier);
                fichier = null;
                return prete;
            }
            finally
            {
                if (fichier != null) fichier.Dispose();
                Supprimer(nouveau);
            }
        }

        // ---------- Poignée verrouillée et renommage ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle h, out InfoFichier info);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle h, int classe, IntPtr info, uint taille);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathNameW(string court, StringBuilder longue, uint taille);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder buf, uint len, uint flags);

        // BY_HANDLE_FILE_INFORMATION : les FILETIME sont alignés sur 4 octets.
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct InfoFichier
        {
            public uint Attributs;
            public long Creation, DernierAcces, DerniereEcriture;
            public uint NumeroVolume, TailleHaute, TailleBasse, NombreDeLiens, IndexHaut, IndexBas;
        }

        private const uint GenericRead = 0x80000000, Delete = 0x00010000, PartageLecture = 0x1, OuvrirExistant = 3;
        private const uint OuvrirPointDAnalyse = 0x00200000, AttributPointDAnalyse = 0x400, AttributDossier = 0x10;
        private const int ClasseRenommage = 3; // FileRenameInfo

        /// <summary>
        /// Ouvre le fichier en lecture et suppression, partagé en lecture seulement : plus aucune écriture, aucun renommage ni aucune suppression
        /// par un autre processus tant que la poignée est ouverte (et l'ouverture échoue si quelqu'un l'a déjà ouvert en écriture).
        /// Refuse un lien, un dossier, un fichier à plusieurs noms (lien physique) ou un fichier dont le chemin réel n'est pas celui attendu.
        /// </summary>
        internal static FileStream OuvrirVerrouille(string chemin)
        {
            var h = CreateFileW(chemin, GenericRead | Delete, PartageLecture, IntPtr.Zero, OuvrirExistant, OuvrirPointDAnalyse, IntPtr.Zero);
            if (h.IsInvalid) { var err = Marshal.GetLastWin32Error(); h.Dispose(); throw new IOException("ouverture du fichier téléchargé impossible (erreur " + err + ") ; rien n'a été remplacé."); }
            try
            {
                InfoFichier info;
                if (!GetFileInformationByHandle(h, out info)) throw new IOException("lecture du fichier téléchargé impossible ; rien n'a été remplacé.");
                if ((info.Attributs & (AttributPointDAnalyse | AttributDossier)) != 0 || info.NombreDeLiens != 1) throw new IOException(MessageDossier);
                if (!MemeChemin(CheminReel(h), CheminLong(chemin))) throw new IOException(MessageDossier);
                return new FileStream(h, FileAccess.Read);
            }
            catch (Exception) { h.Dispose(); throw; }
        }

        /// <summary>Décision pure : chemin réel d'une poignée (forme « \\?\C:\… » ou « \\?\UNC\… ») et chemin attendu désignent le même fichier.</summary>
        public static bool MemeChemin(string reel, string attendu)
        {
            if (string.IsNullOrEmpty(reel) || string.IsNullOrEmpty(attendu)) return false;
            if (reel.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) reel = @"\\" + reel.Substring(8);
            else if (reel.StartsWith(@"\\?\", StringComparison.Ordinal)) reel = reel.Substring(4);
            try { return string.Equals(Path.GetFullPath(reel), Path.GetFullPath(attendu), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        /// <summary>Chemin avec les noms longs (un dossier « RUNNER~1 » ou « PROGRA~1 » est développé), pour le comparer au chemin réel.</summary>
        private static string CheminLong(string chemin)
        {
            try
            {
                var dossier = Path.GetDirectoryName(Path.GetFullPath(chemin));
                var sb = new StringBuilder(1024);
                uint n = GetLongPathNameW(dossier, sb, (uint)sb.Capacity);
                return n == 0 || n >= sb.Capacity ? chemin : Path.Combine(sb.ToString(), Path.GetFileName(chemin));
            }
            catch (Exception) { return chemin; }
        }

        private static string CheminReel(SafeFileHandle h)
        {
            var sb = new StringBuilder(1024);
            uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
            if (n == 0 || n >= sb.Capacity) return null;
            return sb.ToString();
        }

        /// <summary>
        /// Chemin complet au format NT (« \??\C:\… », « \??\UNC\serveur\… ») : un nom simple serait résolu par Windows depuis le dossier
        /// courant du processus (erreur 17 si celui-ci est sur un autre lecteur).
        /// </summary>
        public static string CheminNt(string chemin)
        {
            var c = Path.GetFullPath(chemin);
            if (c.StartsWith(@"\\?\", StringComparison.Ordinal) || c.StartsWith(@"\??\", StringComparison.Ordinal)) return @"\??\" + c.Substring(4);
            if (c.StartsWith(@"\\", StringComparison.Ordinal)) return @"\??\UNC\" + c.Substring(2);
            return @"\??\" + c;
        }

        /// <summary>Renomme le fichier ouvert vers <paramref name="cheminNt"/> (FILE_RENAME_INFO), en remplaçant un fichier du même nom.</summary>
        private static void RenommerParPoignee(SafeFileHandle h, string cheminNt)
        {
            var nom = Encoding.Unicode.GetBytes(cheminNt);
            int offRacine = IntPtr.Size, offLongueur = offRacine + IntPtr.Size, offNom = offLongueur + 4;
            int taille = offNom + nom.Length + 2;
            var p = Marshal.AllocHGlobal(taille);
            try
            {
                for (int i = 0; i < taille; i++) Marshal.WriteByte(p, i, 0);
                Marshal.WriteInt32(p, 0, 1);                 // ReplaceIfExists
                Marshal.WriteIntPtr(p, offRacine, IntPtr.Zero); // RootDirectory : aucun, chemin complet
                Marshal.WriteInt32(p, offLongueur, nom.Length);
                Marshal.Copy(nom, 0, p + offNom, nom.Length);
                if (!SetFileInformationByHandle(h, ClasseRenommage, p, (uint)taille))
                    throw new IOException("la nouvelle version n'a pas pu être mise à la place de l'ancienne (erreur " + Marshal.GetLastWin32Error() + ").");
            }
            finally { Marshal.FreeHGlobal(p); }
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

        private static void Supprimer(string f) { try { if (File.Exists(f)) File.Delete(f); } catch (Exception exc) { TraceAppli.Erreur("mise à jour : suppression impossible de " + Path.GetFileName(f), exc); } }

        private static async Task TelechargerAsync(string url, string destination)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd("GamingTweaker/" + VersionCourante);
                using (var r = await c.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    r.EnsureSuccessStatusCode();
                    var annonce = r.Content.Headers.ContentLength;
                    if (annonce.HasValue && annonce.Value > TailleMaxExe) throw new InvalidDataException(MessageTropGros(annonce.Value));
                    using (var src = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await CopierBorneAsync(src, dst, TailleMaxExe).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Copie <paramref name="src"/> dans <paramref name="dst"/> et lève <see cref="InvalidDataException"/> dès que plus de
        /// <paramref name="max"/> octets arrivent (un en-tête Content-Length peut manquer ou mentir). Renvoie le nombre d'octets copiés.
        /// </summary>
        public static async Task<long> CopierBorneAsync(Stream src, Stream dst, long max)
        {
            var tampon = new byte[81920];
            long total = 0;
            int n;
            while ((n = await src.ReadAsync(tampon, 0, tampon.Length).ConfigureAwait(false)) > 0)
            {
                total += n;
                if (total > max) throw new InvalidDataException(MessageTropGros(total));
                await dst.WriteAsync(tampon, 0, n).ConfigureAwait(false);
            }
            return total;
        }

        private static string MessageTropGros(long taille)
        {
            return "le fichier téléchargé dépasse " + (TailleMaxExe / (1024 * 1024)) + " Mo (" + (taille / (1024 * 1024)) + " Mo ou plus) : téléchargement arrêté, rien n'a été remplacé.";
        }

        private static async Task<string> LireSignatureAsync(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = SignatureRelease.TailleMaxFichier })
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd("GamingTweaker/" + VersionCourante);
                using (var r = await c.GetAsync(url).ConfigureAwait(false))
                {
                    r.EnsureSuccessStatusCode();
                    return await r.Content.ReadAsStringAsync().ConfigureAwait(false);
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
