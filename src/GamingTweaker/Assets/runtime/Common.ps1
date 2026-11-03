# Fonctions communes injectees dans chaque script genere (apres l'en-tete de ScriptBuilder).
# Protocole : une ligne par evenement : ##GT|start|id, ##GT|ok|id, ##GT|fail|id|message, ##GT|skip|id|raison.

function Format-GTText([string]$Text) {
    if ($null -eq $Text) { return '' }
    return (($Text -replace '\|', '/') -replace '\s*[\r\n]+\s*', ' ').Trim()
}

function Write-GTEvent([string]$Kind, [string]$Id, [string]$Message) {
    $line = '##GT|' + $Kind + '|' + $Id
    if ($PSBoundParameters.ContainsKey('Message')) { $line += '|' + (Format-GTText $Message) }
    [Console]::Out.WriteLine($line)
    [Console]::Out.Flush()
}

# Utilisable depuis un script d'application : abandonne l'option sans erreur (##GT|skip).
function Skip-GTItem([string]$Reason) {
    throw ('GTSKIP:' + $Reason)
}

# Execute une option : start / ok / fail / skip. $Apply s'execute dans sa propre portee.
function Invoke-GTItem([string]$Id, [scriptblock]$Apply) {
    $script:GT_CurrentItem = $Id
    Write-GTEvent 'start' $Id
    try {
        & $Apply
        Write-GTEvent 'ok' $Id
    } catch {
        $msg = [string]$_.Exception.Message
        if ($msg.StartsWith('GTSKIP:')) { Write-GTEvent 'skip' $Id $msg.Substring(7) }
        else { Write-GTEvent 'fail' $Id $msg }
    }
}

# ---------------------------------------------------------------------------
# Journal d'annulation (journal.jsonl, une ligne JSON compacte par entree) et fonctions Set-Tweak*.
# Chaque fonction ecrit puis vide son entree sur disque AVANT de modifier : si l'ecriture echoue, elle leve
# une erreur et ne change rien. Kinds : registry, defender, service, task, power, bcd, hosts, app, quarantine.
# ---------------------------------------------------------------------------

function Add-GTJournalLine([string]$Line) {
    # Ajoute une ligne et la force sur disque. Un fragment final sans saut de ligne final (ecriture interrompue) est tronque d'abord :
    # il precede toujours la modification, donc n'a jamais ete applique.
    try {
        $dir = Split-Path -Parent $GT_JournalPath
        if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($Line + "`n")
        $fs = New-Object System.IO.FileStream($GT_JournalPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
        try {
            if ($fs.Length -gt 0) {
                $all = New-Object byte[] ([int]$fs.Length)
                $fs.Position = 0
                $got = 0
                while ($got -lt $all.Length) { $n = $fs.Read($all, $got, $all.Length - $got); if ($n -le 0) { break }; $got += $n }
                if ($all[$all.Length - 1] -ne 10) { $fs.SetLength([long]([Array]::LastIndexOf($all, [byte]10) + 1)) }
            }
            $fs.Position = $fs.Length
            $fs.Write($bytes, 0, $bytes.Length)
            $fs.Flush($true)
        } finally { $fs.Dispose() }
    } catch {
        throw ('Écriture du journal impossible, aucune modification faite : ' + $_.Exception.Message)
    }
}

function Write-GTJournal([string]$Kind, [string]$Target, [bool]$Existed, $Before, $After, [string]$ValueType) {
    if ([string]::IsNullOrEmpty($GT_JournalPath)) { throw 'Journal d''annulation non configuré.' }
    if ([string]::IsNullOrEmpty([string]$script:GT_CurrentItem)) { throw 'Option courante inconnue : écriture au journal refusée, aucune modification faite.' }
    $e = [ordered]@{
        item    = [string]$script:GT_CurrentItem
        kind    = $Kind
        target  = $Target
        existed = $Existed
        before  = $Before
        after   = $After
    }
    if ($ValueType) { $e['valueType'] = $ValueType }
    Add-GTJournalLine (ConvertTo-Json -InputObject $e -Compress -Depth 5)
}

function Write-GTUndoneMarker([string]$Item) {
    Add-GTJournalLine (ConvertTo-Json -InputObject ([ordered]@{ item = $Item; kind = 'undone' }) -Compress)
}

# ---- Registre -------------------------------------------------------------

function Get-GTRegValue([string]$Path, [string]$Name) {
    # Renvoie @{ Existed; Type; Value } avec Value dans la forme JSON du journal.
    $k = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if (-not $k -or ($k.GetValueNames() -notcontains $Name)) { return @{ Existed = $false; Type = $null; Value = $null } }
    $kind = $k.GetValueKind($Name).ToString()
    $v = $k.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    switch ($kind) {
        'DWord'       { $v = [uint32][BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$v), 0) }
        'QWord'       { $v = [int64]$v }
        'Binary'      { $v = [Convert]::ToBase64String([byte[]]$v) }
        'MultiString' { $v = [string[]]@($v) }
    }
    return @{ Existed = $true; Type = $kind; Value = $v }
}

