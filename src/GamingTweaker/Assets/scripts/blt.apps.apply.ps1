# Liste autorisée (nom de paquet = identifiant Microsoft Store, vide si inconnu) : même contenu que bloatware-list.json (test de cohérence).
$known = [ordered]@{
    'Microsoft.BingNews' = '9WZDNCRFHVFW'
    'Microsoft.BingWeather' = '9WZDNCRFJ3Q2'
    'Microsoft.BingSports' = ''
    'Microsoft.BingFinance' = ''
    'Microsoft.MicrosoftSolitaireCollection' = ''
    'Clipchamp.Clipchamp' = '9P1J8S7CCWWT'
    'Microsoft.Getstarted' = ''
    'MicrosoftTeams' = ''
    'MSTeams' = ''
    'Microsoft.MicrosoftOfficeHub' = '9WZDNCRD29V9'
    'Microsoft.WindowsFeedbackHub' = '9NBLGGH4R32N'
    'Microsoft.WindowsMaps' = ''
    'Microsoft.549981C3F5F10' = ''
    'Microsoft.NetworkSpeedTest' = ''
    'Microsoft.Print3D' = ''
    'Microsoft.CommsPhone' = ''
    'Microsoft.WindowsPhone' = ''
    'OneDrive' = ''
    'Microsoft.GamingApp' = '9MV0B5HZVK9Z'
    'Microsoft.XboxApp' = ''
    'Microsoft.YourPhone' = '9NMPJ99VJBWV'
    'microsoft.windowscommunicationsapps' = '9WZDNCRFHVQM'
    'Microsoft.Appconnector' = ''
    'Microsoft.OutlookForWindows' = ''
    'MicrosoftWindows.CrossDevice' = ''
    'Microsoft.GetHelp' = '9PKDZBMV1H3T'
    'Microsoft.ZuneVideo' = '9WZDNCRFJ3P2'
    'Microsoft.People' = ''
    'Microsoft.Todos' = '9NBLGGH5R558'
    'Microsoft.Messaging' = ''
    'Microsoft.MixedReality.Portal' = ''
    'Microsoft.MSPaint' = ''
    'Microsoft.Wallet' = ''
    'Microsoft.OneConnect' = ''
    'Microsoft.SkypeApp' = ''
    'Microsoft.GroupMe10' = ''
    'Microsoft.3DBuilder' = ''
    'Microsoft.Microsoft3DViewer' = ''
    'Microsoft.Office.OneNote' = ''
    'Microsoft.Office.Sway' = ''
}
if (@($Selection | Where-Object { $_ -eq '*' -or $_.StartsWith('!') }).Count -gt 0) { Skip-GTItem 'Choisissez les applications une à une : « Tout » n''est pas accepté pour ce menu (rien n''a été modifié).' }
$r = Resolve-GTListSelection -Selection $Selection -Allowed @($known.Keys)
if ($r.Unknown.Count -gt 0) { Skip-GTItem ('Applications refusées, hors de la liste autorisée (rien n''a été modifié) : ' + ($r.Unknown -join ', ')) }
if ($r.Names.Count -eq 0) { Skip-GTItem 'Aucune application sélectionnée.' }
$done = 0; $errors = @()
foreach ($n in $r.Names) {
    try {
        if ($n -eq 'OneDrive') { Remove-TweakOneDrive } else { Remove-TweakApp -Package $n -StoreId $known[$n] }
        $done++
    } catch {
        $m = [string]$_.Exception.Message
        if (-not $m.StartsWith('GTSKIP:')) { $errors += ($n + ' : ' + $m) }
    }
}
if ($errors.Count -gt 0) { throw ('Échec pour : ' + ($errors -join ' ; ')) }
if ($done -eq 0) { Skip-GTItem 'Aucune des applications choisies n''est présente sur cette machine.' }
Set-GTItemNote ($done.ToString() + ' application(s) retirée(s). Réinstallation possible depuis le Microsoft Store (annulation).')
