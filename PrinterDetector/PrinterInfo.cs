namespace PrinterDetector;

public class PrinterInfo
{
    public required string Name { get; init; }
    public required string Driver { get; init; }
    public required string Status { get; init; }
    public required string ConnectionType { get; init; }
    public required string Address { get; init; }
    public string Consumables { get; set; } = "Checking...";
    public List<SupplyReading>? Supplies { get; set; }
    public bool IsWsd { get; init; }
    public bool IsRedirected { get; init; }
    public bool IsReadyPrint { get; init; }

    public string WarningFlags => string.Join(", ", new[]
    {
        IsWsd ? "⚠ WSD" : null,
        IsRedirected ? "⚠ Redirected (Easy Print)" : null,
        IsReadyPrint ? "⚠ Ready Print" : null
    }.Where(w => w != null));
}