function ConvertTo-GTRegData([string]$Type, $Value) {
    switch ($Type) {
        'DWord'       { return [int][BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]$Value), 0) }
        'QWord'       { return [int64]$Value }
        'Binary'      { if ($Value -is [string]) { return ,[byte[]][Convert]::FromBase64String($Value) } else { return ,[byte[]]@($Value) } }
        'MultiString' { return ,[string[]]@($Value) }
        default       { return [string]$Value }
    }
}

function Set-GTRegRaw([string]$Path, [string]$Name, [string]$Type, $Data) {
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -Path $Path -Force | Out-Null }
    New-ItemProperty -LiteralPath $Path -Name $Name -PropertyType $Type -Value $Data -Force | Out-Null
}

function Set-TweakRegistry {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Name,
          [Parameter(Mandatory)][ValidateSet('DWord','QWord','String','ExpandString','MultiString','Binary')][string]$Type,
          [Parameter(Mandatory)]$Value)
    $cur = Get-GTRegValue $Path $Name
    $data = ConvertTo-GTRegData $Type $Value
    # La valeur journalisee « after » a la meme forme que « before » (relue apres ecriture dans la meme representation).
    $afterJson = switch ($Type) {
        'DWord'  { [uint32][BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$data), 0) }
        'Binary' { [Convert]::ToBase64String([byte[]]$data) }
        default  { $data }
    }
    Write-GTJournal 'registry' ($Path + '|' + $Name) $cur.Existed $cur.Value $afterJson $(if ($cur.Existed) { $cur.Type } else { $Type })
    Set-GTRegRaw $Path $Name $Type $data
}

# ---- Defender ---------------------------------------------------------------

function Set-TweakDefenderRealtime {
    param([Parameter(Mandatory)][bool]$Enabled)
    $pref = Get-MpPreference
    $before = -not [bool]$pref.DisableRealtimeMonitoring
    Write-GTJournal 'defender' 'RealtimeMonitoring' $true $before $Enabled
    Set-MpPreference -DisableRealtimeMonitoring (-not $Enabled)
}

# ---- Services ---------------------------------------------------------------

function Get-GTServiceStart([string]$Name) {
    # Automatic / AutomaticDelayed / Manual / Disabled (lu dans le registre : Start + DelayedAutostart).
    $k = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $Name) -ErrorAction SilentlyContinue
    if (-not $k) { return $null }
    switch ([int]$k.Start) {
        2 { if ([int]$k.DelayedAutostart -eq 1) { return 'AutomaticDelayed' } else { return 'Automatic' } }
        3 { return 'Manual' }
        4 { return 'Disabled' }
        0 { return 'Boot' }
        1 { return 'System' }
    }
    return $null
}

