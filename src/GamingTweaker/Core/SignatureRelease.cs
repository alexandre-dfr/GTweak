using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Signature des versions publiées, vérifiée avant toute mise à jour intégrée.
    /// L'empreinte SHA-256 publiée sur GitHub ne prouve que l'intégrité : elle vient de la même release que l'exe, donc quiconque peut publier
    /// une release (compte ou jeton GitHub compromis, CI détournée) peut publier l'empreinte qui va avec. La signature, elle, est faite par le
    /// mainteneur sur son PC avec une clé privée qui ne quitte jamais ce PC (tools/signer-release.ps1) ; l'exe ne connaît que la clé publique.
    /// Message signé : « GamingTweaker.exe|X.Y.Z|sha256 » : la version est liée, un ancien exe signé ne peut pas être republié sous un tag plus récent.
    /// Clés publiques : Assets/cles-release.txt (une clé RSA au format XML par ligne). Aucune clé : la mise à jour intégrée est désactivée,
    /// seule la page de la version est proposée.
    /// </summary>
    public static class SignatureRelease
    {
        /// <summary>Nom de la signature jointe à la release, à côté de GamingTweaker.exe.</summary>
        public const string NomFichier = "GamingTweaker.exe.sig";
        /// <summary>Taille minimale acceptée d'une clé RSA.</summary>
        public const int TailleMinimale = 3072;
        /// <summary>Taille maximale lue pour un fichier de signature (une signature RSA-4096 en base64 fait moins de 700 octets).</summary>
        public const int TailleMaxFichier = 8192;

        private static string[] cles;

        /// <summary>Clés publiques intégrées à l'exe (lues une fois) ; tableau vide si aucune n'est configurée.</summary>
        public static string[] Cles
        {
            get
            {
                if (cles == null) cles = LireCles(LireRessource()).ToArray();
                return cles;
            }
        }

        /// <summary>Texte signé pour une version et l'empreinte de son exe.</summary>
        public static string Message(Version version, string sha256)
        {
            if (version == null) throw new ArgumentNullException("version");
            return MiseAJour.NomExe + "|" + version.Major + "." + version.Minor + "." + Math.Max(0, version.Build) + "|" + (sha256 ?? "").Trim().ToLowerInvariant();
        }

        /// <summary>Lignes du fichier des clés : une clé XML par ligne ; lignes vides et commentaires (#) ignorés ; clé illisible ou trop courte écartée.</summary>
        public static IEnumerable<string> LireCles(string texte)
        {
            foreach (var brute in (texte ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var l = brute.Trim();
                if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                if (TailleCle(l) >= TailleMinimale) yield return l;
            }
        }

        /// <summary>Taille en bits d'une clé publique RSA au format XML, 0 si illisible ou si elle contient une partie privée.</summary>
        public static int TailleCle(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml) || xml.IndexOf("<D>", StringComparison.Ordinal) >= 0) return 0;
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(xml);
                    return rsa.KeySize;
                }
            }
            catch (Exception) { return 0; }
        }

        /// <summary>Vrai si <paramref name="signatureBase64"/> signe la version et l'empreinte données avec l'une des clés ; ne lève jamais.</summary>
        public static bool Verifier(string signatureBase64, Version version, string sha256, IEnumerable<string> clesPubliques)
        {
            if (string.IsNullOrWhiteSpace(signatureBase64) || version == null || string.IsNullOrWhiteSpace(sha256) || clesPubliques == null) return false;
            byte[] signature;
            try { signature = Convert.FromBase64String(signatureBase64.Trim()); }
            catch (FormatException) { return false; }
            var donnees = Encoding.UTF8.GetBytes(Message(version, sha256));
            foreach (var cle in clesPubliques)
            {
                if (TailleCle(cle) < TailleMinimale) continue;
                try
                {
                    using (var rsa = new RSACryptoServiceProvider())
                    {
                        rsa.PersistKeyInCsp = false;
                        rsa.FromXmlString(cle);
                        if (rsa.VerifyData(donnees, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return true;
                    }
                }
                catch (CryptographicException) { }
            }
            return false;
        }

        private static string LireRessource()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("cles-release.txt"))
                {
                    if (s == null) return "";
                    using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch (Exception) { return ""; }
        }
    }
}
