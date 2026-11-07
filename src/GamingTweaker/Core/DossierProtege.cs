using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GamingTweaker.Core
{
    public enum ActionProtection { Aucune, Appliquer, Supprimer }

    /// <summary>Ce qui a été lu sur un élément du dossier (par poignée, sans suivre les liens).</summary>
    public class EntreeProtection
    {
        public bool EstRacine { get; set; }
        public bool EstDossier { get; set; }
        /// <summary>Point d'analyse (jonction, lien symbolique…) : jamais suivi.</summary>
        public bool EstLien { get; set; }
        /// <summary>Nombre de liens physiques du fichier.</summary>
        public int NombreLiens { get; set; }
        /// <summary>L'élément est sur un autre volume que la racine (redirection possible).</summary>
        public bool EstVolumeInattendu { get; set; }
        public string ProprietaireSid { get; set; }
        public bool DaclSure { get; set; }
    }

    public class DecisionProtection
    {
        public ActionProtection Action { get; set; }
        public string Avertissement { get; set; }
    }

    public class AceInfo
    {
        public string Sid { get; set; }
        public int Masque { get; set; }
        public bool Autorise { get; set; }
    }

    public class ResultatProtection
    {
        public List<string> Avertissements { get; } = new List<string>();
        /// <summary>Non null : le dossier n'a pas pu être sécurisé ; l'appli élevée ne doit pas s'en servir.</summary>
        public string Erreur { get; set; }
    }

    /// <summary>
    /// Dossier de données utilisé avec des droits élevés (C:\ProgramData\GamingTweaker) : propriétaire BUILTIN\Administrators,
    /// DACL protégée (héritage coupé) SYSTEM et Administrateurs contrôle total, Utilisateurs lecture et exécution, appliquée à tout le contenu.
    /// Tout élément dont le propriétaire n'est ni SYSTEM ni Administrateurs — comme une jonction, un lien symbolique, un lien physique
    /// ou un élément sur un autre volume — est SUPPRIMÉ (jamais repris ni suivi), pour qu'un utilisateur standard ne puisse ni déposer de DLL,
    /// de .config ou de journaux à côté de l'exe lancé élevé par la tâche, ni rediriger la modification des droits vers un fichier système.
    /// Les poignées sont ouvertes sans partage d'écriture ni de suppression (une poignée concurrente arrête l'application) et l'identité
    /// de chaque élément (volume + index) est revérifiée avant toute suppression.
    /// </summary>
    public static class DossierProtege
    {
        public const string SidSysteme = "S-1-5-18";
        public const string SidAdministrateurs = "S-1-5-32-544";
        public const string SidUtilisateurs = "S-1-5-32-545";
        public const string NomJournal = "securite.log";

        private const int LectureExecution = 0x1200A9;          // FileSystemRights.ReadAndExecute (Synchronize inclus)
        private const int ControleTotal = 0x1F01FF;             // FileSystemRights.FullControl
        private const int LectureGenerique = unchecked((int)0x80000000) | 0x20000000; // GENERIC_READ | GENERIC_EXECUTE

        // ---------- Décision pure ----------

        public static bool ProprietaireDeConfiance(string sid)
        {
            return sid == SidSysteme || sid == SidAdministrateurs;
        }

        /// <summary>Vrai si aucune entrée « autoriser » ne donne plus que lecture/exécution à un autre compte que SYSTEM ou Administrateurs. DACL nulle : faux.</summary>
        public static bool DaclSure(IEnumerable<AceInfo> aces)
        {
            if (aces == null) return false;
            foreach (var a in aces)
            {
                if (!a.Autorise || ProprietaireDeConfiance(a.Sid)) continue;
                if ((a.Masque & ~(LectureExecution | LectureGenerique)) != 0) return false;
            }
            return true;
        }

        /// <param name="appliquerProprietaire">Vrai quand l'appli est élevée (le propriétaire peut être fixé) : un propriétaire non fiable fait supprimer l'élément.
        /// Hors élévation (tests), on ne peut pas prendre possession : la règle de propriétaire n'est pas appliquée, seuls les liens et redirections sont supprimés.</param>
        public static DecisionProtection Decider(EntreeProtection e, bool appliquerProprietaire, string chemin)
        {
            if (e.EstLien)
                return new DecisionProtection { Action = ActionProtection.Supprimer, Avertissement = "Lien ou jonction supprimé (jamais suivi) : " + chemin };
            if (!e.EstDossier && e.NombreLiens > 1)
                return new DecisionProtection { Action = ActionProtection.Supprimer, Avertissement = "Lien physique supprimé (la cible n'est pas modifiée) : " + chemin };
            if (e.EstVolumeInattendu)
                return new DecisionProtection { Action = ActionProtection.Supprimer, Avertissement = "Élément sur un autre volume supprimé (redirection) : " + chemin };
            if (appliquerProprietaire && !ProprietaireDeConfiance(e.ProprietaireSid))
                return new DecisionProtection
                {
                    Action = ActionProtection.Supprimer,
                    Avertissement = "Élément au propriétaire non fiable (" + (e.ProprietaireSid ?? "inconnu") + ") supprimé : " + chemin,
                };
            if (e.EstRacine || !e.DaclSure) return new DecisionProtection { Action = ActionProtection.Appliquer };
            return new DecisionProtection { Action = ActionProtection.Aucune };
        }

        /// <summary>DACL appliquée : SYSTEM et Administrateurs contrôle total, Utilisateurs lecture et exécution (héritables pour un dossier).</summary>
        public static RawAcl DaclAttendue(bool dossier)
        {
            var flags = dossier ? AceFlags.ContainerInherit | AceFlags.ObjectInherit : AceFlags.None;
            var acl = new RawAcl(GenericAcl.AclRevision, 3);
            acl.InsertAce(acl.Count, new CommonAce(flags, AceQualifier.AccessAllowed, ControleTotal, new SecurityIdentifier(SidSysteme), false, null));
            acl.InsertAce(acl.Count, new CommonAce(flags, AceQualifier.AccessAllowed, ControleTotal, new SecurityIdentifier(SidAdministrateurs), false, null));
            acl.InsertAce(acl.Count, new CommonAce(flags, AceQualifier.AccessAllowed, LectureExecution, new SecurityIdentifier(SidUtilisateurs), false, null));
            return acl;
        }

        // ---------- Application ----------

        /// <summary>
        /// Au démarrage élevé, avant toute lecture ou écriture du dossier. Non élevé : rien (aucun droit à protéger, et impossible de fixer le propriétaire).
        /// Les avertissements sont ajoutés à securite.log dans le dossier une fois sécurisé.
        /// </summary>
        public static ResultatProtection SecuriserAuDemarrage(string racine, Func<bool> estAdministrateur, bool definirProprietaire = true)
        {
            var r = new ResultatProtection();
            bool admin;
            try { admin = estAdministrateur(); } catch (Exception) { admin = false; }
            if (!admin) return r;
            try
            {
                r.Avertissements.AddRange(Securiser(racine, definirProprietaire));
                Journaliser(racine, r.Avertissements);
            }
            catch (Exception ex)
            {
                r.Erreur = "Le dossier de données " + racine + " n'a pas pu être sécurisé : " + ex.Message;
            }
            return r;
        }

        /// <summary>Ajoute des avertissements à securite.log (dossier déjà sécurisé : son propriétaire et sa DACL interdisent à un utilisateur de le remplacer). Non bloquant.</summary>
        public static void Journaliser(string racine, IEnumerable<string> avertissements)
        {
            var l = avertissements.ToList();
            if (l.Count == 0) return;
            try
            {
                var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.AppendAllText(Path.Combine(racine, NomJournal), string.Concat(l.Select(a => date + "  AVERTISSEMENT  " + a + "\r\n")), new UTF8Encoding(true));
            }
            catch (Exception) { }
        }

        /// <summary>Crée le dossier si besoin et le sécurise avec tout son contenu. Renvoie les avertissements ; lève une exception en cas d'échec.</summary>
        public static List<string> Securiser(string racine)
        {
            return Securiser(racine, true);
        }

        internal static List<string> Securiser(string racine, bool definirProprietaire)
        {
            if (string.IsNullOrWhiteSpace(racine)) throw new ArgumentException("Dossier à sécuriser non précisé.");
            racine = Path.GetFullPath(racine).TrimEnd('\\');
            ActiverPrivileges();
            var avert = new List<string>();
            // La racine doit finir en vrai dossier, propriétaire fiable (en mode élevé) et non lien. Un élément douteux est supprimé puis recréé vide.
            for (int essai = 0; ; essai++)
            {
                if (!Existe(racine)) Directory.CreateDirectory(racine);
                if (Traiter(racine, true, definirProprietaire, 0, avert)) break;
                if (essai >= 3) throw new IOException("Le dossier " + racine + " est remplacé par un élément non fiable à chaque tentative.");
            }
            return avert;
        }

        /// <summary>
        /// Traite un élément par sa poignée (ouverte sans partage d'écriture ni suppression). Renvoie faux si l'élément a été supprimé.
        /// Pour un dossier conservé, applique la DACL protégée puis descend : la poignée est tenue pendant tout le parcours des enfants,
        /// et le dossier étant alors sécurisé, aucun enfant ne peut être échangé.
        /// </summary>
        private static bool Traiter(string chemin, bool estRacine, bool definirProprietaire, uint volumeRacine, List<string> avert)
        {
            DecisionProtection d;
            bool estDossier, estLien;
            Identite id;
            using (var h = Ouvrir(chemin, false, definirProprietaire))
            {
                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(h, out info)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Lecture des attributs impossible : " + chemin);
                estDossier = (info.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                estLien = (info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                id = new Identite { Volume = info.VolumeSerialNumber, IndexHaut = info.FileIndexHigh, IndexBas = info.FileIndexLow };
                if (estRacine) volumeRacine = info.VolumeSerialNumber;

                string proprio; List<AceInfo> aces;
                LireSecurite(h, chemin, out proprio, out aces);
                d = Decider(new EntreeProtection
                {
                    EstRacine = estRacine, EstDossier = estDossier, EstLien = estLien, NombreLiens = (int)info.NumberOfLinks,
                    EstVolumeInattendu = !estRacine && info.VolumeSerialNumber != volumeRacine,
                    ProprietaireSid = proprio, DaclSure = DaclSure(aces),
                }, definirProprietaire, chemin);
                if (d.Avertissement != null) avert.Add(d.Avertissement);

                bool conserve = d.Action != ActionProtection.Supprimer;
                // On verrouille (propriétaire + DACL protégée) tout élément conservé, et, en mode élevé, un dossier à supprimer (pour que
                // ses enfants deviennent non modifiables et que la suppression récursive soit sûre). Jamais un lien, ni un FICHIER à supprimer :
                // écrire la DACL d'un lien physique modifierait aussi sa cible hors du dossier.
                if (!estLien && (conserve || (definirProprietaire && estDossier)))
                    Appliquer(h, chemin, estDossier, definirProprietaire);

                if (estDossier && !estLien && (conserve || definirProprietaire))
                    foreach (var enfant in Directory.GetFileSystemEntries(chemin))
                        Traiter(enfant, false, definirProprietaire, volumeRacine, avert);
            }
            if (d.Action != ActionProtection.Supprimer) return true;
            Supprimer(chemin, estDossier, id);
            return false;
        }

        /// <summary>
        /// Rouvre l'élément (point d'analyse non suivi) avec le droit DELETE, vérifie que c'est bien le même objet (volume + index) que celui
        /// examiné, puis demande sa suppression à la fermeture de la poignée. Un dossier conservé a déjà été vidé par la récursion.
        /// </summary>
        private static void Supprimer(string chemin, bool estDossier, Identite attendue)
        {
            using (var h = Ouvrir(chemin, true, false))
            {
                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(h, out info)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Lecture avant suppression impossible : " + chemin);
                if (info.VolumeSerialNumber != attendue.Volume || info.FileIndexHigh != attendue.IndexHaut || info.FileIndexLow != attendue.IndexBas)
                    throw new IOException("L'élément a changé d'identité pendant le traitement (remplacement) : " + chemin);
                var disp = new FILE_DISPOSITION_INFO { DeleteFile = true };
                if (!SetFileInformationByHandle(h, FileDispositionInfo, ref disp, Marshal.SizeOf(typeof(FILE_DISPOSITION_INFO))))
                {
                    int e = Marshal.GetLastWin32Error();
                    throw new Win32Exception(e, "Suppression impossible (" + new Win32Exception(e).Message + ") : " + chemin);
                }
            }
        }

        private struct Identite { public uint Volume, IndexHaut, IndexBas; }

        private static SafeFileHandle Ouvrir(string chemin, bool pourSuppression, bool definirProprietaire)
        {
            // FILE_READ_DATA (liste pour un dossier) est demandé pour que le contrôle de partage s'applique : une poignée concurrente détenant
            // l'écriture ou la suppression (partage incompatible avec FILE_SHARE_READ) fait alors échouer l'ouverture.
            uint acces = READ_CONTROL | FILE_READ_ATTRIBUTES | FILE_READ_DATA;
            if (pourSuppression) acces |= DELETE;
            else { acces |= WRITE_DAC; if (definirProprietaire) acces |= WRITE_OWNER; }
            // Partage en lecture seule : ni écriture ni suppression partagées. Une poignée concurrente (ex. DELETE pré-ouvert) provoque un refus de partage.
            var h = CreateFile(@"\\?\" + chemin, acces, FILE_SHARE_READ, IntPtr.Zero, 3 /* OPEN_EXISTING */,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int e = Marshal.GetLastWin32Error();
                h.Dispose();
                if (e == ERROR_SHARING_VIOLATION || e == ERROR_LOCK_VIOLATION)
                    throw new IOException("Le dossier de données est ouvert par un autre programme (partage refusé), impossible de le sécuriser : " + chemin);
                throw new Win32Exception(e, "Ouverture impossible : " + chemin);
            }
            return h;
        }

        private static void LireSecurite(SafeFileHandle h, string chemin, out string proprio, out List<AceInfo> aces)
        {
            IntPtr pOwner, pGroup, pDacl, pSacl, pSd;
            int err = GetSecurityInfo(h, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION, out pOwner, out pGroup, out pDacl, out pSacl, out pSd);
            if (err != 0) throw new Win32Exception(err, "Lecture des droits impossible : " + chemin);
            try
            {
                proprio = pOwner == IntPtr.Zero ? null : new SecurityIdentifier(pOwner).Value;
                aces = null;
                if (pDacl != IntPtr.Zero)
                {
                    int taille = Marshal.ReadInt16(pDacl, 2) & 0xFFFF;
                    var octets = new byte[taille];
                    Marshal.Copy(pDacl, octets, 0, taille);
                    aces = new List<AceInfo>();
                    foreach (GenericAce a in new RawAcl(octets, 0))
                    {
                        var k = a as KnownAce;
                        if (k == null) { aces.Add(new AceInfo { Sid = "?", Masque = -1, Autorise = true }); continue; } // inconnu : considéré dangereux
                        bool autorise = k.AceType == AceType.AccessAllowed || k.AceType == AceType.AccessAllowedObject || k.AceType == AceType.AccessAllowedCallback || k.AceType == AceType.AccessAllowedCallbackObject;
                        aces.Add(new AceInfo { Sid = k.SecurityIdentifier.Value, Masque = k.AccessMask, Autorise = autorise });
                    }
                }
            }
            finally { LocalFree(pSd); }
        }

        /// <summary>
        /// Écrit propriétaire et DACL sur la poignée par SetKernelObjectSecurity : contrairement à SetSecurityInfo / SetNamedSecurityInfo,
        /// aucune propagation automatique vers le contenu (qui pourrait atteindre, via un lien physique ou une jonction pas encore traités,
        /// un fichier hors du dossier). Chaque élément reçoit sa propre DACL explicite.
        /// </summary>
        private static void Appliquer(SafeFileHandle h, string chemin, bool estDossier, bool definirProprietaire)
        {
            var sd = new RawSecurityDescriptor(
                ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected | ControlFlags.SelfRelative,
                definirProprietaire ? new SecurityIdentifier(SidAdministrateurs) : null, null, null, DaclAttendue(estDossier));
            var octets = new byte[sd.BinaryLength]; sd.GetBinaryForm(octets, 0);
            uint quoi = DACL_SECURITY_INFORMATION | (definirProprietaire ? OWNER_SECURITY_INFORMATION : 0);
            if (!SetKernelObjectSecurity(h, quoi, octets))
            {
                int e = Marshal.GetLastWin32Error();
                throw new Win32Exception(e, "Application des droits impossible (" + new Win32Exception(e).Message + ") : " + chemin);
            }
        }

        private static bool Existe(string chemin)
        {
            return GetFileAttributes(@"\\?\" + chemin) != INVALID_FILE_ATTRIBUTES;
        }

        /// <summary>Prise de possession et restauration (sans effet hors élévation) : nécessaires sur un élément planté qui refuse l'accès aux administrateurs.</summary>
        private static void ActiverPrivileges()
        {
            IntPtr jeton;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008 /* ADJUST_PRIVILEGES | QUERY */, out jeton)) return;
            try
            {
                foreach (var nom in new[] { "SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege" })
                {
                    long luid;
                    if (!LookupPrivilegeValue(null, nom, out luid)) continue;
                    var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 2 /* SE_PRIVILEGE_ENABLED */ };
                    AdjustTokenPrivileges(jeton, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally { CloseHandle(jeton); }
        }

        // ---------- Win32 ----------

        private const uint FILE_READ_DATA = 0x0001, DELETE = 0x00010000, READ_CONTROL = 0x00020000, WRITE_DAC = 0x00040000, WRITE_OWNER = 0x00080000, FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_SHARE_READ = 0x1;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_REPARSE_POINT = 0x400, INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;
        private const int SE_FILE_OBJECT = 1, FileDispositionInfo = 4;
        private const int ERROR_SHARING_VIOLATION = 32, ERROR_LOCK_VIOLATION = 33;
        private const uint OWNER_SECURITY_INFORMATION = 0x1, DACL_SECURITY_INFORMATION = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_DISPOSITION_INFO { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TOKEN_PRIVILEGES { public int PrivilegeCount; public long Luid; public int Attributes; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string nom, uint acces, uint partage, IntPtr sec, uint disposition, uint flags, IntPtr modele);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle h, out BY_HANDLE_FILE_INFORMATION info);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle h, int classe, ref FILE_DISPOSITION_INFO info, int taille);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributes(string nom);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll")]
        private static extern int GetSecurityInfo(SafeFileHandle h, int type, uint quoi, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetKernelObjectSecurity(SafeFileHandle h, uint quoi, byte[] sd);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint acces, out IntPtr jeton);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string systeme, string nom, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr jeton, bool toutDesactiver, ref TOKEN_PRIVILEGES nouveaux, int taille, IntPtr precedents, IntPtr tailleRetour);
    }
}