function Set-GTServiceStart([string]$Name, [string]$StartType) {
    $map = @{ Automatic = 'auto'; AutomaticDelayed = 'delayed-auto'; Manual = 'demand'; Disabled = 'disabled'; Boot = 'boot'; System = 'system' }
    if (-not $map.ContainsKey($StartType)) { throw ('Type de démarrage inconnu : ' + $StartType) }
    $out = & sc.exe config $Name start= $map[$StartType] 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('sc.exe config a échoué pour ' + $Name + ' : ' + ($out -join ' ')) }
}

function Set-TweakService {
    param([Parameter(Mandatory)][string]$Name,
          [Parameter(Mandatory)][ValidateSet('Automatic','AutomaticDelayed','Manual','Disabled')][string]$StartType)
    $before = Get-GTServiceStart $Name
    if (-not $before) { Skip-GTItem ('Service ' + $Name + ' absent de cette machine.') }
    Write-GTJournal 'service' $Name $true $before $StartType
    Set-GTServiceStart $Name $StartType
}

# ---- Taches planifiees --------------------------------------------------------

function Split-GTTaskPath([string]$Path) {
    $i = $Path.LastIndexOf('\')
    $dir = $Path.Substring(0, $i + 1); if (-not $dir) { $dir = '\' }
    return @{ Dir = $dir; Name = $Path.Substring($i + 1) }
}

function Set-TweakTask {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][bool]$Enabled)
    $p = Split-GTTaskPath $Path
    $t = Get-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name -ErrorAction SilentlyContinue
    if (-not $t) { Skip-GTItem ('Tâche planifiée ' + $Path + ' absente de cette machine.') }
    $before = [bool]($t.State -ne 'Disabled')
    Write-GTJournal 'task' $Path $true $before $Enabled
    if ($Enabled) { Enable-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name | Out-Null }
    else { Disable-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name | Out-Null }
}

# ---- Alimentation -----------------------------------------------------------

