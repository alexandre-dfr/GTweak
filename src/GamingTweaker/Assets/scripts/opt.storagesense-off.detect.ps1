try {
    Get-GTTweakState -Registry @(,@('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy', '01', 'DWord', 0))
} catch {
    'Unknown'
}
