using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamingTweaker.Core
{
    public class ListeWinget
    {
        /// <summary>"tout-sauf" ou "liste".</summary>
        [JsonProperty("mode")] public string Mode { get; set; } = "tout-sauf";
        [JsonProperty("exclusions")] public List<string> Exclusions { get; set; } = new List<string>();
        [JsonProperty("elements")] public List<string> Elements { get; set; } = new List<string>();
    }

    public class ListeWindowsUpdate
    {
        [JsonProperty("exclusionsKb")] public List<string> ExclusionsKb { get; set; } = new List<string>();
    }

    public class ListePilote
    {
        /// <summary>"GameReady" ou "Studio".</summary>
        [JsonProperty("branche")] public string Branche { get; set; }
    }

    public class ProfilListes
    {
        [JsonProperty("winget", NullValueHandling = NullValueHandling.Ignore)] public ListeWinget Winget { get; set; }
        [JsonProperty("windowsUpdate", NullValueHandling = NullValueHandling.Ignore)] public ListeWindowsUpdate WindowsUpdate { get; set; }
        [JsonProperty("bloatware", NullValueHandling = NullValueHandling.Ignore)] public List<string> Bloatware { get; set; }
        [JsonProperty("services", NullValueHandling = NullValueHandling.Ignore)] public List<string> Services { get; set; }
        [JsonProperty("pilote", NullValueHandling = NullValueHandling.Ignore)] public ListePilote Pilote { get; set; }

        /// <summary>Clés non reconnues : ignorées, signalées au chargement.</summary>
        [JsonExtensionData] public IDictionary<string, JToken> Inconnues { get; set; }

        /// <summary>Copie indépendante (les listes ne sont jamais partagées entre le profil chargé et celui qu'on enregistre).</summary>
        public ProfilListes Cloner()
        {
            return new ProfilListes
            {
                Winget = Winget == null ? null : new ListeWinget { Mode = Winget.Mode, Exclusions = new List<string>(Winget.Exclusions ?? new List<string>()), Elements = new List<string>(Winget.Elements ?? new List<string>()) },
                WindowsUpdate = WindowsUpdate == null ? null : new ListeWindowsUpdate { ExclusionsKb = new List<string>(WindowsUpdate.ExclusionsKb ?? new List<string>()) },
                Bloatware = Bloatware == null ? null : new List<string>(Bloatware),
                Services = Services == null ? null : new List<string>(Services),
                Pilote = Pilote == null ? null : new ListePilote { Branche = Pilote.Branche },
                Inconnues = Inconnues == null ? null : Inconnues.ToDictionary(kv => kv.Key, kv => kv.Value == null ? null : kv.Value.DeepClone()),
            };
        }
    }

    /// <summary>Profil : uniquement des identifiants, jamais de commandes.</summary>
    public class Profil
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;
        [JsonProperty("nom")] public string Nom { get; set; } = "";
        [JsonProperty("options")] public List<string> Options { get; set; } = new List<string>();
        [JsonProperty("listes")] public ProfilListes Listes { get; set; } = new ProfilListes();
    }

    public class ProfilLoad
    {
        public Profil Profil { get; set; }
        public string Erreur { get; set; }
        public List<string> Avertissements { get; set; } = new List<string>();
    }

    public class ResolvedProfil
    {
        public Selection Selection { get; set; } = new Selection();
        public List<string> Inconnues { get; set; } = new List<string>();
        public List<string> Incompatibles { get; set; } = new List<string>();
    }

    public static class Profils
    {
        public const int VersionCourante = 1;

        /// <summary>
        /// <paramref name="clesListes"/> : clés de listes génériques connues (les <c>Liste</c> du catalogue). Une telle clé doit contenir un tableau de
        /// chaînes valides ; toute autre clé non typée est ignorée avec un avertissement.
        /// </summary>
        public static ProfilLoad Load(string path, IEnumerable<string> clesListes = null)
        {
            var nom = Path.GetFileName(path ?? "");
            try
            {
                if (!File.Exists(path)) return Echec($"Profil « {nom} » introuvable.");
                var texte = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(texte)) return Echec($"Le profil « {nom} » est vide.");

                JObject racine;
                try { racine = JToken.Parse(texte) as JObject; }
                catch (JsonException e) { return Echec($"Le profil « {nom} » n’est pas un JSON valide : {e.Message}"); }
                if (racine == null) return Echec($"Le profil « {nom} » est invalide : un objet JSON est attendu.");

                var v = racine["version"];
                if (v == null || v.Type != JTokenType.Integer) return Echec($"Le profil « {nom} » est invalide : champ « version » manquant.");
                var version = v.Value<long>();
                if (version > VersionCourante) return Echec($"Le profil « {nom} » est un profil créé par une version plus récente de GamingTweaker (version {version}).");
                if (version != VersionCourante) return Echec($"Le profil « {nom} » est invalide : version {version} non prise en charge.");

                Profil p;
                try { p = racine.ToObject<Profil>(); }
                catch (JsonException e) { return Echec($"Le profil « {nom} » est invalide : {e.Message}"); }
                if (p == null) return Echec($"Le profil « {nom} » est invalide.");

                p.Options = p.Options ?? new List<string>();
                p.Nom = p.Nom ?? "";
                p.Listes = p.Listes ?? new ProfilListes();

                var invalide = Valider(p);
                if (invalide != null) return Echec($"Le profil « {nom} » est invalide : {invalide}");

                var connues = new HashSet<string>(clesListes ?? new string[0], StringComparer.Ordinal);
                var r = new ProfilLoad { Profil = p };
                if (p.Listes.Inconnues != null)
                    foreach (var k in p.Listes.Inconnues.Keys)
                    {
                        if (!connues.Contains(k)) continue;
                        var tab = p.Listes.Inconnues[k] as JArray;
                        if (tab == null) return Echec($"Le profil « {nom} » est invalide : liste « {k} » : un tableau de chaînes est attendu.");
                        foreach (var t in tab)
                            if (t.Type != JTokenType.String || !ElementValide((string)t))
                                return Echec($"Le profil « {nom} » est invalide : liste « {k} » : valeur « {(t.Type == JTokenType.String ? (string)t : t.ToString())} » invalide.");
                    }
                if (p.Listes.Inconnues != null)
                    foreach (var k in p.Listes.Inconnues.Keys.Where(k => !connues.Contains(k)))
                        r.Avertissements.Add($"Liste « {k} » inconnue dans le profil « {nom} » : ignorée.");
                return r;
            }
            catch (Exception e)
            {
                return Echec($"Impossible de lire le profil « {nom} » : {e.Message}");
            }
        }

        private static readonly Regex ReOption = new Regex(@"^[a-z0-9][a-z0-9.\-]*\z", RegexOptions.CultureInvariant);
        private static readonly Regex ReWinget = new Regex(@"^[A-Za-z0-9][A-Za-z0-9.\-_+]*\z", RegexOptions.CultureInvariant);
        private static readonly Regex ReKb = new Regex(@"^KB\d{4,8}\z", RegexOptions.CultureInvariant);
        /// <summary>
        /// Élément d'une liste générique : « * », ou un nom éventuellement précédé de « ! » (lettres et chiffres Unicode, espaces, _ . - + ( ) : ' , &amp; ® ™ ;
        /// les deux-points servent aux clés de démarrage « HKCU:Run:Nom »). Guillemets, $, accent grave, ; | &lt; &gt;, retours à la ligne et caractères de contrôle sont refusés.
        /// </summary>
        private static readonly Regex ReGenerique = new Regex(@"^(\*|!?[\p{L}\p{N}][\p{L}\p{M}\p{N} _.\-+():',&\u00AE\u2122]*)\z", RegexOptions.CultureInvariant);
        public static bool ElementValide(string v) { return v != null && ReGenerique.IsMatch(v); }
        private static readonly Regex ReNom = new Regex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]*\z", RegexOptions.CultureInvariant);

        /// <summary>Contenu invalide = erreur de chargement (jamais ignoré ni élargi). Renvoie le message, ou null.</summary>
        private static string Valider(Profil p)
        {
            string e;
            if ((e = Verifier("options", p.Options, ReOption)) != null) return e;
            var l = p.Listes;
            if (l.Winget != null)
            {
                var mode = l.Winget.Mode == null ? null : l.Winget.Mode.Trim();
                if (mode != "tout-sauf" && mode != "liste")
                    return $"liste « winget », champ « mode » : valeur « {l.Winget.Mode} » non reconnue (attendu « tout-sauf » ou « liste »).";
                l.Winget.Mode = mode;
                if (l.Winget.Exclusions == null) l.Winget.Exclusions = new List<string>();
                if (l.Winget.Elements == null) l.Winget.Elements = new List<string>();
                if ((e = Verifier("winget (exclusions)", l.Winget.Exclusions, ReWinget)) != null) return e;
                if ((e = Verifier("winget (elements)", l.Winget.Elements, ReWinget)) != null) return e;
            }
            if (l.WindowsUpdate != null)
            {
                if (l.WindowsUpdate.ExclusionsKb == null) l.WindowsUpdate.ExclusionsKb = new List<string>();
                if ((e = Verifier("windowsUpdate (exclusionsKb)", l.WindowsUpdate.ExclusionsKb, ReKb)) != null) return e;
            }
            if (l.Bloatware != null && (e = Verifier("bloatware", l.Bloatware, ReNom)) != null) return e;
            if (l.Services != null && (e = Verifier("services", l.Services, ReNom)) != null) return e;
            if (l.Pilote != null)
            {
                var b = l.Pilote.Branche == null ? null : l.Pilote.Branche.Trim();
                if (b != "GameReady" && b != "Studio")
                    return $"liste « pilote », champ « branche » : valeur « {l.Pilote.Branche} » non reconnue (attendu « GameReady » ou « Studio »).";
                l.Pilote.Branche = b;
            }
            return null;
        }

        private static string Verifier(string liste, List<string> valeurs, Regex re)
        {
            if (valeurs == null) return null;
            foreach (var v in valeurs)
                if (v == null || !re.IsMatch(v))
                    return $"liste « {liste} » : valeur « {v ?? "null"} » invalide.";
            return null;
        }

        private static ProfilLoad Echec(string msg) => new ProfilLoad { Erreur = msg };

        /// <summary>Écriture atomique : fichier temporaire dans le même dossier, puis remplacement.</summary>
        public static void Save(Profil p, string path)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            p.Version = VersionCourante;
            var json = JsonConvert.SerializeObject(p, Formatting.Indented);
            var tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            }
        }

        public static ResolvedProfil Resolve(Profil p, IReadOnlyList<CatalogItem> catalogue, MachineFacts facts)
        {
            var res = new ResolvedProfil();
            var parId = new Dictionary<string, CatalogItem>();
            foreach (var c in catalogue) parId[c.Id] = c;

            foreach (var id in (p.Options ?? new List<string>()).Distinct())
            {
                CatalogItem item;
                if (!parId.TryGetValue(id, out item)) res.Inconnues.Add(id);
                else if (!Compatibilite.IsCompatible(item.Compatibilite ?? new CompatRule(), facts)) res.Incompatibles.Add(id);
                else res.Selection.OptionIds.Add(id);
            }

            var l = p.Listes ?? new ProfilListes();
            Attacher(res, catalogue, "winget", l.Winget == null ? null : WingetListe(l.Winget));
            Attacher(res, catalogue, "windowsUpdate", l.WindowsUpdate == null ? null
                : new List<string> { "*" }.Concat((l.WindowsUpdate.ExclusionsKb ?? new List<string>()).Select(k => "!" + k)).ToList());
            Attacher(res, catalogue, "bloatware", l.Bloatware);
            Attacher(res, catalogue, "services", l.Services);
            Attacher(res, catalogue, "pilote", l.Pilote == null || string.IsNullOrEmpty(l.Pilote.Branche) ? null : new List<string> { l.Pilote.Branche });
            if (l.Inconnues != null)
                foreach (var kv in l.Inconnues)
                {
                    var tab = kv.Value as JArray;
                    if (tab == null || tab.Any(t => t.Type != JTokenType.String || !ElementValide((string)t))) continue;
                    Attacher(res, catalogue, kv.Key, tab.Select(t => (string)t).ToList());
                }
            return res;
        }

        /// <summary>Faux si la clé de profil ne peut pas contenir « * » (services, bloatware, bloatware-tierces, pilote, taches).</summary>
        public static bool CleSupporteTous(string cle) { return cle != "services" && cle != "bloatware" && cle != "bloatware-tierces" && cle != "pilote" && cle != "taches"; }

        /// <summary>Retire la liste de cette clé (typée ou générique) du profil.</summary>
        public static void RetirerListe(ProfilListes l, string cle)
        {
            if (l == null || string.IsNullOrEmpty(cle)) return;
            switch (cle)
            {
                case "winget": l.Winget = null; return;
                case "windowsUpdate": l.WindowsUpdate = null; return;
                case "bloatware": l.Bloatware = null; return;
                case "services": l.Services = null; return;
                case "pilote": l.Pilote = null; return;
            }
            if (l.Inconnues != null) l.Inconnues.Remove(cle);
        }

        private static readonly string[] ClesTypees = { "winget", "windowsUpdate", "bloatware", "services", "pilote" };

        /// <summary>
        /// Écrit la sélection d'une option à liste dans le profil : clé typée si elle existe, sinon clé générique (nom de <c>CatalogItem.Liste</c>).
        /// Renvoie false si elle ne peut pas être représentée sans l'élargir (rien n'est écrit). Pour une liste explicite générique, les éléments
        /// invalides sont écartés (la sélection ne fait que se réduire) et signalés dans <paramref name="ecartes"/>.
        /// </summary>
        public static bool AjouterListe(ProfilListes l, string cle, IReadOnlyList<string> valeurs, List<string> ecartes = null)
        {
            if (l == null || string.IsNullOrEmpty(cle) || valeurs == null) return false;
            bool tous = valeurs.Contains("*");
            var exclus = valeurs.Where(v => v != null && v.StartsWith("!", StringComparison.Ordinal)).Select(v => v.Substring(1)).ToList();
            var positifs = valeurs.Where(v => v != null && v != "*" && !v.StartsWith("!", StringComparison.Ordinal)).ToList();
            switch (cle)
            {
                case "winget":
                {
                    var src = tous ? exclus : positifs;
                    if (src.Any(v => !ReWinget.IsMatch(v))) return false;
                    l.Winget = tous ? new ListeWinget { Mode = "tout-sauf", Exclusions = exclus } : new ListeWinget { Mode = "liste", Elements = positifs };
                    return true;
                }
                case "windowsUpdate":
                    if (!tous || positifs.Count > 0 || exclus.Any(v => !ReKb.IsMatch(v))) return false;
                    l.WindowsUpdate = new ListeWindowsUpdate { ExclusionsKb = exclus };
                    return true;
                case "bloatware":
                case "services":
                {
                    if (tous || positifs.Any(v => !ReNom.IsMatch(v))) return false;
                    if (cle == "services") l.Services = positifs; else l.Bloatware = positifs;
                    return true;
                }
                case "pilote":
                    if (tous || positifs.Count != 1 || (positifs[0] != "GameReady" && positifs[0] != "Studio")) return false;
                    l.Pilote = new ListePilote { Branche = positifs[0] };
                    return true;
            }
            if (tous && (exclus.Any(v => !ElementValide("!" + v)))) return false;
            var ok = new List<string>();
            if (tous) { ok.Add("*"); ok.AddRange(exclus.Select(v => "!" + v)); }
            else
                foreach (var v in positifs)
                {
                    if (ElementValide(v)) ok.Add(v);
                    else if (ecartes != null) ecartes.Add(v);
                }
            if (l.Inconnues == null) l.Inconnues = new Dictionary<string, JToken>();
            l.Inconnues[cle] = new JArray(ok);
            return true;
        }

        private static List<string> WingetListe(ListeWinget w)
        {
            if (string.Equals(w.Mode, "liste", StringComparison.OrdinalIgnoreCase))
                return new List<string>(w.Elements ?? new List<string>());
            return new List<string> { "*" }.Concat((w.Exclusions ?? new List<string>()).Select(x => "!" + x)).ToList();
        }

        private static void Attacher(ResolvedProfil res, IReadOnlyList<CatalogItem> cat, string cle, List<string> valeurs)
        {
            if (valeurs == null) return;
            foreach (var item in cat.Where(c => c.Liste == cle && res.Selection.OptionIds.Contains(c.Id)))
                res.Selection.Listes[item.Id] = new List<string>(valeurs);
        }

        public static bool ContientRisque(Profil p, IReadOnlyList<CatalogItem> catalogue)
        {
            var ids = new HashSet<string>(p.Options ?? new List<string>());
            return catalogue.Any(c => ids.Contains(c.Id) && c.Risque == RiskLevel.Risque);
        }
    }
}
