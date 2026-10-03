# GigLedger: Software Requirements Specification

**Version:** 0.10 · 2026-10-03 · DRAFT, for review. 0.10 adds cancelled trips (FR-3a, FR-21b) and defaults a one-drop trip's return miles to its route miles (FR-3). 0.9 drops the tip-tracking setting: the accept screen always offers the promised tip, and entering it is what tracks a trip; the tip can be added or changed after accept (FR-6a). 0.8 makes an offer's pay the total the platform shows, tip included, and **reverses 0.2's tip rule** (FR-6): a recorded tip is never added on top of that total; optional tip tracking (FR-6a) records the promised tip and what posts. 0.7 adds entry checks (section 5.9, FR-35 to FR-38): values past a limit are confirmed or explained, never silently stored, and values that cannot be true are refused. 0.6 records declined offers with their reasons (FR-2a, FR-21a), **reversing 0.2's "no declined offers"** (see FR-2), and lets accept start a shift (FR-2b). 0.5 allows a charge session's odometer, state of charge, and purpose to be unknown, since a charging receipt carries none of them, specifies the receipt import (FR-22) and the payout import (FR-23), and makes imported payouts the record for reconciliation where they exist (FR-31). 0.4 settles four Slice 3 decisions: a place and purpose on every drive, shift spans logged as business, a reason on every correction, receipts stored in the database. 0.3 added the odometer to a charge session, fixes the efficiency method and the energy window, and sets a placeholder home rate. 0.2 closed the four open questions: tips per trip, no declined offers, home and fast charging separated, vehicle not shared.
**Author:** Dominick Trolian
**Stack:** C# / .NET 8 · ASP.NET Core · Blazor · EF Core + SQLite · xUnit

---

## 1. Purpose

GigLedger records the work of a delivery driver and reports what the work actually paid, per
offer, per shift, and per mile, after energy cost. It is also the driver's **system of record**:
it keeps every entry permanently, for record keeping and for tax preparation.

Delivery platforms show a driver three numbers at the accept screen: pay, stated miles, and an
estimated time. Measured over several weeks of real driving, those numbers leave out much of what
the driver needs to decide:

- **Stated miles stop at the last drop.** The drive back is unpaid and unreported. On measured
  days it ran 43% to 50% of a run's miles.
- **Pay is gross.** Fast charging consumed about 22% of driving gross over two independent windows
  that agreed within about 2%.
- **Tips post later.** Tips were about half of trip earnings and arrive hours after the trip. The
  accept screen shows one total with the tip inside it, visible only by opening the offer, and what
  posts later can differ from what was promised.

GigLedger turns each of those into a measured number and applies the driver's own accept rule to
each offer.

## 2. Scope

**In scope:** one driver, one vehicle, one platform at a time (the data model allows more).
Electric vehicle first; the energy model accepts a gasoline vehicle through the same interface.

**Out of scope for 0.x:** multi-user accounts, platform integrations or scraping, route
optimization, and filing a return. Tax *preparation* is in scope (section 5.7); filing is not.

## 3. Definitions

| Term | Meaning |
|---|---|
| **Offer** | One trip as the platform presents it: pay, stated miles, drops, items, estimated time. |
| **Trip** | An accepted offer, with what actually happened: actual time, route miles, return miles. |
| **Shift** | A span of work, bounded by odometer and clock readings at start and end. Contains trips and the time between them. |
| **Deadhead** | Miles driven for work that no trip pays for: returns, repositioning. |
| **Charge session** | One charging event: kWh delivered, cost, start and end state of charge, location. |
| **Gross** | What the platform pays, including tips once posted. |
| **Net** | Gross minus the energy cost of the miles that produced it. |
| **Forecast** | A number computed at the accept screen, before the trip happens. |
| **Grade** | Where a number came from: `stated` (platform), `entered` (driver), `measured` (odometer, receipt), `derived` (computed by GigLedger). |

## 4. The accept rule

The driver's rule, in his words: *"If I don't think I will make 25+ I don't take it."*

- It is a **per-offer forecast of net dollars per hour**, made at the accept screen.
- It is **not** a shift grade and **not** a flat $25 per offer.
- The threshold is a setting, default $25.00/hr.