function Get-GTActiveScheme {
    $o = (& powercfg.exe /getactivescheme) -join ' '
    if ($o -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') { return $Matches[1] }
    throw 'Plan d''alimentation actif introuvable.'
}

function ConvertFrom-GTPowerQuery([string[]]$Lines) {
    # Independant de la langue : les deux dernieres valeurs hexadecimales du bloc sont AC puis DC.
    $vals = @()
    foreach ($l in $Lines) {
        $m = [regex]::Match([string]$l, ':\s*0x([0-9a-fA-F]+)\s*$')
        if ($m.Success) { $vals += [Convert]::ToInt64($m.Groups[1].Value, 16) }
    }
    if ($vals.Count -lt 2) { throw 'Valeur d''alimentation illisible.' }
    return @{ AC = $vals[$vals.Count - 2]; DC = $vals[$vals.Count - 1] }
}

function Get-GTPowerValue([string]$Scheme, [string]$Sub, [string]$Setting) {
    $o = & powercfg.exe /query $Scheme $Sub $Setting
    if ($LASTEXITCODE -ne 0) { throw ('powercfg /query a échoué pour ' + $Sub + ' ' + $Setting) }
    return (ConvertFrom-GTPowerQuery ([string[]]@($o)))
}

function Set-GTPowerValue([string]$Scheme, [string]$Sub, [string]$Setting, $Ac, $Dc) {
    & powercfg.exe /setacvalueindex $Scheme $Sub $Setting ([string]$Ac); if ($LASTEXITCODE -ne 0) { throw 'powercfg /setacvalueindex a échoué.' }
    & powercfg.exe /setdcvalueindex $Scheme $Sub $Setting ([string]$Dc); if ($LASTEXITCODE -ne 0) { throw 'powercfg /setdcvalueindex a échoué.' }
    # Reappliquer le plan actif pour prise d'effet ; ne jamais changer de plan.
    if ((Get-GTActiveScheme) -ieq $Scheme) { & powercfg.exe /setactive $Scheme | Out-Null }
}

function Set-TweakPower {
    param([Parameter(Mandatory)][string]$Sub, [Parameter(Mandatory)][string]$Setting, [Parameter(Mandatory)][int]$Value)
    $scheme = Get-GTActiveScheme
    $cur = Get-GTPowerValue $scheme $Sub $Setting
    Write-GTJournal 'power' ($scheme + '|' + $Sub + '|' + $Setting) $true @($cur.AC, $cur.DC) @($Value, $Value)
    Set-GTPowerValue $scheme $Sub $Setting $Value $Value
}

# ---- BCD --------------------------------------------------------------------

function Get-GTBcdValue([string[]]$Lines, [string]$Name) {
    # Renvoie la valeur de l'element dans la sortie de « bcdedit /enum {current} », ou $null s'il est absent.
    foreach ($l in $Lines) {
        if ($l -match ('^\s*' + [regex]::Escape($Name) + '\s+(.+?)\s*$')) { return $Matches[1] }
    }
    return $null
}

function ConvertTo-GTBcdCanonical([string]$Value) {
    # Les booleens s'affichent traduits (Yes/Oui/Ja/Si/Sim, No/Non/Nein/Nao...) : on les ramene a yes/no.
    # Une autre valeur n'est acceptee que si c'est un mot ASCII simple (non traduit par bcdedit) ; sinon erreur.
    $d = $Value.Normalize([Text.NormalizationForm]::FormD)
    $plain = (($d.ToCharArray() | Where-Object { [Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne 'NonSpacingMark' }) -join '').ToLowerInvariant()
    if (@('yes', 'oui', 'ja', 'si', 'sim', 'true') -contains $plain) { return 'yes' }
    if (@('no', 'non', 'nein', 'nao', 'nee', 'false') -contains $plain) { return 'no' }
    if ($Value -match '^[A-Za-z0-9_{}\-.:,]+$') { return $Value }
    throw ('Valeur BCD « ' + $Value + ' » non reconnue : modification refusée.')
}

function Set-TweakBcd {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Value)
    $lines = & bcdedit.exe /enum '{current}' 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('bcdedit /enum a échoué, aucune modification faite : ' + ($lines -join ' ')) }
    $shown = Get-GTBcdValue ([string[]]@($lines)) $Name
    $cur = $null; if ($null -ne $shown) { $cur = ConvertTo-GTBcdCanonical $shown }
    Write-GTJournal 'bcd' $Name ($null -ne $cur) $cur $Value
    $out = & bcdedit.exe /set $Name $Value 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('bcdedit /set a échoué : ' + ($out -join ' ')) }
}

# ---- Fichier hosts ----------------------------------------------------------

$script:GT_HostsMarker = '# GamingTweaker'

function Get-GTHostsDefault { return (Join-Path $env:SystemRoot 'System32\drivers\etc\hosts') }

function Add-GTHostsLines([string]$HostsPath, [string[]]$NewLines) {
    $enc = New-Object System.Text.UTF8Encoding($false)
    $existing = @(); if (Test-Path -LiteralPath $HostsPath) { $existing = @([IO.File]::ReadAllLines($HostsPath)) }
    [IO.File]::WriteAllLines($HostsPath, [string[]]($existing + $NewLines), $enc)
}

function Remove-GTHostsLines([string]$HostsPath, [string[]]$Lines) {
    if (-not (Test-Path -LiteralPath $HostsPath)) { return }
    $enc = New-Object System.Text.UTF8Encoding($false)
    $left = New-Object System.Collections.Generic.List[string]
    $toRemove = New-Object System.Collections.Generic.List[string]; $toRemove.AddRange([string[]]@($Lines))
    foreach ($l in [IO.File]::ReadAllLines($HostsPath)) {
        $i = $toRemove.IndexOf($l)
        if ($i -ge 0) { $toRemove.RemoveAt($i) } else { $left.Add($l) }
    }
    [IO.File]::WriteAllLines($HostsPath, $left.ToArray(), $enc)
}

