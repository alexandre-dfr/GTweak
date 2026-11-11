try {
    Get-GTTweakState -Registry @(,@('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters', 'DisabledComponents', 'DWord', 255))
} catch {
    'Unknown'
}
