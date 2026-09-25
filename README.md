# GigLedger

GigLedger records the work of a delivery driver and reports what that work actually paid, per
offer, per trip, per shift, and per mile, after the cost of the energy that moved the car. It is
also the driver's system of record: nothing is ever overwritten or deleted.

**The one property that distinguishes it: it never shows an estimate as a measurement.** Every
stored number carries its grade (stated by the platform, entered by the driver, measured from an
odometer or receipt, or derived), and every computed number lists the defaults it relied on.

Start with the documents, in this order:

1. [Product Overview](Docs/GigLedger_Product_Overview.md): what it is and why it exists
2. [Software Requirements Specification](Docs/GigLedger_SRS.md): what it must do, numbered and testable
3. [Software Design Document](Docs/GigLedger_SDD.md): how, the build slices, and the test plan

## How it was built

The documents came first, then the tests, then the code, and the commit history shows it: each
step lands as a commit of failing tests, followed by the commit that makes them pass.

Acceptance data is the author's own hand-kept driving log from August and September 2026. A
requirement passes when GigLedger reproduces a number the log already computed by hand. Where the
two disagreed, the disagreement was investigated before either was changed: the log's Sunday 08/02
shift rate turned out to be off by five cents (it rounded 4.15 hours up to 4.16), and the test says so.

## Status

**Slice 1, the demo core, is complete.**

| Requirement | What works |
|---|---|
| FR-1 to FR-4 | Offers, trips, and shifts are captured; an offer is stored only when accepted |
| FR-7 | Every stored number carries its grade, through the database and the API |
| FR-10, NFR-2 | Every result lists the defaults behind it, on screen and in the API |
| FR-11 to FR-13 | Forecast net $/hr at the accept screen, the $25 verdict, the return estimate |
| FR-14, FR-15 | Per trip: true $/mile and unpaid (deadhead) share on the shift screen. Actual gross and net $/hr are computed and tested in Core but not yet shown on screen or returned by the API |
| FR-18 to FR-20 | Shift miles, deadhead miles, shift rate beside trip rate, the shift summary |
| FR-25 (structure) | Nothing is updated or deleted, enforced by EF and by database triggers |
| FR-33, FR-34 | A JSON API over the same services the screens use; the Web project computes nothing |
| NFR-1, 3, 5, 6 | Tests first; local SQLite, no account; money is `decimal`; ISO dates |

**Deferred, not built yet.** Nothing below is shown as working anywhere in the app.

| Slice | Requirements |
|---|---|
| 1a. Container | NFR-4: Dockerfile and a single-replica Kubernetes manifest |
| 2. Measured energy | FR-5 charge sessions, FR-6 payouts and tips, FR-8 measured efficiency, FR-9 home and fast charging cost, FR-16 estimate error, FR-17 tips recomputing a trip |
| 3. The record | FR-21 to FR-24 reports, imports, exports; FR-25 correction screens; FR-26 mileage log; FR-27 receipts; FR-28 expenses; FR-29 to FR-31 tax summary and reconciliation; FR-32 full export; NFR-7 backup |

Until Slice 2, energy cost rests on two settings, 4.0 mi/kWh and $0.69/kWh, and every number that
uses them says so. The settings, including the $25 threshold, are stored as versions but cannot yet
be changed from the screens or the API.

## Design notes worth reading the code for

- **Append-only is enforced by the database, not only the ORM.** An EF `SaveChanges` guard refuses
  updates and deletes, but `ExecuteUpdate` and `ExecuteDelete` go straight to SQL and never pass
  through it. The tests caught that (commit `6a32e5f`, 45 of 47), and SQLite triggers closed it
  (`0954511`).
- **All arithmetic runs in C#, never in SQL.** SQLite has no decimal type, and EF stores `decimal` as
  text, so a SQL `SUM` or `ORDER BY` on money would be wrong or refused.
- **The calculations are pure functions in `GigLedger.Core`,** with no storage or web dependency. The
  screens, the API, and the tests all get their numbers from the same place, and a structural test
  fails if multiplication or division appears in the Web project.
- **A check has to be able to fail.** Structural tests are first shown failing on a planted
  violation. Three API tests that passed before any endpoint existed (every URL was already a 404)
  were tightened until they could only pass against a real endpoint.

## Build, test, run

Requires the .NET 8 SDK (pinned in `global.json`).

```
dotnet test
dotnet run --project src/GigLedger.Web
```

The app creates `gigledger.db` in the working directory on first run. Set
`ConnectionStrings__Ledger` to put it elsewhere.

## Stack

C# on .NET 8 · ASP.NET Core with Blazor (interactive server) · EF Core on SQLite · xUnit and bUnit

## Author

Dominick Trolian
