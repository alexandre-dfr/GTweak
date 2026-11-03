using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using Microsoft.Win32;

namespace GamingTweaker.Lists
{
    /// <summary>Une entrée de démarrage brute : <c>Scope</c> = HKCU ou HKLM ; <c>Kind</c> = Run, Run32 ou StartupFolder.</summary>
    public class StartupRaw
    {
        public string Scope { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Command { get; set; }
        /// <summary>Valeur binaire de StartupApproved (null si absente).</summary>
        public byte[] Approved { get; set; }
    }

    public interface IStartupSource
    {
        IReadOnlyList<StartupRaw> Lire();
    }

    /// <summary>Clé d'une entrée : « portée:type:nom » (le nom peut contenir des deux-points). Même format que Get-GTStartupKey (Common.ps1).</summary>
    public static class StartupKey
    {
        public static readonly string[] Scopes = { "HKCU", "HKLM" };
        public static readonly string[] Kinds = { "Run", "Run32", "StartupFolder" };

        public static string Make(string scope, string kind, string name) => scope + ":" + kind + ":" + name;

        public static bool TryParse(string key, out string scope, out string kind, out string name)
        {
            scope = kind = name = null;
            if (string.IsNullOrEmpty(key)) return false;
            var a = key.IndexOf(':');
            if (a < 0) return false;
            var b = key.IndexOf(':', a + 1);
            if (b < 0) return false;
            var s = key.Substring(0, a); var k = key.Substring(a + 1, b - a - 1); var n = key.Substring(b + 1);
            if (Array.IndexOf(Scopes, s) < 0 || Array.IndexOf(Kinds, k) < 0 || n.Length == 0) return false;
            scope = s; kind = k; name = n;
            return true;
        }
    }

    public static class StartupApproved
    {
        /// <summary>Windows note une entrée désactivée par un premier octet impair (3 ou 7) ; activée : 2 ou 6, ou valeur absente.</summary>
        public static bool EstDesactive(byte[] valeur) => valeur != null && valeur.Length > 0 && (valeur[0] & 1) == 1;
    }

    public class RegistryStartupSource : IStartupSource
    {
        private const string Racine = @"Software\Microsoft\Windows\CurrentVersion";

        public IReadOnlyList<StartupRaw> Lire()
        {
            var res = new List<StartupRaw>();
            Registre(res, Registry.CurrentUser, "HKCU", "Run", Racine + @"\Run");
            Registre(res, Registry.LocalMachine, "HKLM", "Run", Racine + @"\Run");
            Registre(res, Registry.LocalMachine, "HKLM", "Run32", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run");
            Dossier(res, Registry.CurrentUser, "HKCU", Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            Dossier(res, Registry.LocalMachine, "HKLM", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
            return res;
        }

        private static byte[] Approuve(RegistryKey hive, string kind, string nom)
        {
            try
            {
                using (var k = hive.OpenSubKey(Racine + @"\Explorer\StartupApproved\" + kind))
                    return k == null ? null : k.GetValue(nom) as byte[];
            }
            catch (Exception) { return null; }
        }

        private static void Registre(List<StartupRaw> res, RegistryKey hive, string scope, string kind, string chemin)
        {
            try
            {
                using (var k = hive.OpenSubKey(chemin))
                {
                    if (k == null) return;
                    foreach (var n in k.GetValueNames().Where(x => x.Length > 0))
                        res.Add(new StartupRaw { Scope = scope, Kind = kind, Name = n, Command = Convert.ToString(k.GetValue(n)), Approved = Approuve(hive, kind, n) });
                }
            }
            catch (Exception) { /* clé illisible : ignorée */ }
        }

        private static void Dossier(List<StartupRaw> res, RegistryKey hive, string scope, string dossier)
        {
            try
            {
                if (string.IsNullOrEmpty(dossier) || !Directory.Exists(dossier)) return;
                foreach (var f in Directory.GetFiles(dossier).Where(x => !string.Equals(Path.GetFileName(x), "desktop.ini", StringComparison.OrdinalIgnoreCase)))
                    res.Add(new StartupRaw { Scope = scope, Kind = "StartupFolder", Name = Path.GetFileName(f), Command = f, Approved = Approuve(hive, "StartupFolder", Path.GetFileName(f)) });
            }
            catch (Exception) { /* dossier illisible : ignoré */ }
        }
    }

    /// <summary>Programmes lancés au démarrage (Run, Run32, dossier Démarrage), de l'utilisateur et de la machine, avec leur état StartupApproved.</summary>
    public class StartupProvider : IListProvider
    {
        private readonly IStartupSource source;

        public StartupProvider(IStartupSource source = null) { this.source = source ?? new RegistryStartupSource(); }

        public string ListId { get { return "startup"; } }

        private static string Origine(string scope, string kind)
        {
            var qui = scope == "HKCU" ? "Utilisateur" : "Tous les utilisateurs";
            switch (kind)
            {
                case "Run32": return qui + " (registre, 32 bits)";
                case "StartupFolder": return qui + " (dossier Démarrage)";
                default: return qui + " (registre)";
            }
        }

        public Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
        {
            return Task.Run<IReadOnlyList<ListEntry>>(() =>
            {
                var res = new List<ListEntry>();
                foreach (var r in source.Lire())
                {
                    ct.ThrowIfCancellationRequested();
                    string s, k, n;
                    var cle = StartupKey.Make(r.Scope ?? "", r.Kind ?? "", r.Name ?? "");
                    if (!StartupKey.TryParse(cle, out s, out k, out n)) continue;
                    var detail = Origine(s, k) + (string.IsNullOrWhiteSpace(r.Command) ? "" : " : " + r.Command.Trim());
                    if (StartupApproved.EstDesactive(r.Approved)) detail += " : désactivé";
                    res.Add(new ListEntry { Key = cle, Titre = n, Detail = detail, Risque = RiskLevel.Avance });
                }
                return res;
            }, ct);
        }
    }
}
