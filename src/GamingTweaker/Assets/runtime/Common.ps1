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

# Utilisable depuis un script d'application : joint une note au succes de l'option (##GT|ok|id|note), affichee dans le rapport.
function Set-GTItemNote([string]$Note) {
    $script:GT_ItemNote = $Note
}

# Execute une option : start / ok / fail / skip. $Apply s'execute dans sa propre portee.
function Invoke-GTItem([string]$Id, [scriptblock]$Apply) {
    $script:GT_CurrentItem = $Id
    $script:GT_ItemNote = $null
    Write-GTEvent 'start' $Id
    try {
        & $Apply
        if ([string]::IsNullOrWhiteSpace([string]$script:GT_ItemNote)) { Write-GTEvent 'ok' $Id }
        else { Write-GTEvent 'ok' $Id ([string]$script:GT_ItemNote) }
    } catch {
        $msg = [string]$_.Exception.Message
        if ($msg.StartsWith('GTSKIP:')) { Write-GTEvent 'skip' $Id $msg.Substring(7) }
        else { Write-GTEvent 'fail' $Id $msg }
    }
}

# ---------------------------------------------------------------------------
# Journal d'annulation (journal.jsonl, une ligne JSON compacte par entree) et fonctions Set-Tweak*.
# Chaque fonction ecrit puis vide son entree sur disque AVANT de modifier : si l'ecriture echoue, elle leve
# une erreur et ne change rien. Kinds : registry, service, task, power, bcd, hosts, app, quarantine, regkey, regkeynew, nvdrs.
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

# ---- Cles de registre entieres ------------------------------------------------------------

