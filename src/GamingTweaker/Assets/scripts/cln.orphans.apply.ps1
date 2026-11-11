# La liste des fichiers utilisés vient de l'API WindowsInstaller.Installer ; si elle échoue ou ne renvoie rien, l'option échoue sans rien déplacer.
$gtOrphelins = @(Find-OrphanInstallerFiles)
if ($gtOrphelins.Count -eq 0) { Skip-GTItem 'Aucun fichier .msi ou .msp orphelin.' }
$gtN = 0; $gtOctets = [long]0
foreach ($gtF in $gtOrphelins) {
    if (-not (Test-Path -LiteralPath $gtF)) { continue }
    $gtLen = (Get-Item -LiteralPath $gtF).Length
    Move-TweakQuarantine -File $gtF
    $gtN++; $gtOctets += $gtLen
}
Set-GTItemNote ([string]$gtN + ' fichier(s) déplacé(s) en quarantaine (' + (Format-GTSize $gtOctets) + '), restaurables depuis Historique.')
