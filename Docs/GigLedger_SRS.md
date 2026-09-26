# GigLedger: Software Requirements Specification

**Version:** 0.5 · 2026-09-26 · DRAFT, for review. 0.5 allows a charge session's odometer, state of charge, and purpose to be unknown, since a charging receipt carries none of them, and specifies the receipt import (FR-22). 0.4 settles four Slice 3 decisions: a place and purpose on every drive, shift spans logged as business, a reason on every correction, receipts stored in the database. 0.3 added the odometer to a charge session, fixes the efficiency method and the energy window, and sets a placeholder home rate. 0.2 closed the four open questions: tips per trip, no declined offers, home and fast charging separated, vehicle not shared.
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
- **Tips post later.** Tips were about half of trip earnings, arrive hours after the trip, and
  appear nowhere at the accept screen.

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
  the time it was offered.
- **FR-2** Record the time an offer was accepted. **Declined offers are not recorded.** The
  forecast in 5.3 can be run on any offer before deciding, and nothing is kept unless it is accepted.
- **FR-3** For an accepted offer, record actual elapsed time, actual route miles and return miles.
- **FR-4** Record a shift with start and end odometer readings and start and end times.
- **FR-5** Record a charge session with kWh, cost, start and end state of charge, **the odometer
  reading at the session**, charger, and whether the energy was for work or personal driving. Every session has a **charge type**: home
  or DC fast. A home session usually has no receipt, so its cost is its kWh times the home
  electricity rate, which is stored as data with an effective date, and the cost is graded
  `derived`. Until the rate is read from a bill, a **placeholder of $0.15/kWh** is used, and every
  number that depends on it says so (NFR-2). **The odometer, the state of charge, and the
  purpose may be unknown** (a receipt carries none of them). Unknown is recorded as unknown, never as zero or a guess.
- **FR-6** Record a payout: amount, date posted, and which trip or trips it covers. A **tip**
  belongs to exactly one trip, and its amount is the total for the whole trip. On a trip with two
  or more drops the platform does not break the tip out by drop, so GigLedger records no per-drop
  tip and computes nothing that would require one.
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
- **FR-17** Once a tip posts, recompute the trip's rates and keep the pre-tip figures visible.

### 5.5 Per shift

- **FR-18** Compute shift miles from odometer readings, and deadhead miles as shift miles minus
  the sum of paid route miles.
- **FR-19** Compute **shift rate** (all pay over all clock time) next to **trip rate** (all pay
  over time spent on trips), and show the gap between them.
- **FR-20** Summarize a shift: trips, gross, energy cost, net, hours, and miles.

### 5.6 Reporting and import

- **FR-21** Report by day, week, and custom range, with totals and rates.
- **FR-22** Import charge sessions from a CSV of charging receipts, one row per receipt, read by
  column name: `receipt_number`, `start_local`, `timezone`, `kwh`, `net_total`, `station_id`,
  `location_name`. Each row becomes a DC fast session with kWh and cost graded measured, and its
  odometer, state of charge, and purpose unknown (FR-5). **All or nothing:** a row that cannot be
  read, or that FR-5 would refuse, stops the whole file and names its line. A receipt number
  already in the ledger is skipped, so importing the same file twice stores it once.
- **FR-23** Import payouts from a CSV of platform earnings.
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
  month.
- **FR-32 Full export.** The entire ledger, including attachments, exports to an open format
  (CSV plus the attached files) for an accountant or an audit.

### 5.8 Agent client (AOA)

- **FR-33** Expose the core through a JSON API with one endpoint per capture and query operation,
  so an AI assistant can log an offer or answer "did that shift clear 25?" by calling the same
  operations the UI calls.
- **FR-34** The API and the UI call the same core services. Neither contains business logic the
  other lacks.

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