GigLedger shows the forecast and the rule's verdict. The driver decides.

## 5. Functional requirements

### 5.1 Capture

- **FR-1** Record an offer with pay, stated miles, drops, items, the platform's time estimate, and
  the time it was offered. **Pay is the total the platform shows at the accept screen, tip
  included** (0.8; it is what the driver enters and decides on).
- **FR-2** Record the time an offer was accepted. The forecast in 5.3 can be run on any offer before
  deciding; an offer that is only evaluated, neither accepted nor declined, is not kept.
  **Reversal (0.6):** 0.2 decided that declined offers are not recorded. That left every decline with
  no trace: which offers were turned down, why, and whether the accept rule agreed. A decline is a
  decision made on real numbers, so 0.6 records it (FR-2a).
- **FR-2a Decline an offer, with reasons.** An evaluated offer can be declined. The decline stores
  the offer's numbers (FR-1), the forecast and the rule's verdict at that moment (FR-11, FR-12), the
  time, **one or more reasons** from the list below, and an optional note. **"Other" requires the
  note.** **A decline needs an open shift** and belongs to it; with no shift open there is nothing to
  decline (the Architect, 2026-10-03). A decline is
  never deleted (FR-25). The reasons, a fixed list shown in this order:
  pay too low · too far (includes bad geometry) · too many items · pharmacy · alcohol · heavy ·
  stairs · apartment · too many drops (batched orders) · low charge · ending shift · other.
- **FR-2b Accept starts a shift.** Accepting an offer with no shift open takes the driver straight to
  starting a shift (FR-4), with the offer held. Once the shift is started, the held offer comes back
  and the accept completes from there. The shift's start time defaults to the moment accept was
  pressed, so the accepted trip is never earlier than its shift. If no shift is started, the held
  offer is evaluated only and is not kept (FR-2). (The Architect, 2026-10-03.)
- **FR-3** For an accepted offer, record actual elapsed time, actual route miles and return miles. **On a
  one-drop trip the return miles default to the route miles** (the Architect, 2026-10-03); the driver can
  enter a different figure.
- **FR-3a Cancelled trips.** An accepted trip whose actuals are not yet recorded can be cancelled
  instead. The cancel records **the time it was cancelled**, **who cancelled** (customer, store,
  platform, or the driver), and **when** (before pickup, after pickup, or at the door). Only a driver
  cancel asks **why**, from this list in this order: order not ready · unreachable · wrong address ·
  customer issue (for example dogs, attitude) · emergency · car problems · other; an optional note,
  required with "other". "Customer cancelled" is not a reason: the who-cancelled field carries it.
  - **Not shopped** (before pickup): it paid **nothing**, has **0 miles**, and **no trip time**; the
    time spent becomes the shift's unpaid time, like waiting between trips.
  - **Shopped** (after pickup, or at the door): it paid **the pay minus the tip**. If no promised tip
    was entered (FR-6a), the cancel asks for it. The driver enters the **minutes**, which count as
    trip time, and the **route and return miles** actually driven, the route starting from the
    offer's stated miles, and the return defaulting to the route miles when the offer had one drop;
    the **mileage adjustment** (route miles minus stated miles) is shown. The entry checks apply as
    to any actuals (FR-35 to FR-37).
  - A cancel is stored once and never deleted (FR-25). A tip that posts on a cancelled trip is
    recorded and added to what it paid, since that pay excludes the tip.
- **FR-4** Record a shift with start and end odometer readings and start and end times.
- **FR-5** Record a charge session with kWh, cost, start and end state of charge, **the odometer
  reading at the session**, charger, and whether the energy was for work or personal driving. Every session has a **charge type**: home
  or DC fast. A home session usually has no receipt, so its cost is its kWh times the home
  electricity rate, which is stored as data with an effective date, and the cost is graded
  `derived`. Until the rate is read from a bill, a **placeholder of $0.15/kWh** is used, and every
  number that depends on it says so (NFR-2). **The odometer, the state of charge, and the
  purpose may be unknown** (a receipt carries none of them). Unknown is recorded as unknown, never as zero or a guess.
