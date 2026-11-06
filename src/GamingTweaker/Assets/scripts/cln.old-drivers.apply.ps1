# Pilotes tiers publiés (oemN.inf) : on garde le plus récent de chaque groupe et tous ceux qu'un périphérique utilise.
$gtTous = @(Get-WindowsDriver -Online -All | Where-Object { $_.Driver -match '^oem\d+\.inf$' })
$gtUtilises = @(Get-CimInstance -ClassName Win32_PnPSignedDriver | Where-Object { $_.InfName } | ForEach-Object { [string]$_.InfName })
if ($gtUtilises.Count -eq 0) { throw 'La liste des pilotes utilisés est vide : aucun pilote supprimé.' }
$gtAnciens = @(Select-GTOldDrivers -Drivers $gtTous -InUse $gtUtilises)
if ($gtAnciens.Count -eq 0) { Skip-GTItem 'Aucun ancien pilote à supprimer.' }
$gtOk = 0; $gtGardes = 0
# Code de refus de pnputil pour un pilote encore utilisé par un périphérique (ERROR_INF_IN_USE_BY_DEVICES, 0xE000020F).
# 0 et 3010 (redémarrage conseillé) : supprimé. Tout autre code est un échec explicite : on s'arrête.
$gtCodeUtilise = -536870385
foreach ($gtD in $gtAnciens) {
    # Sans option de forçage : pnputil refuse de retirer un pilote encore utilisé.
    $gtR = Invoke-GTNative 'pnputil.exe' @('/delete-driver', [string]$gtD.Driver)
    if ($gtR.ExitCode -eq 0 -or $gtR.ExitCode -eq 3010) { $gtOk++ }
    elseif ($gtR.ExitCode -eq $gtCodeUtilise) { $gtGardes++ }
    else {
        $gtFin = @($gtR.Output | Where-Object { $_.Trim() } | Select-Object -Last 2) -join ' '
        throw ('pnputil a échoué pour ' + [string]$gtD.Driver + ' (code ' + $gtR.ExitCode + ') après ' + $gtOk + ' pilote(s) supprimé(s) : ' + $gtFin)
    }
}
Set-GTItemNote ([string]$gtOk + ' ancien(s) pilote(s) supprimé(s)' + $(if ($gtGardes -gt 0) { ' ; ' + $gtGardes + ' conservé(s) car encore utilisé(s)' } else { '' }))