function Add-TweakHosts {
    param([Parameter(Mandatory)][string[]]$Domains, [string]$HostsPath)
    if (-not $HostsPath) { $HostsPath = Get-GTHostsDefault }
    $existing = @(); if (Test-Path -LiteralPath $HostsPath) { $existing = @([IO.File]::ReadAllLines($HostsPath)) }
    $new = @()
    foreach ($d in $Domains) {
        $line = '0.0.0.0 ' + $d + ' ' + $script:GT_HostsMarker
        if (($existing -notcontains $line) -and ($new -notcontains $line)) { $new += $line }
    }
    if ($new.Count -eq 0) { return }
    Write-GTJournal 'hosts' $HostsPath $true $null $new
    Add-GTHostsLines $HostsPath $new
}

# ---- Applications -----------------------------------------------------------

function Remove-TweakApp {
    param([Parameter(Mandatory)][string]$Package)
    $pk = @(Get-AppxPackage -Name $Package -AllUsers -ErrorAction SilentlyContinue)
    if ($pk.Count -eq 0) { Skip-GTItem ('Application ' + $Package + ' absente de cette machine.') }
    foreach ($p in $pk) {
        Write-GTJournal 'app' $p.PackageFullName $true $p.PackageFullName $null
        Remove-AppxPackage -Package $p.PackageFullName -AllUsers
    }
}

# ---- Quarantaine ------------------------------------------------------------

function Move-TweakQuarantine {
    param([Parameter(Mandatory)][string]$File)
    if ([string]::IsNullOrEmpty($GT_QuarantineDir)) { throw 'Dossier de quarantaine non configuré.' }
    if (-not (Test-Path -LiteralPath $File)) { Skip-GTItem ('Fichier ' + $File + ' absent.') }
    $dest = Join-Path $GT_QuarantineDir ([Guid]::NewGuid().ToString('N') + '_' + (Split-Path -Leaf $File))
    Write-GTJournal 'quarantine' $File $true $File $dest
    New-Item -ItemType Directory -Force -Path $GT_QuarantineDir | Out-Null
    Move-Item -LiteralPath $File -Destination $dest -Force
}

# ---- Annulation -------------------------------------------------------------

function Read-GTJournal {
    # Entrees du journal (objets). Derniere ligne illisible ignoree ; ligne illisible au milieu : erreur.
    if (-not (Test-Path -LiteralPath $GT_JournalPath)) { return @() }
    $lines = @([IO.File]::ReadAllLines($GT_JournalPath, [Text.Encoding]::UTF8) | Where-Object { $_.Trim() })
    $res = @()
    for ($i = 0; $i -lt $lines.Count; $i++) {
        try { $res += ,(ConvertFrom-Json -InputObject $lines[$i]) }
        catch {
            if ($i -eq $lines.Count - 1) { continue }
            throw ('Journal corrompu (entrée ' + ($i + 1) + '), annulation impossible.')
        }
    }
    return $res
}

function Restore-GTEntry($E) {
    switch ($E.kind) {
        'registry' {
            $p = $E.target.IndexOf('|'); $path = $E.target.Substring(0, $p); $name = $E.target.Substring($p + 1)
            if ($E.existed) { Set-GTRegRaw $path $name $E.valueType (ConvertTo-GTRegData $E.valueType $E.before) }
            elseif ((Get-GTRegValue $path $name).Existed) { Remove-ItemProperty -LiteralPath $path -Name $name -ErrorAction Stop }
        }
        'defender'   { Set-MpPreference -DisableRealtimeMonitoring (-not [bool]$E.before) }
        'service'    { Set-GTServiceStart $E.target ([string]$E.before) }
        'task' {
            $p = Split-GTTaskPath $E.target
            if ([bool]$E.before) { Enable-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name | Out-Null }
            else { Disable-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name | Out-Null }
        }
        'power' {
            $t = $E.target -split '\|'
            Set-GTPowerValue $t[0] $t[1] $t[2] @($E.before)[0] @($E.before)[1]
        }
        'bcd' {
            if ($E.existed) { $o = & bcdedit.exe /set $E.target ([string]$E.before) 2>&1 }
            else { $o = & bcdedit.exe /deletevalue $E.target 2>&1 }
            if ($LASTEXITCODE -ne 0) { throw ('bcdedit a échoué : ' + ($o -join ' ')) }
        }
        'hosts'      { Remove-GTHostsLines $E.target ([string[]]@($E.after)) }
        'quarantine' {
            if ((Test-Path -LiteralPath $E.before) -and -not (Test-Path -LiteralPath $E.after)) { return }  # deja restaure
            if (Test-Path -LiteralPath $E.before) { throw ('Impossible de restaurer ' + $E.before + ' : un fichier existe déjà à cet emplacement.') }
            $dir = Split-Path -Parent $E.before
            if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
            Move-Item -LiteralPath $E.after -Destination $E.before
        }
        'app' { }
        default { throw ('Type d''entrée de journal inconnu : ' + $E.kind) }
    }
}

