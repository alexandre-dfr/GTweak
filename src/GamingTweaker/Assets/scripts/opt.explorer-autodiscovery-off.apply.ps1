$cur = Get-GTRegValue 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell' 'FolderType'
if (-not ($cur.Existed -and $cur.Type -eq 'String' -and ([string]$cur.Value -ieq 'NotSpecified'))) {
    $null = Remove-TweakRegistryKey -Path 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags'
    $null = Remove-TweakRegistryKey -Path 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU'
    Set-TweakRegistryIfNeeded -Path 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell' -Name 'FolderType' -Type String -Value 'NotSpecified'
}
