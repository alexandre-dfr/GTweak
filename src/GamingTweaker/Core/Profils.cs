using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public static ProfilLoad Load(string path)
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

                p.Options = (p.Options ?? new List<string>()).Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
                p.Nom = p.Nom ?? "";
                p.Listes = p.Listes ?? new ProfilListes();

                var r = new ProfilLoad { Profil = p };
                if (p.Listes.Inconnues != null)
                    foreach (var k in p.Listes.Inconnues.Keys)
                        r.Avertissements.Add($"Liste « {k} » inconnue dans le profil « {nom} » : ignorée.");
                return r;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Echec($"Impossible de lire le profil « {nom} » : {e.Message}");
            }
        }

        private static ProfilLoad Echec(string msg) => new ProfilLoad { Erreur = msg };

        /// <summary>Écriture atomique : fichier temporaire dans le même dossier, puis remplacement.</summary>
        public static void Save(Profil p, string path)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
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
            return res;
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
