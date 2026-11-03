try {
    Get-GTServiceManualState @('XblAuthManager', 'XblGameSave', 'XboxGipSvc', 'XboxNetApiSvc')
} catch {
    'Unknown'
}
