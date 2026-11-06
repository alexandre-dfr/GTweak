$setup = Get-ChildItem -Path (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\*\Installer\setup.exe') -ErrorAction SilentlyContinue |
    Sort-Object -Property FullName -Descending | Select-Object -First 1
if (-not $setup) { Skip-GTItem 'Microsoft Edge n''est pas installé (désinstalleur introuvable).' }
# Autorise la désinstallation (valeur nécessaire dans l'Espace économique européen), puis désinstalle sans toucher au profil de l'utilisateur.
Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Microsoft\EdgeUpdateDev' -Name 'AllowUninstall' -Type String -Value ''
Write-GTJournal 'wingetapp' 'Microsoft.Edge' $true 'winget' $null
$p = Start-Process -FilePath $setup.FullName -ArgumentList '--uninstall --system-level --force-uninstall' -Wait -PassThru
if (Test-Path -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe')) { throw ('Edge est toujours installé (code de sortie ' + $p.ExitCode + ').') }
# Empêche seulement la réinstallation automatique d'Edge par Windows Update.
Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate' -Name 'DoNotUpdateToEdgeWithChromium' -Type DWord -Value 1
Set-GTItemNote 'Edge désinstallé, profil conservé. Annulation : réinstallation par winget (Microsoft.Edge).'
