try {
    Get-GTTweakState -Registry @(
        @('HKCU:\Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}', 'System.IsPinnedToNameSpaceTree', 'DWord', 0),
        @('HKCU:\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}', 'System.IsPinnedToNameSpaceTree', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'LaunchTo', 'DWord', 1)
    )
} catch {
    'Unknown'
}
