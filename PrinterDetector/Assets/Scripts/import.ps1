param(
    [Parameter(Mandatory)][string]$BundleDir,
    [Parameter(Mandatory)][string]$NamesFile,
    [switch]$Replace
)
# Installs the selected print queues from a bundle made by export.ps1.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$names = @(Get-Content -LiteralPath $NamesFile -Encoding UTF8 | Where-Object { $_.Trim() })
$m = Get-Content -LiteralPath (Join-Path $BundleDir 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json

$done = 0
$pending = @()
foreach ($it in @($m.Printers)) {
    if ($names -notcontains $it.Name) { continue }
    try {
        Write-Output "Installing '$($it.Name)'..."

        if (-not (Get-PrinterDriver -Name $it.DriverName -ErrorAction SilentlyContinue)) {
            if ($it.DriverDir) {
                $inf = Join-Path (Join-Path (Join-Path $BundleDir 'drivers') $it.DriverDir) $it.InfName
                Write-Output "  Adding driver package $($it.InfName)"
                $out = & pnputil.exe /add-driver $inf /install 2>&1
                if ($LASTEXITCODE -notin 0, 3010) { throw "pnputil failed ($LASTEXITCODE): $($out -join ' ')" }
            }
            Add-PrinterDriver -Name $it.DriverName
        }

        $portName = $it.PortName
        $portHere = Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue
        if ($it.PortKind -eq 'vendor' -and -not $portHere) {
            # A vendor port (for example EpsonNet Print Port) is not on this PC; use a Standard TCP/IP port to the same address.
            $portName = "IP_$($it.HostAddress)"
            $portHere = Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue
            Write-Output "  '$($it.PortName)' is a $($it.PortMonitor) port that is not on this PC. Using Standard TCP/IP port '$portName' to $($it.HostAddress) (RAW 9100) instead."
        }
        if ($portHere) {
            # port already exists, nothing to create
        } elseif ($it.HostAddress) {
            if ($it.PortKind -ne 'vendor' -and ($it.Protocol -eq 'LPR' -or $it.Protocol -eq '2')) {
                Add-PrinterPort -Name $portName -LprHostAddress $it.HostAddress -LprQueueName ([string]$it.LprQueue)
            } else {
                $port = if ($it.PortNumber) { [int]$it.PortNumber } else { 9100 }
                Add-PrinterPort -Name $portName -PrinterHostAddress $it.HostAddress -PortNumber $port
            }
        } else {
            # USB, WSD or another port that cannot be created from a bundle: park the queue on the NUL: port
            # so it exists with its driver and settings, then the port is chosen by hand.
            if (-not (Get-PrinterPort -Name 'NUL:' -ErrorAction SilentlyContinue)) {
                throw "port '$($it.PortName)' does not exist on this PC and cannot be created from a bundle, and no placeholder port is available. Add the port by hand (Printer properties > Ports > Add Port), then run again"
            }
            $portName = 'NUL:'
            Write-Output "  PORT NEEDS YOU: '$($it.PortName)' cannot be copied, so '$($it.Name)' is created on the placeholder port NUL: and will not print yet."
            Write-Output "        To finish: Settings > Bluetooth & devices > Printers & scanners > '$($it.Name)' > Printer properties > Ports tab."
            Write-Output "        USB printer: plug it in, then tick its USB00x port. Network printer: Add Port > Standard TCP/IP Port > New Port > enter the printer's IP address, then tick it."
        }

        $existing = Get-Printer -Name $it.Name -ErrorAction SilentlyContinue
        if ($existing) {
            if ($Replace) {
                Remove-Printer -Name $it.Name
                Write-Output "  Removed existing queue"
            } else {
                Write-Output "  SKIPPED: a queue with this name already exists (tick 'Replace' to overwrite)"
                continue
            }
        }

        Add-Printer -Name $it.Name -DriverName $it.DriverName -PortName $portName `
            -Comment ([string]$it.Comment) -Location ([string]$it.Location)

        if ($it.DevMode) {
            $pending += $it
        } elseif ($it.TicketFile) {
            $tk = Join-Path (Join-Path $BundleDir 'tickets') $it.TicketFile
            if (Test-Path -LiteralPath $tk) {
                $xml = Get-Content -LiteralPath $tk -Raw -Encoding UTF8
                Set-PrintConfiguration -PrinterName $it.Name -PrintTicketXml $xml
                Write-Output "  Defaults applied"
            }
        }
        $done++
        Write-Output "  OK"
    } catch {
        Write-Output "  FAILED: $($_.Exception.Message)"
    }
}

# Tray, paper size and weight are stored in the queue's DEVMODE in the registry. The spooler caches it,
# so stop the spooler, write the saved values, then start it again.
if ($pending.Count -gt 0) {
    Write-Output "Applying saved tray and paper settings (restarting the print spooler)..."
    Stop-Service -Name Spooler -Force
    try {
        foreach ($it in $pending) {
            try {
                $rk = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey("SYSTEM\CurrentControlSet\Control\Print\Printers\$($it.Name)", $true)
                if (-not $rk) { throw 'queue key not found in registry' }
                $rk.SetValue('Default DevMode', [Convert]::FromBase64String($it.DevMode), [Microsoft.Win32.RegistryValueKind]::Binary)
                $dd = $rk.CreateSubKey('PrinterDriverData')
                foreach ($d in @($it.DriverData)) {
                    if (-not $d.Name) { continue }
                    switch ($d.Kind) {
                        'Binary'      { $dd.SetValue($d.Name, [Convert]::FromBase64String([string]$d.Value), 'Binary') }
                        'DWord'       { $dd.SetValue($d.Name, [int]$d.Value, 'DWord') }
                        'QWord'       { $dd.SetValue($d.Name, [long]$d.Value, 'QWord') }
                        'MultiString' { $dd.SetValue($d.Name, [string[]]@($d.Value), 'MultiString') }
                        default       { $dd.SetValue($d.Name, [string]$d.Value, 'String') }
                    }
                }
                # Drop a leftover per-user copy so the saved defaults are what the user sees.
                $uk = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Printers\DevModePerUser', $true)
                if ($uk) { $uk.DeleteValue($it.Name, $false) }
                Write-Output "  Settings applied to '$($it.Name)'"
            } catch {
                Write-Output "  FAILED to apply settings to '$($it.Name)': $($_.Exception.Message)"
            }
        }
    } finally {
        Start-Service -Name Spooler
    }
}
Write-Output "Done. $done queue(s) installed."
