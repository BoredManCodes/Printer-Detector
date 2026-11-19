param(
    [Parameter(Mandatory)][string]$OutDir,
    [Parameter(Mandatory)][string]$NamesFile,
    [switch]$NoDriverCopy
)
# Copies the selected print queues (driver package, TCP/IP port, defaults) into $OutDir.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$names = @(Get-Content -LiteralPath $NamesFile -Encoding UTF8 | Where-Object { $_.Trim() })
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$drvRoot = Join-Path $OutDir 'drivers'
New-Item -ItemType Directory -Force -Path $drvRoot | Out-Null
$tkRoot = Join-Path $OutDir 'tickets'
New-Item -ItemType Directory -Force -Path $tkRoot | Out-Null

# Keep queues already in the bundle unless re-exported under the same name.
$items = @()
$manifestPath = Join-Path $OutDir 'manifest.json'
if (Test-Path -LiteralPath $manifestPath) {
    try {
        $old = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $items = @($old.Printers | Where-Object { $names -notcontains $_.Name })
    } catch { $items = @() }
}

function Get-VendorPortAddress([string]$PortName, [string]$Monitor) {
    if ($Monitor) {
        $k = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey("SYSTEM\CurrentControlSet\Control\Print\Monitors\$Monitor\Ports\$PortName")
        if ($k) {
            foreach ($v in 'PrinterAddress', 'HostName', 'HostAddress', 'IPAddress', 'IpAddress', 'Address', 'PrinterIP') {
                $val = $k.GetValue($v)
                if ($val -is [string] -and $val.Trim()) { return $val.Trim() }
            }
        }
    }
    if ($PortName -match '(\d{1,3}(\.\d{1,3}){3})') { return $Matches[1] }
    return ''
}

