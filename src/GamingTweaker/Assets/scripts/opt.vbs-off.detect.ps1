try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard', 'EnableVirtualizationBasedSecurity', 'DWord', 0),
        @('HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity', 'Enabled', 'DWord', 0)
    )
} catch {
    'Unknown'
}