function ConvertTo-GTNativeKey([string]$Path) {
    # « HKCU:\x » -> « HKEY_CURRENT_USER\x » (format de reg.exe et de [Registry]::SetValue).
    if ($Path -match '^HKCU:\\?(.*)$') { return ('HKEY_CURRENT_USER\' + $Matches[1]) }
    if ($Path -match '^HKLM:\\?(.*)$') { return ('HKEY_LOCAL_MACHINE\' + $Matches[1]) }
    throw ('Chemin de registre non pris en charge : ' + $Path)
}

# reg.exe ecrit parfois son message de reussite sur la sortie d'erreur : sans ErrorActionPreference = Continue, 2>&1 leverait une erreur.
function Invoke-GTRegExe([string[]]$Arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $o = & reg.exe @Arguments 2>&1 } finally { $ErrorActionPreference = $old }
    return $o
}

function Export-GTRegistryKey([string]$Path, [string]$File) {
    $o = Invoke-GTRegExe @('export', (ConvertTo-GTNativeKey $Path), $File, '/y')
    if (($LASTEXITCODE -ne 0) -or -not (Test-Path -LiteralPath $File)) { throw ('Export de la clé ' + $Path + ' impossible : ' + ($o -join ' ')) }
}

function Import-GTRegistryFile([string]$File) {
    $o = Invoke-GTRegExe @('import', $File)
    if ($LASTEXITCODE -ne 0) { throw ('Import de ' + $File + ' impossible : ' + ($o -join ' ')) }
}

function Remove-GTRegistryKey([string]$Path) {
    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
}

# Vrai si la cle existe et possede une valeur par defaut.
function Test-GTRegistryKeyDefault([string]$Path) {
    $k = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if (-not $k) { return $false }
    return [bool]($k.GetValueNames() -contains '')
}

# Supprime une cle entiere apres l'avoir exportee (.reg) dans le dossier de la session ; l'annulation la reimporte.
# Renvoie $true si la cle existait (et a ete supprimee), $false sinon (rien n'est journalise).
function Remove-TweakRegistryKey {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    if ([string]::IsNullOrEmpty($GT_JournalPath)) { throw 'Journal d''annulation non configuré.' }
    $dir = Split-Path -Parent $GT_JournalPath
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $file = Join-Path $dir ('regkey-' + [Guid]::NewGuid().ToString('N') + '.reg')
    Export-GTRegistryKey $Path $file
    try { Write-GTJournal 'regkey' $Path $true $file $null }
    catch { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue; throw }
    Remove-GTRegistryKey $Path
    return $true
}

# Cree une cle (et ses parents) avec une valeur par defaut vide. L'annulation retire la cle la plus haute creee,
# ou seulement la valeur par defaut si la cle existait deja sans elle.
# Renvoie $true si quelque chose a ete cree, $false si la cle avait deja sa valeur par defaut.
function Add-TweakRegistryKey {
    param([Parameter(Mandatory)][string]$Path)
    if (Test-GTRegistryKeyDefault $Path) { return $false }
    $existed = Test-Path -LiteralPath $Path
    $target = $Path
    if (-not $existed) {
        # Plus haut ancetre absent : c'est lui qu'il faudra supprimer pour revenir a l'etat d'origine.
        $cur = $Path
        while ($true) {
            $parent = Split-Path -Parent $cur
            if ((-not $parent) -or ($parent -match '^HK(CU|LM):\\?$') -or (Test-Path -LiteralPath $parent)) { $target = $cur; break }
            $cur = $parent
        }
    }
    Write-GTJournal 'regkeynew' $target $existed $null $Path
    [Microsoft.Win32.Registry]::SetValue((ConvertTo-GTNativeKey $Path), '', '', [Microsoft.Win32.RegistryValueKind]::String)
    return $true
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
    Clear-GTTaskCache
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
    # /qh : inclut les reglages caches (ex. CPMINCORES), que /query n'affiche pas.
    $o = & powercfg.exe /qh $Scheme $Sub $Setting
    if ($LASTEXITCODE -ne 0) { throw ('powercfg /qh a échoué pour ' + $Sub + ' ' + $Setting) }
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

# Valeur lue ($null si absente) : repli sur HibernateEnabledDefault, puis consideree activee (l'application se poursuit).
function Resolve-GTHibernateEnabled($Enabled, $Default) {
    if ($null -ne $Enabled) { return (ConvertTo-GTHibernateEnabled $Enabled) }
    if ($null -ne $Default) { return (ConvertTo-GTHibernateEnabled $Default) }
    return $true
}

# Lu dans le registre (HibernateEnabled) : independant de la langue de « powercfg /a ». Ne leve jamais d'erreur.
function Get-GTHibernateEnabled {
    $p = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -ErrorAction SilentlyContinue
    if (-not $p) { return $true }
    return (Resolve-GTHibernateEnabled $p.HibernateEnabled $p.HibernateEnabledDefault)
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

# Enumeration WMI mise en cache par processus ; videe apres chaque modification (Set-GTDevicePower).
function Get-GTPowerDevices {
    if ($null -eq $script:GT_PowerDevicesCache) {
        $script:GT_PowerDevicesCache = @(Get-CimInstance -Namespace 'root\wmi' -ClassName 'MSPower_DeviceEnable' -ErrorAction Stop |
            ForEach-Object { [pscustomobject]@{ InstanceName = [string]$_.InstanceName; Enable = [bool]$_.Enable } })
    }
    return $script:GT_PowerDevicesCache
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
    $script:GT_PowerDevicesCache = $null
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
        try { Set-TweakDevicePower -InstanceName $t -Enabled $Enabled }
        catch {
            # Peripherique debranche en cours d'execution : ignore (le marqueur de skip ne doit pas devenir un echec).
            if (-not ([string]$_.Exception.Message).StartsWith('GTSKIP:')) { $errors += ($t + ' : ' + $_.Exception.Message) }
        }
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

# Paquets jamais retires, meme si un script le demande : Store, WebView2, superpositions Xbox, winget, bibliotheques, panneaux GPU/audio.
function Test-GTProtectedApp([string]$Name) {
    if ([string]::IsNullOrEmpty($Name)) { return $true }
    if ($Name -match 'WebView') { return $true }
    return [bool]($Name -match '(?i)^(Microsoft\.WindowsStore|Microsoft\.StorePurchaseApp|Microsoft\.Services\.Store|Microsoft\.DesktopAppInstaller|Microsoft\.Winget|Microsoft\.VCLibs|Microsoft\.UI\.Xaml|Microsoft\.NET\.Native|Microsoft\.WindowsAppRuntime|Microsoft\.WinAppRuntime|MicrosoftCorporationII\.WinAppRuntime|Microsoft\.Xbox(GamingOverlay|IdentityProvider|GameCallableUI|SpeechToTextOverlay|GameOverlay)|Microsoft\.Xbox\.TCUI|Microsoft\.GamingServices|Microsoft\.MicrosoftEdge|NVIDIACorp\.|AdvancedMicroDevicesInc|RealtekSemiconductorCorp|Realtek|AppUp\.Intel)')
}

# -StoreId : identifiant Microsoft Store, ecrit dans le journal (champ after) pour la reinstallation a l'annulation.
function Remove-TweakApp {
    param([Parameter(Mandatory)][string]$Package, [string]$StoreId)
    if (Test-GTProtectedApp $Package) { throw ('Application protégée, jamais retirée : ' + $Package) }
    $pk = @(Get-AppxPackage -Name $Package -AllUsers -ErrorAction SilentlyContinue)
    $prov = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq $Package })
    if ($pk.Count -eq 0 -and $prov.Count -eq 0) { Skip-GTItem ('Application ' + $Package + ' absente de cette machine.') }
    $after = $null; if (-not [string]::IsNullOrWhiteSpace($StoreId)) { $after = $StoreId }
    foreach ($p in $pk) {
        Write-GTJournal 'app' $p.PackageFullName $true $p.PackageFullName $after
        Remove-AppxPackage -Package $p.PackageFullName -AllUsers
    }
    foreach ($q in $prov) {
        # Image provisionnee : retiree pour que l'application ne revienne pas aux nouveaux comptes.
        if ($pk.Count -eq 0) { Write-GTJournal 'app' $q.PackageName $true $q.PackageName $after }
        Remove-AppxProvisionedPackage -Online -PackageName $q.PackageName | Out-Null
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
        'service'    { Set-GTServiceStart $E.target ([string]$E.before) }
        'task' {
            $p = Split-GTTaskPath $E.target
            if ([bool]$E.before) { Enable-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name | Out-Null }
            else { Disable-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name | Out-Null }
            Clear-GTTaskCache
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
        'nvdrs' {
            $id = ConvertFrom-GTNvId ([string]$E.target)
            Initialize-GTNvDrs
            if ($E.existed) { [GTNative.NvDrs]::Ecrire($id, [uint32][long]$E.before) } else { [GTNative.NvDrs]::Retirer($id) }
        }
        'quarantine' {
            if ((Test-Path -LiteralPath $E.before) -and -not (Test-Path -LiteralPath $E.after)) { return }  # deja restaure
            if (Test-Path -LiteralPath $E.before) { throw ('Impossible de restaurer ' + $E.before + ' : un fichier existe déjà à cet emplacement.') }
            $dir = Split-Path -Parent $E.before
            if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
            Move-Item -LiteralPath $E.after -Destination $E.before
        }
        'regkey' {
            # Cle supprimee : ce qui a pu etre recree depuis est remplace par la sauvegarde exportee.
            if (-not (Test-Path -LiteralPath $E.before)) { throw ('Sauvegarde du registre introuvable : ' + $E.before) }
            if (Test-Path -LiteralPath $E.target) { Remove-GTRegistryKey $E.target }
            Import-GTRegistryFile $E.before
        }
        'regkeynew' {
            if ($E.existed) {
                # La cle existait sans valeur par defaut : seule celle-ci est retiree.
                if (Test-GTRegistryKeyDefault $E.after) {
                    $hive = if ($E.after -match '^HKLM:') { [Microsoft.Win32.Registry]::LocalMachine } else { [Microsoft.Win32.Registry]::CurrentUser }
                    $sk = $hive.OpenSubKey(($E.after -replace '^HK(CU|LM):\\?', ''), $true)
                    try { $sk.DeleteValue('', $false) } finally { $sk.Dispose() }
                }
            }
            elseif (Test-Path -LiteralPath $E.target) { Remove-GTRegistryKey $E.target }
        }
        'app' { }
        'newfile' {
            # Fichier cree par l'application : supprime ; son dossier aussi s'il a ete cree par elle et qu'il est vide.
            if (Test-Path -LiteralPath $E.target) { Remove-Item -LiteralPath $E.target -Force -ErrorAction Stop }
            if ([bool]$E.before) {
                $d = Split-Path -Parent $E.target
                if ((Test-Path -LiteralPath $d) -and @(Get-ChildItem -LiteralPath $d -Force -ErrorAction SilentlyContinue).Count -eq 0) { Remove-Item -LiteralPath $d -Force -ErrorAction Stop }
            }
        }
        'wingetapp' {
            if (-not (Install-GTWingetPackage ([string]$E.target) ([string]$E.before))) { throw ('winget est introuvable : réinstallez ' + [string]$E.target + ' à la main (winget install ' + [string]$E.target + ').') }
        }
        'dismfeature' {
            if ((Get-GTOptionalFeatureState ([string]$E.target)) -ne 'Enabled') { Invoke-GTDism -Arguments @('/Online', '/Enable-Feature', ('/FeatureName:' + $E.target), '/NoRestart') | Out-Null }
        }
        'dismcapability' {
            if (@(Get-GTCapabilityInstalled (Get-GTCapabilityBaseName ([string]$E.target))).Count -eq 0) { Invoke-GTDism -Arguments @('/Online', '/Add-Capability', ('/CapabilityName:' + $E.target)) | Out-Null }
        }
        default { throw ('Type d''entrée de journal inconnu : ' + $E.kind) }
    }
}

# Restaure une option : ses entrees posterieures au dernier marqueur « undone », en ordre inverse.
# Un marqueur « undone » avec une cible (fichier restaure ou supprime depuis la quarantaine) n'ecarte que les entrees
# anterieures de cette cible. Une entree en echec n'empeche pas les suivantes ; l'option echoue a la fin (sans marqueur, donc rejouable).
function Restore-GTItem([Parameter(Mandatory)][string]$Item) {
    $all = @(Read-GTJournal | Where-Object { $_.item -eq $Item })
    if ($all.Count -eq 0) { Skip-GTItem 'Rien à annuler : aucune entrée de journal pour cette option.' }
    $lastMarker = -1
    for ($i = 0; $i -lt $all.Count; $i++) { if ($all[$i].kind -eq 'undone' -and [string]::IsNullOrEmpty([string]$all[$i].target)) { $lastMarker = $i } }
    $entries = @()
    for ($i = $lastMarker + 1; $i -lt $all.Count; $i++) {
        $e = $all[$i]
        if ($e.kind -eq 'undone') { continue }
        $cible = [string]$e.target
        $traitee = $false
        if (-not [string]::IsNullOrEmpty($cible)) {
            for ($k = $i + 1; $k -lt $all.Count; $k++) {
                if ($all[$k].kind -eq 'undone' -and [string]$all[$k].target -eq $cible) { $traitee = $true; break }
            }
        }
        if (-not $traitee) { $entries += ,$e }
    }
    if ($entries.Count -eq 0) { Skip-GTItem 'Option déjà annulée.' }
    [array]::Reverse($entries)
    $errors = @(); $manual = @()
    $storeDone = @()
    foreach ($e in $entries) {
        if ($e.kind -eq 'app') {
            # Reinstallation depuis le Store par winget si l'identifiant est connu, sinon message pour la faire a la main.
            $short = ([string]$e.target -split '_')[0]
            $sid = [string]$e.after
            if ([string]::IsNullOrWhiteSpace($sid)) { $manual += $short; continue }
            if ($storeDone -contains $sid) { continue }
            $storeDone += $sid
            try { if (-not (Install-GTWingetPackage $sid 'msstore')) { $manual += $short } }
            catch { $errors += ('app ' + $short + ' : ' + $_.Exception.Message) }
            continue
        }
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
# Les taches sont enumerees une seule fois par processus (une vingtaine de lectures a la detection) ; le cache est vide apres chaque
# modification (Set-TweakTask, Reset-GTTweaks). Si l'enumeration globale echoue, lecture tache par tache comme avant.
function Get-GTTaskEnabled([string]$Path) {
    $p = Split-GTTaskPath $Path
    if ($null -eq $script:GT_TaskCache) {
        $cache = @{}
        try {
            foreach ($t in @(Get-ScheduledTask -ErrorAction Stop)) { $cache[([string]$t.TaskPath + [string]$t.TaskName)] = [bool]($t.State -ne 'Disabled') }
            $script:GT_TaskCache = $cache
        } catch { $script:GT_TaskCache = $null }
    }
    if ($null -ne $script:GT_TaskCache) {
        $k = $p.Dir + $p.Name
        if ($script:GT_TaskCache.ContainsKey($k)) { return $script:GT_TaskCache[$k] }
        return $null
    }
    $t = Get-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name -ErrorAction SilentlyContinue
    if (-not $t) { return $null }
    return [bool]($t.State -ne 'Disabled')
}

function Clear-GTTaskCache { $script:GT_TaskCache = $null }

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
        if ($null -ne $cur -and -not $cur) { $p = Split-GTTaskPath $t; Enable-ScheduledTask -TaskPath $p.Dir -TaskName $p.Name | Out-Null; Clear-GTTaskCache }
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

# Entrees de securite jamais proposees ni desactivees (meme regle que StartupSecurity cote C#).
function Test-GTStartupSecurity([string]$Name) {
    return [bool]($Name -match '^(SecurityHealth\w*|MSASCui\w*|Windows ?Defender\w*|WindowsSecurity\w*|Sophos\b.*|Avast\b.*|AVG\b.*|Bitdefender\b.*|Kaspersky\b.*|Norton\b.*|McAfee\b.*|ESET\b.*|Malwarebytes\b.*)$')
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
            if ($n -and -not (Test-GTStartupSecurity $n)) { $res += [pscustomobject]@{ Key = (Get-GTStartupKey $s[0] $s[1] $n); Scope = $s[0]; Kind = $s[1]; Name = $n; ApprovedPath = ($s[0] + ':' + $cv + '\Explorer\StartupApproved\' + $s[1]) } }
        }
    }
    foreach ($f in @(@('HKCU', [Environment]::GetFolderPath('Startup')), @('HKLM', [Environment]::GetFolderPath('CommonStartup')))) {
        if (-not $f[1] -or -not (Test-Path -LiteralPath $f[1])) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $f[1] -File -Force -ErrorAction SilentlyContinue)) {
            if (($file.Name -ieq 'desktop.ini') -or (Test-GTStartupSecurity $file.Name)) { continue }
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

# ---------------------------------------------------------------------------
# Aides du menu Nettoyage.
# Remove-TweakPath ne supprime que dans $AllowedRoots (chemins resolus, sans tenir compte de la casse, avec separateur : « C:\a » n'autorise pas « C:\ab »).
# Il ne suit jamais une jonction ni un lien symbolique, ignore les fichiers verrouilles (en les comptant), ne touche pas aux donnees personnelles
# (dossiers utilisateur, fichiers de profil des navigateurs) et conserve le dossier racine sauf -RemoveRoot. Aucune suppression n'est journalisee :
# les options de nettoyage sont definitives (sauf les orphelins Windows Installer, deplaces en quarantaine, et le registre, exporte).
# ---------------------------------------------------------------------------

function Format-GTSize([long]$Bytes) {
    if ($Bytes -lt 1024) { return ([string]$Bytes + ' o') }
    $fr = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')
    $v = [double]$Bytes; $u = @('Ko', 'Mo', 'Go', 'To'); $i = -1
    while ($v -ge 1024 -and $i -lt 3) { $v = $v / 1024; $i++ }
    return ($v.ToString('0.0', $fr) + ' ' + $u[$i])
}

# Noms (a n'importe quel niveau du chemin) qui ne sont jamais supprimes, meme dans un dossier autorise.
$script:GT_ProtectedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($gtName in @('Downloads', 'Documents', 'Desktop', 'Pictures', 'Videos', 'Music', 'Saved Games', 'Favorites', 'Contacts',
                      'Login Data', 'Login Data For Account', 'Local State', 'History', 'Cookies', 'Bookmarks', 'Web Data')) { [void]$script:GT_ProtectedSet.Add($gtName) }

function Test-GTProtectedName([string]$Name) {
    if ([string]::IsNullOrEmpty($Name)) { return $false }
    if ($script:GT_ProtectedSet.Contains($Name)) { return $true }
    if ($Name.StartsWith('OneDrive', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    $i = $Name.IndexOf('-')    # « History-journal », « Login Data-journal »
    return ($i -gt 0 -and $script:GT_ProtectedSet.Contains($Name.Substring(0, $i)))
}

function Test-GTProtectedPath([string]$FullPath) {
    foreach ($seg in $FullPath.Split('\')) { if (Test-GTProtectedName $seg) { return $true } }
    return $false
}

function Get-GTFullPath([string]$Path) {
    $f = [IO.Path]::GetFullPath($Path)
    if ($f.Length -gt 3) { $f = $f.TrimEnd('\') }
    return $f
}

# Une racine autorisee peut contenir « * » dans un segment (« Profile * », « webcache* ») : ce segment se compare par motif, les autres a l'identique.
function Test-GTUnderRoot([string]$Full, [string]$Root) {
    if ($Root.IndexOf('*') -lt 0) { return ($Full -ieq $Root -or $Full.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase)) }
    $f = $Full.Split('\'); $r = $Root.Split('\')
    if ($f.Count -lt $r.Count) { return $false }
    for ($i = 0; $i -lt $r.Count; $i++) {
        if ($r[$i].IndexOf('*') -ge 0) { if ($f[$i] -notlike $r[$i]) { return $false } }
        elseif ($f[$i] -ine $r[$i]) { return $false }
    }
    return $true
}

function Test-GTPathAllowed([string]$Path) {
    try { $full = Get-GTFullPath $Path } catch { return $false }
    if (Test-GTProtectedPath $full) { return $false }
    foreach ($r in @($AllowedRoots)) {
        if ([string]::IsNullOrWhiteSpace($r)) { continue }
        # GetFullPath refuse « * » : une racine a motif est deja absolue (construite par Join-Path), on la prend telle quelle.
        if ($r.IndexOf('*') -ge 0) { $root = $r.TrimEnd([char]92) } else { try { $root = Get-GTFullPath $r } catch { continue } }
        if ($root.Length -le 3) { continue }   # jamais une racine de disque
        if (Test-GTUnderRoot $full $root) { return $true }
    }
    return $false
}

# Dossier de Steam (registre, repli sur Program Files (x86)) ; $null s'il est introuvable.
function Get-GTSteamDir {
    $p = $null
    try { $p = [string](Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -Name 'SteamPath' -ErrorAction Stop).SteamPath } catch { }
    if (-not $p) { $pf = ${env:ProgramFiles(x86)}; if ($pf) { $p = Join-Path $pf 'Steam' } }
    if (-not $p) { return $null }
    try { return (Get-GTFullPath $p) } catch { return $null }
}

# Dossier temporaire de l'utilisateur du bureau. $env:TEMP du processus est le dossier Run de l'outil (environnement reconstruit par
# PowerShellLanceur) : il ne doit jamais servir au nettoyage. GT_USERTEMP vient de ProfileList, sinon LOCALAPPDATA\Temp ; $null si inconnu.
function Get-GTUserTemp {
    if ($env:GT_USERTEMP) { return [string]$env:GT_USERTEMP }
    if ($env:LOCALAPPDATA) { return (Join-Path $env:LOCALAPPDATA 'Temp') }
    return $null
}

function Get-GTDefaultAllowedRoots {
    $roots = New-Object System.Collections.Generic.List[string]
    $sr = [string]$env:SystemRoot; $la = [string]$env:LOCALAPPDATA; $ap = [string]$env:APPDATA; $pd = [string]$env:ProgramData; $up = [string]$env:USERPROFILE
    # Dossier temporaire deregle (profil, racine d'un disque) : ignore. Un dossier temporaire s'appelle Temp ou Tmp (ou en est un sous-dossier : Temp\1).
    $tmp = [string](Get-GTUserTemp)
    if ($tmp) {
        try {
            $t = Get-GTFullPath $tmp
            $leaf = Split-Path -Leaf $t
            $parent = Split-Path -Leaf (Split-Path -Parent $t)
            if ($t.Length -gt 3 -and ($t -ine $up) -and (($leaf -match '^(?i:temp|tmp)$') -or ($parent -match '^(?i:temp|tmp)$'))) { $roots.Add($t) }
        } catch { }
    }
    if ($sr) {
        foreach ($s in @('Temp', 'SoftwareDistribution\Download', 'ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache',
                         'Minidump', 'MEMORY.DMP', 'Logs\CBS', 'Logs\DISM', 'Logs\WindowsUpdate', 'Traces\WindowsUpdate', 'Prefetch')) { $roots.Add((Join-Path $sr $s)) }
    }
    $sd = [string]$env:SystemDrive
    if ($sd) { $roots.Add($sd + '\Windows.old') }
    if ($pd) {
        foreach ($s in @('Microsoft\Windows\WER', 'Microsoft\VSApplicationInsights', 'vstelemetry')) { $roots.Add((Join-Path $pd $s)) }
    }
    if ($la) {
        # Uniquement les dossiers de cache reellement nettoyes (« * » : un segment quelconque, voir Test-GTUnderRoot).
        foreach ($s in @('Microsoft\Windows\Explorer', 'CrashDumps', 'D3DSCache', 'NVIDIA\DXCache', 'NVIDIA\GLCache', 'AMD\DxCache',
                         'Microsoft\Windows\INetCache\IE', 'EpicGamesLauncher\Saved\webcache*', 'Microsoft\VSCommon\*\SQM', 'Microsoft\VSApplicationInsights')) { $roots.Add((Join-Path $la $s)) }
        foreach ($b in @('Google\Chrome\User Data', 'Microsoft\Edge\User Data')) {
            foreach ($prof in @('Default', 'Profile *')) {
                foreach ($c in @('Cache', 'Code Cache', 'GPUCache')) { $roots.Add((Join-Path $la ($b + '\' + $prof + '\' + $c))) }
            }
            $roots.Add((Join-Path $la ($b + '\Crashpad\reports')))
        }
        $roots.Add((Join-Path $la 'Mozilla\Firefox\Profiles\*\cache2'))
    }
    if ($ap) {
        foreach ($s in @('Microsoft\Windows\Recent', 'Sun\Java\Deployment\cache', 'vstelemetry')) { $roots.Add((Join-Path $ap $s)) }
    }
    if ($up) { $roots.Add((Join-Path $up '.dotnet\TelemetryStorageService')) }
    $steam = Get-GTSteamDir
    if ($steam) { foreach ($s in @('appcache\httpcache', 'dumps')) { $roots.Add((Join-Path $steam $s)) } }
    return $roots.ToArray()
}

$AllowedRoots = @(Get-GTDefaultAllowedRoots)

# Cumul par option du resultat des nettoyages (la note affichee dans le rapport).
function Update-GTCleanNote($Stats, [string]$Detail) {
    if ($script:GT_CleanItem -ne $script:GT_CurrentItem -or $null -eq $script:GT_CleanTotals) {
        $script:GT_CleanItem = $script:GT_CurrentItem
        $script:GT_CleanTotals = @{ Files = 0; Bytes = [long]0; Locked = 0; Links = 0 }
        $script:GT_CleanDetails = @()
    }
    $t = $script:GT_CleanTotals
    if ($Stats) { $t.Files += $Stats.Files; $t.Bytes += [long]$Stats.Bytes; $t.Locked += $Stats.Locked; $t.Links += $Stats.Links }
    if ($Detail) { $script:GT_CleanDetails += $Detail }
    $parts = @()
    if ($t.Files -gt 0) { $parts += ((Format-GTSize $t.Bytes) + ' libérés (' + $t.Files + ' fichier' + $(if ($t.Files -gt 1) { 's' } else { '' }) + ')') }
    $parts += @($script:GT_CleanDetails)
    if ($parts.Count -eq 0 -and $t.Locked -eq 0) { $parts += 'Rien à supprimer' }
    if ($t.Locked -gt 0) { $parts += ([string]$t.Locked + $(if ($t.Locked -gt 1) { ' fichiers verrouillés ignorés' } else { ' fichier verrouillé ignoré' })) }
    if ($t.Links -gt 0) { $parts += ([string]$t.Links + $(if ($t.Links -gt 1) { ' liens ou jonctions non suivis' } else { ' lien ou jonction non suivi' })) }
    Set-GTItemNote ($parts -join ' ; ')
}

function Add-GTCleanDetail([string]$Detail) { Update-GTCleanNote $null $Detail }

# Suppression par poignee (outil eleve dans des dossiers ou l'utilisateur peut ecrire : Temp, caches) :
# l'element est ouvert sans suivre un lien final (FILE_FLAG_OPEN_REPARSE_POINT), son chemin reel est lu sur la poignee
# (GetFinalPathNameByHandle) et doit rester sous le chemin reel de la racine, puis il est supprime par cette meme poignee
# (FileDispositionInfo). Un dossier parent remplace par une jonction, ou un lien du gestionnaire d'objets, entre l'enumeration
# et la suppression, fait sortir le chemin reel de la racine : rien n'est supprime.
$script:GT_SafeFsSource = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GTNative
{
    public static class SafeFs
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder buf, uint len, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathNameW(string shortPath, StringBuilder buf, uint len);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle h, out ByHandleInfo info);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle h, int cls, ref BasicInfo info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle h, int cls, ref DispositionInfo info, uint size);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleInfo
        {
            public uint Attributes, C1, C2, A1, A2, W1, W2, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicInfo
        {
            public long Creation, LastAccess, LastWrite, Change;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DispositionInfo
        {
            public byte Delete;
        }

        private const uint DeleteAccess = 0x00010000, ReadAttributes = 0x80, WriteAttributes = 0x100;
        private const uint ShareAll = 7, OpenExisting = 3, OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
        private const uint AttrReadOnly = 0x1, AttrDirectory = 0x10, AttrNormal = 0x80, AttrReparse = 0x400;

        private static string Prefixe(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path;
            return @"\\?\" + path;
        }

        private static string SansPrefixe(string path)
        {
            if (path == null) return null;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return path;
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path.Substring(4);
            return path;
        }

        private static SafeFileHandle Ouvrir(string path, uint access)
        {
            return CreateFileW(Prefixe(path), access, ShareAll, IntPtr.Zero, OpenExisting, OpenReparsePoint | BackupSemantics, IntPtr.Zero);
        }

        private static string CheminFinal(SafeFileHandle h)
        {
            var sb = new StringBuilder(1024);
            uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
            if (n == 0) return null;
            if (n >= sb.Capacity)
            {
                sb = new StringBuilder((int)n + 1);
                n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                if (n == 0 || n >= sb.Capacity) return null;
            }
            return SansPrefixe(sb.ToString()).TrimEnd('\\');
        }

        /// <summary>Chemin long (sans noms courts 8.3) ; le chemin tel quel si l'API echoue.</summary>
        public static string CheminLong(string path)
        {
            var sb = new StringBuilder(1024);
            uint n = GetLongPathNameW(Prefixe(path), sb, (uint)sb.Capacity);
            if (n == 0 || n >= sb.Capacity) return path.TrimEnd('\\');
            return SansPrefixe(sb.ToString()).TrimEnd('\\');
        }

        public static bool EstSous(string full, string root)
        {
            if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(root)) return false;
            return full.Length > root.Length + 1 && full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Chemin reel d'un dossier ordinaire (ni jonction ni lien), ou null. Avec attendu : null aussi si le chemin reel differe
        /// du chemin attendu (un dossier parent redirige par un lien). Attendu vide : PowerShell passe $null comme chaine vide.
        /// </summary>
        public static string DossierReel(string dir, string attendu)
        {
            using (var h = Ouvrir(dir, ReadAttributes))
            {
                if (h.IsInvalid) return null;
                ByHandleInfo i;
                if (!GetFileInformationByHandle(h, out i)) return null;
                if ((i.Attributes & AttrReparse) != 0 || (i.Attributes & AttrDirectory) == 0) return null;
                var reel = CheminFinal(h);
                if (reel == null) return null;
                if (!string.IsNullOrEmpty(attendu) && !string.Equals(reel, CheminLong(attendu), StringComparison.OrdinalIgnoreCase)) return null;
                return reel;
            }
        }

        /// <summary>
        /// Supprime un element sous la racine reelle. 0 : supprime ; 1 : echec (verrouille, acces refuse, dossier non vide) ;
        /// 2 : chemin reel hors de la racine (lien ou redirection), rien n'est supprime ; 3 : type inattendu, rien n'est supprime.
        /// lien : l'element attendu est une jonction ou un lien symbolique (seul le lien est retire).
        /// </summary>
        public static int Supprimer(string path, string racineReelle, bool dossier, bool lien)
        {
            if (string.IsNullOrEmpty(racineReelle)) return 2;
            var h = Ouvrir(path, DeleteAccess | ReadAttributes | WriteAttributes);
            if (h.IsInvalid) { h.Dispose(); h = Ouvrir(path, DeleteAccess | ReadAttributes); }
            using (h)
            {
                if (h.IsInvalid) return 1;
                ByHandleInfo i;
                if (!GetFileInformationByHandle(h, out i)) return 1;
                bool estLien = (i.Attributes & AttrReparse) != 0, estDossier = (i.Attributes & AttrDirectory) != 0;
                if (estLien != lien) return 3;
                if (!lien && estDossier != dossier) return 3;
                if (!EstSous(CheminFinal(h), racineReelle)) return 2;
                if ((i.Attributes & AttrReadOnly) != 0)
                {
                    uint a = i.Attributes & ~AttrReadOnly;
                    var b = new BasicInfo { Attributes = a == 0 ? AttrNormal : a };
                    SetFileInformationByHandle(h, 0, ref b, (uint)Marshal.SizeOf(typeof(BasicInfo)));
                }
                var d = new DispositionInfo { Delete = 1 };
                return SetFileInformationByHandle(h, 4, ref d, (uint)Marshal.SizeOf(typeof(DispositionInfo))) ? 0 : 1;
            }
        }
    }
}
'@

function Initialize-GTSafeFs {
    if ('GTNative.SafeFs' -as [type]) { return }
    Add-Type -TypeDefinition $script:GT_SafeFsSource -Language CSharp
}

# Vrai si l'element est (ou ne peut pas etre prouve ne pas etre) une jonction ou un lien symbolique.
function Test-GTReparse([string]$Path) {
    try { return [bool]([IO.File]::GetAttributes($Path) -band [IO.FileAttributes]::ReparsePoint) } catch { return $true }
}

# Propriete et droits aux administrateurs, sur cet element seulement (jamais /r ni /t ; /L : l'element lui-meme) et jamais sur un lien.
function Grant-GTOwnership([string]$Path) {
    if (Test-GTReparse $Path) { return }
    $null = Invoke-GTNative 'takeown.exe' @('/f', $Path, '/a')
    $null = Invoke-GTNative 'icacls.exe' @($Path, '/grant', '*S-1-5-32-544:F', '/c', '/q', '/L')
}

# Parcours commun a la suppression et a l'estimation. $Stats : table (Files, Bytes, Locked, Links, Protected).
# $Own : prise de possession element par element, seulement quand un acces est refuse ($GT_OwnAlways : test, la fait avant chaque element).
# $GT_WalkHook : test, appele avant de descendre dans un sous-dossier.
# $Real : chemin reel de la racine (suppression uniquement) ; chaque suppression est verifiee contre lui (GTNative.SafeFs).
function Invoke-GTWalk([string]$Dir, [string]$Filter, [bool]$Recurse, [bool]$Delete, [bool]$RemoveLinks, $Stats, $Exclude, [bool]$Own = $false, [string]$Real = $null) {
    if ($Own -and $GT_OwnAlways) { Grant-GTOwnership $Dir }
    $entries = $null
    try { $entries = (New-Object IO.DirectoryInfo($Dir)).GetFileSystemInfos() } catch {
        if ($Own) { Grant-GTOwnership $Dir; try { $entries = (New-Object IO.DirectoryInfo($Dir)).GetFileSystemInfos() } catch { } }
    }
    if ($null -eq $entries) { $Stats.Locked++; return }
    foreach ($e in $entries) {
        $full = $e.FullName
        if ($null -ne $Exclude -and $Exclude.Contains($full)) { continue }
        if (Test-GTProtectedName $e.Name) { $Stats.Protected++; continue }
        $attr = $e.Attributes
        if ($attr -band [IO.FileAttributes]::Directory) {
            if (-not ($attr -band [IO.FileAttributes]::ReparsePoint)) {
                if (-not $Recurse) { continue }
                if ($GT_WalkHook) { & $GT_WalkHook $full }
            }
            # Verification juste avant de descendre : le dossier a pu devenir un lien depuis l'enumeration.
            if ((Test-GTReparse $full) -or ($attr -band [IO.FileAttributes]::ReparsePoint)) {
                # Jonction ou lien symbolique : jamais suivi. Seul le lien est retire, et seulement sur demande.
                $Stats.Links++
                if ($Delete -and $RemoveLinks) { $null = [GTNative.SafeFs]::Supprimer($full, $Real, $true, $true) }
                continue
            }
            # Suppression : le dossier doit encore etre, par son chemin reel, sous la racine (sinon il a ete redirige : non suivi).
            if ($Delete -and -not [GTNative.SafeFs]::EstSous([GTNative.SafeFs]::DossierReel($full, $null), $Real)) { $Stats.Links++; continue }
            Invoke-GTWalk $full $Filter $Recurse $Delete $RemoveLinks $Stats $Exclude $Own $Real
            # Un dossier vide est retire (jamais le dossier racine) ; un dossier non vide (fichiers verrouilles) reste.
            if ($Delete -and $Filter -eq '*') {
                if (([GTNative.SafeFs]::Supprimer($full, $Real, $true, $false) -eq 1) -and $Own) {
                    Grant-GTOwnership $full; $null = [GTNative.SafeFs]::Supprimer($full, $Real, $true, $false)
                }
            }
        } else {
            if ($e.Name -notlike $Filter) { continue }
            if (-not $Delete) { $Stats.Files++; $Stats.Bytes += [long]$e.Length; continue }
            $len = [long]$e.Length
            if ($Own -and $GT_OwnAlways) { Grant-GTOwnership $full }
            $done = $false; $code = 1
            $isLink = [bool]($attr -band [IO.FileAttributes]::ReparsePoint)
            for ($try = 0; $try -lt 2 -and -not $done; $try++) {
                $code = [GTNative.SafeFs]::Supprimer($full, $Real, $false, $isLink)
                if ($code -eq 0) { $done = $true }
                elseif ($code -eq 1 -and $Own -and $try -eq 0) { Grant-GTOwnership $full }
                else { break }
            }
            if ($done) { $Stats.Files++; $Stats.Bytes += $len } elseif ($code -ge 2) { $Stats.Links++ } else { $Stats.Locked++ }
        }
    }
}

function New-GTCleanStats { return @{ Files = 0; Bytes = [long]0; Locked = 0; Links = 0; Protected = 0 } }

function ConvertTo-GTExcludeSet([string[]]$ExcludePath) {
    if (-not $ExcludePath -or $ExcludePath.Count -eq 0) { return $null }
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($x in $ExcludePath) { if ($x) { [void]$set.Add((Get-GTFullPath $x)) } }
    return $set
}

# Verifie tous les chemins avant de toucher a quoi que ce soit. Renvoie les chemins resolus.
# Une racine, ou l'un de ses dossiers parents, qui est une jonction ou un lien symbolique est refusee.
function Assert-GTCleanPaths([string[]]$Path) {
    $res = @()
    foreach ($p in $Path) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        if (-not (Test-GTPathAllowed $p)) { throw ('Chemin refusé (hors des dossiers autorisés ou protégé) : ' + $p) }
        $full = Get-GTFullPath $p
        if ([IO.Directory]::Exists($full) -and ([IO.File]::GetAttributes($full) -band [IO.FileAttributes]::ReparsePoint)) {
            throw ('Dossier refusé (jonction ou lien symbolique, non suivi) : ' + $p)
        }
        $anc = Split-Path -Parent $full
        while ($anc -and $anc.Length -gt 3) {
            if ([IO.Directory]::Exists($anc) -and ([IO.File]::GetAttributes($anc) -band [IO.FileAttributes]::ReparsePoint)) {
                throw ('Dossier refusé (un dossier parent est une jonction ou un lien symbolique : ' + $anc + ') : ' + $p)
            }
            $anc = Split-Path -Parent $anc
        }
        $res += $full
    }
    return $res
}

function Remove-TweakPath {
    param([string[]]$Path = @(), [string]$Filter = '*', [switch]$NoRecurse, [switch]$RemoveRoot, [switch]$RemoveLinks,
          [switch]$TakeOwnership, [string[]]$ExcludePath = @())
    $roots = @(Assert-GTCleanPaths $Path)
    if ($TakeOwnership) {
        foreach ($r in $roots) { if ((Split-Path -Leaf $r) -ine 'Windows.old') { throw ('-TakeOwnership est réservé à Windows.old : ' + $r) } }
    }
    $exclude = ConvertTo-GTExcludeSet $ExcludePath
    $stats = New-GTCleanStats
    Initialize-GTSafeFs
    foreach ($root in $roots) {
        if ([IO.File]::Exists($root)) {
            try { [IO.File]::SetAttributes($root, [IO.FileAttributes]::Normal); $len = (New-Object IO.FileInfo($root)).Length; [IO.File]::Delete($root); $stats.Files++; $stats.Bytes += [long]$len }
            catch { $stats.Locked++ }
            continue
        }
        if (-not [IO.Directory]::Exists($root)) { continue }
        if ($TakeOwnership) {
            # Les fichiers de Windows.old appartiennent a TrustedInstaller : propriete et droits repris element par element
            # (racine ici, puis chaque element non lie lors du parcours, quand l'acces est refuse). Jamais recursif : une jonction n'est pas traversee.
            Grant-GTOwnership $root
        }
        # Chemin reel de la racine, relu sur une poignee : la racine ou un dossier parent devenu lien depuis la verification est refuse.
        $real = [GTNative.SafeFs]::DossierReel($root, $root)
        if (-not $real) { throw ('Dossier refusé (jonction, lien symbolique ou dossier parent redirigé) : ' + $root) }
        Invoke-GTWalk $root $Filter (-not $NoRecurse) $true ([bool]$RemoveLinks) $stats $exclude ([bool]$TakeOwnership) $real
        if ($RemoveRoot) {
            try { [IO.Directory]::Delete($root, $false) } catch { Add-GTCleanDetail ('Le dossier ' + $root + ' n''a pas pu être supprimé entièrement') }
        }
    }
    Update-GTCleanNote $stats
    return [pscustomobject]@{ Files = $stats.Files; Bytes = $stats.Bytes; Locked = $stats.Locked; Links = $stats.Links; Protected = $stats.Protected }
}

# Taille (octets) de ce que Remove-TweakPath supprimerait avec les memes parametres. Lecture seule.
function Get-TweakPathSize {
    param([string[]]$Path = @(), [string]$Filter = '*', [switch]$NoRecurse, [string[]]$ExcludePath = @())
    $roots = @(Assert-GTCleanPaths $Path)
    $exclude = ConvertTo-GTExcludeSet $ExcludePath
    $stats = New-GTCleanStats
    foreach ($root in $roots) {
        if ([IO.File]::Exists($root)) { $stats.Bytes += [long](New-Object IO.FileInfo($root)).Length; continue }
        if (-not [IO.Directory]::Exists($root)) { continue }
        Invoke-GTWalk $root $Filter (-not $NoRecurse) $false $false $stats $exclude
    }
    return [long]$stats.Bytes
}

# Lance un programme natif sans que la sortie d'erreur ne leve d'exception. Renvoie ExitCode et Output (lignes).
function Invoke-GTNative([string]$Exe, [string[]]$Arguments = @()) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $o = & $Exe @Arguments 2>&1; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $old }
    return [pscustomobject]@{ ExitCode = $code; Output = @($o | ForEach-Object { [string]$_ }) }
}

# ---- DISM ----------------------------------------------------------------------------------

# $GT_DismPath : remplacement pour les tests. 0 et 3010 (redemarrage conseille) sont des reussites.
function Invoke-GTDism([string[]]$Arguments) {
    $exe = if ($GT_DismPath) { $GT_DismPath } else { Join-Path $env:SystemRoot 'System32\dism.exe' }
    $r = Invoke-GTNative $exe $Arguments
    if ($r.ExitCode -ne 0 -and $r.ExitCode -ne 3010) {
        $tail = @($r.Output | Where-Object { $_.Trim() } | Select-Object -Last 3) -join ' '
        throw ('DISM a échoué (code ' + $r.ExitCode + ') : ' + $tail)
    }
    return [pscustomobject]@{ ExitCode = $r.ExitCode; RebootRequired = ($r.ExitCode -eq 3010); Output = $r.Output }
}

# ---- Orphelins de Windows Installer ------------------------------------------------------------

# Acces COM : fonctions separees pour pouvoir les remplacer dans les tests.
function New-GTInstallerObject { return (New-Object -ComObject WindowsInstaller.Installer) }
function Read-GTInstallerValue($Obj, [string]$Name, $Argv) {
    return $Obj.GetType().InvokeMember($Name, [Reflection.BindingFlags]::GetProperty, $null, $Obj, $Argv)
}
# LocalPackage d'un produit ou correctif. Une lecture qui echoue leve une erreur si $Required (produit installe : le fichier existe forcement),
# sinon la propriete est consideree comme absente (aucun fichier reference).
function Get-GTInstallerPackageProp($Obj, [string]$Name, $Argv, [bool]$Required) {
    try { return [string](Read-GTInstallerValue $Obj $Name $Argv) }
    catch {
        if ($Required) { throw ('lecture de LocalPackage impossible pour un produit installé : ' + $_.Exception.Message) }
        return ''
    }
}

# Chemins (LocalPackage) de tous les .msi et .msp references par Windows Installer : produits et correctifs de tous les contextes
# (par machine, par utilisateur, geres), pour tous les utilisateurs. Leve une erreur si l'enumeration echoue, ou si la lecture du
# LocalPackage d'un produit installe echoue : sans cela, son fichier serait deplace a tort.
function Get-GTInstallerReferencedPackages {
    $inst = New-GTInstallerObject
    $res = New-Object System.Collections.Generic.List[string]
    # s-1-1-0 (tout le monde) et contexte 7 (par utilisateur gere + non gere + machine) : necessite les droits administrateur
    # ($GT_InstallerUserSid : remplacement pour les tests non eleves).
    $sidAll = if ($null -ne $GT_InstallerUserSid) { [string]$GT_InstallerUserSid } else { 's-1-1-0' }
    $products = @(Read-GTInstallerValue $inst 'ProductsEx' @('', $sidAll, 7))
    foreach ($p in $products) {
        $code = [string](Read-GTInstallerValue $p 'ProductCode' $null)
        $sid = [string](Read-GTInstallerValue $p 'UserSid' $null)
        $ctx = [int](Read-GTInstallerValue $p 'Context' $null)
        # Produit annonce mais non installe (ProductState 1 ou 2) : pas de LocalPackage, la lecture peut echouer sans consequence.
        $pstate = 5
        try { $pstate = [int](Read-GTInstallerValue $inst 'ProductState' @($code)) } catch { }
        $lp = Get-GTInstallerPackageProp $p 'InstallProperty' @('LocalPackage') ($pstate -ne 1 -and $pstate -ne 2)
        if ($lp) { $res.Add($lp) }
        foreach ($pt in @(Read-GTInstallerValue $inst 'PatchesEx' @($code, $sid, $ctx, 15))) {
            $lpp = Get-GTInstallerPackageProp $pt 'PatchProperty' @('LocalPackage') $false
            if ($lpp) { $res.Add($lpp) }
        }
    }
    # Ancienne API (produits du contexte courant) : union, pour ne rien oublier.
    foreach ($code in @(Read-GTInstallerValue $inst 'Products' $null)) {
        $state = 5
        try { $state = [int](Read-GTInstallerValue $inst 'ProductState' @([string]$code)) } catch { }   # illisible : traite comme installe
        $lp = Get-GTInstallerPackageProp $inst 'ProductInfo' @([string]$code, 'LocalPackage') ($state -ne 1 -and $state -ne 2)
        if ($lp) { $res.Add($lp) }
        foreach ($pc in @(Read-GTInstallerValue $inst 'Patches' @([string]$code))) {
            $lpp = Get-GTInstallerPackageProp $inst 'PatchInfo' @([string]$pc, 'LocalPackage') $false
            if ($lpp) { $res.Add($lpp) }
        }
    }
    return $res.ToArray()
}

# $GT_InstallerDir et $GT_InstallerEnumerator : remplacements pour les tests. Seuls les .msi et .msp a la racine du dossier sont examines
# (jamais $PatchCache$ ni un autre sous-dossier). Si la lecture echoue ou ne renvoie rien alors que le dossier contient des paquets : erreur, rien n'est deplace.
function Find-OrphanInstallerFiles {
    $dir = if ($GT_InstallerDir) { [string]$GT_InstallerDir } else { Join-Path $env:SystemRoot 'Installer' }
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { throw ('Dossier Windows Installer introuvable : ' + $dir) }
    $files = @(Get-ChildItem -LiteralPath $dir -File -Force | Where-Object { $_.Extension -ieq '.msi' -or $_.Extension -ieq '.msp' })
    try {
        if ($GT_InstallerEnumerator) { $ref = @(& $GT_InstallerEnumerator) } else { $ref = @(Get-GTInstallerReferencedPackages) }
    } catch {
        throw ('Lecture de Windows Installer impossible, aucun fichier déplacé : ' + $_.Exception.Message)
    }
    $ref = @($ref | Where-Object { $_ })
    if ($ref.Count -eq 0) {
        if ($files.Count -gt 0) { throw 'Windows Installer ne renvoie aucun paquet alors que le dossier en contient : aucun fichier déplacé.' }
        return @()
    }
    $used = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($r in $ref) { try { [void]$used.Add([IO.Path]::GetFullPath([string]$r)) } catch { } }
    return @($files | Where-Object { -not $used.Contains($_.FullName) } | ForEach-Object { $_.FullName })
}

# ---- Anciens pilotes -----------------------------------------------------------------------------

function ConvertTo-GTVersion([string]$Text) {
    try { return [version]$Text } catch { return [version]'0.0' }
}

# Parmi des pilotes publies (objets de Get-WindowsDriver : Driver, OriginalFileName, ProviderName, ClassName, Version, Date, BootCritical),
# ceux qu'on peut retirer : dans chaque groupe (meme fichier .inf d'origine, meme editeur, meme classe), tous sauf le plus recent
# (date, version, puis numero oemN) et sauf ceux utilises ($InUse : noms oemN.inf). Un pilote critique pour le demarrage est toujours garde.
function Select-GTOldDrivers {
    param($Drivers, [string[]]$InUse = @())
    $use = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($u in $InUse) { if ($u) { [void]$use.Add($u) } }
    $groups = @($Drivers | Where-Object { $_.Driver } | Group-Object { ([string](Split-Path -Leaf ([string]$_.OriginalFileName))).ToLowerInvariant() + '|' + $_.ProviderName + '|' + $_.ClassName })
    foreach ($g in $groups) {
        $sorted = @($g.Group | Sort-Object -Descending @{ Expression = { [datetime]$_.Date } }, @{ Expression = { ConvertTo-GTVersion ([string]$_.Version) } }, @{ Expression = { [int]('0' + [regex]::Match([string]$_.Driver, '\d+').Value) } })
        for ($i = 1; $i -lt $sorted.Count; $i++) {
            $d = $sorted[$i]
            if ($use.Contains([string]$d.Driver)) { continue }
            if ($d.BootCritical) { continue }
            $d
        }
    }
}

# ---- Registre : valeurs journalisees et listes d'historique ------------------------------------------------

# Supprime une valeur apres l'avoir journalisee (l'annulation la remet). Renvoie $true si elle existait.
function Remove-TweakRegistryValue {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Name)
    $cur = Get-GTRegValue $Path $Name
    if (-not $cur.Existed) { return $false }
    Write-GTJournal 'registry' ($Path + '|' + $Name) $true $cur.Value $null $cur.Type
    Remove-ItemProperty -LiteralPath $Path -Name ([WildcardPattern]::Escape($Name)) -ErrorAction Stop
    return $true
}

# Vide les valeurs d'une cle HKCU (listes de fichiers recents, historiques). Definitif, sans journal. -Recurse : sous-cles comprises (les cles restent).
function Clear-GTRegistryValues {
    param([Parameter(Mandatory)][string]$Path, [switch]$Recurse)
    if ($Path -notmatch '^HKCU:\\') { throw ('Clear-GTRegistryValues est limitée à HKCU : ' + $Path) }
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $n = 0
    foreach ($name in (Get-Item -LiteralPath $Path).GetValueNames()) {
        if ($name -eq '') { continue }
        Remove-ItemProperty -LiteralPath $Path -Name ([WildcardPattern]::Escape($name)) -ErrorAction Stop
        $n++
    }
    if ($Recurse) {
        foreach ($c in @(Get-ChildItem -LiteralPath $Path -ErrorAction SilentlyContinue)) {
            $n += (Clear-GTRegistryValues -Path ($Path.TrimEnd('\') + '\' + $c.PSChildName) -Recurse)
        }
    }
    return $n
}

# ---------------------------------------------------------------------------
# Aides des menus Bloatware et Mises a jour.
# ---------------------------------------------------------------------------

# ---- winget --------------------------------------------------------------------------------

# $GT_WingetPath : chemin de winget.exe vérifié par l'outil (paquet DesktopAppInstaller sous Program Files\WindowsApps), écrit dans l'en-tête
# de chaque script ; remplacé dans les tests. Jamais cherché dans le PATH (réduit aux dossiers système, l'alias WindowsApps n'y est pas).
# Renvoie $null si winget est absent.
function Get-GTWingetPath {
    if ($GT_WingetPath) { return $GT_WingetPath }
    return $null
}

# 0 = reussite ; -1978335135 (0x8A150061) = deja installe ; -1978335189 (0x8A15002B) = aucune mise a jour applicable.
# 0x8A15010D (-1978334963) = l'operation a abouti mais un redemarrage est requis : succes, avec une note de redemarrage.
function Test-GTWingetOk([int]$Code) {
    return ($Code -eq 0 -or $Code -eq -1978335135 -or $Code -eq -1978335189 -or (Test-GTWingetReboot $Code))
}

function Test-GTWingetReboot([int]$Code) { return ($Code -eq -1978334963) }

# Code acceptable pour la LISTE « winget upgrade » : 0, aucune mise a jour applicable ou aucun paquet trouve (meme regle que WingetProvider.CodeListeAcceptable).
function Test-GTWingetListOk([int]$Code) { return ($Code -eq 0 -or $Code -eq -1978335189 -or $Code -eq -1978335212) }

# Reinstalle un paquet. Renvoie $false si winget est absent (reinstallation a faire a la main), leve une erreur si winget echoue.
function Install-GTWingetPackage([string]$Id, [string]$Source) {
    $exe = Get-GTWingetPath
    if (-not $exe) { return $false }
    $r = Invoke-GTNative $exe @('install', '--source', $Source, '--id', $Id, '--exact', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity', '--silent')
    if (-not (Test-GTWingetOk $r.ExitCode)) {
        $tail = @($r.Output | Where-Object { $_.Trim() } | Select-Object -Last 2) -join ' '
        throw ('winget a échoué pour ' + $Id + ' (code ' + $r.ExitCode + ') : ' + $tail)
    }
    return $true
}

# Analyse la sortie de « winget upgrade » par la position des colonnes lues sur la ligne d'en-tete, au-dessus des tirets
# (independant de la langue). Meme algorithme que WingetProvider.Parse. Renvoie des objets Id / Name / Version / Available / Source.
function ConvertFrom-GTWingetUpgrade([string[]]$Lines) {
    $clean = @()
    foreach ($l in @($Lines)) {
        $t = [string]$l
        $cr = $t.LastIndexOf([char]13)
        if ($cr -ge 0) { $t = $t.Substring($cr + 1) }
        $clean += $t.TrimStart([char]0xFEFF).TrimEnd()
    }
    $sep = -1
    for ($i = 1; $i -lt $clean.Count; $i++) {
        if ($clean[$i] -match '^-{5,}$' -and $clean[$i - 1].Trim().Length -gt 0) { $sep = $i; break }
    }
    if ($sep -lt 0) { return @() }
    $header = $clean[$sep - 1]
    $starts = @()
    for ($p = 0; $p -lt $header.Length; $p++) {
        if ($header[$p] -ne ' ' -and ($p -eq 0 -or ($header[$p - 1] -eq ' ' -and ($p -eq 1 -or $header[$p - 2] -eq ' ')))) { $starts += $p }
    }
    if ($starts.Count -lt 3) { return @() }
    $res = @()
    for ($i = $sep + 1; $i -lt $clean.Count; $i++) {
        $row = $clean[$i]
        if ($row.Trim().Length -eq 0) { break }
        $ok = $true
        for ($k = 1; $k -lt $starts.Count; $k++) { if ($row.Length -gt $starts[$k] - 1 -and $row[$starts[$k] - 1] -ne ' ') { $ok = $false } }
        if (-not $ok -or $row.Length -le $starts[1]) { continue }
        $cells = @()
        for ($k = 0; $k -lt $starts.Count; $k++) {
            $from = $starts[$k]
            $to = if ($k + 1 -lt $starts.Count) { $starts[$k + 1] } else { $row.Length }
            if ($from -ge $row.Length) { $cells += ''; continue }
            $cells += $row.Substring($from, [Math]::Min($to, $row.Length) - $from).Trim()
        }
        $id = $cells[1]
        if ($id -notmatch '^[A-Za-z0-9][A-Za-z0-9._+\-]*$') { continue }
        $res += [pscustomobject]@{
            Id = $id; Name = $cells[0]; Version = $cells[2]
            Available = $(if ($cells.Count -gt 3) { $cells[3] } else { '' }); Source = $(if ($cells.Count -gt 4) { $cells[4] } else { '' })
        }
    }
    return $res
}

# ---- Applications : DISM ----------------------------------------------------------------------

function Get-GTOptionalFeatureState([string]$Name) {
    $f = Get-WindowsOptionalFeature -Online -FeatureName $Name -ErrorAction SilentlyContinue
    if ($null -eq $f) { return $null }
    return [string]$f.State
}

# Fonctionnalite facultative desactivee (journalisee). Renvoie $false si elle est absente ou deja desactivee.
function Set-TweakDismFeatureOff {
    param([Parameter(Mandatory)][string]$Name)
    if ((Get-GTOptionalFeatureState $Name) -ne 'Enabled') { return $false }
    Write-GTJournal 'dismfeature' $Name $true 'Enabled' 'Disabled'
    $r = Invoke-GTDism -Arguments @('/Online', '/Disable-Feature', ('/FeatureName:' + $Name), '/NoRestart')
    if ($r.RebootRequired) { $script:GT_DismReboot = $true }
    return $true
}

# Nom sans le suffixe de version (« App.StepsRecorder~~~~0.0.1.0 » -> « App.StepsRecorder »).
function Get-GTCapabilityBaseName([string]$Name) { return ($Name -replace '~~~~.*$', '') }

# Noms complets (avec suffixe de version) des fonctionnalites a la demande installees dont le nom commence par $Prefix.
function Get-GTCapabilityInstalled([string]$Prefix) {
    if ($null -eq $script:GT_CapabilityCache) {
        $script:GT_CapabilityCache = @(Get-WindowsCapability -Online -ErrorAction SilentlyContinue | Where-Object { [string]$_.State -eq 'Installed' } | ForEach-Object { [string]$_.Name })
    }
    return @($script:GT_CapabilityCache | Where-Object { $_ -like ($Prefix + '~~~~*') })
}

# Fonctionnalite a la demande retiree (journalisee). Renvoie le nombre de fonctionnalites retirees.
function Set-TweakDismCapabilityOff {
    param([Parameter(Mandatory)][string]$Prefix)
    $n = 0
    foreach ($name in @(Get-GTCapabilityInstalled $Prefix)) {
        Write-GTJournal 'dismcapability' $name $true 'Installed' 'NotPresent'
        $r = Invoke-GTDism -Arguments @('/Online', '/Remove-Capability', ('/CapabilityName:' + $name))
        if ($r.RebootRequired) { $script:GT_DismReboot = $true }
        $n++
    }
    return $n
}

# ---- Pilote NVIDIA -------------------------------------------------------------------------------

# Vrai si la signature est valide ET delivree a NVIDIA Corporation : nom commun (CN) exactement « NVIDIA Corporation »
# (un sujet qui ne fait que contenir ce texte, comme « CN=Not NVIDIA Corporation », est refuse).
function Test-GTNvidiaSignature($Signature) {
    if ($null -eq $Signature) { return $false }
    if ([string]$Signature.Status -ne 'Valid') { return $false }
    $subject = ''
    if ($null -ne $Signature.SignerCertificate) { $subject = [string]$Signature.SignerCertificate.Subject }
    return [bool]($subject -match '(?i)(^|,)\s*CN\s*=\s*"?NVIDIA Corporation"?\s*(,|$)')
}

# Verifie l'installateur telecharge ; sinon le supprime et leve une erreur. -Signature : remplacement pour les tests.
function Confirm-GTNvidiaInstaller {
    param([Parameter(Mandatory)][string]$Path, $Signature)
    if ($null -eq $Signature) { $Signature = Get-AuthenticodeSignature -LiteralPath $Path }
    if (-not (Test-GTNvidiaSignature $Signature)) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        throw 'La signature de l''installateur téléchargé n''est pas celle de NVIDIA Corporation : fichier supprimé, rien n''a été installé.'
    }
}

# Adresse de telechargement acceptee : https, domaine nvidia.com.
function Test-GTNvidiaUrl([string]$Url) {
    return [bool]($Url -match '^https://([a-z0-9\-]+\.)*nvidia\.com/[^\s]+$')
}

# JSON de DriverManualLookup -> @{ Version; Url } ou $null (reponse inconnue, echec ou adresse refusee).
function ConvertFrom-GTNvidiaLookup([string]$Json) {
    try {
        $o = ConvertFrom-Json -InputObject $Json -ErrorAction Stop
        if ([string]$o.Success -ne '1') { return $null }
        $d = @($o.IDS)[0].downloadInfo
        if ($null -eq $d -or [string]$d.Success -ne '1') { return $null }
        $v = [string]$d.Version; $u = [string]$d.DownloadURL
        if ([string]::IsNullOrWhiteSpace($v) -or -not (Test-GTNvidiaUrl $u)) { return $null }
        return @{ Version = $v.Trim(); Url = $u.Trim() }
    } catch { return $null }
}

# Compare deux versions de pilote (« 621.01 », « 32.0.16.2101 ») ; renvoie -1, 0 ou 1.
function Compare-GTDriverVersion([string]$A, [string]$B) {
    $pa = @($A -split '\.' | ForEach-Object { [int64]($_ -replace '\D', '0') })
    $pb = @($B -split '\.' | ForEach-Object { [int64]($_ -replace '\D', '0') })
    $n = [Math]::Max($pa.Count, $pb.Count)
    for ($i = 0; $i -lt $n; $i++) {
        $x = if ($i -lt $pa.Count) { $pa[$i] } else { 0 }
        $y = if ($i -lt $pb.Count) { $pb[$i] } else { 0 }
        if ($x -lt $y) { return -1 }
        if ($x -gt $y) { return 1 }
    }
    return 0
}

# ---- OneDrive ----------------------------------------------------------------------------------

# Cree un fichier vide absent (et son dossier au besoin) apres l'avoir journalise (kind newfile ; before = dossier cree par nous).
# L'annulation supprime le fichier. Renvoie $true si le fichier a ete cree, $false s'il existait deja (rien n'est alors journalise ni supprime plus tard).
function New-TweakEmptyFile {
    param([Parameter(Mandatory)][string]$Path)
    if (Test-Path -LiteralPath $Path) { return $false }
    $dir = Split-Path -Parent $Path
    $dirNew = -not (Test-Path -LiteralPath $dir)
    Write-GTJournal 'newfile' $Path $false $dirNew $null
    if ($dirNew) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    New-Item -ItemType File -Force -Path $Path | Out-Null
    return $true
}

# Desinstalle OneDrive par son programme officiel. Les dossiers de l'utilisateur ne sont jamais touches.
# Annulation : reinstallation par winget (Microsoft.OneDrive).
# OneDriveSetup.exe reste dans Windows apres la desinstallation : la detection se fait sur OneDrive.exe installe (par utilisateur ou pour la machine).
function Get-GTOneDriveExePaths {
    $res = @()
    foreach ($pair in @(@($env:LOCALAPPDATA, 'Microsoft\OneDrive\OneDrive.exe'), @($env:ProgramFiles, 'Microsoft OneDrive\OneDrive.exe'), @(${env:ProgramFiles(x86)}, 'Microsoft OneDrive\OneDrive.exe'))) {
        if (-not [string]::IsNullOrEmpty($pair[0])) { $res += (Join-Path $pair[0] $pair[1]) }
    }
    return $res
}

function Test-GTOneDriveInstalled {
    foreach ($p in @(Get-GTOneDriveExePaths)) { if (Test-Path -LiteralPath $p) { return $true } }
    return $false
}

# $GT_OneDriveSetupRoot : remplacement du dossier Windows dans les tests (SystemRoot ne doit jamais etre modifie : .NET en depend).
function Remove-TweakOneDrive {
    if (-not (Test-GTOneDriveInstalled)) { Skip-GTItem 'OneDrive absent de cette machine (installation du profil de l''utilisateur courant).' }
    $racine = if ($GT_OneDriveSetupRoot) { [string]$GT_OneDriveSetupRoot } else { [string]$env:SystemRoot }
    $exe = Join-Path $racine 'SysWOW64\OneDriveSetup.exe'
    if (-not (Test-Path -LiteralPath $exe)) { $exe = Join-Path $racine 'System32\OneDriveSetup.exe' }
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Le désinstalleur de OneDrive (OneDriveSetup.exe) est introuvable.' }
    Write-GTJournal 'wingetapp' 'Microsoft.OneDrive' $true 'winget' $null
    Get-Process -Name 'OneDrive' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $exe -ArgumentList '/uninstall' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw ('Le désinstalleur de OneDrive a échoué (code ' + $p.ExitCode + ').') }
}

# ---- Pilote NVIDIA : recherche et installation -------------------------------------------------------

function Invoke-GTHttpText([string]$Url) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    $r = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 30
    return [string]$r.Content
}

function Get-GTNvidiaSmi {
    foreach ($c in @((Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'), (Join-Path $env:ProgramFiles 'NVIDIA Corporation\NVSMI\nvidia-smi.exe'))) {
        if (Test-Path -LiteralPath $c) { return $c }
    }
    $g = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($g) { return [string]$g.Source }
    return $null
}

# Derniere version du pilote pour ce GPU (branche GameReady ou Studio) : @{ Version; Url } ou $null si la recherche echoue.
function Get-GTNvidiaLatest([string]$GpuName, [string]$Branch) {
    try {
        $name = ($GpuName -replace '^NVIDIA\s+', '').Trim()
        $os = if ((Get-GTWindowsBuild) -ge 22000) { 135 } else { 57 }
        $crd = if ($Branch -eq 'Studio') { 1 } else { 0 }
        $xml = [xml](Invoke-GTHttpText 'https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3')
        foreach ($v in $xml.SelectNodes('//LookupValue')) {
            if ($v.SelectSingleNode('Name').InnerText -ine $name) { continue }
            $psid = $v.GetAttribute('ParentID'); $pfid = $v.SelectSingleNode('Value').InnerText
            if ($psid -notmatch '^\d+$' -or $pfid -notmatch '^\d+$') { continue }
            $json = Invoke-GTHttpText ('https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid=' + $psid + '&pfid=' + $pfid +
                '&osID=' + $os + '&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=' + $crd + '&qnf=0&sort1=1&numberOfResults=1')
            $info = ConvertFrom-GTNvidiaLookup $json
            if ($info) { return $info }
        }
    } catch { }
    return $null
}

# Dossier de telechargement reserve aux administrateurs : %ProgramFiles%\GamingTweaker\Telechargements, ou un utilisateur standard
# ne peut rien pre-creer ($GT_DownloadRoot le remplace dans les tests).
function Get-GTDownloadRoot {
    if ($GT_DownloadRoot) { return $GT_DownloadRoot }
    return (Join-Path $env:ProgramFiles 'GamingTweaker\Telechargements')
}

# ACL en memoire : Administrateurs et SYSTEM en controle total, heritage coupe sans copie des droits herites (donc aucun droit pour les utilisateurs).
function New-GTAdminOnlyAcl {
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @('S-1-5-32-544', 'S-1-5-18')) {
        $id = New-Object System.Security.Principal.SecurityIdentifier($sid)
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    return $acl
}

# Cree le dossier avec cette ACL, ou l'applique a un dossier existant. Remplacee par un double dans les tests.
function Initialize-GTAdminDir([string]$Path) {
    $acl = New-GTAdminOnlyAcl
    if (Test-Path -LiteralPath $Path) { Set-Acl -LiteralPath $Path -AclObject $acl }
    else { [IO.Directory]::CreateDirectory($Path, $acl) | Out-Null }
}

# <racine>\<guid>, racine et sous-dossier reserves aux administrateurs : un utilisateur standard ne peut pas y remplacer l'installateur.
function New-GTSecureDownloadDir {
    $root = Get-GTDownloadRoot
    Initialize-GTAdminDir $root
    $dir = Join-Path $root ([Guid]::NewGuid().ToString('N'))
    Initialize-GTAdminDir $dir
    return $dir
}

# Ouvre le fichier en lecture en interdisant a tout autre d'y ecrire, de le renommer ou de le supprimer, jusqu'a Dispose().
function Open-GTLockedFile([string]$Path) {
    return (New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read))
}

# Telecharge l'installateur dans un dossier reserve aux administrateurs, le verrouille (aucune ecriture possible), verifie sa signature
# (sinon suppression et erreur), l'installe en silencieux sans redemarrage, puis supprime le dossier. Le verrou est tenu de la verification au lancement.
function Install-TweakNvidiaDriver {
    param([Parameter(Mandatory)][string]$Url)
    if (-not (Test-GTNvidiaUrl $Url)) { throw 'Adresse de téléchargement refusée (ce n''est pas un domaine NVIDIA en https).' }
    $leaf = [IO.Path]::GetFileName(([Uri]$Url).AbsolutePath)
    if ($leaf -notmatch '^[A-Za-z0-9._\-]+\.exe$') { throw 'Nom de fichier du pilote inattendu.' }
    $dir = New-GTSecureDownloadDir
    $dest = Join-Path $dir $leaf
    $lock = $null
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $dest -TimeoutSec 3000
        $lock = Open-GTLockedFile $dest
        Confirm-GTNvidiaInstaller -Path $dest
        $p = Start-Process -FilePath $dest -ArgumentList '-s', '-noreboot' -Wait -PassThru
        if ($p.ExitCode -ne 0) { throw ('L''installateur NVIDIA a échoué (code ' + $p.ExitCode + ').') }
        return $p.ExitCode
    } finally {
        if ($null -ne $lock) { $lock.Dispose() }
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- Reglages du pilote NVIDIA (profil global, API NVAPI DRS) --------------------------------------
# nvapi64.dll est installee avec le pilote (System32) : aucun outil externe. Les reglages vont dans le profil global courant
# (celui que modifie « Parametres globaux » du panneau NVIDIA). Chaque ecriture est journalisee (kind nvdrs, target = id en hexadecimal) :
# l'annulation remet la valeur d'avant, ou retire le reglage du profil (valeur par defaut du pilote) s'il n'y etait pas defini.
# Structure NVDRS_SETTING_V1 (12320 octets) manipulee par decalages : version 0, settingId 4100, settingType 4104,
# settingLocation 4108, u32CurrentValue 8220.
$script:GT_NvDrsSource = @'
using System;
using System.Runtime.InteropServices;

namespace GTNative
{
    public static class NvDrs
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FCreate(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FSession(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FProfile(IntPtr session, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FGet(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FSet(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FDelete(IntPtr session, IntPtr profile, uint id);

        public const int NotFound = -160;
        private const int Size = 12320;
        private const int Version = Size | (1 << 16);

        private static T F<T>(uint id) where T : class
        {
            IntPtr p = QueryInterface(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("Fonction NVAPI absente de ce pilote (0x" + id.ToString("X8") + ").");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        private static void Ok(int s, string quoi)
        {
            if (s != 0) throw new InvalidOperationException("NVAPI " + quoi + " a échoué (code " + s + ").");
        }

        private static IntPtr Ouvrir(out IntPtr profil)
        {
            Ok(F<FInit>(0x0150E828)(), "Initialize");
            IntPtr h;
            Ok(F<FCreate>(0x0694D52E)(out h), "DRS_CreateSession");
            try
            {
                Ok(F<FSession>(0x375DBD6B)(h), "DRS_LoadSettings");
                Ok(F<FProfile>(0x617BFF9F)(h, out profil), "DRS_GetCurrentGlobalProfile");
            }
            catch { F<FSession>(0xDAD9CFF8)(h); throw; }
            return h;
        }

        private static void Fermer(IntPtr h) { F<FSession>(0xDAD9CFF8)(h); }

        private static IntPtr Tampon()
        {
            IntPtr b = Marshal.AllocHGlobal(Size);
            Marshal.Copy(new byte[Size], 0, b, Size);
            Marshal.WriteInt32(b, 0, Version);
            return b;
        }

        /// <summary>{ defini dans le profil global (1/0), valeur effective (-1 si inconnue) }.</summary>
        public static long[] Lire(uint id)
        {
            IntPtr profil; IntPtr h = Ouvrir(out profil); IntPtr b = Tampon();
            try
            {
                int s = F<FGet>(0x73BF8338)(h, profil, id, b);
                if (s == NotFound) return new long[] { 0, -1 };
                Ok(s, "DRS_GetSetting");
                if (Marshal.ReadInt32(b, 4104) != 0) throw new InvalidOperationException("Réglage NVIDIA 0x" + id.ToString("X8") + " : type inattendu (pas un entier).");
                bool defini = Marshal.ReadInt32(b, 4108) == 0;
                return new long[] { defini ? 1 : 0, (long)(uint)Marshal.ReadInt32(b, 8220) };
            }
            finally { Marshal.FreeHGlobal(b); Fermer(h); }
        }

        public static void Ecrire(uint id, uint valeur)
        {
            IntPtr profil; IntPtr h = Ouvrir(out profil); IntPtr b = Tampon();
            try
            {
                Marshal.WriteInt32(b, 4100, (int)id);
                Marshal.WriteInt32(b, 4104, 0);
                Marshal.WriteInt32(b, 8220, unchecked((int)valeur));
                Ok(F<FSet>(0x577DD202)(h, profil, b), "DRS_SetSetting");
                Ok(F<FSession>(0xFCBC7E14)(h), "DRS_SaveSettings");
            }
            finally { Marshal.FreeHGlobal(b); Fermer(h); }
        }

        /// <summary>Retire le reglage du profil global (le pilote reprend sa valeur par defaut).</summary>
        public static void Retirer(uint id)
        {
            IntPtr profil; IntPtr h = Ouvrir(out profil);
            try
            {
                int s = F<FDelete>(0xE4A26362)(h, profil, id);
                if (s == NotFound) return;
                Ok(s, "DRS_DeleteProfileSetting");
                Ok(F<FSession>(0xFCBC7E14)(h), "DRS_SaveSettings");
            }
            finally { Fermer(h); }
        }
    }
}
'@

function Initialize-GTNvDrs {
    if ('GTNative.NvDrs' -as [type]) { return }
    if (-not (Test-Path -LiteralPath (Join-Path $env:SystemRoot 'System32\nvapi64.dll'))) { Skip-GTItem 'Pilote NVIDIA introuvable (nvapi64.dll absent) : réglage ignoré.' }
    Add-Type -TypeDefinition $script:GT_NvDrsSource -Language CSharp
}

function Format-GTNvId([uint32]$Id) { return ('0x{0:X8}' -f $Id) }

function ConvertFrom-GTNvId([string]$Text) { return [Convert]::ToUInt32(($Text -replace '^0[xX]', ''), 16) }

# @{ Existed = defini dans le profil global ; Value = valeur effective ou $null si le pilote n'en donne pas }.
function Get-GTNvSetting([uint32]$Id) {
    Initialize-GTNvDrs
    $r = [GTNative.NvDrs]::Lire($Id)
    $v = $null; if ($r[1] -ge 0) { $v = [uint32]$r[1] }
    return @{ Existed = ($r[0] -eq 1); Value = $v }
}

# Ecrit un reglage du profil global apres l'avoir journalise. Rien n'est ecrit ni journalise s'il y est deja defini a cette valeur.
function Set-TweakNvSetting {
    param([Parameter(Mandatory)][uint32]$Id, [Parameter(Mandatory)][uint32]$Value)
    $cur = Get-GTNvSetting $Id
    if ($cur.Existed -and $cur.Value -eq $Value) { return }
    $before = $null; if ($cur.Existed) { $before = [long]$cur.Value }
    Write-GTJournal 'nvdrs' (Format-GTNvId $Id) ([bool]$cur.Existed) $before ([long]$Value)
    [GTNative.NvDrs]::Ecrire($Id, $Value)
}

# Annulation sans journal (« Tout restaurer ») : reglages retires du profil global, valeurs par defaut du pilote.
function Reset-GTNvSettings([uint32[]]$Ids) {
    Initialize-GTNvDrs
    foreach ($id in $Ids) { [GTNative.NvDrs]::Retirer($id) }
}

# 'Applied' si chaque reglage a la valeur voulue. $Pairs : @(@(id, valeur), ...).
function Get-GTNvState([object[]]$Pairs) {
    foreach ($p in $Pairs) {
        $c = Get-GTNvSetting ([uint32]$p[0])
        if ($null -eq $c.Value -or $c.Value -ne [uint32]$p[1]) { return 'NotApplied' }
    }
    return 'Applied'
}
