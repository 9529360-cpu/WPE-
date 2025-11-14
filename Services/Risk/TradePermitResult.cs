namespace 币安量化机器人.Services.Risk;

public record TradePermitResult
{
    public bool Allowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public object? Metadata { get; init; }

    public static TradePermitResult Permit() => new() { Allowed = true };
    public static TradePermitResult Deny(string reason, object? metadata = null) => new() { Allowed = false, Reason = reason, Metadata = metadata };
}