# Restaure une option : ses entrees posterieures au dernier marqueur « undone », en ordre inverse.
# Une entree en echec n'empeche pas les suivantes ; l'option echoue a la fin (sans marqueur, donc rejouable).
function Restore-GTItem([Parameter(Mandatory)][string]$Item) {
    $all = @(Read-GTJournal | Where-Object { $_.item -eq $Item })
    if ($all.Count -eq 0) { Skip-GTItem 'Rien à annuler : aucune entrée de journal pour cette option.' }
    $lastMarker = -1
    for ($i = 0; $i -lt $all.Count; $i++) { if ($all[$i].kind -eq 'undone') { $lastMarker = $i } }
    $entries = @()
    for ($i = $lastMarker + 1; $i -lt $all.Count; $i++) { $entries += ,$all[$i] }
    if ($entries.Count -eq 0) { Skip-GTItem 'Option déjà annulée.' }
    [array]::Reverse($entries)
    $errors = @(); $manual = @()
    foreach ($e in $entries) {
        if ($e.kind -eq 'app') { $manual += $e.target; continue }
        try { Restore-GTEntry $e } catch { $errors += ([string]$e.kind + ' ' + [string]$e.target + ' : ' + $_.Exception.Message) }
    }
    if ($errors.Count -gt 0) { throw ($errors -join ' ; ') }
    Write-GTUndoneMarker $Item
    if ($manual.Count -gt 0) { Skip-GTItem ('Réinstallation manuelle nécessaire (Microsoft Store) : ' + ($manual -join ', ')) }
}

# ---------------------------------------------------------------------------
# Aides des scripts du catalogue (detect / apply / revert).
# Les scripts apply restent idempotents : une valeur deja a l'etat voulu n'est ni reecrite ni journalisee,
# donc le journal ne garde que l'etat d'origine. Un element (tache, service) absent de la machine est ignore sans erreur.
# ---------------------------------------------------------------------------

# Vrai si la valeur existe avec ce type et cette valeur.
function Test-GTRegistryValue([string]$Path, [string]$Name, [string]$Type, $Value) {
    $cur = Get-GTRegValue $Path $Name
    if (-not $cur.Existed -or $cur.Type -ne $Type) { return $false }
    try {
        if ($Type -eq 'DWord' -or $Type -eq 'QWord') { return ([int64]$cur.Value -eq [int64]$Value) }
        return ([string]$cur.Value -ieq [string]$Value)
    } catch { return $false }
}

function Set-TweakRegistryIfNeeded {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Name,
          [Parameter(Mandatory)][string]$Type, [Parameter(Mandatory)]$Value)
    if (Test-GTRegistryValue $Path $Name $Type $Value) { return }
    Set-TweakRegistry -Path $Path -Name $Name -Type $Type -Value $Value
}

# Renvoie $true si le service existe sur cette machine (modifie ou deja a l'etat voulu), $false sinon.
function Set-TweakServiceIfNeeded {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$StartType)
    $cur = Get-GTServiceStart $Name
    if (-not $cur) { return $false }
    if ($cur -ne $StartType) { Set-TweakService -Name $Name -StartType $StartType }
    return $true
}

