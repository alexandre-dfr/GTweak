if (Test-Path -LiteralPath ($env:SystemDrive + '\Windows.old')) { 'NotApplied' } else { 'Applied' }
