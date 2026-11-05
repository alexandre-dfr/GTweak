Remove-TweakPath -Path @(
    (Join-Path $env:SystemRoot 'Logs\CBS'),
    (Join-Path $env:SystemRoot 'Logs\DISM'),
    (Join-Path $env:SystemRoot 'Logs\WindowsUpdate'),
    (Join-Path $env:SystemRoot 'Traces\WindowsUpdate')
) | Out-Null
# Journaux d'événements : tous sauf le journal Sécurité (piste d'audit). Ceux que Windows refuse d'effacer sont ignorés.
$gtJournaux = Invoke-GTNative 'wevtutil.exe' @('el')
$gtEfface = 0
foreach ($gtLog in @($gtJournaux.Output)) {
    if (-not $gtLog.Trim() -or $gtLog.Trim() -ieq 'Security') { continue }
    $gtR = Invoke-GTNative 'wevtutil.exe' @('cl', $gtLog.Trim())
    if ($gtR.ExitCode -eq 0) { $gtEfface++ }
}
Add-GTCleanDetail ([string]$gtEfface + ' journaux d''événements effacés (journal Sécurité conservé)')
