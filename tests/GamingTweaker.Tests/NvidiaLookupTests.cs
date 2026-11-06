using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Lists;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Recherche du pilote NVIDIA. Fixtures capturées sur les vrais accès NVIDIA (RTX 2050 portable : psid 111, pfid 978) ; la réponse
    /// « introuvable » est celle de la branche Studio pour ce GPU. Aucun pilote n'est téléchargé ni exécuté par ces tests.
    /// </summary>
    public class NvidiaLookupTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "GTNv-" + Guid.NewGuid().ToString("N"));
        public NvidiaLookupTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static string GameReady => Fixtures.Lire("nvidia-gameready.json");
        private static string Introuvable => Fixtures.Lire("nvidia-introuvable.json");
        private static string Produits => Fixtures.Lire("nvidia-produits.xml");
        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

        [Fact] public void Nvidia_extrait_version_et_url()
        {
            var info = NvidiaLookup.Parse(GameReady);
            Assert.Equal("621.14", info.Version);
            Assert.StartsWith("https://", info.Url);
            Assert.EndsWith(".exe", info.Url);
        }

        [Fact] public void Nvidia_reponse_inconnue_renvoie_null() => Assert.Null(NvidiaLookup.Parse("<html>"));

        [Fact] public void Nvidia_pilote_introuvable_renvoie_null() => Assert.Null(NvidiaLookup.Parse(Introuvable));

        [Theory]
        [InlineData("")]
        [InlineData("{}")]
        [InlineData("{\"Success\":\"1\",\"IDS\":[]}")]
        [InlineData("{\"Success\":\"1\",\"IDS\":[{\"downloadInfo\":{\"Success\":\"1\",\"Version\":\"\",\"DownloadURL\":\"https://fr.download.nvidia.com/x.exe\"}}]}")]
        [InlineData("{\"Success\":\"1\",\"IDS\":[{\"downloadInfo\":{\"Success\":\"1\",\"Version\":\"1.0\",\"DownloadURL\":\"http://fr.download.nvidia.com/x.exe\"}}]}")]
        [InlineData("{\"Success\":\"1\",\"IDS\":[{\"downloadInfo\":{\"Success\":\"1\",\"Version\":\"1.0\",\"DownloadURL\":\"https://nvidia.com.evil.example/x.exe\"}}]}")]
        [InlineData("{\"Success\":\"1\",\"IDS\":[{\"downloadInfo\":{\"Success\":\"1\",\"Version\":\"1.0\",\"DownloadURL\":\"https://evil.example/nvidia.com/x.exe\"}}]}")]
        public void Nvidia_reponse_incomplete_ou_adresse_refusee_renvoie_null(string json) => Assert.Null(NvidiaLookup.Parse(json));

        [Fact] public void Les_produits_sont_trouves_par_le_nom_du_GPU()
        {
            var p = NvidiaLookup.ParseProduits(Produits, "NVIDIA GeForce RTX 2050");
            Assert.Contains(p, x => x.Psid == "111" && x.Pfid == "978");
            Assert.Empty(NvidiaLookup.ParseProduits(Produits, "NVIDIA GeForce Inconnue 9999"));
            Assert.Empty(NvidiaLookup.ParseProduits("<html>", "NVIDIA GeForce RTX 2050"));
        }

        [Theory]
        [InlineData("32.0.16.2101", "621.01")]
        [InlineData("32.0.15.7283", "572.83")]
        [InlineData("31.0.15.5123", "551.23")]
        [InlineData("n'importe quoi", null)]
        public void La_version_Windows_donne_la_numerotation_NVIDIA(string windows, string attendu) => Assert.Equal(attendu, NvidiaLookup.VersionNvidiaDepuisWindows(windows));

        [Fact] public async Task La_recherche_enchaine_produits_puis_pilote_et_choisit_la_branche()
        {
            var urls = new List<string>();
            Func<string, Task<string>> get = url =>
            {
                urls.Add(url);
                if (url.StartsWith(NvidiaLookup.UrlProduits)) return Task.FromResult(Produits);
                return Task.FromResult(url.Contains("upCRD=1") ? Introuvable : GameReady);
            };
            var gr = await NvidiaLookup.FindLatestAsync("NVIDIA GeForce RTX 2050", "GameReady", get, 135);
            Assert.Equal("621.14", gr.Version);
            Assert.Contains(urls, u => u.Contains("psid=111") && u.Contains("pfid=978") && u.Contains("osID=135") && u.Contains("upCRD=0"));
            Assert.Null(await NvidiaLookup.FindLatestAsync("NVIDIA GeForce RTX 2050", "Studio", get, 135));
        }

        [Fact] public async Task Un_echec_reseau_renvoie_null_sans_exception()
        {
            Assert.Null(await NvidiaLookup.FindLatestAsync("NVIDIA GeForce RTX 2050", "GameReady", u => { throw new System.Net.WebException("hors ligne"); }, 135));
            Assert.Null(await NvidiaLookup.FindLatestAsync("NVIDIA GeForce RTX 2050", "GameReady", u => Task.FromResult("<html>"), 135));
            Assert.Null(await NvidiaLookup.FindLatestAsync("GPU inconnu", "GameReady", u => Task.FromResult(Produits), 135));
        }

        // ---- repli de l'interface -----------------------------------------------------------------------

        private class GpuFactice : IGpuSource
        {
            private readonly GpuInfo[] g;
            public GpuFactice(params GpuInfo[] g) { this.g = g; }
            public IReadOnlyList<GpuInfo> Lire() => g;
        }

        [Fact] public async Task Pilote_NVIDIA_propose_les_deux_branches_avec_les_versions()
        {
            var info = NvidiaLookup.Parse(GameReady);
            var p = new GpuDriverProvider(new GpuFactice(new GpuInfo { Name = "NVIDIA GeForce RTX 2050", DriverVersion = "32.0.16.2101" }),
                (nom, branche) => Task.FromResult(branche == "GameReady" ? info : null));
            var l = await p.LoadAsync(CancellationToken.None);
            Assert.Equal(new[] { "GameReady", "Studio" }, l.Select(e => e.Key));
            Assert.Contains("621.14", l[0].Detail);
            Assert.Contains("621.01", l[0].Detail);
            Assert.Contains("mise à jour disponible", l[0].Detail);
            Assert.Contains("introuvable", l[1].Detail);
            Assert.Equal(NvidiaLookup.PageOfficielle, p.Lien);
            Assert.Equal(new[] { "GameReady" }, p.SelectionParDefaut);
        }

        [Fact] public async Task Recherche_en_echec_affiche_un_message_et_le_lien_officiel()
        {
            var p = new GpuDriverProvider(new GpuFactice(new GpuInfo { Name = "NVIDIA GeForce RTX 2050", DriverVersion = "32.0.16.2101" }),
                (nom, branche) => Task.FromResult<NvidiaDriverInfo>(null));
            await p.LoadAsync(CancellationToken.None);
            Assert.Contains("a échoué", p.Message);
            Assert.Contains("page officielle", p.Message);
            Assert.Equal("https://www.nvidia.com/fr-fr/drivers/", p.Lien);
        }

        [Fact] public async Task GPU_AMD_ou_Intel_affiche_la_version_et_le_lien_sans_rien_proposer()
        {
            var amd = new GpuDriverProvider(new GpuFactice(new GpuInfo { Name = "AMD Radeon RX 7800 XT", DriverVersion = "31.0.24033.1003" }),
                (n, b) => { throw new InvalidOperationException("ne doit pas être appelé"); });
            Assert.Empty(await amd.LoadAsync(CancellationToken.None));
            Assert.Contains("31.0.24033.1003", amd.Message);
            Assert.Equal(GpuDriverProvider.LienAmd, amd.Lien);
            var intel = new GpuDriverProvider(new GpuFactice(new GpuInfo { Name = "Intel(R) Arc(TM) A770", DriverVersion = "32.0.101.5972" }));
            Assert.Empty(await intel.LoadAsync(CancellationToken.None));
            Assert.Contains("32.0.101.5972", intel.Message);
            Assert.Equal(GpuDriverProvider.LienIntel, intel.Lien);
            Assert.StartsWith("https://", GpuDriverProvider.LienAmd);
        }

        // ---- côté PowerShell (mêmes fixtures, signature factice) ---------------------------------------

        [Fact] public void PowerShell_et_C_sharp_lisent_pareil_la_reponse_NVIDIA()
        {
            Assert.Equal("621.14", string.Join("", PsCommon.Eval("$i = ConvertFrom-GTNvidiaLookup ([IO.File]::ReadAllText(" + Q(Fixtures.Chemin("nvidia-gameready.json")) + ", [Text.Encoding]::UTF8))\r\n$i.Version")));
            Assert.Equal("NULL", string.Join("", PsCommon.Eval("if ($null -eq (ConvertFrom-GTNvidiaLookup ([IO.File]::ReadAllText(" + Q(Fixtures.Chemin("nvidia-introuvable.json")) + ", [Text.Encoding]::UTF8)))) { 'NULL' } else { 'X' }")));
            Assert.Equal("NULL", string.Join("", PsCommon.Eval("if ($null -eq (ConvertFrom-GTNvidiaLookup '<html>')) { 'NULL' } else { 'X' }")));
        }

        [Theory]
        [InlineData("Valid", "CN=NVIDIA Corporation, O=NVIDIA Corporation, L=Santa Clara, S=California, C=US", "OK")]
        [InlineData("Valid", "CN=Autre Editeur", "REFUSE")]
        [InlineData("NotSigned", "CN=NVIDIA Corporation", "REFUSE")]
        [InlineData("HashMismatch", "CN=NVIDIA Corporation", "REFUSE")]
        [InlineData("UnknownError", "CN=NVIDIA Corporation", "REFUSE")]
        public void La_signature_doit_etre_valide_et_delivree_a_NVIDIA(string statut, string sujet, string attendu)
        {
            var r = PsCommon.Eval("$s = [pscustomobject]@{ Status = " + Q(statut) + "; SignerCertificate = [pscustomobject]@{ Subject = " + Q(sujet) + " } }\r\nif (Test-GTNvidiaSignature $s) { 'OK' } else { 'REFUSE' }");
            Assert.Equal(attendu, string.Join("", r));
            Assert.Equal("REFUSE", string.Join("", PsCommon.Eval("if (Test-GTNvidiaSignature $null) { 'OK' } else { 'REFUSE' }")));
            Assert.Equal("REFUSE", string.Join("", PsCommon.Eval("if (Test-GTNvidiaSignature ([pscustomobject]@{ Status = 'Valid'; SignerCertificate = $null })) { 'OK' } else { 'REFUSE' }")));
        }

        [Fact] public void Un_installateur_mal_signe_est_supprime_et_l_installation_echoue()
        {
            var f = Path.Combine(tmp, "pilote.exe");
            File.WriteAllText(f, "faux");
            var r = PsCommon.Eval("try { Confirm-GTNvidiaInstaller -Path " + Q(f) + " -Signature ([pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = 'CN=Pirate' } }); 'PAS-D-ERREUR' } catch { 'ERREUR:' + $_.Exception.Message }");
            Assert.StartsWith("ERREUR:", string.Join("", r));
            Assert.Contains("NVIDIA Corporation", string.Join("", r));
            Assert.False(File.Exists(f), "l'installateur refusé doit être supprimé");
        }

        [Fact] public void Un_installateur_bien_signe_est_conserve()
        {
            var f = Path.Combine(tmp, "pilote.exe");
            File.WriteAllText(f, "faux");
            var r = PsCommon.Eval("Confirm-GTNvidiaInstaller -Path " + Q(f) + " -Signature ([pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = 'CN=NVIDIA Corporation, O=NVIDIA Corporation' } })\r\n'FIN'");
            Assert.Equal("FIN", string.Join("", r));
            Assert.True(File.Exists(f));
        }

        [Theory]
        [InlineData("https://fr.download.nvidia.com/Windows/621.14/x.exe", true)]
        [InlineData("https://us.download.nvidia.com/a.exe", true)]
        [InlineData("http://fr.download.nvidia.com/a.exe", false)]
        [InlineData("https://nvidia.com.evil.example/a.exe", false)]
        [InlineData("https://evil.example/nvidia.com/a.exe", false)]
        [InlineData("ftp://download.nvidia.com/a.exe", false)]
        public void Seules_les_adresses_https_de_nvidia_com_sont_acceptees(string url, bool attendu)
        {
            Assert.Equal(attendu, NvidiaLookup.UrlAcceptee(url));
            Assert.Equal(attendu ? "OK" : "REFUSE", string.Join("", PsCommon.Eval("if (Test-GTNvidiaUrl " + Q(url) + ") { 'OK' } else { 'REFUSE' }")));
        }

        [Theory]
        [InlineData("621.01", "621.14", -1)]
        [InlineData("621.14", "621.14", 0)]
        [InlineData("622.00", "621.14", 1)]
        [InlineData("32.0.16.2101", "32.0.16.2114", -1)]
        public void Les_versions_de_pilote_se_comparent_champ_par_champ(string a, string b, int attendu)
        {
            Assert.Equal(attendu.ToString(), string.Join("", PsCommon.Eval("Compare-GTDriverVersion " + Q(a) + " " + Q(b))));
            Assert.Equal(attendu, GpuDriverProvider.Compare(a, b));
        }
    }
}
