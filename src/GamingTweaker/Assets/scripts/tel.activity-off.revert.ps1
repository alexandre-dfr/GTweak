Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'EnableActivityFeed', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'PublishUserActivities', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'UploadUserActivities', 'DWord', $null)
)
