try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors', 'DisableLocation', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors', 'DisableLocationScripting', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy', 'LetAppsAccessLocation', 'DWord', 2),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location', 'Value', 'String', 'Deny'),
        @('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}', 'SensorPermissionState', 'DWord', 0)
    ) `
        -Service @(,@('lfsvc', 'Disabled'))
} catch {
    'Unknown'
}