# $null si la tache est absente, sinon $true (activee) ou $false (desactivee).
function Get-GTTaskEnabled([string]$Path) {
    $p = Split-GTTaskPath $Path
    $t = Get-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name -ErrorAction SilentlyContinue
    if (-not $t) { return $null }
    return [bool]($t.State -ne 'Disabled')
}

# Renvoie $true si la tache existe sur cette machine (modifiee ou deja a l'etat voulu), $false sinon.
function Set-TweakTaskIfNeeded {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][bool]$Enabled)
    $cur = Get-GTTaskEnabled $Path
    if ($null -eq $cur) { return $false }
    if ($cur -ne $Enabled) { Set-TweakTask -Path $Path -Enabled $Enabled }
    return $true
}

function Test-GTHostsBlocked([string[]]$Domains) {
    $path = Get-GTHostsDefault
    $existing = @(); if (Test-Path -LiteralPath $path) { $existing = @([IO.File]::ReadAllLines($path)) }
    foreach ($d in $Domains) { if ($existing -notcontains ('0.0.0.0 ' + $d + ' ' + $script:GT_HostsMarker)) { return $false } }
    return $true
}

# Detection : « Applied » si tous les elements presents sont a l'etat modifie (au moins un element present), sinon « NotApplied ».
# -Registry : @(chemin, nom, type, valeur) ; -Service : @(nom, demarrage) ; -Task : chemins (desactivees) ; -HostName : domaines bloques.
# Un service ou une tache absent de la machine est ignore ; une valeur de registre absente compte comme non appliquee.
function Get-GTTweakState {
    param([object[]]$Registry = @(), [object[]]$Service = @(), [string[]]$Task = @(), [string[]]$HostName = @())
    $seen = 0
    foreach ($r in $Registry) { $seen++; if (-not (Test-GTRegistryValue $r[0] $r[1] $r[2] $r[3])) { return 'NotApplied' } }
    foreach ($s in $Service) {
        $cur = Get-GTServiceStart $s[0]
        if (-not $cur) { continue }
        $seen++; if ($cur -ne $s[1]) { return 'NotApplied' }
    }
    foreach ($t in $Task) {
        $cur = Get-GTTaskEnabled $t
        if ($null -eq $cur) { continue }
        $seen++; if ($cur) { return 'NotApplied' }
    }
    if ($HostName.Count -gt 0) { $seen++; if (-not (Test-GTHostsBlocked $HostName)) { return 'NotApplied' } }
    if ($seen -eq 0) { return 'NotApplied' }
    return 'Applied'
}

# Retour aux valeurs par defaut de Windows (scripts revert, utilises sans journal).
# -Registry : @(chemin, nom, type, defaut) ; defaut $null = supprimer la valeur ; -Service : @(nom, demarrage) ; -Task : chemins reactivees.
function Reset-GTTweaks {
    param([object[]]$Registry = @(), [object[]]$Service = @(), [string[]]$Task = @(), [string[]]$HostName = @())
    foreach ($r in $Registry) {
        if ($null -eq $r[3]) {
            if ((Get-GTRegValue $r[0] $r[1]).Existed) { Remove-ItemProperty -LiteralPath $r[0] -Name $r[1] -ErrorAction Stop }
        } elseif (-not (Test-GTRegistryValue $r[0] $r[1] $r[2] $r[3])) {
            Set-GTRegRaw $r[0] $r[1] $r[2] (ConvertTo-GTRegData $r[2] $r[3])
        }
    }
    foreach ($s in $Service) {
        $cur = Get-GTServiceStart $s[0]
        if ($cur -and $cur -ne $s[1]) { Set-GTServiceStart $s[0] $s[1] }
    }
    foreach ($t in $Task) {
        $cur = Get-GTTaskEnabled $t
        if ($null -ne $cur -and -not $cur) { $p = Split-GTTaskPath $t; Enable-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name | Out-Null }
    }
    if ($HostName.Count -gt 0) {
        Remove-GTHostsLines (Get-GTHostsDefault) ([string[]]@($HostName | ForEach-Object { '0.0.0.0 ' + $_ + ' ' + $script:GT_HostsMarker }))
    }
}