- **FR-6** Record a posted tip against its trip: amount and date posted. A trip can have **one or more**
  posted tips (the platform may post a trip's tip in pieces); they are summed, with no breakdown by
  order or drop. **A posted tip is never added on top of the trip's pay**, since pay already includes
  the tip (FR-1); without tip tracking (FR-6a) it is kept as a record only.
  **Reversal (0.8):** 0.2 decided one tip per trip, added to pay. Measured against how pay is entered,
  that counts every tip twice. The tip lives inside the pay.
- **FR-6a Tip tracking.** The accept screen always offers the **promised tip**: the total tip across
  all drops, read from the opened offer. It is optional, and it cannot exceed the pay. Entering it is
  what tracks the trip (0.9: the on/off setting of 0.8 is dropped, the Architect, 2026-10-03). **Every
  tip field is optional:** with none used, the whole pay counts as the platform's fee and nothing as
  tip; a driver who does not want the split for taxes need not enter it. The promised tip can be
  **added after accept** (no reason needed while the shift is open: it fills a blank) or **changed**
  (with a one-line reason, FR-25), at any time, including after *all tips in*; the gross and the
  adjustment follow. **Adding it after the trip's shift has closed needs the reason too** (the Architect,
  2026-10-03: "to add a tip after a shift is complete, requires explanation"). Posted tips need none:
  they normally arrive after the shift. The trip's
  **base** is pay minus the promised tip. The trip shows **tip pending** until the driver marks it
  **all tips in**; until then its gross is the pay. Once marked, its gross is the base plus the
  posted tips, and the **adjustment** (posted minus promised) is shown. A trip accepted without a
  promised tip is untracked: its gross is its pay, whatever posts. **The platform lets a customer
  change a tip for 24 hours after delivery**, so *all tips in* is offered only once 24 hours have
  passed since the trip ended (its accept time plus its actual minutes). **A tip that posts after
  *all tips in* is accepted**, and the gross and the adjustment are recomputed (the Architect,
  2026-10-03).
- **FR-7** Every stored number carries its grade (section 3). A value entered by the driver is
  never displayed as measured.

### 5.2 Energy model

- **FR-8** Compute **measured efficiency** (miles per kWh) wall to wheel: the miles between the
  odometer readings of two charge sessions, divided by the kWh bought from the first of them up to
  but not including the last. This counts charging losses, which the driver pays for. It is exact
  when the battery arrived at the same state of charge at both sessions; when it did not, or either
  is unknown, the result carries that as an assumption. The two sessions are the first and last
  in time with a known odometer; energy bought between them counts even where the odometer is
  unknown.
- **FR-9** Compute **energy cost per mile** from measured efficiency and the average cost per kWh
  over a chosen window, reported three ways: home only, fast only, and the actual blend for the
  window. Reports show how much of the energy cost came from fast charging.
- **FR-9a** A shift's and a trip's energy cost use the **30 days of charging before the shift
  started**. Where that window has too little data to measure (fewer than two sessions for
  efficiency, none for price), the configured default is used and named (FR-10).
- **FR-10** When no measured efficiency exists yet, use a configured default and mark every result
  that depends on it as `derived from default`.

### 5.3 Per offer

- **FR-11** At capture, compute the **forecast net $/hr**: pay, minus the energy cost of stated
  miles plus a return estimate, divided by the platform's time estimate.
- **FR-12** Show the accept rule's verdict for the offer: clears or does not clear the threshold.
- **FR-13** The return estimate defaults to the offer's stated miles (a one-way route returns the
  same distance). The driver can override it.

### 5.4 Per trip, after the fact

- **FR-14** Compute actual gross $/hr, actual net $/hr, and true $/mile (pay over route plus
  return miles).
- **FR-15** Compute the **deadhead share**: return miles over total miles for the trip.
- **FR-16** Compare the platform's time estimate to actual time, and stated miles to actual
  miles, and report the error of each.
- **FR-17** A trip's rates use its gross (FR-6a). On a tracked trip the rates on its base are shown
  beside them; once all tips are in, the rates use the base plus the posted tips.

### 5.5 Per shift

