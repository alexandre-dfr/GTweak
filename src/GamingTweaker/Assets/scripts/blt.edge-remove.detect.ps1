if (Test-Path -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe')) { 'NotApplied' } else { 'Applied' }
