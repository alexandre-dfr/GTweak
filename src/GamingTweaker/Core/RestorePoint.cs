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
    }

    public interface IRestorePoint
    {
        RestorePointResult TryCreate(string description);
    }

    /// <summary>
    /// Checkpoint-Computer ne lève pas d'erreur quand la limite de fréquence (1 point / 24 h) est atteinte :
    /// il émet un avertissement. On compare donc le dernier point (Get-ComputerRestorePoint) avant et après.
    /// </summary>
    public class RestorePoint : IRestorePoint
    {
        private const string Script = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
function Get-LastSeq { try { $p = Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber | Select-Object -Last 1; if ($p) { [int]$p.SequenceNumber } else { 0 } } catch { 0 } }
$before = Get-LastSeq
try {
    Checkpoint-Computer -Description $env:GT_RP_DESC -RestorePointType MODIFY_SETTINGS -WarningAction SilentlyContinue -ErrorAction Stop
} catch {
    Write-Output ('FAIL|' + ($_.Exception.Message -replace '\s*[\r\n]+\s*', ' '))
    exit 0
}
$after = Get-LastSeq
if ($after -gt $before) { Write-Output 'OK' } else { Write-Output 'LIMIT' }
";

        public RestorePointResult TryCreate(string description)
        {
            var file = Path.Combine(Path.GetTempPath(), "gt-rp-" + Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                File.WriteAllText(file, Script, new UTF8Encoding(true));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + file + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                psi.EnvironmentVariables["GT_RP_DESC"] = description ?? "GamingTweaker";
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
                    var output = (outTask.Result ?? "").Trim();
                    if (output.EndsWith("OK", StringComparison.Ordinal)) return new RestorePointResult { Created = true };
                    if (output.EndsWith("LIMIT", StringComparison.Ordinal))
                        return new RestorePointResult { Message = "Windows limite les points de restauration (un par 24 h par défaut) : aucun nouveau point n'a été créé." };
                    if (output.StartsWith("FAIL|", StringComparison.Ordinal))
                        return new RestorePointResult { Message = "Point de restauration impossible (la protection du système est peut-être désactivée) : " + output.Substring(5) };
                    return new RestorePointResult { Message = "Point de restauration impossible : réponse inattendue de PowerShell." };
                }
            }
            catch (Exception ex)
            {
                return new RestorePointResult { Message = "Point de restauration impossible : " + ex.Message };
            }
            finally { try { File.Delete(file); } catch { } }
        }
    }
}
