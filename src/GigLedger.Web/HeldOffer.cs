using GigLedger.Core;

namespace GigLedger.Web;

/// <summary>
/// FR-2b: an offer accepted with no shift open, held for one browser session until a shift
/// starts (SDD 6.9). Scoped, so one session's offer never reaches another.
/// </summary>
public sealed class HeldOffer
{
    /// <summary>The offer waiting for a shift, or null when nothing is held.</summary>
    public Offer? Offer { get; private set; }

    /// <summary>The moment accept was pressed: the trip's accept time and the shift's default start.</summary>
    public DateTimeOffset AcceptPressedAt { get; private set; }

    public void Hold(Offer offer, DateTimeOffset acceptPressedAt)
    {
        Offer = offer;
        AcceptPressedAt = acceptPressedAt;
    }

    public void Clear()
    {
        Offer = null;
        AcceptPressedAt = default;
    }
}
