using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using Newtonsoft.Json.Linq;

namespace GamingTweaker.Lists
{
    /// <summary>Une tâche planifiée brute : chemin complet (« \Dossier\Nom »), état (« Ready », « Disabled »…) et programme lancé.</summary>
    public class TacheBrute
    {
        public string Chemin { get; set; }
        public string Etat { get; set; }
        public string Programme { get; set; }
    }

    public interface ITachesSource
    {
        Task<IReadOnlyList<TacheBrute>> LireAsync(CancellationToken ct);
    }

    /// <summary>Règles partagées avec Common.ps1 (Get-GTTaskKey, Test-GTTaskTierce) : mêmes tâches proposées, mêmes clés de profil.</summary>
    public static class TachesTierces
    {
        private static readonly Regex HorsProfil = new Regex(@"[^\p{L}\p{M}\p{N} _.\-+():',&®™]", RegexOptions.CultureInvariant);
        private static readonly Regex Debut = new Regex(@"^[\p{L}\p{N}]", RegexOptions.CultureInvariant);
        private static readonly Regex MajNavigateur = new Regex(
            @"(GoogleUpdate|GoogleUpdater|MicrosoftEdgeUpdate|BraveSoftwareUpdate|Firefox Background Update|Opera scheduled Autoupdate|Vivaldi ?Update)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Clé de profil : chemin sans « \ » initial, « \ » → « : », accolades et crochets → parenthèses, autre caractère refusé par les profils → « _ ». Null si inutilisable.</summary>
        public static string Cle(string chemin)
        {
            if (string.IsNullOrEmpty(chemin)) return null;
            var s = chemin.TrimStart('\\').Replace('\\', ':').Replace('{', '(').Replace('[', '(').Replace('}', ')').Replace(']', ')');
            s = HorsProfil.Replace(s, "_");
            return Debut.IsMatch(s) ? s : null;
        }

        /// <summary>Vrai pour une tâche d'application : hors des dossiers Microsoft et GamingTweaker, et pas d'un antivirus.</summary>
        public static bool EstTierce(string chemin)
        {
            if (string.IsNullOrEmpty(chemin)) return false;
            if (chemin.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) || chemin.StartsWith(@"\GamingTweaker\", StringComparison.OrdinalIgnoreCase)) return false;
            var nom = chemin.Substring(chemin.LastIndexOf('\\') + 1);
            var dossier = chemin.TrimStart('\\').Split('\\')[0];
            return !StartupSecurity.EstSecurite(nom) && !StartupSecurity.EstSecurite(dossier);
        }

        /// <summary>Programme de mise à jour d'un navigateur : le désactiver prive le navigateur de ses correctifs de sécurité (Risqué).</summary>
        public static bool EstMiseAJourNavigateur(string chemin) => !string.IsNullOrEmpty(chemin) && MajNavigateur.IsMatch(chemin);
    }

    /// <summary>Lecture par Get-ScheduledTask (lecture seule), sortie JSON.</summary>
    public class PowerShellTachesSource : ITachesSource
    {
        private const string Script =
            "$l = @(Get-ScheduledTask -ErrorAction Stop | ForEach-Object { [pscustomobject]@{ Chemin = [string]$_.TaskPath + [string]$_.TaskName; " +
            "Etat = [string]$_.State; Programme = [string](@($_.Actions | Where-Object { $_.Execute }) | Select-Object -First 1).Execute } })\r\n" +
            "ConvertTo-Json -InputObject $l -Compress -Depth 3";

        public async Task<IReadOnlyList<TacheBrute>> LireAsync(CancellationToken ct)
        {
            return Analyser(await PowerShellQuery.RunAsync(Script, ct));
        }

        /// <summary>Tableau JSON [{Chemin, Etat, Programme}] ; un objet seul ou une sortie vide sont acceptés.</summary>
        public static IReadOnlyList<TacheBrute> Analyser(string json)
        {
            var res = new List<TacheBrute>();
            if (string.IsNullOrWhiteSpace(json)) return res;
            var jeton = JToken.Parse(json.Trim());
            var tab = jeton as JArray ?? new JArray(jeton);
            foreach (var o in tab)
            {
                if (o.Type != JTokenType.Object) continue;
                res.Add(new TacheBrute { Chemin = (string)o["Chemin"], Etat = (string)o["Etat"], Programme = (string)o["Programme"] });
            }
            return res;
        }
    }

    /// <summary>Tâches planifiées des applications installées (opt.tasks-tierces), détectées au premier affichage de la liste.</summary>
    public class TachesProvider : IListProvider
    {
        private readonly ITachesSource source;

        public TachesProvider(ITachesSource source = null) { this.source = source ?? new PowerShellTachesSource(); }

        public string ListId { get { return "taches"; } }

        public async Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
        {
            var brutes = await source.LireAsync(ct);
            var res = new List<ListEntry>();
            var vues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in brutes)
            {
                ct.ThrowIfCancellationRequested();
                if (!TachesTierces.EstTierce(t.Chemin)) continue;
                var cle = TachesTierces.Cle(t.Chemin);
                if (cle == null || !vues.Add(cle)) continue;
                var nav = TachesTierces.EstMiseAJourNavigateur(t.Chemin);
                var detail = string.IsNullOrWhiteSpace(t.Programme) ? t.Chemin : t.Chemin + " : " + t.Programme.Trim();
                if (nav) detail = "Mise à jour du navigateur (correctifs de sécurité) — " + detail;
                if (string.Equals(t.Etat, "Disabled", StringComparison.OrdinalIgnoreCase)) detail += " : désactivée";
                var nom = t.Chemin.Substring(t.Chemin.LastIndexOf('\\') + 1);
                res.Add(new ListEntry { Key = cle, Titre = nom, Detail = detail, Risque = nav ? RiskLevel.Risque : RiskLevel.Avance });
            }
            res.Sort((a, b) => a.Risque != b.Risque ? a.Risque.CompareTo(b.Risque) : string.Compare(a.Titre, b.Titre, StringComparison.CurrentCultureIgnoreCase));
            return res;
        }
    }
}
