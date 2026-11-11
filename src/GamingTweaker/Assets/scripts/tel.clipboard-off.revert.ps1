Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'AllowCrossDeviceClipboard', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'AllowClipboardHistory', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Clipboard', 'CloudClipboardAutomaticUpload', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Clipboard', 'EnableClipboardHistory', 'DWord', $null)
)
