$setup = Get-GTEdgeSetup (Get-GTEdgeAppDir)
if (-not $setup) { Skip-GTItem 'Microsoft Edge n''est pas installé (désinstalleur introuvable).' }
# Méthode de WinUtil : un fichier factice MicrosoftEdge.exe dans le dossier de l'ancien Edge débloque le désinstalleur officiel.
# Le fichier est journalisé avant sa création (supprimé à l'annulation) ; le profil de l'utilisateur n'est jamais supprimé.
New-TweakEmptyFile -Path (Get-GTEdgeDummyPath) | Out-Null
# Autorise la désinstallation (valeur nécessaire dans l'Espace économique européen), journalisée.
Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Microsoft\EdgeUpdateDev' -Name 'AllowUninstall' -Type String -Value ''
Write-GTJournal 'wingetapp' 'Microsoft.Edge' $true 'winget' $null
$p = Start-Process -FilePath $setup -ArgumentList '--uninstall --system-level --force-uninstall' -Wait -PassThru
if (Test-Path -LiteralPath (Join-Path (Get-GTEdgeAppDir) 'msedge.exe')) { throw ('Edge est toujours installé (code de sortie ' + $p.ExitCode + ').') }
Set-GTItemNote 'Edge désinstallé, profil conservé. Annulation : réinstallation par winget (Microsoft.Edge) et suppression du fichier factice.'
