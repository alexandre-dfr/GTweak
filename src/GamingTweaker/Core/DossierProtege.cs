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
    /// <summary>Aucune : rien à changer ; Appliquer : réécrire la DACL protégée ; Refuser : élément douteux, l'application s'arrête (jamais repris ni supprimé).</summary>
    public enum ActionProtection { Aucune, Appliquer, Refuser }

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
        /// <summary>Raison en français d'un refus.</summary>
        public string Raison { get; set; }
    }

    public class AceInfo
    {
        public string Sid { get; set; }
        public int Masque { get; set; }
        public bool Autorise { get; set; }
    }

    public class ResultatProtection
    {
        /// <summary>Non null : le dossier n'est pas sûr ; l'appli élevée s'arrête (code 2 en routine).</summary>
        public string Erreur { get; set; }
    }

    /// <summary>
    /// Dossier des données écrites avec des droits élevés et de la copie de la routine : %ProgramFiles%\GamingTweaker, où un utilisateur
    /// standard ne peut rien créer (ni pré-créer le dossier, ni y garder une poignée ouverte). Il est créé de façon atomique avec sa DACL protégée
    /// (SYSTEM et Administrateurs contrôle total, Utilisateurs lecture et exécution, propriétaire Administrateurs). S'il existe déjà, il doit
    /// appartenir à SYSTEM, aux Administrateurs ou à TrustedInstaller et ne pas être un point d'analyse ; sinon l'application refuse de continuer :
    /// rien n'est jamais repris ni supprimé. Le contenu est parcouru par poignée (liens non suivis, partage en lecture seule) et sa DACL réappliquée
    /// si elle accorde plus que la lecture à un autre compte ; un lien, un lien physique, un élément d'un autre volume ou (en mode élevé) un
    /// propriétaire non fiable dans le contenu provoquent aussi un refus.
    /// </summary>
    public static class DossierProtege
    {
        public const string SidSysteme = "S-1-5-18";
        public const string SidAdministrateurs = "S-1-5-32-544";
        public const string SidUtilisateurs = "S-1-5-32-545";
        public const string SidTrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

        private const int LectureExecution = 0x1200A9;          // FileSystemRights.ReadAndExecute (Synchronize inclus)
        private const int ControleTotal = 0x1F01FF;             // FileSystemRights.FullControl
        private const int LectureGenerique = unchecked((int)0x80000000) | 0x20000000; // GENERIC_READ | GENERIC_EXECUTE

        // ---------- Décision pure ----------

        public static bool ProprietaireDeConfiance(string sid)
        {
            return sid == SidSysteme || sid == SidAdministrateurs || sid == SidTrustedInstaller;
        }

        /// <summary>Vrai si aucune entrée « autoriser » ne donne plus que lecture/exécution à un autre compte que SYSTEM, Administrateurs ou TrustedInstaller. DACL nulle : faux.</summary>
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

        /// <param name="verifierProprietaire">Vrai quand l'appli est élevée. Hors élévation (tests), tout est créé par l'utilisateur courant : seuls liens et redirections sont refusés.</param>
        public static DecisionProtection Decider(EntreeProtection e, bool verifierProprietaire, string chemin)
        {
            if (e.EstLien)
                return Refus("lien ou jonction à la place d'un élément du dossier de données : " + chemin);
            if (e.EstRacine && !e.EstDossier)
                return Refus("le dossier de données est un fichier : " + chemin);
            if (!e.EstDossier && e.NombreLiens > 1)
                return Refus("lien physique dans le dossier de données : " + chemin);
            if (e.EstVolumeInattendu)
                return Refus("élément situé sur un autre volume (redirection) : " + chemin);
            if (verifierProprietaire && !ProprietaireDeConfiance(e.ProprietaireSid))
                return Refus("propriétaire non fiable (" + (e.ProprietaireSid ?? "inconnu") + ") : " + chemin);
            if (e.EstRacine || !e.DaclSure) return new DecisionProtection { Action = ActionProtection.Appliquer };
            return new DecisionProtection { Action = ActionProtection.Aucune };
        }

        private static DecisionProtection Refus(string raison) { return new DecisionProtection { Action = ActionProtection.Refuser, Raison = raison }; }

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

        /// <summary>Sécurité de création : DACL protégée attendue et, en mode élevé, propriétaire Administrateurs.</summary>
        public static DirectorySecurity SecuriteDeCreation(bool definirProprietaire)
        {
            var s = new DirectorySecurity();
            s.SetAccessRuleProtection(true, false);
            var heritage = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(SidSysteme), FileSystemRights.FullControl, heritage, PropagationFlags.None, AccessControlType.Allow));
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(SidAdministrateurs), FileSystemRights.FullControl, heritage, PropagationFlags.None, AccessControlType.Allow));
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(SidUtilisateurs), FileSystemRights.ReadAndExecute, heritage, PropagationFlags.None, AccessControlType.Allow));
            if (definirProprietaire) s.SetOwner(new SecurityIdentifier(SidAdministrateurs));
            return s;
        }

        // ---------- Application ----------

        /// <summary>Au démarrage élevé, avant toute lecture ou écriture du dossier. Non élevé : rien.</summary>
        public static ResultatProtection SecuriserAuDemarrage(string racine, Func<bool> estAdministrateur, bool definirProprietaire = true)
        {
            var r = new ResultatProtection();
            bool admin;
            try { admin = estAdministrateur(); } catch (Exception) { admin = false; }
            if (!admin) return r;
            try { Securiser(racine, definirProprietaire); }
            catch (Exception ex) { r.Erreur = "Le dossier de données " + racine + " n'est pas sûr : " + ex.Message; }
            return r;
        }

        /// <summary>Crée un sous-dossier de façon atomique avec la DACL protégée (aucun instant sans protection).</summary>
        public static void CreerDossier(string chemin, bool definirProprietaire = true)
        {
            Directory.CreateDirectory(chemin, SecuriteDeCreation(definirProprietaire));
        }

        /// <summary>Crée le dossier (DACL protégée, atomique) ou vérifie celui qui existe, puis contrôle tout son contenu. Lève une exception (refus) si un élément n'est pas sûr.</summary>
        public static void Securiser(string racine)
        {
            Securiser(racine, true);
        }

        internal static void Securiser(string racine, bool definirProprietaire)
        {
            if (string.IsNullOrWhiteSpace(racine)) throw new ArgumentException("Dossier à sécuriser non précisé.");
            racine = Path.GetFullPath(racine).TrimEnd('\\');
            if (!Existe(racine)) CreerDossier(racine, definirProprietaire);
            // Run\ ne contient que des fichiers temporaires des scripts : il est vidé avant le contrôle. Un outil lancé par un script
            // (winget dans les versions précédentes) a pu y laisser des éléments appartenant à l'utilisateur, ce qui bloquait le démarrage.
            if (definirProprietaire)
            {
                var run = Path.Combine(racine, "Run");
                if (Existe(run)) ViderSansSuivre(run);
            }
            Traiter(racine, true, definirProprietaire, 0);
        }

        /// <summary>
        /// Supprime le contenu d'un dossier sans jamais suivre de lien. Le dossier est tenu ouvert sans partage en suppression pendant tout
        /// le traitement (il ne peut être ni renommé ni remplacé par une jonction) ; chaque sous-dossier est vidé de la même façon puis
        /// supprimé par sa poignée ; une jonction, un lien ou un fichier est supprimé lui-même (FILE_FLAG_OPEN_REPARSE_POINT). Un élément
        /// impossible à supprimer (utilisé, droits) est laissé : le contrôle qui suit le signale.
        /// </summary>
        internal static void ViderSansSuivre(string dossier)
        {
            using (var h = CreateFile(@"\\?\" + dossier, FILE_READ_ATTRIBUTES | FILE_READ_DATA | SYNCHRONIZE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, 3,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero))
            {
                if (h.IsInvalid) return;
                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(h, out info)) return;
                if ((info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 || (info.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0) return;
                string[] enfants;
                try { enfants = Directory.GetFileSystemEntries(dossier); } catch (Exception) { return; }
                foreach (var e in enfants) SupprimerSansSuivre(e);
            }
        }

        private static void SupprimerSansSuivre(string chemin)
        {
            uint attrs = GetFileAttributes(@"\\?\" + chemin);
            if (attrs == INVALID_FILE_ATTRIBUTES) return;
            // Dossier ordinaire : vidé d'abord (avec sa propre poignée tenue) ; un lien n'est jamais parcouru.
            if ((attrs & FILE_ATTRIBUTE_DIRECTORY) != 0 && (attrs & FILE_ATTRIBUTE_REPARSE_POINT) == 0) ViderSansSuivre(chemin);
            using (var h = CreateFile(@"\\?\" + chemin, DELETE | FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero, 3,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero))
            {
                if (h.IsInvalid) return;
                BY_HANDLE_FILE_INFORMATION info;
                if (GetFileInformationByHandle(h, out info) && (info.FileAttributes & FILE_ATTRIBUTE_READONLY) != 0)
                {
                    // Lecture seule : levée sur la même poignée, sinon la suppression est refusée.
                    uint a = info.FileAttributes & ~FILE_ATTRIBUTE_READONLY;
                    var b = new FILE_BASIC_INFO { FileAttributes = a == 0 ? 0x80u : a };
                    SetFileInformationByHandle(h, 0 /* FileBasicInfo */, ref b, (uint)Marshal.SizeOf(typeof(FILE_BASIC_INFO)));
                }
                var d = new FILE_DISPOSITION_INFO { DeleteFile = 1 };
                SetFileInformationByHandle(h, 4 /* FileDispositionInfo */, ref d, 1);
            }
        }

        /// <summary>
        /// Traite un élément par sa poignée, ouverte sans suivre les liens et en partage lecture seule. Pour un dossier, la poignée est tenue
        /// pendant le traitement de ses enfants. Lève une exception (texte en français) au premier élément refusé.
        /// </summary>
        private static void Traiter(string chemin, bool estRacine, bool verifierProprietaire, uint volumeRacine)
        {
            using (var h = Ouvrir(chemin))
            {
                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(h, out info)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Lecture des attributs impossible : " + chemin);
                bool estDossier = (info.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                if (estRacine) volumeRacine = info.VolumeSerialNumber;

                string proprio; List<AceInfo> aces;
                LireSecurite(h, chemin, out proprio, out aces);
                var d = Decider(new EntreeProtection
                {
                    EstRacine = estRacine, EstDossier = estDossier, EstLien = (info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0,
                    NombreLiens = (int)info.NumberOfLinks, EstVolumeInattendu = !estRacine && info.VolumeSerialNumber != volumeRacine,
                    ProprietaireSid = proprio, DaclSure = DaclSure(aces),
                }, verifierProprietaire, chemin);

                if (d.Action == ActionProtection.Refuser) throw new IOException("Refus de sécurité, " + d.Raison);
                if (d.Action == ActionProtection.Appliquer) Appliquer(h, chemin, estDossier);
                if (estDossier)
                    foreach (var enfant in Directory.GetFileSystemEntries(chemin))
                        Traiter(enfant, false, verifierProprietaire, volumeRacine);
            }
        }

        private static SafeFileHandle Ouvrir(string chemin)
        {
            // FILE_READ_DATA (liste pour un dossier) fait jouer le contrôle de partage : une poignée concurrente en écriture ou suppression
            // (partage incompatible avec FILE_SHARE_READ) fait échouer l'ouverture.
            uint acces = READ_CONTROL | WRITE_DAC | FILE_READ_ATTRIBUTES | FILE_READ_DATA;
            var h = CreateFile(@"\\?\" + chemin, acces, FILE_SHARE_READ, IntPtr.Zero, 3 /* OPEN_EXISTING */,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int e = Marshal.GetLastWin32Error();
                h.Dispose();
                if (e == ERROR_SHARING_VIOLATION || e == ERROR_LOCK_VIOLATION)
                    throw new IOException("élément ouvert par un autre programme (partage refusé) : " + chemin);
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
        /// Écrit la DACL (jamais le propriétaire) sur la poignée par SetKernelObjectSecurity : aucune propagation automatique vers le contenu.
        /// </summary>
        private static void Appliquer(SafeFileHandle h, string chemin, bool estDossier)
        {
            var sd = new RawSecurityDescriptor(
                ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected | ControlFlags.SelfRelative,
                null, null, null, DaclAttendue(estDossier));
            var octets = new byte[sd.BinaryLength]; sd.GetBinaryForm(octets, 0);
            if (!SetKernelObjectSecurity(h, DACL_SECURITY_INFORMATION, octets))
            {
                int e = Marshal.GetLastWin32Error();
                throw new Win32Exception(e, "Application des droits impossible (" + new Win32Exception(e).Message + ") : " + chemin);
            }
        }

        private static bool Existe(string chemin)
        {
            return GetFileAttributes(@"\\?\" + chemin) != INVALID_FILE_ATTRIBUTES;
        }

        // ---------- Win32 ----------

        private const uint FILE_READ_DATA = 0x0001, READ_CONTROL = 0x00020000, WRITE_DAC = 0x00040000, FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2, FILE_SHARE_DELETE = 0x4, DELETE = 0x00010000, SYNCHRONIZE = 0x00100000;

        private const uint FILE_WRITE_ATTRIBUTES = 0x0100, FILE_ATTRIBUTE_READONLY = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_DISPOSITION_INFO { public byte DeleteFile; }
        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_BASIC_INFO { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint FileAttributes; }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle h, int classe, ref FILE_BASIC_INFO info, uint taille);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle h, int classe, ref FILE_DISPOSITION_INFO info, uint taille);
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_REPARSE_POINT = 0x400, INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;
        private const int SE_FILE_OBJECT = 1;
        private const int ERROR_SHARING_VIOLATION = 32, ERROR_LOCK_VIOLATION = 33;
        private const uint OWNER_SECURITY_INFORMATION = 0x1, DACL_SECURITY_INFORMATION = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string nom, uint acces, uint partage, IntPtr sec, uint disposition, uint flags, IntPtr modele);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle h, out BY_HANDLE_FILE_INFORMATION info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributes(string nom);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);
        [DllImport("advapi32.dll")]
        private static extern int GetSecurityInfo(SafeFileHandle h, int type, uint quoi, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetKernelObjectSecurity(SafeFileHandle h, uint quoi, byte[] sd);
    }
}
