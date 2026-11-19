param(
    [Parameter(Mandatory)][string]$NamesFile
)
# Removes the listed print queues from this PC.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$names = @(Get-Content -LiteralPath $NamesFile -Encoding UTF8 | Where-Object { $_.Trim() })
$done = 0
foreach ($n in $names) {
    try {
        Write-Output "Removing '$n'..."
        Remove-Printer -Name $n
        $done++
        Write-Output "  OK"
    } catch {
        Write-Output "  FAILED: $($_.Exception.Message)"
    }
}
Write-Output "Done. $done of $($names.Count) queue(s) removed."
