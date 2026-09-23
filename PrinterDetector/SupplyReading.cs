namespace PrinterDetector;

public class SupplyReading
{
    public required string Description { get; init; }
    public int? Percent { get; init; }
    public Color BarColor { get; init; } = Color.SteelBlue;
}

public class ConsumablesResult
{
    public required string Summary { get; init; }
    public List<SupplyReading>? Supplies { get; init; }
}
