using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamingTweaker.Lists
{
    /// <summary>Une application installable du menu Installation (<c>applis-list.json</c>). Seules ces clés peuvent être installées.</summary>
    public class AppInstallable
    {
        [JsonProperty("id")] public string Id { get; set; }
        /// <summary>« composants », « launchers », « marques » ou « applis » : l'option ins.&lt;categorie&gt; et sa liste applis-&lt;categorie&gt;.</summary>
        [JsonProperty("categorie")] public string Categorie { get; set; }
        [JsonProperty("nom")] public string Nom { get; set; }
        [JsonProperty("groupe")] public string Groupe { get; set; }
        [JsonProperty("detail")] public string Detail { get; set; }
        /// <summary>Identifiant du paquet winget (ou identifiant Microsoft Store si <c>source</c> vaut « msstore »).</summary>
        [JsonProperty("winget")] public string Winget { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        /// <summary>Lien direct https stable (dernière version), utilisé si winget est absent ou échoue.</summary>
        [JsonProperty("url")] public string Url { get; set; }
        /// <summary>« exe » ou « msi ».</summary>
        [JsonProperty("type")] public string Type { get; set; }
        /// <summary>Arguments de l'installation silencieuse, obligatoires pour un exe (un installateur qui s'affiche bloquerait l'exécution).</summary>
        [JsonProperty("args")] public string Args { get; set; }
        /// <summary>
        /// Noms exacts acceptés pour le CN ou le O du certificat de signature du fichier téléchargé (casse, virgules et points ignorés).
        /// Un nom qui ne fait que contenir l'un d'eux (« Not Valve Corp. ») est refusé.
        /// </summary>
        [JsonProperty("signataires")] public List<string> Signataires { get; set; } = new List<string>();
        [JsonProperty("page")] public string Page { get; set; }
        /// <summary>Débuts de noms (DisplayName) qui signalent l'application déjà installée.</summary>
        [JsonProperty("installe")] public List<string> Installe { get; set; } = new List<string>();
        /// <summary>
        /// Fichiers (avec variables d'environnement, par exemple « %SystemRoot%\System32\d3dx9_43.dll ») qui, tous présents, signalent
        /// l'application déjà installée : pour celles qui n'ont pas d'entrée dans « Programmes et fonctionnalités » (DirectX).
        /// </summary>
        [JsonProperty("fichiers")] public List<string> Fichiers { get; set; } = new List<string>();
        /// <summary>Matériel qui fait proposer l'application d'office : « usb:VVVV », « carte-mere:NOM », « gpu:NVIDIA|AMD », « intel ».</summary>
        [JsonProperty("marque")] public string Marque { get; set; }
        /// <summary>Cochée d'office quand l'option est cochée sans choix (composants).</summary>
        [JsonProperty("defaut")] public bool Defaut { get; set; }

        public bool ViaStore { get { return string.Equals(Source, "msstore", StringComparison.OrdinalIgnoreCase); } }
    }

    public static class AppsCatalogue
    {
        public const string PrefixeListe = "applis-";
        public static readonly string[] Categories = { "composants", "launchers", "marques", "applis" };

        private static string texte;
        private static IReadOnlyList<AppInstallable> integre;

        public static IReadOnlyList<AppInstallable> LoadEmbedded()
        {
            AssemblyLoader.Register();
            return integre ?? (integre = LoadCore());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IReadOnlyList<AppInstallable> LoadCore() { return Parse(Texte()); }

        private static string Texte()
        {
            if (texte != null) return texte;
            using (var s = typeof(AppsCatalogue).Assembly.GetManifestResourceStream("applis-list.json"))
            {
                if (s == null) throw new InvalidOperationException("Ressource introuvable : applis-list.json");
                using (var r = new StreamReader(s, new UTF8Encoding(false), true)) return texte = r.ReadToEnd();
            }
        }

        public static List<AppInstallable> Parse(string json)
        {
            AssemblyLoader.Register();
            return ParseCore(json);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<AppInstallable> ParseCore(string json)
        {
            var l = JsonConvert.DeserializeObject<List<AppInstallable>>(json);
            if (l == null || l.Any(a => a == null || string.IsNullOrWhiteSpace(a.Id))) throw new InvalidDataException("applis-list.json : liste invalide.");
            return l;
        }

        /// <summary>Le catalogue sur une seule ligne, pour le script ($GT_AppCatalog) : jamais de « '@ » en début de ligne.</summary>
        public static string JsonCompact()
        {
            AssemblyLoader.Register();
            return Compacter(Texte());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string Compacter(string json)
        {
            return JToken.Parse(json).ToString(Formatting.None).Replace("\r", "").Replace("\n", "");
        }

        private static readonly Regex ReId = new Regex(@"^[a-z0-9][a-z0-9\-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex ReHttps = new Regex(@"^https://[A-Za-z0-9.\-]+(:\d+)?/\S*$", RegexOptions.CultureInvariant);
        private static readonly Regex ReWinget = new Regex(@"^[A-Za-z0-9][A-Za-z0-9.\-_+]*$", RegexOptions.CultureInvariant);
        private static readonly Regex ReFichier = new Regex(@"^%(SystemRoot|ProgramFiles|ProgramFiles\(x86\)|ProgramData)%\\[^%<>|""*?:]+$", RegexOptions.CultureInvariant);
        private static readonly Regex ReMarque = new Regex(@"^(usb:[0-9A-F]{4}|carte-mere:[A-Z0-9\-]+|gpu:(NVIDIA|AMD)|intel)$", RegexOptions.CultureInvariant);

        /// <summary>Erreurs du catalogue (vide si valide) : chaque application doit pouvoir être installée ou indiquée (winget, lien direct ou page).</summary>
        public static List<string> Validate(IEnumerable<AppInstallable> apps)
        {
            var err = new List<string>();
            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in apps)
            {
                var id = a.Id ?? "(sans id)";
                if (a.Id == null || !ReId.IsMatch(a.Id)) err.Add(id + " : identifiant invalide.");
                else if (!vus.Add(a.Id)) err.Add(id + " : identifiant en double.");
                if (Array.IndexOf(Categories, a.Categorie) < 0) err.Add(id + " : catégorie inconnue « " + a.Categorie + " ».");
                if (string.IsNullOrWhiteSpace(a.Nom)) err.Add(id + " : nom manquant.");
                if (string.IsNullOrEmpty(a.Winget) && string.IsNullOrEmpty(a.Url) && string.IsNullOrEmpty(a.Page)) err.Add(id + " : ni winget, ni lien direct, ni page.");
                if (!string.IsNullOrEmpty(a.Winget) && !ReWinget.IsMatch(a.Winget)) err.Add(id + " : identifiant winget invalide.");
                if (a.Source != null && a.Source != "msstore") err.Add(id + " : source inconnue « " + a.Source + " ».");
                if (!string.IsNullOrEmpty(a.Url))
                {
                    if (!ReHttps.IsMatch(a.Url)) err.Add(id + " : lien direct non https.");
                    if (a.Signataires == null || a.Signataires.Count == 0 || a.Signataires.Any(string.IsNullOrWhiteSpace))
                        err.Add(id + " : lien direct sans signataire attendu (signature).");
                    if (a.Type != "exe" && a.Type != "msi") err.Add(id + " : type « " + a.Type + " » inconnu (exe ou msi).");
                    if (a.Type == "exe" && string.IsNullOrWhiteSpace(a.Args)) err.Add(id + " : exe sans arguments d'installation silencieuse (l'installateur bloquerait l'exécution).");
                    if (a.Args != null && Regex.IsMatch(a.Args, "[\"'`$;|&<>\r\n]")) err.Add(id + " : arguments avec un caractère interdit.");
                }
                if (!string.IsNullOrEmpty(a.Page) && !ReHttps.IsMatch(a.Page)) err.Add(id + " : page non https.");
                foreach (var f in a.Fichiers ?? new List<string>())
                    if (f == null || !ReFichier.IsMatch(f) || f.Contains("..")) err.Add(id + " : fichier de détection « " + f + " » refusé (%SystemRoot%, %ProgramFiles%… suivi d'un chemin).");
                if (!string.IsNullOrEmpty(a.Marque) && !ReMarque.IsMatch(a.Marque)) err.Add(id + " : marque « " + a.Marque + " » non reconnue.");
            }
            return err;
        }
    }

    /// <summary>Noms affichés (DisplayName) des programmes installés, machine (64 et 32 bits) et utilisateur courant.</summary>
    public interface IProgrammesInstalles
    {
        IReadOnlyList<string> Lire();
    }

    public class RegistreProgrammesInstalles : IProgrammesInstalles
    {
        public IReadOnlyList<string> Lire()
        {
            var res = new List<string>();
            const string cle = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            foreach (var racine in new[] { Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryHive.CurrentUser })
                foreach (var vue in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
                {
                    if (racine == Microsoft.Win32.RegistryHive.CurrentUser && vue == Microsoft.Win32.RegistryView.Registry32) continue;
                    try
                    {
                        using (var b = Microsoft.Win32.RegistryKey.OpenBaseKey(racine, vue))
                        using (var k = b.OpenSubKey(cle))
                        {
                            if (k == null) continue;
                            foreach (var n in k.GetSubKeyNames())
                                try { using (var s = k.OpenSubKey(n)) { var d = s == null ? null : s.GetValue("DisplayName") as string; if (!string.IsNullOrWhiteSpace(d)) res.Add(d); } }
                                catch (Exception) { }
                        }
                    }
                    catch (Exception exc) { TraceAppli.Erreur("installation : lecture des programmes installés", exc); }
                }
            return res;
        }
    }

    /// <summary>Matériel de ce PC qui fait proposer un logiciel de marque : identifiants USB (VID), fabricant de la carte mère, cartes graphiques, processeur.</summary>
    public class MaterielMarques
    {
        public HashSet<string> VendeursUsb { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string CarteMere { get; set; } = "";
        public List<string> Gpu { get; set; } = new List<string>();
        public string Cpu { get; set; } = "";

        /// <summary>Vrai si l'étiquette « marque » du catalogue correspond à ce matériel.</summary>
        public bool Correspond(string marque)
        {
            if (string.IsNullOrEmpty(marque)) return false;
            if (marque.StartsWith("usb:", StringComparison.Ordinal)) return VendeursUsb.Contains(marque.Substring(4));
            if (marque.StartsWith("carte-mere:", StringComparison.Ordinal)) return (CarteMere ?? "").IndexOf(marque.Substring(11), StringComparison.OrdinalIgnoreCase) >= 0;
            if (marque.StartsWith("gpu:", StringComparison.Ordinal)) return Gpu.Any(g => string.Equals(g, marque.Substring(4), StringComparison.OrdinalIgnoreCase));
            if (marque == "intel") return string.Equals(Cpu, "Intel", StringComparison.OrdinalIgnoreCase) || Gpu.Any(g => string.Equals(g, "Intel", StringComparison.OrdinalIgnoreCase));
            return false;
        }

        private static readonly Regex ReVid = new Regex(@"VID_([0-9A-Fa-f]{4})", RegexOptions.CultureInvariant);

        /// <summary>Identifiants de périphériques (« USB\VID_046D&amp;PID_C08B\… ») → vendeurs USB.</summary>
        public void AjouterPeripheriques(IEnumerable<string> deviceIds)
        {
            foreach (var d in deviceIds ?? Enumerable.Empty<string>())
            {
                if (d == null) continue;
                var m = ReVid.Match(d);
                if (m.Success) VendeursUsb.Add(m.Groups[1].Value.ToUpperInvariant());
            }
        }
    }

    public interface ISourceMarques
    {
        MaterielMarques Lire();
    }

    /// <summary>WMI : périphériques Plug and Play (USB et HID) et fabricant de la carte mère ; cartes graphiques et processeur depuis la détection de l'appli.</summary>
    public class WmiSourceMarques : ISourceMarques
    {
        private readonly MachineFacts facts;
        public WmiSourceMarques(MachineFacts facts) { this.facts = facts; }

        public MaterielMarques Lire()
        {
            var m = new MaterielMarques();
            if (facts != null) { m.Gpu = new List<string>(facts.GpuVendors ?? new List<string>()); m.Cpu = facts.CpuVendor ?? ""; }
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT Manufacturer FROM Win32_BaseBoard"))
                    foreach (System.Management.ManagementObject o in s.Get()) { m.CarteMere = Convert.ToString(o["Manufacturer"]) ?? ""; break; }
            }
            catch (Exception exc) { TraceAppli.Erreur("installation : carte mère (WMI)", exc); }
            try
            {
                var ids = new List<string>();
                using (var s = new System.Management.ManagementObjectSearcher("SELECT DeviceID FROM Win32_PnPEntity"))
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        var id = Convert.ToString(o["DeviceID"]);
                        if (id != null && (id.StartsWith("USB", StringComparison.OrdinalIgnoreCase) || id.StartsWith("HID", StringComparison.OrdinalIgnoreCase))) ids.Add(id);
                    }
                m.AjouterPeripheriques(ids);
            }
            catch (Exception exc) { TraceAppli.Erreur("installation : périphériques USB et HID (WMI)", exc); }
            return m;
        }
    }

    /// <summary>
    /// Applications d'une catégorie du menu Installation. Celles qui sont déjà installées ne sont pas proposées (signalées sous la liste) ;
    /// pour les logiciels des marques, ceux du matériel de ce PC sont la sélection par défaut.
    /// </summary>
    public class AppsProvider : IListProvider, IListInfo, IListLookup, IListDefaults
    {
        private readonly IReadOnlyList<AppInstallable> apps;
        private readonly IProgrammesInstalles installes;
        private readonly ISourceMarques marques;
        private readonly Func<string, bool> fichierExiste;
        private List<string> suggerees;

        public AppsProvider(string categorie, IReadOnlyList<AppInstallable> catalogue = null, IProgrammesInstalles installes = null, ISourceMarques marques = null,
            Func<string, bool> fichierExiste = null)
        {
            this.fichierExiste = fichierExiste;
            Categorie = categorie;
            apps = (catalogue ?? AppsCatalogue.LoadEmbedded()).Where(a => a.Categorie == categorie).ToList();
            this.installes = installes ?? new RegistreProgrammesInstalles();
            this.marques = marques;
        }

        public static IEnumerable<AppsProvider> Tous(MachineFacts facts)
        {
            var src = new WmiSourceMarques(facts);
            return AppsCatalogue.Categories.Select(c => new AppsProvider(c, null, null, c == "marques" ? src : null));
        }

        public string Categorie { get; private set; }
        public string ListId { get { return AppsCatalogue.PrefixeListe + Categorie; } }
        public string Message { get; private set; } = "";
        public string Lien { get { return ""; } }

        /// <summary>Composants : ceux du catalogue marqués « defaut ». Marques : le matériel détecté (connu après le chargement de la liste).</summary>
        public IReadOnlyList<string> SelectionParDefaut
        {
            get { return suggerees ?? apps.Where(a => a.Defaut).Select(a => a.Id).ToList(); }
        }

        public static string Mode(AppInstallable a)
        {
            if (!string.IsNullOrEmpty(a.Winget))
            {
                var w = a.ViaStore ? "Microsoft Store (winget)" : "winget";
                if (!string.IsNullOrEmpty(a.Url)) return w + ", sinon téléchargement direct";
                return w + (string.IsNullOrEmpty(a.Page) ? "" : ", sinon page officielle");
            }
            return string.IsNullOrEmpty(a.Url) ? "à installer depuis la page officielle" : "téléchargement direct";
        }

        public static ListEntry Entree(AppInstallable a)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(a.Groupe)) parts.Add(a.Groupe);
            if (!string.IsNullOrWhiteSpace(a.Detail)) parts.Add(a.Detail.Trim());
            return new ListEntry
            {
                Key = a.Id, Titre = a.Nom,
                Detail = (parts.Count == 0 ? "" : string.Join(" · ", parts) + " — ") + Mode(a),
                Risque = RiskLevel.Sur,
            };
        }

        /// <summary>
        /// Vrai si un programme installé porte un nom attendu, ou si tous les fichiers de détection existent.
        /// <paramref name="fichierExiste"/> : remplacement pour les tests (par défaut, le disque).
        /// </summary>
        public static bool EstInstallee(AppInstallable a, IEnumerable<string> noms, Func<string, bool> fichierExiste = null)
        {
            if (a.Installe != null && a.Installe.Count > 0
                && noms.Any(n => n != null && a.Installe.Any(p => !string.IsNullOrEmpty(p) && n.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
                return true;
            if (a.Fichiers == null || a.Fichiers.Count == 0) return false;
            var existe = fichierExiste ?? FichierPresent;
            return a.Fichiers.All(f => !string.IsNullOrEmpty(f) && existe(Environment.ExpandEnvironmentVariables(f)));
        }

        private static bool FichierPresent(string chemin)
        {
            try { return File.Exists(chemin); }
            catch (Exception ex) { TraceAppli.Erreur("installation : fichier de détection illisible", ex); return false; }
        }

        public ListEntry Decrire(string key)
        {
            var a = apps.FirstOrDefault(x => string.Equals(x.Id, key, StringComparison.OrdinalIgnoreCase));
            return a == null ? null : Entree(a);
        }

        public async Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
        {
            var noms = await Task.Run(() => installes.Lire() ?? new List<string>(), ct);
            var materiel = marques == null ? null : await Task.Run(() => marques.Lire(), ct);
            var deja = await Task.Run(() => apps.Where(a => EstInstallee(a, noms, fichierExiste)).ToList(), ct);
            var proposees = apps.Where(a => !deja.Contains(a)).ToList();
            if (materiel != null) suggerees = proposees.Where(a => materiel.Correspond(a.Marque)).Select(a => a.Id).ToList();
            else if (Categorie == "composants") suggerees = proposees.Where(a => a.Defaut).Select(a => a.Id).ToList();
            var msg = new List<string>();
            if (deja.Count > 0) msg.Add("Déjà installé" + (deja.Count > 1 ? "s" : "") + " sur ce PC (non proposé" + (deja.Count > 1 ? "s" : "") + ") : " + string.Join(", ", deja.Select(a => a.Nom)) + ".");
            if (suggerees != null && suggerees.Count > 0 && materiel != null)
                msg.Add("Proposé" + (suggerees.Count > 1 ? "s" : "") + " pour le matériel de ce PC : " + string.Join(", ", proposees.Where(a => suggerees.Contains(a.Id)).Select(a => a.Nom)) + ".");
            Message = string.Join(" ", msg);
            // Les applications proposées pour ce PC d'abord, puis l'ordre du catalogue.
            return proposees.OrderBy(a => suggerees != null && suggerees.Contains(a.Id) ? 0 : 1).Select(Entree).ToList();
        }
    }
}
