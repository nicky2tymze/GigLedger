# GigLedger Slice 4: what the road actually does

**Type:** Regular Sprint · **Opened:** 2026-10-02 · **Status:** OPEN

Raised during a walkthrough with a working Spark driver, who will test it.

## Goal

Record the parts of a shift that today leave no trace: offers turned down, trips that got cancelled,
tips that change after the fact, and numbers that cannot be real.

## Order of work (SDD section 1, unchanged)

For each story: the requirement goes into the SRS first, then the tests are written from it and seen
failing, then the code, then the screen. Every acceptance criterion below is checkable in the session
it is worked in.

## Stories

| # | Story | Done when |
|---|---|---|
| 1 | **Decline an offer, with reasons.** An evaluated offer can be declined. The decline is stored with the offer's numbers, one or more reasons from a fixed list, an optional note, and the time. Never deleted. | SRS requirement added; tests first; the Offer page has a Decline action; declines appear in a report by reason. |
| 2 | **Implausible values.** Two levels. *Impossible* values are refused with a sentence saying why (for example: implied speed over a set limit, one charge larger than the battery, a start time in the future, a shift over 24 hours). *Implausible* values are allowed only after the driver confirms, and the record is marked confirmed. | SRS requirement with the limits stated as data, not code; tests for each limit; the screens show the refusal or the confirm step. |
| 3 | **Tip adjustments, opt-in.** Off by default. When on: the tip the offer promised is recorded at accept (the total across all drops); tips that post are recorded per posting, per drop where the platform gives it; the trip shows *tip pending* until complete; the adjustment (promised against posted) is computed only once complete. With it off, behaviour is today's FR-6. | SRS requirement extending FR-6; tests for single-drop, multi-drop, partial posting, later change; a setting to turn it on; a report of adjustments. |
| 4 | **Cancelled trips.** A trip can end cancelled: who cancelled (customer, store, platform, driver), when (before pickup, after pickup, at the door), what it paid (full, partial, none), and the miles and minutes actually driven, including any return to the store. Those still count against the shift. | SRS requirement; tests; a Cancel action on an open trip; the shift summary includes cancelled trips' cost; a cancellation report. |

## Open questions

1. **The decline reasons.** Draft list: heavy item (40 lb+), alcohol, stairs (3rd floor+, no
   elevator), pay too low, too far / long return, too many drops, too many items, apartment complex,
   bad area or road, low charge / range, ending the shift, other (with a note).
2. **Does Spark's earnings workbook list tips per drop, or only per trip?** If per trip only, the
   pieces of a multi-drop tip cannot be told apart, and the trip stays pending until the total posts.
3. **The implausible limits:** the numbers for each (speed, trip length, pay, tip, shift length).

## Already works (checked 2026-10-02, not a story)

Evaluating an offer without a shift: the Offer page evaluates with no shift open and says *"No shift
is open. Start a shift to accept an offer."* (`OfferPage.razor`).

## Retro

At close: what this slice did badly.

## Log

- 2026-10-02: Opened on the Architect's word, four stories.
