$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowCrossDeviceClipboard' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowClipboardHistory' -Type DWord -Value 0
$k = 'HKCU:\Software\Microsoft\Clipboard'
Set-TweakRegistryIfNeeded -Path $k -Name 'CloudClipboardAutomaticUpload' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableClipboardHistory' -Type DWord -Value 0
