Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors', 'DisableLocation', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors', 'DisableLocationScripting', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy', 'LetAppsAccessLocation', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location', 'Value', 'String', 'Allow'),
    @('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}', 'SensorPermissionState', 'DWord', 1)
) `
    -Service @(,@('lfsvc', 'Manual'))
