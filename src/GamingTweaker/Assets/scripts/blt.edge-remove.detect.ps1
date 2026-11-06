if (Test-Path -LiteralPath (Join-Path (Get-GTEdgeAppDir) 'msedge.exe')) { 'NotApplied' } else { 'Applied' }
