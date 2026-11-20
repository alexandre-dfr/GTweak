using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace GamingTweaker.Core
{
    /// <summary>Résultat d'une mise à jour installée : exe à relancer et remarques (copies protégées non remplacées…).</summary>
    public class ResultatMiseAJour
    {
        public string Exe { get; set; }
        public List<string> Remarques { get; } = new List<string>();
    }

    /// <summary>
    /// Mise à jour en un clic, depuis une release GitHub de ce dépôt :
    /// 1. l'exe est téléchargé en mémoire (jamais relu depuis le disque ensuite : aucun fichier ne peut être échangé entre contrôle et copie) ;
    /// 2. son empreinte SHA-256 doit être celle que GitHub publie pour la release (API en https) ;
    /// 3. si l'exe en cours est signé (Authenticode), le nouveau doit l'être aussi, valablement, par le même éditeur ;
    /// 4. l'exe en cours est renommé en « .ancien » (Windows l'autorise pendant l'exécution), le nouveau prend sa place, puis il est relancé ;
    ///    les copies protégées (routine, démarrage avec Windows) sont remplacées de la même façon ;
    /// 5. au lancement suivant, les « .ancien » sont supprimés.
    /// </summary>
    public static class MiseAJourInstallation
    {
        public const string SuffixeAncien = ".ancien";
        public const string ArgumentRelance = "--relance";
        private const long TailleMax = 64L * 1024 * 1024;

        public static string Sha256(byte[] donnees)
        {
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(donnees)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Contrôle du contenu téléchargé : un exe Windows (MZ) dont l'empreinte est celle attendue. Null si correct, sinon le motif du refus.</summary>
        public static string VerifierContenu(byte[] donnees, string sha256Attendu)
        {
            if (donnees == null || donnees.Length < 1024) return "le fichier téléchargé est vide ou tronqué";
            if (donnees[0] != (byte)'M' || donnees[1] != (byte)'Z') return "le fichier téléchargé n'est pas un programme Windows";
            if (string.IsNullOrEmpty(sha256Attendu)) return "GitHub ne publie pas l'empreinte de cet exe";
            var obtenu = Sha256(donnees);
            if (!string.Equals(obtenu, sha256Attendu.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                return "l'empreinte SHA-256 ne correspond pas à celle publiée par GitHub (attendue " + sha256Attendu + ", obtenue " + obtenu + ")";
            return null;
        }

        /// <summary>
        /// Décision pure sur les signatures (éditeur = sujet du certificat Authenticode valide, null si non signé ou invalide) :
        /// un exe signé n'est remplacé que par un exe valablement signé par le même éditeur. Null si accepté, sinon le motif du refus.
        /// </summary>
        public static string DecisionSignature(string editeurActuel, string editeurNouveau)
        {
            if (string.IsNullOrEmpty(editeurActuel)) return null;
            if (string.IsNullOrEmpty(editeurNouveau)) return "l'exe en cours est signé, mais le nouvel exe n'a pas de signature valide";
            if (!string.Equals(editeurActuel, editeurNouveau, StringComparison.OrdinalIgnoreCase))
                return "le nouvel exe est signé par un autre éditeur (" + editeurNouveau + ")";
            return null;
        }

        /// <summary>Télécharge, vérifie et installe la version ; InvalidOperationException avec un message lisible en cas de refus ou d'échec.</summary>
        public static async Task<ResultatMiseAJour> InstallerAsync(VersionDisponible v, Func<string, Task<byte[]>> telecharger = null)
        {
            if (v == null || !v.InstallableEnUnClic) throw new InvalidOperationException("Cette version ne peut pas être installée en un clic : ouvrez sa page et téléchargez l'exe.");
            TraceAppli.Ecrire("mise à jour : téléchargement de la version " + v.Version);
            byte[] donnees;
            try { donnees = await (telecharger ?? TelechargerAsync)(v.UrlExe).ConfigureAwait(false); }
            catch (Exception ex) { throw new InvalidOperationException("Téléchargement impossible : " + ex.Message, ex); }
            var refus = VerifierContenu(donnees, v.Sha256);
            if (refus != null) throw new InvalidOperationException("Mise à jour refusée : " + refus + ".");

            string exe;
            using (var p = Process.GetCurrentProcess()) exe = p.MainModule.FileName;
            refus = DecisionSignature(Editeur(exe), EditeurContenu(exe, donnees));
            if (refus != null) throw new InvalidOperationException("Mise à jour refusée : " + refus + ".");

            var r = new ResultatMiseAJour { Exe = exe };
            Remplacer(exe, donnees);
            // Copies protégées lancées par les tâches planifiées : sinon elles resteraient à l'ancienne version.
            foreach (var copie in new[] { DemarrageWindows.ExeCopie, Planification.ExeCopie })
            {
                try
                {
                    if (!File.Exists(copie) || string.Equals(Path.GetFullPath(copie), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) continue;
                    Remplacer(copie, donnees);
                }
                catch (Exception ex)
                {
                    TraceAppli.Erreur("mise à jour : copie protégée non remplacée (" + copie + ")", ex);
                    r.Remarques.Add("La copie " + copie + " n'a pas été mise à jour : " + ex.Message);
                }
            }
            TraceAppli.Ecrire("mise à jour : version " + v.Version + " installée (" + exe + ")");
            return r;
        }

        /// <summary>Renomme le fichier en « .ancien » puis écrit le nouveau contenu à sa place ; en cas d'échec, l'ancien est remis.</summary>
        public static void Remplacer(string chemin, byte[] donnees)
        {
            var ancien = chemin + SuffixeAncien;
            if (File.Exists(ancien)) File.Delete(ancien);   // échoue si un ancien exe tourne encore : rien n'est modifié
            File.Move(chemin, ancien);
            try
            {
                using (var f = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write, FileShare.None)) f.Write(donnees, 0, donnees.Length);
            }
            catch (Exception)
            {
                try { if (File.Exists(chemin)) File.Delete(chemin); File.Move(ancien, chemin); } catch (Exception) { }
                throw;
            }
        }

        /// <summary>Relance l'exe mis à jour ; il attend la fin de ce processus avant de prendre le verrou d'interface.</summary>
        public static void Relancer(string exe)
        {
            int pid;
            using (var p = Process.GetCurrentProcess()) pid = p.Id;
            Process.Start(new ProcessStartInfo(exe, ArgumentRelance + " " + pid) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        /// <summary>« --relance &lt;pid&gt; » : attend (30 s au plus) que l'ancienne instance soit fermée.</summary>
        public static void AttendreFin(string pid)
        {
            int id;
            if (!int.TryParse(pid, out id)) return;
            try { using (var p = Process.GetProcessById(id)) p.WaitForExit(30000); }
            catch (Exception) { }   // déjà fermée
        }

        /// <summary>Supprime les « .ancien » laissés par une mise à jour (exe en cours et copies protégées). Sans effet s'ils sont encore utilisés.</summary>
        public static void NettoyerAnciens()
        {
            try
            {
                string exe;
                using (var p = Process.GetCurrentProcess()) exe = p.MainModule.FileName;
                foreach (var f in new[] { exe, DemarrageWindows.ExeCopie, Planification.ExeCopie })
                    try { if (File.Exists(f + SuffixeAncien)) File.Delete(f + SuffixeAncien); } catch (Exception) { }
            }
            catch (Exception ex) { TraceAppli.Erreur("mise à jour : anciens exe non supprimés", ex); }
        }

        private static async Task<byte[]> TelechargerAsync(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var c = new HttpClient { Timeout = TimeSpan.FromMinutes(3), MaxResponseContentBufferSize = TailleMax })
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd("GamingTweaker/" + MiseAJour.VersionCourante);
                using (var r = await c.GetAsync(url).ConfigureAwait(false))
                {
                    if (!r.IsSuccessStatusCode) throw new InvalidOperationException("GitHub a répondu " + (int)r.StatusCode + " " + r.ReasonPhrase);
                    return await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
        }

        // ---------- Signature Authenticode ----------

        /// <summary>Éditeur du nouvel exe : écrit à côté de l'exe en cours, gardé ouvert en lecture seule (ni écriture ni suppression) pendant le contrôle, puis supprimé.</summary>
        private static string EditeurContenu(string exe, byte[] donnees)
        {
            var temp = exe + ".verif";
            try
            {
                using (var f = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
                {
                    f.Write(donnees, 0, donnees.Length); f.Flush(true);
                    return Editeur(temp);
                }
            }
            finally { try { File.Delete(temp); } catch (Exception) { } }
        }

        /// <summary>Sujet du certificat si le fichier porte une signature Authenticode valide (WinVerifyTrust), sinon null.</summary>
        public static string Editeur(string chemin)
        {
            try
            {
                if (!SignatureValide(chemin)) return null;
                return new X509Certificate2(X509Certificate.CreateFromSignedFile(chemin)).Subject;
            }
            catch (Exception) { return null; }
        }

        private static readonly Guid VerificationGenerique = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WintrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData, pSIPClientData;
            public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData, pwszURLReference;
            public uint dwProvFlags, dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WintrustData data);

        private static bool SignatureValide(string chemin)
        {
            var info = new WintrustFileInfo { cbStruct = (uint)Marshal.SizeOf(typeof(WintrustFileInfo)), pcwszFilePath = chemin };
            var pInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WintrustFileInfo)));
            try
            {
                Marshal.StructureToPtr(info, pInfo, false);
                var data = new WintrustData
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WintrustData)),
                    dwUIChoice = 2,            // WTD_UI_NONE
                    fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                    dwUnionChoice = 1,         // WTD_CHOICE_FILE
                    pFile = pInfo,
                    dwProvFlags = 0x10,        // WTD_REVOCATION_CHECK_NONE
                };
                var action = VerificationGenerique;
                return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure(pInfo, typeof(WintrustFileInfo));
                Marshal.FreeHGlobal(pInfo);
            }
        }
    }
}
