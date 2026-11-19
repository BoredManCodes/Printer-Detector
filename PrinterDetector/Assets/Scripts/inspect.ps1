param(
    [Parameter(Mandatory)][string]$BundleDir,
    [Parameter(Mandatory)][string]$NamesFile,
    [switch]$Replace,
    [switch]$SavedOnly
)
# Prints what a bundle holds for the chosen queues and, unless -SavedOnly, what installing it on this PC would do.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$names = @(Get-Content -LiteralPath $NamesFile -Encoding UTF8 | Where-Object { $_.Trim() })
$m = Get-Content -LiteralPath (Join-Path $BundleDir 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json

$paperNames = @{ 1 = 'Letter'; 5 = 'Legal'; 7 = 'Executive'; 8 = 'A3'; 9 = 'A4'; 10 = 'A4 Small'; 11 = 'A5'; 12 = 'B4 (JIS)'; 13 = 'B5 (JIS)'; 14 = 'Folio'; 20 = 'Envelope #10'; 27 = 'Envelope DL'; 28 = 'Envelope C5'; 118 = 'A6' ; 70 = 'A6' }
$sourceNames = @{ 1 = 'Upper / Tray 1'; 2 = 'Lower / Tray 2'; 3 = 'Middle'; 4 = 'Manual'; 5 = 'Envelope'; 6 = 'Envelope manual'; 7 = 'Auto select'; 14 = 'Large capacity'; 15 = 'Form source / auto' }
$mediaNames = @{ 1 = 'Standard'; 2 = 'Transparency'; 3 = 'Glossy' }

# Reads the standard fields of a DEVMODE. The driver-private part that follows is opaque and is summarised only.
function Read-DevMode([byte[]]$b) {
    $o = [ordered]@{}
    if ($b.Length -lt 220) { return $o }
    $o['Device name'] = [Text.Encoding]::Unicode.GetString($b, 0, 64).Split([char]0)[0]
    $o['Driver version'] = [BitConverter]::ToUInt16($b, 66)
    $dmSize = [BitConverter]::ToUInt16($b, 68)
    $extra = [BitConverter]::ToUInt16($b, 70)
    $f = [BitConverter]::ToUInt32($b, 72)
    if ($f -band 0x1) { $v = [BitConverter]::ToInt16($b, 76); $o['Orientation'] = $(if ($v -eq 1) { 'Portrait' } elseif ($v -eq 2) { 'Landscape' } else { "$v" }) }
    if ($f -band 0x2) { $v = [BitConverter]::ToInt16($b, 78); $nm = $paperNames[[int]$v]; $o['Paper size'] = $(if ($nm) { "$nm ($v)" } else { "code $v" }) }
    if ($f -band 0x4) { $o['Paper length (0.1 mm)'] = [BitConverter]::ToInt16($b, 80) }
    if ($f -band 0x8) { $o['Paper width (0.1 mm)'] = [BitConverter]::ToInt16($b, 82) }
    if ($f -band 0x10) { $o['Scale %'] = [BitConverter]::ToInt16($b, 84) }
    if ($f -band 0x100) { $o['Copies'] = [BitConverter]::ToInt16($b, 86) }
    if ($f -band 0x200) { $v = [BitConverter]::ToInt16($b, 88); $nm = $sourceNames[[int]$v]; $o['Input tray'] = $(if ($nm) { "$nm ($v)" } else { "driver-specific code $v" }) }
    if ($f -band 0x400) { $o['Print quality'] = [BitConverter]::ToInt16($b, 90) }
    if ($f -band 0x800) { $o['Colour'] = $(if ([BitConverter]::ToInt16($b, 92) -eq 2) { 'Colour' } else { 'Monochrome' }) }
    if ($f -band 0x1000) { $v = [BitConverter]::ToInt16($b, 94); $o['Duplex'] = $(switch ($v) { 1 { 'Off' } 2 { 'Long edge' } 3 { 'Short edge' } default { "$v" } }) }
    if ($f -band 0x2000) { $o['Y resolution (dpi)'] = [BitConverter]::ToInt16($b, 96) }
    if ($f -band 0x8000) { $o['Collate'] = $(if ([BitConverter]::ToInt16($b, 100) -eq 1) { 'On' } else { 'Off' }) }
    if ($f -band 0x10000) { $o['Form name'] = [Text.Encoding]::Unicode.GetString($b, 102, 64).Split([char]0)[0] }
    if ($f -band 0x1000000) { $v = [BitConverter]::ToUInt32($b, 196); $nm = $mediaNames[[int]$v]; $o['Media type'] = $(if ($nm) { "$nm ($v)" } else { "driver-specific code $v" }) }
    $priv = [Math]::Max(0, $b.Length - $dmSize)
    $o['Driver-private data'] = "$priv bytes (holds the driver's own tray, weight and finishing choices)"
    return $o
}

function Get-PrivateBytes([byte[]]$b) {
    if ($b.Length -lt 220) { return ,([byte[]]@()) }
    $dmSize = [BitConverter]::ToUInt16($b, 68)
    if ($b.Length -le $dmSize) { return ,([byte[]]@()) }
    $r = New-Object byte[] ($b.Length - $dmSize)
    [Array]::Copy($b, $dmSize, $r, 0, $r.Length)
    return ,$r
}

function Write-Ticket([string]$xmlText) {
    try {
        $x = [xml]$xmlText
        $ns = New-Object Xml.XmlNamespaceManager($x.NameTable)
        $ns.AddNamespace('psf', 'http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework')
        $feat = $x.SelectNodes('//psf:Feature', $ns)
        if ($feat.Count -eq 0) { Write-Output '    (no features)'; return }
        foreach ($f in $feat) {
            $opt = $f.SelectSingleNode('psf:Option', $ns)
            $optName = if ($opt) { $opt.GetAttribute('name') } else { '' }
            Write-Output ('    {0} = {1}' -f $f.GetAttribute('name'), $optName)
        }
    } catch {
        Write-Output "    (could not read ticket: $($_.Exception.Message))"
    }
}

$found = 0
foreach ($it in @($m.Printers)) {
    if ($names -notcontains $it.Name) { continue }
    $found++
    Write-Output ('=' * 70)
    Write-Output "QUEUE: $($it.Name)"
    Write-Output ('=' * 70)
    Write-Output 'SAVED IN BUNDLE'
    Write-Output "  Driver      : $($it.DriverName)$(if ($it.DriverDir) { "  (package $($it.InfName) is in the bundle)" } else { '  (built into Windows)' })"
    $dest = if ($it.PortKind -eq 'vendor') { "$($it.HostAddress)  (address taken from the $($it.PortMonitor) port)" }
            elseif ($it.HostAddress) { "$($it.HostAddress)  $($it.Protocol) port $($it.PortNumber)" }
            else { "not a TCP/IP port ($($it.PortMonitor))" }
    Write-Output "  Port        : $($it.PortName)  ->  $dest"
    if ($it.Comment) { Write-Output "  Comment     : $($it.Comment)" }
    if ($it.Location) { Write-Output "  Location    : $($it.Location)" }

    $saved = $null
    if ($it.DevMode) {
        $bytes = [Convert]::FromBase64String($it.DevMode)
        $saved = $bytes
        Write-Output "  Settings    : $($bytes.Length) bytes, taken from the queue's $($it.DevModeFrom)"
        $dm = Read-DevMode $bytes
        foreach ($k in $dm.Keys) { Write-Output ('    {0,-22}: {1}' -f $k, $dm[$k]) }
    } else {
        Write-Output '  Settings    : none saved (the print ticket below is used instead)'
    }
    $opts = @($it.DriverData)
    Write-Output "  Device options (installed trays and accessories): $($opts.Count)"
    foreach ($d in $opts) {
        if (-not $d.Name) { continue }
        $val = if ($d.Kind -eq 'Binary') { "binary, $([Convert]::FromBase64String([string]$d.Value).Length) bytes" } else { (@($d.Value) -join ', ') }
        Write-Output ('    {0} [{1}] = {2}' -f $d.Name, $d.Kind, $val)
    }
    if ($it.TicketFile) {
        $tk = Join-Path (Join-Path $BundleDir 'tickets') $it.TicketFile
        if (Test-Path -LiteralPath $tk) {
            Write-Output "  Print ticket ($(if ($it.DevMode) { 'saved for reference, not applied because the settings above are used' } else { 'will be applied' })):"
            Write-Ticket (Get-Content -LiteralPath $tk -Raw -Encoding UTF8)
        }
    }

    if ($SavedOnly) { Write-Output ''; continue }

    Write-Output ''
    Write-Output 'WHAT INSTALLING ON THIS PC WILL DO'
    if (Get-PrinterDriver -Name $it.DriverName -ErrorAction SilentlyContinue) {
        Write-Output "  Driver : already installed here, no change"
    } elseif ($it.DriverDir) {
        Write-Output "  Driver : install $($it.InfName) from the bundle with pnputil, then add '$($it.DriverName)'"
    } else {
        Write-Output "  Driver : add '$($it.DriverName)' from Windows"
    }
    $port = Get-PrinterPort -Name $it.PortName -ErrorAction SilentlyContinue
    if ($it.PortKind -eq 'vendor' -and -not $port) {
        $ipPort = Get-PrinterPort -Name "IP_$($it.HostAddress)" -ErrorAction SilentlyContinue
        if ($ipPort) { Write-Output "  Port   : '$($it.PortName)' ($($it.PortMonitor)) is not on this PC. The existing Standard TCP/IP port 'IP_$($it.HostAddress)' will be used." }
        else { Write-Output "  Port   : '$($it.PortName)' ($($it.PortMonitor)) is not on this PC. A Standard TCP/IP port 'IP_$($it.HostAddress)' (RAW 9100) will be created and the queue pointed at it." }
    } elseif ($port) {
        $hostNow = [string]$port.PrinterHostAddress
        if ($it.HostAddress -and $hostNow -and $hostNow -ne $it.HostAddress) {
            Write-Output "  Port   : '$($it.PortName)' already exists here but points to $hostNow, not $($it.HostAddress). The existing port is kept."
        } else {
            Write-Output "  Port   : '$($it.PortName)' already exists, no change"
        }
    } elseif ($it.HostAddress) {
        Write-Output "  Port   : create $($it.Protocol) port '$($it.PortName)' to $($it.HostAddress)"
    } else {
        Write-Output "  Port   : '$($it.PortName)' cannot be created from a bundle (USB or WSD). The queue will be created on the placeholder port NUL:"
        Write-Output "           and you then choose the port: Printer properties > Ports tab, tick the USB port, or Add Port > Standard TCP/IP Port."
    }

    $local = Get-Printer -Name $it.Name -ErrorAction SilentlyContinue
    if ($local) {
        if ($Replace) { Write-Output "  Queue  : a queue with this name exists and will be REMOVED and recreated" }
        else { Write-Output "  Queue  : a queue with this name exists, so it will be SKIPPED (tick Replace to overwrite)" }
    } else {
        Write-Output "  Queue  : create '$($it.Name)'"
    }

    if ($it.DevMode -and ($Replace -or -not $local)) {
        Write-Output "  Settings: write the $($saved.Length) byte settings block and $($opts.Count) device option(s) into the queue,"
        Write-Output "            delete this user's own copy for the queue, and restart the print spooler"
    } elseif ($it.TicketFile -and ($Replace -or -not $local)) {
        Write-Output "  Settings: apply the saved print ticket (no saved settings block)"
    } elseif ($local) {
        Write-Output "  Settings: unchanged"
    }

    if ($local -and $saved) {
        Write-Output ''
        Write-Output '  COMPARED WITH THE QUEUE CURRENTLY ON THIS PC'
        $cur = $null
        $uk = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Printers\DevModePerUser')
        if ($uk) { $cur = $uk.GetValue($it.Name) }
        if (-not ($cur -is [byte[]]) -or $cur.Length -eq 0) {
            $rk = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey("SYSTEM\CurrentControlSet\Control\Print\Printers\$($it.Name)")
            $cur = $null
            if ($rk) { $cur = $rk.GetValue('Default DevMode') }
        }
        if ($cur -is [byte[]] -and $cur.Length -gt 0) {
            $a = Read-DevMode $cur
            $b2 = Read-DevMode $saved
            $diff = 0
            foreach ($k in $b2.Keys) {
                if ($k -eq 'Driver-private data') { continue }
                if ("$($a[$k])" -ne "$($b2[$k])") { Write-Output ('    {0,-22}: {1}  ->  {2}' -f $k, $a[$k], $b2[$k]); $diff++ }
            }
            $pa = Get-PrivateBytes $cur
            $pb = Get-PrivateBytes $saved
            $n = [Math]::Min($pa.Length, $pb.Length)
            $pd = [Math]::Abs($pa.Length - $pb.Length)
            for ($i = 0; $i -lt $n; $i++) { if ($pa[$i] -ne $pb[$i]) { $pd++ } }
            Write-Output "    Driver-private data   : $pd byte(s) differ (tray, weight and finishing choices live here)"
            if ($diff -eq 0 -and $pd -eq 0) { Write-Output '    The saved settings are identical to the current ones.' }
        } else {
            Write-Output '    (could not read the current settings)'
        }
    }
    Write-Output ''
}
if ($found -eq 0) { Write-Output 'None of the selected queues were found in the bundle.' }
