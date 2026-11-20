param(
    [Parameter(Mandatory)][string]$Folder,
    [string]$UserName = 'Scans',
    [string]$ShareName = 'Scans',
    [switch]$DesktopShortcut,
    [switch]$QuickAccess
)
# Turns this PC into a network scanning host: SMB on, private network with discovery, a "Scans" account,
# a folder and an SMB share the scanner can save into. Needs administrator rights.
# The password arrives in the SCANHOST_PASSWORD environment variable (not on the command line) and is never written out.
# Printer Detector checks for share name clashes, existing accounts and unsuitable folders before running this,
# so the checks here are a safety net.
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

# Runs one part of a step; a failure is reported and counted but the rest of the step carries on.
function Part([string]$What, [scriptblock]$Body) {
    try { & $Body }
    catch { Write-Output "  FAILED ($What): $($_.Exception.Message)"; $script:problems++ }
}

try {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw 'Administrator rights are needed. Close Printer Detector and run it as administrator.' }
    if (-not $plain -or $plain.Length -lt 8) { throw 'No usable password was supplied.' }
    if ($UserName -notmatch '^[\w\.\- ]{1,20}$') { throw "'$UserName' is not a valid account name." }
    if ($ShareName -notmatch '^[\w\.\- ]{1,80}$') { throw "'$ShareName' is not a valid share name." }
    if ($UserName -ieq $env:COMPUTERNAME) { throw "The account name cannot be the same as this PC's name ($env:COMPUTERNAME)." }

    $Folder = [IO.Path]::GetFullPath($Folder).TrimEnd('\')
    if ($Folder.Length -le 3) { throw "Pick a folder, not a drive root ($Folder)." }
    if (Test-Path -LiteralPath $Folder -PathType Leaf) { throw "$Folder is a file, not a folder." }
    $account = "$env:COMPUTERNAME\$UserName"

    # Fail before changing anything if the share name is taken by a different folder.
    $existingShare = Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue
    if ($existingShare -and $existingShare.Path.TrimEnd('\') -ne $Folder) {
        throw "A share called '$ShareName' already points at $($existingShare.Path). Run this again and pick a different share name, or use that folder."
    }

    # --- Network profile and discovery ---
    Step 'Setting public networks to private...' {
        $public = @(Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' })
        if ($public.Count -eq 0) { Write-Output '  Already private or domain, nothing to change'; return }
        foreach ($p in $public) {
            Part "'$($p.Name)'" {
                Set-NetConnectionProfile -InterfaceIndex $p.InterfaceIndex -NetworkCategory Private
                Write-Output "  '$($p.Name)' is now Private"
            }
        }
    }

    Step 'Turning on network discovery...' {
        foreach ($svc in 'fdPHost', 'FDResPub', 'SSDPSRV') {
            Part "service $svc" {
                if (-not (Get-Service -Name $svc -ErrorAction SilentlyContinue)) { Write-Output "  Service $svc is not on this PC, skipped"; return }
                Set-Service -Name $svc -StartupType Automatic
                if ((Get-Service -Name $svc).Status -ne 'Running') { Start-Service -Name $svc }
            }
        }
        # Group ids instead of display names so this works on any Windows language.
        Part 'firewall rules' { Enable-NetFirewallRule -Group '@FirewallAPI.dll,-32752' }
        Write-Output '  Network discovery is on'
    }

    # --- SMB ---
    Step 'Checking SMB file sharing...' {
        $cfg = Get-SmbServerConfiguration
        if ($cfg.EnableSMB2Protocol) { Write-Output '  SMB 2/3 server already enabled' }
        else { Set-SmbServerConfiguration -EnableSMB2Protocol $true -Force -Confirm:$false; Write-Output '  SMB 2/3 server enabled' }
        Set-Service -Name LanmanServer -StartupType Automatic
        if ((Get-Service -Name LanmanServer).Status -ne 'Running') { Start-Service -Name LanmanServer; Write-Output '  Server service started' }
        Part 'firewall rules' {
            Enable-NetFirewallRule -Group '@FirewallAPI.dll,-28502'
            Write-Output '  File and Printer Sharing allowed through the firewall'
        }
    } -Critical

    # --- Account ---
    Step "Setting up the '$UserName' account..." {
        $secure = ConvertTo-SecureString $plain -AsPlainText -Force
        $u = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
        if ($u) {
            Set-LocalUser -Name $UserName -Password $secure -PasswordNeverExpires $true -AccountNeverExpires
            if (-not $u.Enabled) { Enable-LocalUser -Name $UserName; Write-Output "  '$UserName' was disabled, now enabled" }
            Write-Output "  '$UserName' already existed. Password reset, expiry switched off"
        } else {
            New-LocalUser -Name $UserName -Password $secure -PasswordNeverExpires -AccountNeverExpires `
                -Description 'Network scanning account' | Out-Null
            Write-Output "  '$UserName' created, password never expires"
        }
        # Network logons need the Users group (by SID so it works on any Windows language).
        # Add and treat "already a member" as success: Get-LocalGroupMember breaks on groups holding deleted accounts.
        try { Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $UserName }
        catch { if ($_.Exception.GetType().Name -ne 'MemberExistsException') { throw } }
    } -Critical
    $plain = $null
    $sid = (Get-LocalUser -Name $UserName).SID.Value

    # --- Folder and permissions ---
    Step "Preparing folder $Folder..." {
        if (-not (Test-Path -LiteralPath $Folder)) {
            New-Item -ItemType Directory -Force -Path $Folder | Out-Null
            Write-Output '  Folder created'
        } else { Write-Output '  Folder already exists' }
        # icacls only adds an entry, so it works on folders this admin does not own (Set-Acl tries to rewrite the owner).
        $out = & icacls.exe $Folder /grant "*${sid}:(OI)(CI)F" 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Could not set permissions: $(($out | Out-String).Trim())" }
        Write-Output "  $account has full control of the folder"
    } -Critical

    # --- Share ---
    Step "Sharing the folder as '$ShareName'..." {
        $share = Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue
        if ($share) {
            Grant-SmbShareAccess -Name $ShareName -AccountName $account -AccessRight Change -Force | Out-Null
            Write-Output '  Share already existed. Access granted'
        } else {
            New-SmbShare -Name $ShareName -Path $Folder -ChangeAccess $account -Description 'Network scans' | Out-Null
            Write-Output '  Share created'
        }
        Write-Output "  $account has read / write on the share"
    } -Critical

    # --- Shortcuts (for the person signed in now) ---
    if ($DesktopShortcut) {
        Step 'Adding a desktop shortcut...' {
            $desktop = [Environment]::GetFolderPath('Desktop')
            $shell = New-Object -ComObject WScript.Shell
            $base = Split-Path -Leaf $Folder
            $lnk = Join-Path $desktop "$base.lnk"
            $n = 2
            while (Test-Path -LiteralPath $lnk) {
                if ($shell.CreateShortcut($lnk).TargetPath.TrimEnd('\') -ieq $Folder) { Write-Output "  Already on the desktop as $(Split-Path -Leaf $lnk)"; return }
                $lnk = Join-Path $desktop "$base ($n).lnk"; $n++
            }
            $s = $shell.CreateShortcut($lnk)
            $s.TargetPath = $Folder
            $s.Description = 'Network scans'
            $s.Save()
            Write-Output "  Shortcut '$(Split-Path -Leaf $lnk)' added to $desktop"
        }
    }

    if ($QuickAccess) {
        Step 'Pinning the folder to Quick Access...' {
            $shell = New-Object -ComObject Shell.Application
            $qaId = 'shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}'
            function IsPinned { [bool]($shell.Namespace($qaId).Items() | Where-Object { $_.Path.TrimEnd('\') -ieq $Folder }) }
            if (IsPinned) { Write-Output '  Already in Quick Access'; return }
            $item = $shell.Namespace($Folder)
            if (-not $item) { throw 'Explorer could not open the folder.' }
            $item.Self.InvokeVerb('pintohome')
            for ($i = 0; $i -lt 10 -and -not (IsPinned); $i++) { Start-Sleep -Milliseconds 500 }
            if (IsPinned) { Write-Output '  Pinned to Quick Access' }
            else { throw 'Windows did not confirm the pin. Right-click the folder in Explorer and choose Pin to Quick access.' }
        }
    }

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
    # Everything critical worked by this point; the remaining failures are optional extras.
    if ($problems -gt 0) { Write-Output "Done. This PC is ready to receive scans, but $problems optional step(s) failed. See FAILED above." }
    else { Write-Output 'Done. This PC is ready to receive scans.' }
} catch {
    Write-Output "FAILED: $($_.Exception.Message)"
    exit 1
}