- **FR-18** Compute shift miles from odometer readings, and deadhead miles as shift miles minus
  the sum of paid route miles.
- **FR-19** Compute **shift rate** (all pay over all clock time) next to **trip rate** (all pay
  over time spent on trips), and show the gap between them.
- **FR-20** Summarize a shift: trips, gross, energy cost, net, hours, and miles.

### 5.6 Reporting and import

- **FR-21** Report by day, week, and custom range, with totals and rates.
- **FR-21a** Report declines for a range: count by reason (an offer with two reasons counts under
  both), and how many declined offers the accept rule said would clear, against how many it said
  would not.
- **FR-21b** Report cancellations for a range: how many, by who cancelled, by when, and by the
  driver's reason; what they paid; and the miles driven on shopped cancels.
- **FR-22** Import charge sessions from a CSV of charging receipts, one row per receipt, read by
  column name: `receipt_number`, `start_local`, `timezone`, `kwh`, `net_total`, `station_id`,
  `location_name`. Each row becomes a DC fast session with kWh and cost graded measured, and its
  odometer, state of charge, and purpose unknown (FR-5). **All or nothing:** a row that cannot be
  read, or that FR-5 would refuse, stops the whole file and names its line. A receipt number
  already in the ledger is skipped, so importing the same file twice stores it once.
- **FR-23** Import payouts from the platform's earnings export. For Spark that is an .xlsx whose
  Transactions sheet has one row per payment. Each row becomes a **payout**: platform, the
  platform's trip ID, the time and its zone as printed, the type (trip earnings, tip, incentive,
  adjustment credit), the amount graded stated, and the deposit status and date. Payouts stand on
  their own, keyed by the platform's trip ID; they need no shift or trip logged in GigLedger.
  **All or nothing:** a row that cannot be read, or an earning type not listed here, stops the
  file and names the row. **The file checks itself:** the sum of its rows must equal the total on
  its Summary sheet, or the file is refused. Re-importing stores only what is not already stored,
  matching identical rows by count, so two real identical tips stay two. Spark labels every time
  `CDT`, winter included; the label is stored as printed and read as UTC−5, an assumption the file
  cannot settle.
- **FR-24** Export any report to CSV.

### 5.7 Record keeping and tax preparation

- **FR-25 Nothing is deleted.** Every entry is kept permanently. A correction is stored as a new
  version that points to the entry it replaces; the original stays readable, with who changed it
  and when. **Every correction carries a one-line reason**, shown in the record's history beside
  the original.
- **FR-26 Mileage log.** Every drive carries date, start and end odometer, miles, a purpose
  (business or personal), and **a short statement of where and why**. The business mileage log can
  be printed or exported per year in a form that meets a contemporaneous-log standard.
- **FR-26a** A shift's odometer span is logged as a business drive automatically. The drive from
  home to where a shift starts, and back, is logged as its own drive, and the driver sets its
  purpose. GigLedger records; it does not decide what is deductible.
- **FR-27 Receipts.** Any charge session, payout, or expense can have its source document attached
  (a receipt image, PDF, or export file). The file is stored **inside the database**, so one file
  still backs up the whole ledger (NFR-7), and its SHA-256 hash is kept so a later change to the
  file is detectable.
- **FR-28 Expenses.** Record business expenses beyond charging (phone, parking, tolls, supplies,
  vehicle costs) with category, amount, date, and receipt.
