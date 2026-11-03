try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Policies\Microsoft\Dsh', 'AllowNewsAndInterests', 'DWord', 0))
} catch {
    'Unknown'
}
