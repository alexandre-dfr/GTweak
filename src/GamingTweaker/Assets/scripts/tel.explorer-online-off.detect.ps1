try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'AllowOnlineTips', 'DWord', 0),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoInternetOpenWith', 'DWord', 1),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoOnlinePrintsWizard', 'DWord', 1),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoPublishingWizard', 'DWord', 1),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoWebServices', 'DWord', 1)
    )
} catch {
    'Unknown'
}
