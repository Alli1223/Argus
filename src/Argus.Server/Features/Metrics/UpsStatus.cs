namespace Argus.Server.Features.Metrics;

/// <summary>Reads Network UPS Tools' status flags, such as "OB DISCHRG" or "OL CHRG LB".</summary>
public static class UpsStatus
{
    /// <summary>Running from its battery because line power is gone.</summary>
    public static bool OnBattery(string status) => Has(status, "OB");

    /// <summary>The battery is nearly empty: NUT shuts machines down at this point.</summary>
    public static bool LowBattery(string status) => Has(status, "LB");

    private static bool Has(string status, string flag) =>
        status.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(flag, StringComparer.OrdinalIgnoreCase);
}
