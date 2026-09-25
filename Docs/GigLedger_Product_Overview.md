# GigLedger: Product Overview

**Product:** GigLedger
**Document type:** Product Overview / Concept of Operations
**Version:** 0.1 · 2026-09-25 · DRAFT, for review
**Author:** Dominick Trolian
**Audience:** anyone approaching GigLedger for the first time, before the SRS

---

## 1. What GigLedger is

GigLedger records the work of a delivery driver and reports what that work actually paid: per
offer, per trip, per shift, and per mile, after the cost of the energy that moved the car.

It is also the driver's **system of record**. Every entry is kept permanently, in a form that
supports tax preparation and would stand up in an audit.

## 2. The one property that distinguishes it

**GigLedger never shows an estimate as a measurement.**

Every number it stores carries where it came from:

| Grade | Meaning |
|---|---|
| Stated | Shown by the platform: pay, stated miles, estimated time. |
| Entered | Typed in by the driver. |
| Measured | Read from an instrument or a document: the odometer, a charging receipt. |
| Derived | Computed by GigLedger from the other three. |

Every computed number also lists the defaults it used. A forecast built on a default efficiency
says so on screen, beside the number, and the same result from the API says so in its response.
When a real measurement replaces the default, the note disappears without anyone having to
remember to remove it.

## 3. Who it is for

**The driver**, at two moments: at the accept screen, with seconds to decide, and afterward, with
time to look back at what a trip, a shift, or a week really paid.

**Whoever prepares the driver's taxes.** The mileage log, the charging receipts, and the payouts
have to agree with each other and with the platform's annual tax form. GigLedger keeps them in one
place and shows where they do not agree.

## 4. Why it exists

A delivery platform shows three numbers at the accept screen: pay, stated miles, and estimated
time. Measured over several weeks of real driving in August and September 2026, those numbers
leave out much of what the driver needs:

- **Stated miles stop at the last drop.** The drive back is unpaid and unreported. On measured
  days it was 43% to 50% of a run's miles.
- **Pay is gross.** Fast charging consumed about 22% of driving gross, over two independent windows
  that agreed within about 2%.
- **Tips post later.** Tips were about half of trip earnings. They arrive hours after the trip and
  appear nowhere at the accept screen.

The platform's time estimates held up well. Its mileage did not: it ends where the platform stops
paying.

## 5. How it works, from the driver's side

### 5.1 At the accept screen

The driver types in the offer: pay, stated miles, drops, items, estimated time. GigLedger
subtracts the energy cost of the stated miles plus the drive back, divides by the estimated time,
and shows a **forecast of net dollars per hour** with the accept rule's verdict. Nothing is saved
unless the driver accepts.

The accept rule is the driver's own: *"If I don't think I will make 25+ I don't take it."* It is a
forecast for one offer, not a grade for the shift. The threshold is a setting. **GigLedger shows the
verdict; the driver decides.**

### 5.2 After the trip

The driver records what actually happened: elapsed time, route miles, and return miles. GigLedger
reports the actual gross and net rate, the true dollars per mile, and the share of the miles that
nobody paid for.

### 5.3 At the end of the shift

Two odometer readings bound the shift. GigLedger reports the **shift rate** (all pay over all
clock time) beside the **trip rate** (all pay over time on trips). The gap between them is the
time spent charging, repositioning, and waiting for offers, which is on the driver's clock and on
no trip.

### 5.4 Later

Tips post, and each trip's rates are recomputed with the pre-tip figures kept visible. Charging
receipts replace the default efficiency with a measured one. At year end, the tax summary puts
the standard mileage deduction and the actual-expense deduction side by side and leaves the choice
to the driver.

## 6. The governing principle

**Nothing is overwritten and nothing is deleted.** A correction is stored as a new version that
points to the one it replaces, and the original stays readable. This is enforced in the data
layer, not left to the screens: any attempt to update or delete a stored record fails.

## 7. What GigLedger is not

- **Not a platform integration.** It does not connect to, scrape, or automate any delivery app.
- **Not a route optimizer.** It measures what a route cost, and does not plan one.
- **Not a tax filer.** It prepares the numbers; filing is out of scope.
- **Not a decision maker.** It shows the forecast and the rule's verdict. The driver takes or
  declines the offer.
- **Not multi-user**, in version 0.x. One driver and one vehicle, with a data model that allows more.

## 8. How it is built

- **Documents first:** this overview, then the SRS (what it must do), then the SDD (how).
- **Tests before code.** Each requirement gets a test written from the SRS. The test is run and
  seen to fail before the code that makes it pass is written.
- **Acceptance against real data.** The first dataset is the author's own hand-kept driving log.
  A requirement passes when GigLedger reproduces the numbers the log already computed by hand.
- **Three slices,** each finished and demonstrable before the next: the offer-to-shift core with
  its JSON API, then measured energy, then the full record and tax preparation.
- **Stack:** C# on .NET 8, ASP.NET Core with Blazor, EF Core on SQLite, xUnit. It runs on one
  machine with no account and no network, and ships with a Dockerfile and a single-replica
  Kubernetes manifest.
- **Two clients, one core.** The Blazor screens and the JSON API call the same services, so an AI
  assistant can log an offer or answer "did that shift clear 25?" through the same operations the
  driver uses.

## 9. Where to go next

- **GigLedger SRS:** the requirements, numbered, each one testable.
- **GigLedger SDD:** the design, the build slices, and the test plan.
