if ((Get-GTWindowsBuild) -lt 19041) { Skip-GTItem 'La planification GPU matérielle demande Windows 10 version 2004 (build 19041) ou plus récente.' }
Set-TweakRegistryIfNeeded -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name 'HwSchMode' -Type DWord -Value 2