$winInf = Join-Path $env:windir 'INF'
$done = 0
foreach ($n in $names) {
    try {
        Write-Output "Copying '$n'..."
        $p = Get-Printer -Name $n
        $port = Get-PrinterPort -Name $p.PortName -ErrorAction SilentlyContinue
        $hostAddr = [string]$port.PrinterHostAddress
        $portMonitor = [string]$port.PortMonitor
        $portKind = 'tcpip'
        if (-not $hostAddr) {
            # Vendor ports (for example "EpsonNet Print Port") cannot be copied, but the printer's IP is usually in
            # the port's own registry values or in its name. If found, install recreates it as a Standard TCP/IP port.
            $noIpMonitors = 'Local Port', 'USB Monitor', 'WSD Port', 'Microsoft Shared Fax Monitor', 'Virtual Port Monitor'
            $found = ''
            if ($port -and $portMonitor -notin $noIpMonitors -and -not $p.PortName.StartsWith('USB') -and -not $p.PortName.StartsWith('WSD')) {
                $found = Get-VendorPortAddress -PortName $p.PortName -Monitor $portMonitor
            }
            if ($found) {
                $hostAddr = $found
                $portKind = 'vendor'
                Write-Output "  NOTE: port '$($p.PortName)' is a '$portMonitor' port, which cannot be copied. Its address is $found,"
                Write-Output "        so installing will create a Standard TCP/IP port 'IP_$found' (RAW, port 9100) and point the queue at it."
                Write-Output "        If that printer does not accept raw printing on 9100, open the queue's Properties > Ports and switch the port to LPR."
            } else {
                $portKind = 'other'
                if ($p.PortName -like 'USB*' -or $portMonitor -eq 'USB Monitor') {
                    Write-Output "  WARNING: '$($p.PortName)' is a USB port, which belongs to the printer plugged into this PC. The queue is saved without it."
                    Write-Output "        On the new PC: plug the printer in and wait for Windows to create its USB port, run the install,"
                    Write-Output "        then open the queue's Properties > Ports tab and tick the new USB port (USB001 and so on)."
                } elseif ($p.PortName -like 'WSD*' -or $portMonitor -eq 'WSD Port') {
                    Write-Output "  WARNING: '$($p.PortName)' is a WSD port tied to this PC's discovery of the printer, so it cannot be copied."
                    Write-Output "        On the new PC: run the install, then open the queue's Properties > Ports tab > Add Port > Standard TCP/IP Port > New Port,"
                    Write-Output "        enter the printer's IP address and tick the new port."
                } else {
                    Write-Output "  WARNING: port '$($p.PortName)' ($portMonitor) is not a TCP/IP port and no IP address could be found for it."
                    Write-Output "        On the new PC: run the install, then open the queue's Properties > Ports tab > Add Port > Standard TCP/IP Port > New Port,"
                    Write-Output "        enter the printer's IP address and tick the new port."
                }
            }
        }

        $drv = Get-PrinterDriver -Name $p.DriverName -ErrorAction Stop |
            Where-Object { $_.PrinterEnvironment -eq 'Windows x64' } | Select-Object -First 1
        if (-not $drv) { $drv = Get-PrinterDriver -Name $p.DriverName | Select-Object -First 1 }

        $driverDir = ''
        $infName = ''
        if ($drv.InfPath -and -not $drv.InfPath.StartsWith($winInf, [StringComparison]::OrdinalIgnoreCase)) {
            $srcDir = Split-Path -Parent $drv.InfPath
            $infName = Split-Path -Leaf $drv.InfPath
            $driverDir = ($drv.Name -replace '[^\w\.\-]', '_')
            $dest = Join-Path $drvRoot $driverDir
            if (-not $NoDriverCopy -and -not (Test-Path -LiteralPath $dest)) {
                Copy-Item -LiteralPath $srcDir -Destination $dest -Recurse -Force
            }
        } else {
            Write-Output "  Driver '$($p.DriverName)' is built into Windows; it will be added by name."
        }

        $ticketFile = ''
        try {
            $cfg = Get-PrintConfiguration -PrinterName $n
            if ($cfg.PrintTicketXML) {
                $ticketFile = ($n -replace '[^\w\.\-]', '_') + '.xml'
                Set-Content -LiteralPath (Join-Path $tkRoot $ticketFile) -Value $cfg.PrintTicketXML -Encoding UTF8
            }
        } catch {
            Write-Output "  WARNING: could not read default settings: $($_.Exception.Message)"
        }

        # Printing Defaults and Preferences (tray, paper size, weight) live in a DEVMODE blob, not in the print ticket.
        # A per-user DEVMODE (Preferences) overrides the machine one (Printing Defaults), so it is the effective setting.
        $devMode = ''
        $devSource = ''
        $driverData = @()
        if (-not $n.StartsWith('\\')) {
            try {
                $rk = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey("SYSTEM\CurrentControlSet\Control\Print\Printers\$n")
                # Assign inside the if block: using "if" as an expression would unroll the byte[] into object[].
                $machine = $null
                if ($rk) { $machine = $rk.GetValue('Default DevMode') }
                $uk = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Printers\DevModePerUser')
                $user = $null
                if ($uk) { $user = $uk.GetValue($n) }
                if ($user -is [byte[]] -and $user.Length -gt 0) {
                    $devMode = [Convert]::ToBase64String($user); $devSource = 'preferences'
                } elseif ($machine -is [byte[]] -and $machine.Length -gt 0) {
                    $devMode = [Convert]::ToBase64String($machine); $devSource = 'defaults'
                }
                if ($rk) {
                    $pdd = $rk.OpenSubKey('PrinterDriverData')
                    if ($pdd) {
                        foreach ($vn in $pdd.GetValueNames()) {
                            if ($vn -in 'DevmodeSize', 'DriverDefaultDevmode') { continue }
                            $kind = $pdd.GetValueKind($vn).ToString()
                            $raw = $pdd.GetValue($vn, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                            $val = switch ($kind) {
                                'Binary'      { [Convert]::ToBase64String($raw) }
                                'MultiString' { @($raw) }
                                default       { "$raw" }
                            }
                            $driverData += [pscustomobject]@{ Name = $vn; Kind = $kind; Value = $val }
                        }
                    }
                }
            } catch {
                Write-Output "  WARNING: could not read tray and paper settings: $($_.Exception.Message)"
            }
        }
        if ($devMode) { Write-Output "  Saved settings from $devSource ($($driverData.Count) device option(s))" }
        else { Write-Output "  WARNING: no saved tray and paper settings found for this queue" }

        $items += [pscustomobject]@{
            Name        = $p.Name
            DriverName  = $p.DriverName
            DriverDir   = $driverDir
            InfName     = $infName
            PortName    = $p.PortName
            HostAddress = $hostAddr
            PortKind    = $portKind
            PortMonitor = $portMonitor
            PortNumber  = [int]$port.PortNumber
            Protocol    = [string]$port.Protocol
            LprQueue    = [string]$port.LprQueueName
            Comment     = [string]$p.Comment
            Location    = [string]$p.Location
            TicketFile  = $ticketFile
            DevMode     = $devMode
            DevModeFrom = $devSource
            DriverData  = @($driverData)
        }
        $done++
        Write-Output "  OK"
    } catch {
        Write-Output "  FAILED: $($_.Exception.Message)"
    }
}

$json = [pscustomobject]@{
    ExportedFrom = $env:COMPUTERNAME
    ExportedAt   = (Get-Date).ToString('s')
    Printers     = @($items)
} | ConvertTo-Json -Depth 8
Set-Content -LiteralPath $manifestPath -Value $json -Encoding UTF8
Write-Output "Done. $done of $($names.Count) queue(s) saved to $OutDir"
