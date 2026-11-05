# Raccourcis récents (Accès rapide) : fichiers .lnk à la racine du dossier ; les listes épinglées (CustomDestinations) ne sont pas touchées.
Remove-TweakPath -Path (Join-Path $env:APPDATA 'Microsoft\Windows\Recent') -Filter '*.lnk' -NoRecurse | Out-Null
# Listes de l'historique récent (Exécuter, recherches, boîtes Ouvrir/Enregistrer, Paint, WordPad) : valeurs vidées, clés conservées.
$gtVal = 0
foreach ($gtK in @(
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU', $false),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery', $false),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs', $true),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSaveMRU', $true),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU', $true),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedMRU', $false),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU', $false),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Applets\Paint\Recent File List', $false),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Applets\Wordpad\Recent File List', $false),
    @('HKCU:\Software\Microsoft\Direct3D\MostRecentApplication', $false)
)) {
    $gtVal += Clear-GTRegistryValues -Path $gtK[0] -Recurse:([bool]$gtK[1])
}
Add-GTCleanDetail ([string]$gtVal + ' valeur(s) d''historique effacée(s)')
