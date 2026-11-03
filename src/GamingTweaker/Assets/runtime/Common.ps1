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
    & powercfg.exe /setacvalueindex $Scheme $Sub $Setting ([string]$Ac) | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'powercfg /setacvalueindex a échoué.' }
    & powercfg.exe /setdcvalueindex $Scheme $Sub $Setting ([string]$Dc) | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'powercfg /setdcvalueindex a échoué.' }
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

# $null si le reglage n'existe pas dans ce plan (reglage cache absent de certaines machines).
function Get-GTPowerValueOrNull([string]$Scheme, [string]$Sub, [string]$Setting) {
    try { return (Get-GTPowerValue $Scheme $Sub $Setting) } catch { return $null }
}

# Idempotent : ne journalise ni ne modifie si AC et DC valent deja la valeur. Renvoie $false si le reglage est absent.
function Set-TweakPowerIfNeeded {
    param([Parameter(Mandatory)][string]$Sub, [Parameter(Mandatory)][string]$Setting, [Parameter(Mandatory)][int]$Value)
    $cur = Get-GTPowerValueOrNull (Get-GTActiveScheme) $Sub $Setting
    if ($null -eq $cur) { return $false }
    if (($cur.AC -ne $Value) -or ($cur.DC -ne $Value)) { Set-TweakPower -Sub $Sub -Setting $Setting -Value $Value }
    return $true
}

# 'Applied' si le reglage existe dans le plan actif et vaut la valeur en secteur et sur batterie, sinon 'NotApplied'.
function Get-GTPowerState {
    param([Parameter(Mandatory)][string]$Sub, [Parameter(Mandatory)][string]$Setting, [Parameter(Mandatory)][int]$Value)
    $cur = Get-GTPowerValueOrNull (Get-GTActiveScheme) $Sub $Setting
    if ($null -eq $cur) { return 'NotApplied' }
    if (($cur.AC -eq $Value) -and ($cur.DC -eq $Value)) { return 'Applied' }
    return 'NotApplied'
}

# Retour a une valeur par defaut (scripts revert, sans journal), sur le plan actif.
function Reset-GTPowerValue([string]$Sub, [string]$Setting, [int]$Ac, [int]$Dc) {
    $scheme = Get-GTActiveScheme
    if ($null -eq (Get-GTPowerValueOrNull $scheme $Sub $Setting)) { return }
    Set-GTPowerValue $scheme $Sub $Setting $Ac $Dc
}

# ---- Plans d'alimentation ----------------------------------------------------

function ConvertFrom-GTPowerSchemeList([string[]]$Lines) {
    # Sortie de « powercfg /list » (toute langue) : une ligne par plan, GUID puis nom entre parentheses, « * » pour le plan actif.
    $res = @()
    foreach ($l in $Lines) {
        $m = [regex]::Match([string]$l, '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*(?:\((.*)\))?\s*(\*)?\s*$')
        if ($m.Success) { $res += ,@{ Guid = $m.Groups[1].Value.ToLowerInvariant(); Name = $m.Groups[2].Value; Active = $m.Groups[3].Success } }
    }
    return $res
}

function Test-GTSchemeInList([string[]]$Lines, [string]$Guid) {
    foreach ($s in @(ConvertFrom-GTPowerSchemeList $Lines)) { if ($s.Guid -eq $Guid.ToLowerInvariant()) { return $true } }
    return $false
}

function Get-GTSchemeLines { return [string[]]@(& powercfg.exe /list) }

function Set-GTActiveScheme([string]$Guid) {
    & powercfg.exe /setactive $Guid | Out-Null
    if ($LASTEXITCODE -ne 0) { throw ('powercfg /setactive a échoué pour le plan ' + $Guid + '.') }
}

# Change le plan actif ; l'annulation remet le plan precedent. Idempotent : rien si le plan est deja actif.
function Set-TweakActiveScheme {
    param([Parameter(Mandatory)][string]$Guid)
    $cur = Get-GTActiveScheme
    if ($cur -ieq $Guid) { return }
    if (-not (Test-GTSchemeInList (Get-GTSchemeLines) $Guid)) { throw ('Plan d''alimentation ' + $Guid + ' introuvable sur cette machine.') }
    Write-GTJournal 'powerplan' 'ActiveScheme' $true $cur $Guid.ToLowerInvariant()
    Set-GTActiveScheme $Guid
}

