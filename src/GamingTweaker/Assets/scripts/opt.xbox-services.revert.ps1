Reset-GTTweaks -Service @(
    @('XblAuthManager', 'Manual'),
    @('XblGameSave', 'Manual'),
    @('XboxGipSvc', 'Manual'),
    @('XboxNetApiSvc', 'Manual')
)
