# GigLedger: Software Design Document

**Version:** 0.5 · 2026-09-26 · DRAFT. Designs against SRS 0.5. 0.5 orders efficiency by time, allows unknown odometer and state of charge (6.5), and adds the charge import (6.7). 0.4 adds section 6.6, reports and the tax summary (Slice 3b). 0.3 added section 6.5, the Slice 2 energy design. 0.2 recorded the three section 11 decisions.
**Author:** Dominick Trolian
**Stack:** C# / .NET 8 · ASP.NET Core · Blazor (interactive server) · EF Core + SQLite · xUnit

---

## 1. Purpose and approach

This document says how GigLedger meets SRS 0.2. It designs the whole system at once, so the data
model does not have to be reworked as features arrive, and it builds the system in three slices.
Each slice is finished, tested, and demonstrable before the next one starts.

The order of work inside every slice is fixed: the tests are written from the SRS first, they are
run and seen to fail, and only then is the code written (NFR-1).

## 2. Build slices

The cut was approved on 2026-09-25.

| Slice | Delivers | Requirements |
|---|---|---|
| **1. Demo core** | Evaluate an offer against the accept rule, record the trip, close the shift, see the summary. The JSON API calls the same core as the UI. | FR-1 to FR-4, FR-7, FR-10 to FR-15, FR-18 to FR-20, FR-33, FR-34; NFR-1, 2, 3, 5, 6 |
| **1a. Container** | Dockerfile and a Kubernetes manifest for one replica. Built as soon as Slice 1 runs. | NFR-4 |
| **2. Measured energy** | Charge sessions, measured efficiency, home and fast charging cost, tips posted after the trip. | FR-5, FR-6, FR-8, FR-9, FR-16, FR-17 |
| **3. The record** | Corrections as versions, the mileage log, receipts, expenses, the tax summary, imports and exports, backup. | FR-21 to FR-32; NFR-7 |

**Two structural parts of Slice 3 are built in Slice 1**, because adding them later means migrating
live data: the append-only rule (section 5.3) and the grade on every stored number (section 5.2).
Slice 1 has no correction screen, but nothing in Slice 1 can overwrite or delete a record.

A requirement not yet built is listed as deferred in the README. It is never shown as working.

## 3. Solution structure

```
GigLedger.sln
  src/GigLedger.Core        domain types, calculations, service interfaces. No EF, no ASP.NET.
  src/GigLedger.Data        EF Core DbContext, SQLite, migrations, repositories.
  src/GigLedger.Web         Blazor pages and the JSON API, in one ASP.NET Core host.
  tests/GigLedger.Tests     xUnit: calculation, persistence, API, and acceptance tests.
  deploy/                   Dockerfile, k8s manifest (Slice 1a).
```

**Core has no dependency on storage or the web.** The calculations are plain functions of their
inputs, so they are tested without a database, and the same numbers come out of the UI, the API,
and the tests.

**One host serves both clients (FR-34).** Blazor pages and API endpoints both call the services in
Core through dependency injection. Neither contains a calculation. A test enforces this: the Web
project must not contain arithmetic on money types (section 9.4).

## 4. Domain model

### 4.1 Entities

| Entity | Holds | Notes |
|---|---|---|
| **Vehicle** | name, energy type (EV or gas) | One in 0.x. |
| **Platform** | name | Spark first. |
| **Shift** | vehicle, platform, start time, start odometer | Contains trips. |
| **ShiftClose** | shift, end time, end odometer | Written once when the shift ends (section 5.3). |
| **Trip** | the offer as presented (pay, stated miles, drops, items, estimated time, offered time) and the accept time | An offer is only stored once accepted (FR-2), so an offer and its trip are one record. |
| **TripActuals** | trip, elapsed time, route miles, return miles | Written once after the trip (section 5.3). A correction supersedes it. |
| **ChargeSession** | time, kWh, cost, start and end state of charge, charger, charge type (home or DC fast), purpose (work or personal) | Slice 2. |
| **Payout** | amount, date posted, kind (base or tip), trip | A tip belongs to exactly one trip and is the whole trip's tip (FR-6). Slice 2. |
| **Drive** | date, start and end odometer, purpose (business or personal) | The mileage log. A shift produces a business drive. Slice 3. |
| **Expense** | date, category, amount | Slice 3. |
| **Attachment** | owning record, file name, stored bytes, SHA-256 hash | Slice 3. |
| **Rate** | kind (home electricity $/kWh, standard mileage $/mi), value, effective date or tax year | Rates are data, never code (FR-30). Slice 2 and 3. |
| **Setting** | accept threshold ($25.00/hr default), default efficiency (mi/kWh), default energy cost ($/kWh) | Slice 1. |

### 4.2 Units

