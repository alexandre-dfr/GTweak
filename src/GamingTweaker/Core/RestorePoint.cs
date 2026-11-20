using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GamingTweaker.Core
{
    public class RestorePointResult
    {
        public bool Created { get; set; }
        public string Message { get; set; } = "";
        /// <summary>Échec parce que la protection du système est désactivée sur le disque système (l'activer permet de réessayer).</summary>
        public bool ProtectionDesactivee { get; set; }
    }

    public interface IRestorePoint
    {
        RestorePointResult TryCreate(string description);
    }

    /// <summary>Activation de la protection du système sur le disque système, puis création du point (proposée quand elle est désactivée).</summary>
    public interface IProtectionSysteme
    {
        RestorePointResult ActiverEtCreer(string description);
    }

    /// <summary>
    /// Checkpoint-Computer ne lève pas d'erreur quand la limite de fréquence (1 point / 24 h) est atteinte : il émet un avertissement.
    /// La limite est levée le temps de la création (valeur de registre rétablie ensuite), et le dernier point (Get-ComputerRestorePoint)
    /// est comparé avant et après pour ne jamais annoncer un point qui n'existe pas.
    /// </summary>
    public class RestorePoint : IRestorePoint, IProtectionSysteme
    {
        private const string Script = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
function Get-LastSeq { try { $p = Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber | Select-Object -Last 1; if ($p) { [int]$p.SequenceNumber } else { 0 } } catch { 0 } }
# Protection du système active sur le disque système ? (liste des volumes protégés tenue par le service de clichés instantanés)
function Test-Protection {
    try {
        $v = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients' -ErrorAction Stop).'{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}'
        return (($v -join ' ') -match ('\(' + $env:SystemDrive.TrimEnd(':') + '%3A\)'))
    } catch { return $false }
}
if ($env:GT_RP_ACTIVER -eq '1') {
    try { Enable-ComputerRestore -Drive ($env:SystemDrive + '\') -ErrorAction Stop }
    catch { Write-Output ('FAIL|Activation de la protection du système impossible : ' + ($_.Exception.Message -replace '\s*[\r\n]+\s*', ' ')); exit 0 }
}
$before = Get-LastSeq
# Windows refuse un second point dans les 24 h (SystemRestorePointCreationFrequency, 1440 min par défaut) : la limite est levée le temps
# de la création, puis la valeur d'origine est rétablie (ou retirée si elle était absente).
$freqKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
$freqNom = 'SystemRestorePointCreationFrequency'
$freqAvant = $null; $freqExistait = $false
try {
    $k = Get-Item -LiteralPath $freqKey -ErrorAction Stop
    if ($k.GetValueNames() -contains $freqNom) { $freqExistait = $true; $freqAvant = $k.GetValue($freqNom) }
    New-ItemProperty -LiteralPath $freqKey -Name $freqNom -PropertyType DWord -Value 0 -Force | Out-Null
} catch { }
try {
    Checkpoint-Computer -Description $env:GT_RP_DESC -RestorePointType MODIFY_SETTINGS -WarningAction SilentlyContinue -ErrorAction Stop
} catch {
    $etat = if (Test-Protection) { 'FAIL|' } else { 'FAILOFF|' }
    Write-Output ($etat + ($_.Exception.Message -replace '\s*[\r\n]+\s*', ' '))
    exit 0
} finally {
    try {
        if ($freqExistait) { New-ItemProperty -LiteralPath $freqKey -Name $freqNom -PropertyType DWord -Value ([int]$freqAvant) -Force | Out-Null }
        else { Remove-ItemProperty -LiteralPath $freqKey -Name $freqNom -ErrorAction SilentlyContinue }
    } catch { }
}
$after = Get-LastSeq
if ($after -gt $before) { Write-Output 'OK' } else { Write-Output 'LIMIT' }
";

        public RestorePointResult TryCreate(string description) { return Executer(description, false); }

        /// <summary>Active la protection du système sur le disque système (Enable-ComputerRestore), puis crée le point.</summary>
        public RestorePointResult ActiverEtCreer(string description)
        {
            TraceAppli.Ecrire("point de restauration : activation de la protection du système demandée");
            return Executer(description, true);
        }

        /// <summary>Interprète la dernière ligne du script (OK, LIMIT, FAIL|…, FAILOFF|…) ; public pour les tests.</summary>
        public static RestorePointResult Interpreter(string sortie)
        {
            var output = (sortie ?? "").Trim();
            var i = output.LastIndexOf('\n');
            if (i >= 0) output = output.Substring(i + 1).Trim();
            if (output == "OK") return new RestorePointResult { Created = true };
            if (output == "LIMIT")
                return new RestorePointResult { Message = "Windows limite les points de restauration (un par 24 h par défaut) : aucun nouveau point n'a été créé." };
            if (output.StartsWith("FAILOFF|", StringComparison.Ordinal))
                return new RestorePointResult { ProtectionDesactivee = true,
                    Message = "Point de restauration impossible : la protection du système est désactivée sur le disque système (" + output.Substring(8) + ")." };
            if (output.StartsWith("FAIL|", StringComparison.Ordinal))
                return new RestorePointResult { Message = "Point de restauration impossible : " + output.Substring(5) };
            return new RestorePointResult { Message = "Point de restauration impossible : réponse inattendue de PowerShell." };
        }

        private RestorePointResult Executer(string description, bool activer)
        {
            try
            {
                // Aucun fichier : script passé en -EncodedCommand, environnement propre ; la description passe par une variable.
                var psi = PowerShellLanceur.Creer(PowerShellLanceur.ArgumentsEncodes(Script));
                psi.EnvironmentVariables["GT_RP_DESC"] = description ?? "GamingTweaker";
                psi.EnvironmentVariables["GT_RP_ACTIVER"] = activer ? "1" : "0";
                using (var p = Process.Start(psi))
                {
                    p.StandardInput.Close();
                    var errTask = p.StandardError.ReadToEndAsync();
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(180000))
                    {
                        try { p.Kill(); } catch { }
                        return new RestorePointResult { Message = "La création du point de restauration a dépassé le délai." };
                    }
                    return Interpreter(outTask.Result);
                }
            }
            catch (Exception ex)
            {
                TraceAppli.Erreur("point de restauration", ex);
                return new RestorePointResult { Message = "Point de restauration impossible : " + ex.Message };
            }
        }
    }
}
