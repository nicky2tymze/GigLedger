namespace GigLedger.Core;

/// <summary>An offer as the platform presents it (FR-1).</summary>
/// <param name="Pay">The total the platform shows at the accept screen, tip included (FR-1).</param>
/// <param name="ReturnMilesOverride">The driver's own return estimate; null means use the stated miles (FR-13).</param>
/// <param name="PromisedTip">The tip inside the pay, read from the opened offer; null means untracked (FR-6a).</param>
public sealed record Offer(
    Graded<decimal> Pay,
    Graded<decimal> StatedMiles,
    int Drops,
    int Items,
    Graded<int> EstimatedMinutes,
    DateTimeOffset OfferedAt,
    decimal? ReturnMilesOverride = null,
    Graded<decimal>? PromisedTip = null);

/// <summary>What actually happened on an accepted trip (FR-3).</summary>
public sealed record TripActuals(int ElapsedMinutes, decimal RouteMiles, decimal ReturnMiles);

/// <summary>A trip's pay, actuals, and posted tip, as a shift summary needs them.</summary>
public sealed record TripRecord(decimal Pay, TripActuals Actuals, decimal Tip = 0m);

/// <summary>The span of a shift, bounded by clock and odometer (FR-4).</summary>
public sealed record ShiftSpan(int ClockMinutes, decimal StartOdometer, decimal EndOdometer);

/// <summary>The accept rule's answer for one offer (FR-12).</summary>
public enum Verdict { Clears, DoesNotClear }

/// <summary>The energy figures a cost calculation rests on, and where they came from.</summary>
public sealed record EnergyBasis(decimal MilesPerKwh, decimal PricePerKwh, IReadOnlyList<Assumption> Assumptions)
{
    /// <summary>Slice 1: both figures come from settings, so both are assumptions (FR-10).</summary>
    public static EnergyBasis FromDefaults(decimal defaultMilesPerKwh, decimal defaultPricePerKwh) =>
        new(defaultMilesPerKwh, defaultPricePerKwh,
        [
            new Assumption("efficiency", $"default {defaultMilesPerKwh} mi/kWh"),
            new Assumption("energy price", $"default ${defaultPricePerKwh}/kWh"),
        ]);
}

/// <summary>One shift, summarized (FR-18 to FR-20). TripRate and RateGap are null when there were no trips.</summary>
public sealed record ShiftSummary(
    int Trips,
    decimal Gross,
    Result EnergyCost,
    Result Net,
    decimal ClockHours,
    decimal TripHours,
    decimal ShiftMiles,
    decimal DeadheadMiles,
    Result ShiftRate,
    Result? TripRate,
    decimal? RateGap,
    decimal Tips = 0m);