- Money is `decimal` (NFR-5). Miles, hours, and kWh are also `decimal`, so no binary rounding
  enters a money result.
- Durations are stored in whole minutes, as the driver reads them from the platform.
- Times are stored as ISO 8601 text with the UTC offset, and shown in 24-hour form (NFR-6).

**SQLite has no decimal type.** EF Core stores `decimal` as text there, and a SQL `SUM` or `ORDER BY`
on it is wrong or refused. **So all arithmetic happens in Core, in memory, never in a query.** At one
driver's volume (a few thousand trips a year) this costs nothing. It is also what keeps the
calculations in one place.

## 5. Cross-cutting rules

### 5.1 Grades (FR-7)

Every stored number is a `Graded<T>`: the value and where it came from.

```csharp
public enum Grade { Stated, Entered, Measured, Derived }
public readonly record struct Graded<T>(T Value, Grade Grade);
```

- **Stated:** shown by the platform (pay, stated miles, estimated time).
- **Entered:** typed by the driver from memory or estimate.
- **Measured:** read from an instrument or a document (odometer, receipt).
- **Derived:** computed by GigLedger.

EF Core maps each graded field to two columns (`Pay`, `PayGrade`). The UI renders the grade beside
the number; an Entered value can never render as Measured, because the grade travels with the value
and the view only reads it.

### 5.2 Results name their assumptions (FR-10, NFR-2)

A computed number is returned as a `Result`: the value, plus every default or estimate it used.

```csharp
public sealed record Result(decimal Value, IReadOnlyList<Assumption> Assumptions)
{
    public bool RestsOnDefault => Assumptions.Count > 0;
}
public sealed record Assumption(string Input, string Source);   // ("efficiency", "default 4.0 mi/kWh")
```

The UI shows each assumption under the number. The API returns them in the response body. **A
result with assumptions is never displayed without them.** This is one rule in one type, so it cannot
be applied in one screen and forgotten in another.

### 5.3 Nothing is overwritten or deleted (FR-25)

Every record carries `Id`, `RecordedAt`, and `SupersedesId`. A correction inserts a new record that
points at the one it replaces. The current view of a record is the newest one that nothing
supersedes.

**Enforced in the DbContext, not by convention:** `SaveChanges` throws if any tracked ledger entity
is in the `Modified` or `Deleted` state. A test proves that an update and a delete both fail
(TC-25a, TC-25b in section 9).

Two things look like they need an update: ending a shift, and entering a trip's actuals after it
ends. Both are modeled as their own records (`ShiftClose`, `TripActuals`) that are inserted, so
the rule has no exceptions in code.

## 6. Calculations

All in `GigLedger.Core.Calculations`, as pure functions. Symbols: *pay* in dollars, *miles* in
miles, *hours* in hours, *eff* in mi/kWh, *price* in $/kWh.

**Energy cost per mile** = price / eff. In Slice 1 both come from settings, so every result that
uses them carries two assumptions. In Slice 2, eff becomes measured (FR-8) and price becomes the
window's actual blend (FR-9), and the assumptions drop off by themselves.

### 6.1 Per offer (FR-11 to FR-13)

- return estimate = stated miles, unless the driver overrides it (FR-13). When not overridden it is
  an assumption.
- forecast net $/hr = (pay − cost per mile × (stated miles + return estimate)) / estimated hours
- verdict = **clears** when forecast net $/hr ≥ threshold, otherwise **does not clear** (FR-12).
  "25+" includes 25.

Evaluating an offer stores nothing. Only accepting does (FR-2).

### 6.2 Per trip (FR-14, FR-15)

- total miles = route miles + return miles
- actual gross $/hr = pay / actual hours
- actual net $/hr = (pay − cost per mile × total miles) / actual hours
- true $/mile = pay / total miles
- deadhead share = return miles / total miles

### 6.3 Per shift (FR-18 to FR-20)

- shift miles = end odometer − start odometer
- deadhead miles = shift miles − sum of route miles
- shift rate = total pay / shift clock hours
- trip rate = total pay / sum of trip hours
- the gap = trip rate − shift rate, shown with both
- summary: trips, gross, energy cost, net, hours, miles
- **a shift with no trips is allowed** (decided 2026-09-25). Its shift rate is $0/hr; its trip rate
  and the gap have no value, because there was no trip time to divide by. The screen says so.

### 6.5 Measured energy (Slice 2: FR-5, FR-6, FR-8, FR-9, FR-9a, FR-16, FR-17)

- **Charge cost.** A fast session's cost is its receipt (measured). A home session with no receipt
  costs kWh × the home rate in effect on its date, graded derived; while that rate is the
  placeholder, the cost names it as an assumption.