# Cree un plan d'alimentation systeme (High performance, Ultimate Performance...) sous son GUID d'origine, independant de la langue,
# s'il n'existe pas encore ; sinon le reutilise. La creation est journalisee (kind powerscheme) mais n'est pas supprimee a l'annulation :
# le plan reste disponible pour la prochaine application.
function Initialize-GTScheme {
    param([Parameter(Mandatory)][string]$Guid)
    if (Test-GTSchemeInList (Get-GTSchemeLines) $Guid) { return }
    $g = $Guid.ToLowerInvariant()
    Write-GTJournal 'powerscheme' $g $false $null $g
    $out = & powercfg.exe -duplicatescheme $g $g 2>&1
    if (($LASTEXITCODE -ne 0) -or -not (Test-GTSchemeInList (Get-GTSchemeLines) $g)) {
        throw ('Le plan d''alimentation ' + $g + ' n''est pas disponible sur cette machine : ' + ($out -join ' '))
    }
}

# ---- Veille prolongee ---------------------------------------------------------

function ConvertTo-GTHibernateEnabled($RegValue) {
    if ($null -eq $RegValue) { return $false }
    try { return ([int64]$RegValue -ne 0) } catch { return $false }
}

# Lu dans le registre (HibernateEnabled) : independant de la langue de « powercfg /a ».
function Get-GTHibernateEnabled {
    $p = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name 'HibernateEnabled' -ErrorAction SilentlyContinue
    if (-not $p) { return $false }
    return (ConvertTo-GTHibernateEnabled $p.HibernateEnabled)
}

function Set-GTHibernate([bool]$Enabled) {
    $arg = 'off'; if ($Enabled) { $arg = 'on' }
    $out = & powercfg.exe /hibernate $arg 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('powercfg /hibernate ' + $arg + ' a échoué : ' + ($out -join ' ')) }
}

function Set-TweakHibernate {
    param([Parameter(Mandatory)][bool]$Enabled)
    $cur = Get-GTHibernateEnabled
    if ($cur -eq $Enabled) { return }
    Write-GTJournal 'hibernate' 'Hibernate' $true $cur $Enabled
    Set-GTHibernate $Enabled
}

# ---- Gestion d'alimentation des peripheriques (MSPower_DeviceEnable) ---------------

function Get-GTPowerDevices {
    return @(Get-CimInstance -Namespace 'root\wmi' -ClassName 'MSPower_DeviceEnable' -ErrorAction Stop |
        ForEach-Object { [pscustomobject]@{ InstanceName = [string]$_.InstanceName; Enable = [bool]$_.Enable } })
}

function Select-GTUsbInstances([string[]]$InstanceNames) {
    return @($InstanceNames | Where-Object { $_ -match '^USB\\' })
}

# Une instance WMI porte l'identifiant PnP suivi de « _ » et d'un numero.
function Select-GTInstancesByPnpIds([string[]]$InstanceNames, [string[]]$PnpIds) {
    $res = @()
    foreach ($n in $InstanceNames) {
        foreach ($p in $PnpIds) {
            if ($p -and ($n -match ('^' + [regex]::Escape($p) + '_\d+$'))) { $res += $n; break }
        }
    }
    return $res
}

function Get-GTDevicePowerTargets([ValidateSet('Usb', 'Nic')][string]$Kind) {
    $names = @(Get-GTPowerDevices | ForEach-Object { $_.InstanceName })
    if ($Kind -eq 'Usb') { return @(Select-GTUsbInstances $names) }
    $ids = @(Get-CimInstance -ClassName 'Win32_NetworkAdapter' -Filter 'PhysicalAdapter=True' | ForEach-Object { [string]$_.PNPDeviceID } | Where-Object { $_ })
    return @(Select-GTInstancesByPnpIds $names $ids)
}

function Set-GTDevicePower([string]$InstanceName, [bool]$Enabled) {
    $inst = Get-CimInstance -Namespace 'root\wmi' -ClassName 'MSPower_DeviceEnable' | Where-Object { $_.InstanceName -eq $InstanceName } | Select-Object -First 1
    if (-not $inst) { throw ('Périphérique ' + $InstanceName + ' introuvable.') }
    $inst.Enable = $Enabled
    Set-CimInstance -InputObject $inst
}

function Set-TweakDevicePower {
    param([Parameter(Mandatory)][string]$InstanceName, [Parameter(Mandatory)][bool]$Enabled)
    $cur = @(Get-GTPowerDevices | Where-Object { $_.InstanceName -eq $InstanceName })
    if ($cur.Count -eq 0) { Skip-GTItem ('Périphérique ' + $InstanceName + ' absent de cette machine.') }
    if ($cur[0].Enable -eq $Enabled) { return }
    Write-GTJournal 'devicepower' $InstanceName $true $cur[0].Enable $Enabled
    Set-GTDevicePower $InstanceName $Enabled
}

