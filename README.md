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

**Slices 1, 1a, 2, and 3a are complete: the demo core, the container, measured energy, and the record.**

| Requirement | What works |
|---|---|
| FR-1 to FR-4 | Offers, trips, and shifts are captured; an offer is stored only when accepted |
| FR-7 | Every stored number carries its grade, through the database and the API |
| FR-10, NFR-2 | Every result lists the defaults behind it, on screen and in the API |
| FR-11 to FR-13 | Forecast net $/hr at the accept screen, the $25 verdict, the return estimate |
| FR-14, FR-15 | Per trip: net $/hr, true $/mile, and unpaid (deadhead) share on the shift screen; every rate in the API's trip report |
| FR-18 to FR-20 | Shift miles, deadhead miles, shift rate beside trip rate, the shift summary |
| FR-25 | Nothing is updated or deleted, enforced by EF and by database triggers. A correction is a new version with a required reason; the original stays readable in the history. Drives, expenses, trip actuals, and charge sessions are correctable |
| FR-33, FR-34 | A JSON API over the same services the screens use; the Web project computes nothing |
| NFR-1, 3, 5, 6 | Tests first; local SQLite, no account; money is `decimal`; ISO dates |
| NFR-4 | Docker image and a single-replica Kubernetes manifest, run and verified (below) |
| FR-5 | Charge sessions with odometer, kWh, cost, state of charge, type, and purpose; home sessions costed at the home rate on their date |
| FR-6, FR-17 | One tip per trip; the trip's rates before and after it; shift gross includes tips |
| FR-8 | Efficiency measured wall to wheel, with any state-of-charge mismatch named |
| FR-9, FR-9a | Price per kWh home, fast, and blended, and the fast share of cost; shifts, trips, and offers use the 30 days of charging before them |
| FR-16 | How far off the platform's time and mileage estimates were, per trip |
| FR-26, FR-26a | The mileage log: every drive with where and why; a shift's span is logged as business when it ends |
| FR-27 | Receipts stored in the database with a SHA-256 that detects a changed file |
| FR-28 | Business expenses by category |
| NFR-7 | Dated backups of the whole ledger that never overwrite each other |

**Deferred, not built yet.** Nothing below is shown as working anywhere in the app.

| Slice | Requirements |
|---|---|
| 3b. Tax and reports | FR-21 reports by range, FR-24 export to CSV, FR-29 to FR-31 annual tax summary, mileage rate by year, and 1099 reconciliation, FR-32 full export |
| 3c. Imports | FR-22 charging receipts and FR-23 platform earnings from CSV. Waiting on a real export file from each; no importer is written against a guessed format |

Expense and charge corrections are available through the API; the screens correct drives only so far.

Energy cost now comes from the last 30 days of charging. Where that window is too thin to measure,
the settings (4.0 mi/kWh, $0.69/kWh) stand in, and every number that uses them says so. The home
electricity rate is a **placeholder of $0.15/kWh** until it is read from a bill, and it is labeled
wherever it is used; it can be set from the Charging page or the API. The $25 threshold and the
defaults are stored as versions but cannot yet be changed from the screens or the API.

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

### In a container

```
docker build -t gigledger:local .
docker run -p 8080:8080 -v gigledger-data:/data gigledger:local
```

The ledger lives on the `/data` volume, never in the image, and the app runs as a non-root user.
Backups go to `/data/backups` on the same volume. That protects against a bad write or a mistake,
not against losing the disk: copy them off the machine for that. The container's clock is UTC, so
backup file names are stamped in UTC.

### On Kubernetes

```
kubectl create namespace gigledger
kubectl -n gigledger apply -f deploy/gigledger.yaml
kubectl -n gigledger port-forward svc/gigledger 8080:8080
```

**Give every build its own tag.** A rebuilt image under the same tag is not picked up by a restart:
the node keeps the copy it already has. Tag the build and point the deployment at it:

```
docker build -t gigledger:<version> .
kubectl -n gigledger set image deploy/gigledger gigledger=gigledger:<version>
```

**Exactly one replica, replaced rather than rolled.** SQLite allows one writer and the ledger is
one file on one volume, so the manifest pins `replicas: 1`, uses the `Recreate` strategy (a rolling
update would briefly run two writers), and claims the volume `ReadWriteOnce`. Scaling out would mean
replacing SQLite, not raising the number. The comment at the top of the manifest says the same.

Verified on Docker Desktop's Kubernetes (v1.36): a shift written through the API survived killing
the pod and starting its replacement, and a plain `docker run` kept its data across a new image and
container on the same volume. Deploying Slice 2 over that volume ran its migrations against the
existing ledger: the Slice 1 shift was still there, and the new tables took writes.

One claim had to be corrected. The first Kubernetes check took "the pod runs as uid 1654" as proof
that the fixed image was running, but the manifest's `runAsUser` produces the same result on the old
image, and the rebuild under the same tag was probably never picked up. The persistence result
stands either way. The fixed image was confirmed running when Slice 2 was deployed under its own tag
and its new endpoints answered. Two defects surfaced only by running it, both fixed: the data folder
was root-owned, so the first write would have failed, and a user named rather than numbered made
Kubernetes refuse to start the pod under `runAsNonRoot`.

## Stack

C# on .NET 8 · ASP.NET Core with Blazor (interactive server) · EF Core on SQLite · xUnit and bUnit

## Author

Dominick Trolian