- **Efficiency, wall to wheel (FR-8).** Order the window's sessions by time. The ends are the
  first and last sessions with a known odometer. Miles = last reading − first reading. kWh =
  everything bought from the first end up to, not including, the last, counting sessions whose
  odometer is unknown. Fewer than two known readings, or no miles, means no measurement. If the
  battery arrived at the two ends at different states of charge, or either is unknown, the
  result says so.
- **Unknown values (SRS 0.5).** Odometer and state of charge are nullable in the model and the
  table. Null means unknown: it is never stored as zero, and the screen shows "unknown".
- **Price per kWh (FR-9).** Total cost over total kWh, three ways: home only, fast only, blend.
  Fast share = fast cost over all cost. Each carries the assumptions of the costs inside it.
- **The window (FR-9a).** For a shift, the sessions in the 30 days before it started. Measured
  efficiency and blended price replace the defaults wherever they exist; whichever is missing
  stays the default and is named.
- **Tips (FR-6, FR-17).** One tip per trip, the whole trip's tip. A trip's rates are reported
  before and after its tip, and a shift's gross includes tips with the tip total shown.
- **Estimate error (FR-16).** Actual minutes − the platform's estimate, and actual route miles −
  stated miles. Positive means the platform understated.

### 6.6 Reports and the tax summary (Slice 3b: FR-21, FR-24, FR-29 to FR-32)

- **Reports (FR-21).** A period report sums the summaries of the closed shifts that started in it:
  trips, gross, tips, energy cost, net, clock and trip hours, miles, unpaid miles, and the shift
  and trip rates over the period. Each shift keeps its own 30-day energy basis. A week runs Monday
  to Sunday. Every report exports to CSV (FR-24), one row per shift and a total row.
- **Tax summary (FR-29, FR-30).** For a year: gross by platform, business and personal miles from
  the mileage log, charging cost, and expenses by category, with both deduction methods side by
  side. **This method is the author's reading of the IRS rules and must be confirmed with a tax
  preparer before it is relied on:**
  - *Standard mileage:* business miles × the year's rate, plus parking and tolls.
  - *Actual expenses:* (all charging + vehicle expenses) × the business-use share (business miles ÷
    all miles), plus parking and tolls.
  - Phone, supplies, and other business expenses are listed on their own; they are the same under
    either method.
  The rate is entered per tax year from the published figure; until it is, the standard method
  shows "rate not entered" rather than a number. GigLedger shows both methods and chooses neither.
- **Reconciliation (FR-31).** The driver enters the platform's tax form: which form (1099-K or
  1099-NEC), the annual total, and, for a 1099-K, the monthly amounts. Recorded payouts are trip pay
  by the date the trip was accepted and tips by the date they posted. The difference is shown for
  the year, and by month where the form gives months.
- **Full export (FR-32).** One zip: a CSV per ledger table, every version included, and every
  receipt as its original file, named by its id.

### 6.7 Charge import (Slice 3c: FR-22)

- **Reading.** `Csv.Read` parses RFC 4180 (quoted fields, doubled quotes). `ChargeImport.Parse`
  maps each row by header name, so column order and extra columns do not matter. The time is
  `start_local` in the named zone: `CDT` is UTC−5, `CST` is UTC−6, and any other zone is refused
  rather than guessed. Numbers parse with the invariant culture.
- **The session.** DC fast; kWh and cost measured; charger is `Blink <station_id>, <location_name>`;
  the receipt number is stored on the session. Odometer, state of charge, and purpose are null.
  Purpose becomes nullable for this; nothing computes from it (the tax summary takes all charging
  times the business share).
- **Storing.** One transaction. Every row is parsed and validated first; the first failure
  refuses the file with its line number and nothing is stored. Rows whose receipt number is
  already stored are skipped and counted. The result is the count imported and the count skipped.
- **Interface.** `POST /api/charges/import`, body `text/csv`. 200 with the counts; 400 with the
  reason for a refused file.

### 6.4 Guards

- Zero or negative hours or miles return an error, never a division by zero or infinity.
- An end odometer below the start reading is rejected at entry.
- Rounding happens only for display: money to the cent, rates to the cent, shares to whole percent.
  Stored and intermediate values are never rounded.

## 7. Interfaces

### 7.1 Services (Core)

| Service | Operations |
|---|---|
| `IOfferService` | `Evaluate(offer)` → forecast and verdict, stores nothing · `Accept(offer, shiftId)` → trip |
| `ITripService` | `RecordActuals(tripId, actuals)` · `Get(tripId)` → trip with its rates |
| `IShiftService` | `Start(...)` · `End(shiftId, time, odometer)` · `Summary(shiftId)` |
| `ISettingsService` | `Get()` · `Set(...)`, which stores a new version (section 5.3) |

### 7.2 JSON API (FR-33)

One endpoint per operation. Each calls exactly one service method.

