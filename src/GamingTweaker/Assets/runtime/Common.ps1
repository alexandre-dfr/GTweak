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
