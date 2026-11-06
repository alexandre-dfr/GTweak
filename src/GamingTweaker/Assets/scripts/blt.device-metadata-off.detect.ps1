try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata', 'PreventDeviceMetadataFromNetwork', 'DWord', 1))
} catch {
    'Unknown'
}