| Method and path | Service call |
|---|---|
| `POST /api/offers/evaluate` | `IOfferService.Evaluate` |
| `POST /api/shifts/{id}/trips` | `IOfferService.Accept` |
| `POST /api/trips/{id}/actuals` | `ITripService.RecordActuals` |
| `GET /api/trips/{id}` | `ITripService.Get` |
| `POST /api/shifts` | `IShiftService.Start` |
| `POST /api/shifts/{id}/end` | `IShiftService.End` |
| `GET /api/shifts/{id}/summary` | `IShiftService.Summary` |

Every number in a response carries its grade, and every result carries its assumptions.

### 7.3 Pages (Blazor)

- **Offer:** type in the offer, see the forecast, the verdict, and the assumptions. Accept or walk away.
- **Shift:** start, list of trips, enter actuals for each, end.
- **Summary:** the shift's numbers, shift rate beside trip rate, deadhead.

Pages are thin: bind inputs, call a service, render the result.

## 8. Storage and deployment

- One SQLite file, path from configuration (NFR-3, NFR-7). No account, no network.
- EF Core migrations are checked in. The app applies them at startup.
- **Slice 1a:** a multi-stage Dockerfile (SDK image builds, ASP.NET runtime image runs) and a
  Kubernetes manifest: one Deployment with **exactly one replica**, and a PersistentVolumeClaim
  holding the SQLite file. The manifest says why it is one replica: SQLite takes one writer, and two
  pods on one file would corrupt it. The same reason sets the update strategy to `Recreate`: a
  rolling update starts the new pod before stopping the old one.

## 9. Test plan

### 9.1 Levels

| Level | What it proves | Tools |
|---|---|---|
| Calculation | Every formula in section 6, including the guards | xUnit, no database |
| Persistence | Grades round-trip, the append-only rule holds, decimals survive SQLite | xUnit, SQLite in memory |
| API | Each endpoint returns what its service returns, with grades and assumptions | `WebApplicationFactory` |
| Acceptance | The numbers from the hand log come out the same | xUnit, seed data |

### 9.2 Naming

Each test is named for the requirement it checks (`FR11_ForecastSubtractsEnergyOfStatedPlusReturn`),
so the traceability from requirement to test can be read from the test list itself.

### 9.3 Acceptance data (SRS section 7)

The first dataset is the driver's own log from August and September 2026. A test case is a row the
log already computed by hand, with the log line it came from. **A requirement passes when GigLedger
reproduces the hand number.** Where the two disagree, the disagreement is investigated before either
one is corrected: the hand log has been wrong before, and so can the code.

### 9.4 Structural tests

- **TC-25a / TC-25b:** updating and deleting a stored trip both throw.
- **TC-34:** the Web project computes nothing: every number it shows comes from Core. Checked by
  scanning every Web source file, with comments, strings, and markup removed, for multiplication or
  division, which every rate, cost, and share needs. **It does not see addition or subtraction**, so
  a sum computed in Web would pass it; the API tests close part of that gap by comparing each
  response to Core's own result.
- **TC-NFR5:** no `double` or `float` appears in a Core money path.

Each structural test is first shown to fail on a deliberately planted violation, then the plant is
removed. A check that cannot fail proves nothing.

## 10. Traceability, Slice 1

| Requirement | Design | Test |
|---|---|---|
| FR-1, FR-2 | 4.1 Trip; 6.1 evaluate stores nothing | FR1, FR2 |
| FR-3 | 4.1 Trip actuals; 7.1 `RecordActuals` | FR3 |
| FR-4 | 4.1 Shift | FR4 |
| FR-7 | 5.1 `Graded<T>` | FR7, persistence round-trip |
| FR-10, NFR-2 | 5.2 `Result` and assumptions | FR10 |
| FR-11 to FR-13 | 6.1 | FR11 to FR13, acceptance |
| FR-14, FR-15 | 6.2 | FR14, FR15, acceptance |
| FR-18 to FR-20 | 6.3 | FR18 to FR20, acceptance |
| FR-33, FR-34 | 3; 7.2 | API tests; TC-34 |
| FR-25 (structure) | 5.3 | TC-25a, TC-25b |
| NFR-5, NFR-6 | 4.2 | TC-NFR5, persistence |

## 11. Decisions

Confirmed by Dominick Trolian, 2026-09-25.

1. **Slice 1 defaults:** efficiency **4.0 mi/kWh** (recent dash readings ran 3.5 to 4.1) and energy
   cost **$0.69/kWh** (the flat DC fast-charging rate on the charging receipts). Both are settings,
   and every result that uses them lists them as assumptions until Slice 2 measures them.
2. **Time is stored in whole minutes,** as the platform shows it.
3. **Blazor interactive server,** not WebAssembly. The JSON API still serves the agent client.