# Tous les peripheriques USB (-Kind Usb) ou toutes les cartes reseau physiques (-Kind Nic). Renvoie $false si aucun n'existe.
# Un peripherique en erreur n'empeche pas les suivants ; l'option echoue a la fin avec la liste des erreurs.
function Set-TweakDevicePowerKind {
    param([Parameter(Mandatory)][ValidateSet('Usb', 'Nic')][string]$Kind, [Parameter(Mandatory)][bool]$Enabled)
    $targets = @(Get-GTDevicePowerTargets $Kind)
    if ($targets.Count -eq 0) { return $false }
    $state = @{}; foreach ($d in (Get-GTPowerDevices)) { $state[$d.InstanceName] = $d.Enable }
    $errors = @()
    foreach ($t in $targets) {
        if ($state[$t] -eq $Enabled) { continue }
        try { Set-TweakDevicePower -InstanceName $t -Enabled $Enabled } catch { $errors += ($t + ' : ' + $_.Exception.Message) }
    }
    if ($errors.Count -gt 0) { throw ($errors -join ' ; ') }
    return $true
}

function Get-GTDevicePowerState([ValidateSet('Usb', 'Nic')][string]$Kind) {
    $targets = @(Get-GTDevicePowerTargets $Kind)
    if ($targets.Count -eq 0) { return 'NotApplied' }
    $state = @{}; foreach ($d in (Get-GTPowerDevices)) { $state[$d.InstanceName] = $d.Enable }
    foreach ($t in $targets) { if ($state[$t]) { return 'NotApplied' } }
    return 'Applied'
}

# Retour au defaut (scripts revert, sans journal) : reactive la gestion d'alimentation de tous les peripheriques du type.
function Reset-GTDevicePowerKind {
    param([Parameter(Mandatory)][ValidateSet('Usb', 'Nic')][string]$Kind)
    $state = @{}; foreach ($d in (Get-GTPowerDevices)) { $state[$d.InstanceName] = $d.Enable }
    foreach ($t in @(Get-GTDevicePowerTargets $Kind)) { if (-not $state[$t]) { Set-GTDevicePower $t $true } }
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

# Valeur actuelle d'un element du BCD ({current}) ramenee a une forme canonique, ou $null s'il est absent.
function Get-GTBcdCurrent([string]$Name) {
    $lines = & bcdedit.exe /enum '{current}' 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('bcdedit /enum a échoué : ' + ($lines -join ' ')) }
    $shown = Get-GTBcdValue ([string[]]@($lines)) $Name
    if ($null -eq $shown) { return $null }
    return (ConvertTo-GTBcdCanonical $shown)
}

# Idempotent : ne journalise ni ne modifie si l'element a deja la valeur.
function Set-TweakBcdIfNeeded {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Value)
    $cur = Get-GTBcdCurrent $Name
    if (($null -ne $cur) -and ($cur -eq $Value)) { return }
    Set-TweakBcd -Name $Name -Value $Value
}

# Supprime un element du BCD s'il existe (l'annulation le remet a sa valeur d'origine). Rien si absent.
function Remove-TweakBcd {
    param([Parameter(Mandatory)][string]$Name)
    $cur = Get-GTBcdCurrent $Name
    if ($null -eq $cur) { return }
    Write-GTJournal 'bcd' $Name $true $cur $null
    $out = & bcdedit.exe /deletevalue $Name 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('bcdedit /deletevalue a échoué : ' + ($out -join ' ')) }
}

