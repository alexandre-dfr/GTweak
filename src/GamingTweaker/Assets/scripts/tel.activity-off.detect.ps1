try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'EnableActivityFeed', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'PublishUserActivities', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'UploadUserActivities', 'DWord', 0)
    )
} catch {
    'Unknown'
}
