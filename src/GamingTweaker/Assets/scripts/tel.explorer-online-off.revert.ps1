Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'AllowOnlineTips', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoInternetOpenWith', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoOnlinePrintsWizard', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoPublishingWizard', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoWebServices', 'DWord', $null)
)
