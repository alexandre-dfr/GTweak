try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Policies\Microsoft\Windows\TCPIP\v6Transition', 'Teredo_State', 'String', 'Disabled'))
} catch {
    'Unknown'
}