- **FR-29 Annual tax summary.** For a tax year: gross earnings by platform, business miles,
  personal miles, charging and other expenses by category, and both deduction methods side by
  side, **standard mileage** (business miles times that year's rate) and **actual expenses**.
  GigLedger shows both and does not choose.
- **FR-30 Rates are data.** The standard mileage rate is stored per tax year and entered from the
  published figure, never hard-coded.
- **FR-31 Platform reconciliation.** Total recorded payouts per platform per year can be compared
  against the platform's annual tax form (1099-K or 1099-NEC), and any difference is listed by
  month. **Which record:** where the platform's own payouts are imported for the year (FR-23),
  they are the record, counted in the month of the transaction; otherwise the trips logged in
  GigLedger are: a trip's gross in the month it was accepted, except that on a tracked trip with all
  tips in, the base counts in the month accepted and each posted tip in the month it posted. The two are never added together, since the export already contains
  every logged trip. The reconciliation, and the gross in the tax summary (FR-29), name the source
  they used.
- **FR-32 Full export.** The entire ledger, including attachments, exports to an open format
  (CSV plus the attached files) for an accountant or an audit.

### 5.8 Agent client (AOA)

- **FR-33** Expose the core through a JSON API with one endpoint per capture and query operation,
  so an AI assistant can log an offer or answer "did that shift clear 25?" by calling the same
  operations the UI calls.
- **FR-34** The API and the UI call the same core services. Neither contains business logic the
  other lacks.

### 5.9 Entry checks (Slice 4)

Their purpose is to catch entry errors at the moment of entry: a mistyped miles or minutes figure
inflates the mileage deduction or wrecks $/hr. The driver's rule: *"anything above the ridiculous
threshold requires documentation to be used."*

- **FR-35 Three levels.** A checked value at or below its **confirm** limit is stored as entered.
  Above it, the entry is stored only after the driver confirms it, and the record is marked
  **confirmed**. Above the **document** limit, the entry is stored only with a written explanation
  and a second, deliberate confirm, and the record is marked **explained**. Nothing past a limit is
  refused (an explained value may be real: "maybe there is a reason the calculation shows the
  anomaly"). The driver authorizes their own entries; there are no roles.
- **FR-36 The limits are data**, stored as versions like the settings (FR-25), never in code. Initial
  values, and where each is checked:

  | Limit | Checked when | Confirm above | Document above |
  |---|---|---|---|
  | Pay per trip (base, before tip) | an offer is accepted or declined | $80 | $250 |
  | Speed: (route + return miles) over elapsed time | a trip's actuals are entered | 60 mph | 90 mph |
  | Trip length: route + return miles | a trip's actuals are entered | 50 mi | 150 mi |
  | Tip: the promised tip, and each posted tip | an offer is accepted; a tip is recorded | $40 | $150 |
  | Shift length | a shift ends | 12 h | 16 h |

  The offer's stated miles and time estimate are not checked: they feed only the forecast, never the
  mileage log.
- **FR-37 Refused outright**, with a sentence saying why, because no explanation makes them true:
  negative pay, tip, miles, or minutes; a shift start time more than 5 minutes after the current
  time; a shift longer than 24 hours; a trip whose elapsed time is longer than 24 hours (added by the
  Architect after the live check, 2026-10-03); a charge session whose kWh is more than the battery size plus
  25% (the plug measures energy before charging losses, which run well under 25%). The battery size
  is data, entered per vehicle; the initial value is 64.8 kWh.
- **FR-38 Explained-values report.** Every explained value stays marked and appears on one report:
  the date, the record, the limit, the value, the limit it passed, and the explanation, for review
  before filing. Confirmed values stay marked on their records.

## 6. Non-functional requirements

- **NFR-1 Tests first.** Every FR has at least one xUnit test written from this document before
  its implementation.
- **NFR-2 No silent defaults.** A result that depends on a default or an estimate says so on screen
  and in the API response.
- **NFR-3 Local first.** Runs on one machine with SQLite. No account, no network required.
- **NFR-4 Containerized.** Ships with a Dockerfile and a Kubernetes manifest for a single replica.
- **NFR-5 Money is decimal.** Currency values use `decimal`, never floating point.
- **NFR-6 Time is 24-hour, dates are ISO** in storage and export.
- **NFR-7 Durable.** The database is a single file that can be backed up by copying it. A backup
  command writes a dated copy. Retention is at least seven years of records.

## 7. Seed data

The first dataset is the author's own measured driving, August and September 2026: offers,
routes, returns, shop times, charging sessions, and odometer readings, from a hand-kept log. It is
the acceptance data. A requirement passes when it reproduces the numbers the log already
computed by hand.

## 8. Open questions

None. The last one closed on 2026-09-24: the vehicle is no longer shared, and past shared use was
already separated by the two drivers' own charging accounts, so no "paid by" field is needed.
