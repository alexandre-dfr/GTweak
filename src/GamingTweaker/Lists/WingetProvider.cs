using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;

namespace GamingTweaker.Lists
{
    /// <summary>
    /// Mises à jour d'applications proposées par <c>winget upgrade</c>. La sortie est analysée par la position des colonnes lue sur la ligne
    /// d'en-tête, au-dessus des tirets, donc sans dépendre de la langue ni des noms tronqués (« … »). Même algorithme que ConvertFrom-GTWingetUpgrade (Common.ps1).
    /// </summary>
    /// <summary>winget a rendu un code de sortie d'échec : l'erreur est affichée, la liste n'est pas déclarée vide.</summary>
    public class WingetEchecException : Exception
    {
        public int Code { get; private set; }
        public WingetEchecException(int code, string sortie)
            : base("winget a échoué (code " + code + ")" + (string.IsNullOrWhiteSpace(sortie) ? "" : " : " + sortie.Trim())) { Code = code; }
    }

    public class WingetProvider : IListProvider, IListInfo
    {
        private static readonly Regex Tirets = new Regex(@"^-{5,}$", RegexOptions.CultureInvariant);
        private static readonly Regex IdValide = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]*$", RegexOptions.CultureInvariant);

        private readonly Func<CancellationToken, Task<string>> executer;

        /// <summary>0 ; 0x8A15002B (aucune mise à jour applicable) et 0x8A150014 (aucun paquet trouvé) comptent comme une liste vide, tout autre code est un échec.</summary>
        public static bool CodeListeAcceptable(int code) { return code == 0 || code == -1978335189 || code == -1978335212; }

        public WingetProvider(Func<CancellationToken, Task<string>> executer = null) { this.executer = executer ?? ExecuterWinget; }

        public string ListId { get { return "winget"; } }

        /// <summary>Message affiché sous la liste (winget absent, aucune mise à jour...) ; vide si tout va bien.</summary>
        public string Message { get; private set; } = "";
        public string Lien { get { return ""; } }

        /// <summary>Sortie de « winget upgrade » → une entrée par application ayant une mise à jour (Key = identifiant winget).</summary>
        public static IReadOnlyList<ListEntry> Parse(string output)
        {
            var res = new List<ListEntry>();
            if (string.IsNullOrEmpty(output)) return res;
            var lignes = output.Replace("\r\n", "\n").Split('\n').Select(l =>
            {
                var t = l;
                var cr = t.LastIndexOf('\r');
                if (cr >= 0) t = t.Substring(cr + 1);   // barre de progression écrite avec des retours chariot
                return t.TrimStart('﻿').TrimEnd();
            }).ToList();

            int sep = -1;
            for (int i = 1; i < lignes.Count; i++)
                if (Tirets.IsMatch(lignes[i]) && lignes[i - 1].Trim().Length > 0) { sep = i; break; }
            if (sep < 0) return res;

            var entete = lignes[sep - 1];
            var debuts = new List<int>();
            for (int p = 0; p < entete.Length; p++)
                if (entete[p] != ' ' && (p == 0 || (entete[p - 1] == ' ' && (p == 1 || entete[p - 2] == ' ')))) debuts.Add(p);
            if (debuts.Count < 3) return res;

            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = sep + 1; i < lignes.Count; i++)
            {
                var ligne = lignes[i];
                if (ligne.Trim().Length == 0) break;   // fin du tableau : le pied de page et les tableaux suivants sont ignorés
                bool ok = true;
                for (int k = 1; k < debuts.Count; k++)
                    if (ligne.Length > debuts[k] - 1 && ligne[debuts[k] - 1] != ' ') ok = false;
                if (!ok || ligne.Length <= debuts[1]) continue;
                var cellules = new List<string>();
                for (int k = 0; k < debuts.Count; k++)
                {
                    int de = debuts[k];
                    int a = k + 1 < debuts.Count ? debuts[k + 1] : ligne.Length;
                    cellules.Add(de >= ligne.Length ? "" : ligne.Substring(de, Math.Min(a, ligne.Length) - de).Trim());
                }
                var id = cellules[1];
                if (!IdValide.IsMatch(id) || !vus.Add(id)) continue;
                var nom = cellules[0];
                var dispo = cellules.Count > 3 ? cellules[3] : "";
                var source = cellules.Count > 4 ? cellules[4] : "";
                res.Add(new ListEntry
                {
                    Key = id,
                    Titre = (nom.Length == 0 ? id : nom) + " (" + id + ")",
                    Detail = cellules[2] + " → " + dispo + (source.Length > 0 ? " — source : " + source : ""),
                    Risque = RiskLevel.Sur,
                });
            }
            return res;
        }

        public async Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
        {
            string sortie;
            try { sortie = await executer(ct); }
            catch (Win32Exception)
            {
                Message = "winget est introuvable sur ce PC : les mises à jour d'applications seront ignorées. Installez « Programme d'installation d'application » depuis le Microsoft Store.";
                return new List<ListEntry>();
            }
            var liste = Parse(sortie);
            Message = liste.Count == 0 ? "Aucune mise à jour d'application disponible." : "";
            return liste;
        }

        private static async Task<string> ExecuterWinget(CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                // winget vérifié (paquet DesktopAppInstaller sous Program Files\WindowsApps), jamais celui du PATH ; absent : « introuvable ».
                var winget = Core.SystemExe.Winget();
                if (winget == null) throw new Win32Exception(2, "winget introuvable");
                var psi = new ProcessStartInfo(winget, "upgrade --accept-source-agreements --disable-interactivity")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var sortie = p.StandardOutput.ReadToEndAsync();
                    using (ct.Register(() => { try { p.Kill(); } catch (Exception) { } }))
                    {
                        if (!p.WaitForExit(120000)) { try { p.Kill(); } catch (Exception) { } throw new TimeoutException("winget n'a pas répondu à temps."); }
                    }
                    ct.ThrowIfCancellationRequested();
                    var texte = sortie.Result;
                    if (!CodeListeAcceptable(p.ExitCode))
                    {
                        var fin = string.Join(" ", texte.Replace('\r', '\n').Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.All(c => "-/|".IndexOf(c) >= 0 || c == (char)92)).Reverse().Take(2).Reverse());
                        throw new WingetEchecException(p.ExitCode, fin);
                    }
                    return texte;
                }
            }, ct);
        }
    }
}
