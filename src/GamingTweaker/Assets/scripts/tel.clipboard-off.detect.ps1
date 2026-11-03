try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'AllowCrossDeviceClipboard', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'AllowClipboardHistory', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Clipboard', 'CloudClipboardAutomaticUpload', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Clipboard', 'EnableClipboardHistory', 'DWord', 0)
    )
} catch {
    'Unknown'
}
