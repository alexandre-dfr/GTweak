try {
    Get-GTTweakState -Registry @(,@('HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell', 'FolderType', 'String', 'NotSpecified'))
} catch {
    'Unknown'
}
