param(
    [Parameter(Mandatory)][string]$Folder,
    [string]$UserName = 'Scans',
    [string]$ShareName = 'Scans'
)
# Turns this PC into a network scanning host: SMB on, private network with discovery, a "Scans" account,
# a folder and an SMB share the scanner can save into. Needs administrator rights.
# The password arrives in the SCANHOST_PASSWORD environment variable (not on the command line) and is never written out.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$plain = $env:SCANHOST_PASSWORD
Remove-Item Env:SCANHOST_PASSWORD -ErrorAction SilentlyContinue

$problems = 0
function Step([string]$Title, [scriptblock]$Body, [switch]$Critical) {
    Write-Output $Title
    try {
        & $Body
    } catch {
        Write-Output "  FAILED: $($_.Exception.Message)"
        if ($Critical) { throw "Stopped: '$Title' is needed for the rest." }
        $script:problems++
    }
}

try {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw 'Administrator rights are needed. Close Printer Detector and run it as administrator.' }
    if (-not $plain -or $plain.Length -lt 8) { throw 'No usable password was supplied.' }
    if ($UserName -notmatch '^[\w\.\- ]{1,20}$') { throw "'$UserName' is not a valid account name." }
    if ($ShareName -notmatch '^[\w\.\- ]{1,80}$') { throw "'$ShareName' is not a valid share name." }

    $Folder = [IO.Path]::GetFullPath($Folder).TrimEnd('\')
    if ($Folder.Length -le 3) { throw "Pick a folder, not a drive root ($Folder)." }
    $account = "$env:COMPUTERNAME\$UserName"

    # --- Network profile and discovery ---
    Step 'Setting public networks to private...' {
        $public = @(Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' })
        if ($public.Count -eq 0) { Write-Output '  Already private or domain, nothing to change'; return }
        foreach ($p in $public) {
            Set-NetConnectionProfile -InterfaceIndex $p.InterfaceIndex -NetworkCategory Private
            Write-Output "  '$($p.Name)' is now Private"
        }
    }

    Step 'Turning on network discovery...' {
        foreach ($svc in 'fdPHost', 'FDResPub', 'SSDPSRV') {
            Set-Service -Name $svc -StartupType Automatic
            if ((Get-Service -Name $svc).Status -ne 'Running') { Start-Service -Name $svc }
        }
        # Group ids instead of display names so this works on any Windows language.
        Enable-NetFirewallRule -Group '@FirewallAPI.dll,-32752'
        Write-Output '  Network discovery is on'
    }

    # --- SMB ---
    Step 'Checking SMB file sharing...' {
        $cfg = Get-SmbServerConfiguration
        if ($cfg.EnableSMB2Protocol) { Write-Output '  SMB 2/3 server already enabled' }
        else { Set-SmbServerConfiguration -EnableSMB2Protocol $true -Force -Confirm:$false; Write-Output '  SMB 2/3 server enabled' }
        Set-Service -Name LanmanServer -StartupType Automatic
        if ((Get-Service -Name LanmanServer).Status -ne 'Running') { Start-Service -Name LanmanServer; Write-Output '  Server service started' }
        Enable-NetFirewallRule -Group '@FirewallAPI.dll,-28502'
        Write-Output '  File and Printer Sharing allowed through the firewall'
    }

    # --- Account ---
    Step "Setting up the '$UserName' account..." {
        $secure = ConvertTo-SecureString $plain -AsPlainText -Force
        $u = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
        if ($u) {
            Set-LocalUser -Name $UserName -Password $secure -PasswordNeverExpires $true -AccountNeverExpires
            if (-not $u.Enabled) { Enable-LocalUser -Name $UserName }
            Write-Output "  '$UserName' already existed. Password reset, expiry switched off"
        } else {
            New-LocalUser -Name $UserName -Password $secure -PasswordNeverExpires -AccountNeverExpires `
                -Description 'Network scanning account' | Out-Null
            Write-Output "  '$UserName' created, password never expires"
        }
        # Network logons need the Users group (by SID so it works on any Windows language).
        $inUsers = Get-LocalGroupMember -SID 'S-1-5-32-545' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "*\$UserName" }
        if (-not $inUsers) { Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $UserName }
    } -Critical
    $plain = $null

    # --- Folder and permissions ---
    Step "Preparing folder $Folder..." {
        if (-not (Test-Path -LiteralPath $Folder)) {
            New-Item -ItemType Directory -Force -Path $Folder | Out-Null
            Write-Output '  Folder created'
        } else { Write-Output '  Folder already exists' }
        $acl = Get-Acl -LiteralPath $Folder
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($account, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $Folder -AclObject $acl
        Write-Output "  $account has full control of the folder"
    } -Critical

    # --- Share ---
    Step "Sharing the folder as '$ShareName'..." {
        $share = Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue
        if ($share) {
            if ($share.Path.TrimEnd('\') -ne $Folder) {
                throw "A share called '$ShareName' already points at $($share.Path). Remove it first, or use that folder."
            }
            Grant-SmbShareAccess -Name $ShareName -AccountName $account -AccessRight Change -Force | Out-Null
            Write-Output '  Share already existed. Access granted'
        } else {
            New-SmbShare -Name $ShareName -Path $Folder -ChangeAccess $account -Description 'Network scans' | Out-Null
            Write-Output '  Share created'
        }
        Write-Output "  $account has read / write on the share"
    } -Critical

    # --- Summary ---
    $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        ForEach-Object { $_.IPAddress })
    Write-Output ''
    Write-Output 'Enter these on the scanner (Scan to Network / Scan to SMB):'
    Write-Output "  Host name or IP : $(if ($ips.Count) { $ips -join ' or ' } else { $env:COMPUTERNAME })"
    Write-Output "  Share path      : \\$env:COMPUTERNAME\$ShareName"
    Write-Output "  User name       : $UserName"
    Write-Output '  Password        : the one you just set'
    Write-Output ''
    if ($problems -gt 0) { Write-Output "Done, but $problems step(s) failed. See FAILED above." }
    else { Write-Output 'Done. This PC is ready to receive scans.' }
} catch {
    Write-Output "FAILED: $($_.Exception.Message)"
    exit 1
}
