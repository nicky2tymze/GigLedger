using System.Globalization;

namespace GigLedger.Web;

/// <summary>
/// How numbers are shown. Formatting only: every value arrives computed from Core, and
/// this is the one place it is rounded, for display (SDD 6.4).
/// </summary>
public static class Display
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal value) => value.ToString("C2", Us);
    public static string Rate(decimal value) => value.ToString("C2", Us) + "/hr";
    public static string PerMile(decimal value) => value.ToString("C2", Us) + "/mi";
    public static string Share(decimal value) => value.ToString("P0", Us);
    public static string Miles(decimal value) => value.ToString("#,0.0", Us) + " mi";
    public static string Hours(decimal value) => value.ToString("0.00", Us) + " h";
    public static string Time(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm", Us);
    public static string Efficiency(decimal value) => value.ToString("0.00", Us) + " mi/kWh";
    public static string PricePerKwh(decimal value) => value.ToString("C2", Us) + "/kWh";
}