# Retour au defaut (scripts revert, sans journal) : supprime l'element s'il existe.
function Reset-GTBcdValue([string]$Name) {
    if ($null -eq (Get-GTBcdCurrent $Name)) { return }
    $out = & bcdedit.exe /deletevalue $Name 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('bcdedit /deletevalue a échoué : ' + ($out -join ' ')) }
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
            if ($path -ieq 'HKCU:\Control Panel\Mouse') { Update-GTMouseParams }
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
        'powerplan' {
            if (-not (Test-GTSchemeInList (Get-GTSchemeLines) ([string]$E.before))) { throw ('Le plan d''alimentation d''origine ' + $E.before + ' n''existe plus.') }
            Set-GTActiveScheme ([string]$E.before)
        }
        'powerscheme' { }   # le plan cree reste disponible (reutilise a la prochaine application)
        'hibernate'   { Set-GTHibernate ([bool]$E.before) }
        'devicepower' {
            # Un peripherique debranche depuis n'est plus a restaurer.
            if (@(Get-GTPowerDevices | Where-Object { $_.InstanceName -eq $E.target }).Count -gt 0) { Set-GTDevicePower $E.target ([bool]$E.before) }
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

# Passe un service automatique en manuel ; un service deja manuel ou desactive n'est jamais modifie (un service desactive n'est pas reactive).
# Renvoie $true si le service existe sur cette machine, $false sinon.
function Set-TweakServiceManualIfNeeded {
    param([Parameter(Mandatory)][string]$Name)
    $cur = Get-GTServiceStart $Name
    if (-not $cur) { return $false }
    if (($cur -eq 'Automatic') -or ($cur -eq 'AutomaticDelayed')) { Set-TweakService -Name $Name -StartType 'Manual' }
    return $true
}

# 'Applied' si au moins un des services existe et qu'aucun n'est en demarrage automatique.
function Get-GTServiceManualState([string[]]$Names) {
    $seen = 0
    foreach ($n in $Names) {
        $cur = Get-GTServiceStart $n
        if (-not $cur) { continue }
        $seen++
        if (($cur -eq 'Automatic') -or ($cur -eq 'AutomaticDelayed')) { return 'NotApplied' }
    }
    if ($seen -eq 0) { return 'NotApplied' }
    return 'Applied'
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

# ---------------------------------------------------------------------------
# Aides du menu Optimisation.
# ---------------------------------------------------------------------------

function Get-GTWindowsBuild {
    $v = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name 'CurrentBuildNumber' -ErrorAction Stop).CurrentBuildNumber
    return [int]$v
}

# Convention des listes ($Selection) : « * » = tous les elements autorises, « !x » = exclusion de x, sinon noms explicites.
# Seuls les noms de -Allowed peuvent etre retenus. Names : elements a traiter (ordre de -Allowed) ;
# Unknown : noms explicites hors de -Allowed (le script appelant doit alors abandonner l'option, sans rien modifier).
function Resolve-GTListSelection {
    param([string[]]$Selection = @(), [string[]]$Allowed = @())
    $sel = @($Selection | Where-Object { $_ })
    $all = [bool]($sel -contains '*')
    $excluded = @($sel | Where-Object { $_.StartsWith('!') } | ForEach-Object { $_.Substring(1) })
    $explicit = @($sel | Where-Object { ($_ -ne '*') -and -not $_.StartsWith('!') })
    $names = @(); $unknown = @()
    foreach ($a in $Allowed) {
        $isExplicit = (@($explicit | Where-Object { $_ -ieq $a }).Count -gt 0)
        $isExcluded = (@($excluded | Where-Object { $_ -ieq $a }).Count -gt 0)
        if (($isExplicit -or $all) -and -not $isExcluded) { $names += $a }
    }
    foreach ($x in $explicit) {
        if (@($Allowed | Where-Object { $_ -ieq $x }).Count -eq 0) { $unknown += $x }
    }
    return @{ Names = [string[]]$names; Unknown = [string[]]$unknown }
}

# ---- Souris : SPI_SETMOUSE ----------------------------------------------------

function ConvertTo-GTMouseSpiArgs($Threshold1, $Threshold2, $Speed) {
    $r = New-Object 'int[]' 3
    $vals = @($Threshold1, $Threshold2, $Speed)
    for ($i = 0; $i -lt 3; $i++) {
        $n = 0
        if ([int]::TryParse([string]$vals[$i], [ref]$n)) { $r[$i] = $n }
    }
    return ,$r
}

# Applique a la session en cours les valeurs du registre (sans attendre la prochaine ouverture de session). Echec ignore.
function Update-GTMouseParams {
    try {
        $k = Get-ItemProperty -LiteralPath 'HKCU:\Control Panel\Mouse' -ErrorAction Stop
        $a = ConvertTo-GTMouseSpiArgs $k.MouseThreshold1 $k.MouseThreshold2 $k.MouseSpeed
        if (-not ('GTNative.Spi' -as [type])) {
            Add-Type -Namespace 'GTNative' -Name 'Spi' -MemberDefinition '[DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);'
        }
        [void][GTNative.Spi]::SystemParametersInfo(4, 0, $a, 0)
    } catch { }
}

# ---- Programmes lances au demarrage (StartupApproved) ---------------------------------

# Cle d'une entree : portee:type:nom (meme format que StartupKey.Make cote C#).
function Get-GTStartupKey([string]$Scope, [string]$Kind, [string]$Name) { return ($Scope + ':' + $Kind + ':' + $Name) }

# Windows note « desactive » par un premier octet impair (3, 7), « active » par un octet pair (2, 6) ou l'absence de valeur.
function Test-GTStartupDisabled($Bytes) {
    if ($null -eq $Bytes) { return $false }
    $b = [byte[]]@($Bytes)
    return (($b.Length -gt 0) -and (($b[0] -band 1) -eq 1))
}

function New-GTStartupDisabledValue {
    $b = New-Object 'byte[]' 12
    $b[0] = 3
    [Array]::Copy([BitConverter]::GetBytes([DateTime]::UtcNow.ToFileTimeUtc()), 0, $b, 4, 8)
    return ,$b
}

function Get-GTStartupEntries {
    $cv = '\SOFTWARE\Microsoft\Windows\CurrentVersion'
    $res = @()
    $sources = @(
        @('HKCU', 'Run', ('HKCU:' + $cv + '\Run')),
        @('HKLM', 'Run', ('HKLM:' + $cv + '\Run')),
        @('HKLM', 'Run32', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')
    )
    foreach ($s in $sources) {
        $k = Get-Item -LiteralPath $s[2] -ErrorAction SilentlyContinue
        if (-not $k) { continue }
        foreach ($n in $k.GetValueNames()) {
            if ($n) { $res += [pscustomobject]@{ Key = (Get-GTStartupKey $s[0] $s[1] $n); Scope = $s[0]; Kind = $s[1]; Name = $n; ApprovedPath = ($s[0] + ':' + $cv + '\Explorer\StartupApproved\' + $s[1]) } }
        }
    }
    foreach ($f in @(@('HKCU', [Environment]::GetFolderPath('Startup')), @('HKLM', [Environment]::GetFolderPath('CommonStartup')))) {
        if (-not $f[1] -or -not (Test-Path -LiteralPath $f[1])) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $f[1] -File -Force -ErrorAction SilentlyContinue)) {
            if ($file.Name -ieq 'desktop.ini') { continue }
            $res += [pscustomobject]@{ Key = (Get-GTStartupKey $f[0] 'StartupFolder' $file.Name); Scope = $f[0]; Kind = 'StartupFolder'; Name = $file.Name; ApprovedPath = ($f[0] + ':' + $cv + '\Explorer\StartupApproved\StartupFolder') }
        }
    }
    return $res
}

function Get-GTStartupApproved($Entry) {
    $v = Get-GTRegValue $Entry.ApprovedPath $Entry.Name
    if (-not $v.Existed -or $v.Type -ne 'Binary') { return $null }
    return ,[Convert]::FromBase64String([string]$v.Value)
}

# Desactive une entree (journalise par Set-TweakRegistry ; l'annulation retablit la valeur d'origine). Idempotent.
function Set-TweakStartupDisabled {
    param([Parameter(Mandatory)]$Entry)
    if (Test-GTStartupDisabled (Get-GTStartupApproved $Entry)) { return }
    Set-TweakRegistry -Path $Entry.ApprovedPath -Name $Entry.Name -Type Binary -Value (New-GTStartupDisabledValue)
}

# 'Applied' si au moins une entree existe et que toutes sont desactivees.
function Get-GTStartupState {
    $e = @(Get-GTStartupEntries)
    if ($e.Count -eq 0) { return 'NotApplied' }
    foreach ($x in $e) { if (-not (Test-GTStartupDisabled (Get-GTStartupApproved $x))) { return 'NotApplied' } }
    return 'Applied'
}

# ---- Cles a enumerer ---------------------------------------------------------------------

function ConvertTo-GTHklmPath([string]$RegistryName) {
    # « HKEY_LOCAL_MACHINE\x » (Name d'une cle) -> « HKLM:\x »
    return ('HKLM:' + $RegistryName.Substring('HKEY_LOCAL_MACHINE'.Length))
}

# Pour chaque carte graphique PCI : cle MessageSignaledInterruptProperties de ses parametres d'interruption.
function Get-GTGpuMsiKeys {
    $res = @()
    $root = 'HKLM:\SYSTEM\CurrentControlSet\Enum\PCI'
    foreach ($dev in @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue)) {
        foreach ($inst in @(Get-ChildItem -LiteralPath $dev.PSPath -ErrorAction SilentlyContinue)) {
            $p = Get-ItemProperty -LiteralPath $inst.PSPath -ErrorAction SilentlyContinue
            if ($p -and ([string]$p.ClassGUID -ieq '{4d36e968-e325-11ce-bfc1-08002be10318}')) {
                $res += ((ConvertTo-GTHklmPath $inst.Name) + '\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties')
            }
        }
    }
    return $res
}

# Une cle par interface reseau IP.
function Get-GTTcpInterfaceKeys {
    $root = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces'
    return @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue | ForEach-Object { ConvertTo-GTHklmPath $_.Name })
}
