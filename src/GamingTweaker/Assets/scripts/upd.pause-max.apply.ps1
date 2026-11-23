# N'active aucune pause : allonge seulement la durée maximale proposée dans Paramètres > Windows Update.
Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings' -Name 'FlightSettingsMaxPauseDays' -Type DWord -Value 365
