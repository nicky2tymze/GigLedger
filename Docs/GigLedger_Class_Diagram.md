# GigLedger: Class Diagram

**Generated from code, not drawn.** Source: `GigLedger` at commit `f2f234d`. 126 classes in 6 modules.

Regenerate: `python Docs/Scripts/classdiagram.py cs C:/Users/nickt/Desktop/GigLedger/src/GigLedger.Core/GigLedger.Core.csproj C:/Users/nickt/Desktop/GigLedger/src/GigLedger.Data/GigLedger.Data.csproj C:/Users/nickt/Desktop/GigLedger/src/GigLedger.Web/GigLedger.Web.csproj --doc GigLedger --out C:/Users/nickt/Desktop/GigLedger/Docs/GigLedger_Class_Diagram.md`

Arrows: `<|--` inherits, `<|..` implements, `-->` holds a field of that type, `..>` uses it in a method. Links to classes in another diagram are listed under each one.

## GigLedger.Core (1 of 10)

```mermaid
classDiagram
  class IOfferService {
    <<interface>>
    +Evaluate(offer) OfferEvaluation
    +Accept(shiftId, offer, acceptedAt) Guid
  }
  class Offer {
    <<record>>
    +Pay : Graded~decimal~
    +StatedMiles : Graded~decimal~
    +Drops : int
    +Items : int
    +EstimatedMinutes : Graded~int~
    +OfferedAt : DateTimeOffset
    +ReturnMilesOverride : decimal?
  }
  class OfferEvaluation {
    <<record>>
    +Forecast : Result
    +Verdict : Verdict
    +Threshold : decimal
  }
  class Result {
    <<record>>
    +Value : decimal
    +Assumptions : IReadOnlyList~Assumption~
    +RestsOnDefault : bool
  }
  class StoredTrip {
    <<record>>
    +Id : Guid
    +ShiftId : Guid
    +Offer : Offer
    +AcceptedAt : DateTimeOffset
    +Actuals : GradedActuals
    +Tip : Graded~decimal~?
  }
  class TripRates {
    <<record>>
    +GrossPerHourBeforeTip : Result
    +GrossPerHour : Result
    +NetPerHourBeforeTip : Result
    +NetPerHour : Result
    +TruePerMileBeforeTip : Result
    +TruePerMile : Result
  }
  class TripReport {
    <<record>>
    +Trip : StoredTrip
    +Rates : TripRates
    +EstimateError : EstimateError
    +Energy : EnergyBasis
  }
  class Verdict {
    <<enumeration>>
    +Clears
    +DoesNotClear
  }
  OfferEvaluation --> Result
  OfferEvaluation --> Verdict
  StoredTrip --> Offer
  TripRates --> Result
  TripReport --> StoredTrip
  TripReport --> TripRates
  IOfferService ..> Offer
  IOfferService ..> OfferEvaluation
```

Links outside this diagram:

- AcceptOfferRequest holds Offer (`GigLedger.Web`)
- Calculations uses Offer (`GigLedger.Core`)
- Calculations uses Result (`GigLedger.Core`)
- Calculations uses Verdict (`GigLedger.Core`)
- CostedCharge holds Result (`GigLedger.Core`)
- DeductionMethod holds Result (`GigLedger.Core`)
- EnergyCalculations uses Offer (`GigLedger.Core`)
- EnergyCalculations uses Result (`GigLedger.Core`)
- EnergyCalculations uses TripRates (`GigLedger.Core`)
- EnergyPrices holds Result (`GigLedger.Core`)
- EnergyReport holds Result (`GigLedger.Core`)
- IOfferService is implemented by LedgerServices (`GigLedger.Data`)
- ITripService uses StoredTrip (`GigLedger.Core`)
- ITripService uses TripReport (`GigLedger.Core`)
- LedgerServices uses Offer (`GigLedger.Data`)
- LedgerServices uses OfferEvaluation (`GigLedger.Data`)
- LedgerServices uses StoredTrip (`GigLedger.Data`)
- Offer holds Graded (`GigLedger.Core`)
- PeriodReport holds Result (`GigLedger.Core`)
- Result holds Assumption (`GigLedger.Core`)
- ShiftSummary holds Result (`GigLedger.Core`)
- StoredTrip holds Graded (`GigLedger.Core`)
- StoredTrip holds GradedActuals (`GigLedger.Core`)
- Tax uses Result (`GigLedger.Core`)
- TaxSummary holds Result (`GigLedger.Core`)
- TripReport holds EnergyBasis (`GigLedger.Core`)
- TripReport holds EstimateError (`GigLedger.Core`)

## GigLedger.Core (2 of 10)

```mermaid
classDiagram
  class Calculations {
    +EnergyCostPerMile(energy) Result
    +ForecastNetPerHour(offer, energy) Result
    +AcceptVerdict(forecast, threshold) Verdict
    +ActualGrossPerHour(pay, actuals) Result
    +ActualNetPerHour(pay, actuals, energy) Result
    +TruePerMile(pay, actuals) Result
    +DeadheadShare(actuals) Result
    +SummarizeShift(shift, trips, energy) ShiftSummary
  }
  class GradedActuals {
    <<record>>
    +ElapsedMinutes : Graded~int~
    +RouteMiles : Graded~decimal~
    +ReturnMiles : Graded~decimal~
    +Values : TripActuals
  }
  class ICorrectionService {
    <<interface>>
    +CorrectActuals(tripId, corrected, reason)
    +ActualsHistory(tripId) IReadOnlyList~Version of GradedActuals~
    +CorrectCharge(chargeId, corrected, reason) Guid
    +ChargeHistory(chargeId) IReadOnlyList~Version of ChargeSession~
  }
  class IMileageService {
    <<interface>>
    +Log(drive) Guid
    +Year(year) IReadOnlyList~StoredDrive~
    +Totals(year) MileageTotals
    +Correct(driveId, corrected, reason) Guid
    +History(driveId) IReadOnlyList~Version of Drive~
  }
  class ITripService {
    <<interface>>
    +RecordActuals(tripId, actuals)
    +Get(tripId) StoredTrip
    +OnShift(shiftId) IReadOnlyList~StoredTrip~
    +RecordTip(tripId, amount, postedAt)
    +Report(tripId) TripReport
  }
  class TripActuals {
    <<record>>
    +ElapsedMinutes : int
    +RouteMiles : decimal
    +ReturnMiles : decimal
  }
  class TripRecord {
    <<record>>
    +Pay : decimal
    +Actuals : TripActuals
    +Tip : decimal
  }
  class Version {
    <<record>>
    +Id : Guid
    +Value : Version.T
    +RecordedAt : DateTimeOffset
    +Reason : string
  }
  GradedActuals --> TripActuals
  TripRecord --> TripActuals
  Calculations ..> TripActuals
  Calculations ..> TripRecord
  ICorrectionService ..> GradedActuals
  ICorrectionService ..> Version
  IMileageService ..> Version
  ITripService ..> GradedActuals
```

Links outside this diagram:

- Calculations uses EnergyBasis (`GigLedger.Core`)
- Calculations uses Offer (`GigLedger.Core`)
- Calculations uses Result (`GigLedger.Core`)
- Calculations uses ShiftSpan (`GigLedger.Core`)
- Calculations uses ShiftSummary (`GigLedger.Core`)
- Calculations uses Verdict (`GigLedger.Core`)
- CorrectActualsRequest holds GradedActuals (`GigLedger.Web`)
- EnergyCalculations uses TripActuals (`GigLedger.Core`)
- GradedActuals holds Graded (`GigLedger.Core`)
- ICorrectionService is implemented by LedgerServices (`GigLedger.Data`)
- ICorrectionService uses ChargeSession (`GigLedger.Core`)
- IExpenseService uses Version (`GigLedger.Core`)
- IMileageService is implemented by LedgerServices (`GigLedger.Data`)
- IMileageService uses Drive (`GigLedger.Core`)
- IMileageService uses MileageTotals (`GigLedger.Core`)
- IMileageService uses StoredDrive (`GigLedger.Core`)
- ITripService is implemented by LedgerServices (`GigLedger.Data`)
- ITripService uses StoredTrip (`GigLedger.Core`)
- ITripService uses TripReport (`GigLedger.Core`)
- LedgerServices uses GradedActuals (`GigLedger.Data`)
- LedgerServices uses Version (`GigLedger.Data`)
- StoredTrip holds GradedActuals (`GigLedger.Core`)

## GigLedger.Core (3 of 10)

```mermaid
classDiagram
  class Drive {
    <<record>>
    +Date : DateOnly
    +StartOdometer : Graded~decimal~
    +EndOdometer : Graded~decimal~
    +Purpose : Purpose
    +Description : string
  }
  class MileageTotals {
    <<record>>
    +BusinessMiles : decimal
    +PersonalMiles : decimal
  }
  class Reconciliation {
    <<record>>
    +Year : int
    +Platform : string
    +Form : TaxForm
    +Recorded : decimal
    +Reported : decimal
    +Difference : decimal
    +Months : IReadOnlyList~MonthDifference~
    +RecordedFrom : PaymentSource
  }
  class RecordKeeping {
    +Validate(drive)
    +Miles(drive) decimal
    +Totals(drives) MileageTotals
    +Validate(expense)
    +ValidateReason(reason)
    +Sha256(content) string
  }
  class StoredDrive {
    <<record>>
    +Id : Guid
    +Drive : Drive
    +ShiftId : Guid?
  }
  class Tax {
    +Validate(rate)
    +Validate(form)
    +Summarize(year, grossByPlatform, miles, chargingCost, expenses, rate, grossFrom) TaxSummary
    +Reconcile(form, recordedByMonth, source) Reconciliation
  }
  class TaxForm {
    <<enumeration>>
    +Form1099K
    +Form1099Nec
  }
  class TaxSummary {
    <<record>>
    +Year : int
    +GrossByPlatform : IReadOnlyDictionary~string, decimal~
    +Miles : MileageTotals
    +BusinessShare : Result
    +ChargingCost : Result
    +ExpensesByCategory : IReadOnlyDictionary~ExpenseCategory, decimal~
    +Rate : MileageRate
    +StandardMileage : DeductionMethod
    +ActualExpenses : DeductionMethod
    +OtherBusinessExpenses : decimal
    +GrossFrom : IReadOnlyDictionary~string, PaymentSource~
  }
  Reconciliation --> TaxForm
  StoredDrive --> Drive
  TaxSummary --> MileageTotals
  RecordKeeping ..> Drive
  RecordKeeping ..> MileageTotals
  Tax ..> MileageTotals
  Tax ..> Reconciliation
  Tax ..> TaxSummary
```

Links outside this diagram:

- CorrectDriveRequest holds Drive (`GigLedger.Web`)
- Drive holds Graded (`GigLedger.Core`)
- Drive holds Purpose (`GigLedger.Core`)
- IMileageService uses Drive (`GigLedger.Core`)
- IMileageService uses MileageTotals (`GigLedger.Core`)
- IMileageService uses StoredDrive (`GigLedger.Core`)
- ITaxService uses Reconciliation (`GigLedger.Core`)
- ITaxService uses TaxSummary (`GigLedger.Core`)
- LedgerServices uses Drive (`GigLedger.Data`)
- LedgerServices uses Reconciliation (`GigLedger.Data`)
- PlatformForm holds TaxForm (`GigLedger.Core`)
- PlatformFormRow holds TaxForm (`GigLedger.Data`)
- Reconciliation holds MonthDifference (`GigLedger.Core`)
- Reconciliation holds PaymentSource (`GigLedger.Core`)
- RecordKeeping uses Expense (`GigLedger.Core`)
- Tax uses Expense (`GigLedger.Core`)
- Tax uses MileageRate (`GigLedger.Core`)
- Tax uses PaymentSource (`GigLedger.Core`)
- Tax uses PlatformForm (`GigLedger.Core`)
- Tax uses Result (`GigLedger.Core`)
- TaxSummary holds DeductionMethod (`GigLedger.Core`)
- TaxSummary holds ExpenseCategory (`GigLedger.Core`)
- TaxSummary holds MileageRate (`GigLedger.Core`)
- TaxSummary holds PaymentSource (`GigLedger.Core`)
- TaxSummary holds Result (`GigLedger.Core`)

## GigLedger.Core (4 of 10)

```mermaid
classDiagram
  class Expense {
    <<record>>
    +Date : DateOnly
    +Category : ExpenseCategory
    +Amount : Graded~decimal~
    +Description : string
  }
  class IExpenseService {
    <<interface>>
    +Record(expense) Guid
    +Year(year) IReadOnlyList~StoredExpense~
    +Correct(expenseId, corrected, reason) Guid
    +History(expenseId) IReadOnlyList~Version of Expense~
  }
  class ITaxService {
    <<interface>>
    +SetMileageRate(year, perMile)
    +RateFor(year) MileageRate
    +Summary(year) TaxSummary
    +RecordForm(form)
    +RecordedByMonth(year, platform) IReadOnlyList~decimal~
    +Reconcile(year, platform) Reconciliation
  }
  class MileageRate {
    <<record>>
    +Year : int
    +PerMile : decimal
  }
  class MonthDifference {
    <<record>>
    +Month : int
    +Recorded : decimal
    +Reported : decimal
    +Difference : decimal
  }
  class PaymentSource {
    <<enumeration>>
    +LoggedTrips
    +ImportedPayouts
  }
  class PlatformForm {
    <<record>>
    +Year : int
    +Platform : string
    +Form : TaxForm
    +AnnualTotal : decimal
    +Monthly : IReadOnlyList~decimal~
  }
  class StoredExpense {
    <<record>>
    +Id : Guid
    +Expense : Expense
  }
  StoredExpense --> Expense
  IExpenseService ..> Expense
  IExpenseService ..> StoredExpense
  ITaxService ..> MileageRate
  ITaxService ..> PlatformForm
```

Links outside this diagram:

- CorrectExpenseRequest holds Expense (`GigLedger.Web`)
- Expense holds ExpenseCategory (`GigLedger.Core`)
- Expense holds Graded (`GigLedger.Core`)
- IExpenseService is implemented by LedgerServices (`GigLedger.Data`)
- IExpenseService uses Version (`GigLedger.Core`)
- ITaxService is implemented by LedgerServices (`GigLedger.Data`)
- ITaxService uses Reconciliation (`GigLedger.Core`)
- ITaxService uses TaxSummary (`GigLedger.Core`)
- LedgerServices uses Expense (`GigLedger.Data`)
- LedgerServices uses MileageRate (`GigLedger.Data`)
- LedgerServices uses PlatformForm (`GigLedger.Data`)
- PlatformForm holds TaxForm (`GigLedger.Core`)
- Reconciliation holds MonthDifference (`GigLedger.Core`)
- Reconciliation holds PaymentSource (`GigLedger.Core`)
- RecordKeeping uses Expense (`GigLedger.Core`)
- Tax uses Expense (`GigLedger.Core`)
- Tax uses MileageRate (`GigLedger.Core`)
- Tax uses PaymentSource (`GigLedger.Core`)
- Tax uses PlatformForm (`GigLedger.Core`)
- TaxSummary holds MileageRate (`GigLedger.Core`)
- TaxSummary holds PaymentSource (`GigLedger.Core`)

## GigLedger.Core (5 of 10)

```mermaid
classDiagram
  class Graded {
    <<struct>>
    +Value : Graded.T
    +Grade : Grade
  }
  class IReportService {
    <<interface>>
    +Report(from, to) PeriodReport
    +Rows(from, to) IReadOnlyList~ReportRow~
    +Csv(from, to) string
  }
  class IShiftService {
    <<interface>>
    +Start(platform, startedAt, startOdometer) Guid
    +End(shiftId, endedAt, endOdometer)
    +Get(shiftId) StoredShift
    +Open() StoredShift
    +Summary(shiftId) ShiftSummary
  }
  class PeriodReport {
    <<record>>
    +From : DateOnly
    +To : DateOnly
    +Shifts : int
    +Trips : int
    +Gross : decimal
    +Tips : decimal
    +EnergyCost : Result
    +Net : Result
    +ClockHours : decimal
    +TripHours : decimal
    +Miles : decimal
    +UnpaidMiles : decimal
    +ShiftRate : Result
    +TripRate : Result
  }
  class ReportRow {
    <<record>>
    +Date : DateOnly
    +Platform : string
    +Summary : ShiftSummary
  }
  class Reports {
    +WeekOf(day) ValueTuple~DateOnly, DateOnly~
    +Combine(from, to, shifts) PeriodReport
    +ToCsv(total, rows) string
  }
  class ShiftSummary {
    <<record>>
    +Trips : int
    +Gross : decimal
    +EnergyCost : Result
    +Net : Result
    +ClockHours : decimal
    +TripHours : decimal
    +ShiftMiles : decimal
    +DeadheadMiles : decimal
    +ShiftRate : Result
    +TripRate : Result
    +RateGap : decimal?
    +Tips : decimal
  }
  class StoredShift {
    <<record>>
    +Id : Guid
    +Platform : string
    +StartedAt : DateTimeOffset
    +StartOdometer : Graded~decimal~
    +EndedAt : DateTimeOffset?
    +EndOdometer : Graded~decimal~?
  }
  ReportRow --> ShiftSummary
  StoredShift --> Graded
  IReportService ..> PeriodReport
  IReportService ..> ReportRow
  IShiftService ..> Graded
  IShiftService ..> ShiftSummary
  IShiftService ..> StoredShift
  Reports ..> PeriodReport
  Reports ..> ReportRow
  Reports ..> ShiftSummary
```

Links outside this diagram:

- Calculations uses ShiftSummary (`GigLedger.Core`)
- ChargeSession holds Graded (`GigLedger.Core`)
- Drive holds Graded (`GigLedger.Core`)
- EndShiftRequest holds Graded (`GigLedger.Web`)
- Expense holds Graded (`GigLedger.Core`)
- Graded holds Grade (`GigLedger.Core`)
- GradedActuals holds Graded (`GigLedger.Core`)
- IReportService is implemented by LedgerServices (`GigLedger.Data`)
- IShiftService is implemented by LedgerServices (`GigLedger.Data`)
- LedgerServices uses Graded (`GigLedger.Data`)
- LedgerServices uses ReportRow (`GigLedger.Data`)
- LedgerServices uses ShiftSummary (`GigLedger.Data`)
- LedgerServices uses StoredShift (`GigLedger.Data`)
- Offer holds Graded (`GigLedger.Core`)
- Payout holds Graded (`GigLedger.Core`)
- PeriodReport holds Result (`GigLedger.Core`)
- ShiftSummary holds Result (`GigLedger.Core`)
- StartShiftRequest holds Graded (`GigLedger.Web`)
- StoredTrip holds Graded (`GigLedger.Core`)

## GigLedger.Core (6 of 10)

```mermaid
classDiagram
  class EnergyPrices {
    <<record>>
    +HomeOnly : Result
    +FastOnly : Result
    +Blend : Result
    +FastShareOfCost : Result
  }
  class EnergyReport {
    <<record>>
    +Sessions : int
    +Efficiency : Result
    +Prices : EnergyPrices
  }
  class IChargeService {
    <<interface>>
    +Record(session) Guid
    +Between(from, to) IReadOnlyList~CostedCharge~
    +Report(from, to) EnergyReport
    +Import(csv) ImportResult
  }
  class IPayoutService {
    <<interface>>
    +Import(xlsx) ImportResult
    +Year(year) IReadOnlyList~Payout~
  }
  class ImportResult {
    <<record>>
    +Imported : int
    +Skipped : int
  }
  class Payout {
    <<record>>
    +Platform : string
    +TripId : string
    +At : DateTimeOffset
    +Zone : string
    +Type : PayoutType
    +Amount : Graded~decimal~
    +DepositStatus : string
    +DepositedOn : DateOnly?
  }
  class PayoutImport {
    +Parse(xlsx) IReadOnlyList~Payout~
  }
  class PayoutType {
    <<enumeration>>
    +TripEarnings
    +Tip
    +Incentive
    +AdjustmentCredit
  }
  EnergyReport --> EnergyPrices
  Payout --> PayoutType
  IChargeService ..> EnergyReport
  IChargeService ..> ImportResult
  IPayoutService ..> ImportResult
  IPayoutService ..> Payout
  PayoutImport ..> Payout
```

Links outside this diagram:

- EnergyCalculations uses EnergyPrices (`GigLedger.Core`)
- EnergyPrices holds Result (`GigLedger.Core`)
- EnergyReport holds Result (`GigLedger.Core`)
- IChargeService is implemented by LedgerServices (`GigLedger.Data`)
- IChargeService uses ChargeSession (`GigLedger.Core`)
- IChargeService uses CostedCharge (`GigLedger.Core`)
- IPayoutService is implemented by LedgerServices (`GigLedger.Data`)
- LedgerServices uses ImportResult (`GigLedger.Data`)
- Payout holds Graded (`GigLedger.Core`)
- PayoutRow holds PayoutType (`GigLedger.Data`)

## GigLedger.Core (7 of 10)

```mermaid
classDiagram
  class Assumption {
    <<record>>
    +Input : string
    +Source : string
  }
  class CostedCharge {
    <<record>>
    +Session : ChargeSession
    +Cost : Result
    +Id : Guid
  }
  class EnergyBasis {
    <<record>>
    +MilesPerKwh : decimal
    +PricePerKwh : decimal
    +Assumptions : IReadOnlyList~Assumption~
    +FromDefaults(defaultMilesPerKwh, defaultPricePerKwh) EnergyBasis
  }
  class EnergyCalculations {
    +Validate(session)
    +ChargeCost(session, homeRate) Result
    +MeasuredEfficiency(sessions) Result
    +Prices(charges) EnergyPrices
    +ForWindow(window, defaults) EnergyBasis
    +EstimateError(offer, actuals) EstimateError
    +TripRates(pay, tip, actuals, energy) TripRates
  }
  class EstimateError {
    <<record>>
    +MinutesOver : int
    +MilesOver : decimal
  }
  class HomeRate {
    <<record>>
    +PerKwh : decimal
    +EffectiveFrom : DateOnly
    +IsPlaceholder : bool
    +Placeholder : HomeRate
  }
  class ISettingsService {
    <<interface>>
    +Get() Settings
    +Set(settings)
    +HomeRateOn(date) HomeRate
    +SetHomeRate(perKwh, effectiveFrom)
  }
  class Settings {
    <<record>>
    +AcceptThreshold : decimal
    +DefaultMilesPerKwh : decimal
    +DefaultPricePerKwh : decimal
    +Initial : Settings
  }
  EnergyBasis --> Assumption
  EnergyCalculations ..> CostedCharge
  EnergyCalculations ..> EnergyBasis
  EnergyCalculations ..> EstimateError
  EnergyCalculations ..> HomeRate
  EnergyCalculations ..> Settings
  ISettingsService ..> HomeRate
  ISettingsService ..> Settings
```

Links outside this diagram:

- Calculations uses EnergyBasis (`GigLedger.Core`)
- CostedCharge holds ChargeSession (`GigLedger.Core`)
- CostedCharge holds Result (`GigLedger.Core`)
- EnergyCalculations uses ChargeSession (`GigLedger.Core`)
- EnergyCalculations uses EnergyPrices (`GigLedger.Core`)
- EnergyCalculations uses Offer (`GigLedger.Core`)
- EnergyCalculations uses Result (`GigLedger.Core`)
- EnergyCalculations uses TripActuals (`GigLedger.Core`)
- EnergyCalculations uses TripRates (`GigLedger.Core`)
- IChargeService uses CostedCharge (`GigLedger.Core`)
- ISettingsService is implemented by LedgerServices (`GigLedger.Data`)
- LedgerServices uses CostedCharge (`GigLedger.Data`)
- LedgerServices uses HomeRate (`GigLedger.Data`)
- LedgerServices uses Settings (`GigLedger.Data`)
- Result holds Assumption (`GigLedger.Core`)
- TripReport holds EnergyBasis (`GigLedger.Core`)
- TripReport holds EstimateError (`GigLedger.Core`)

## GigLedger.Core (8 of 10)

```mermaid
classDiagram
  class ChargeImport {
    +Parse(csv) IReadOnlyList~ChargeSession~
  }
  class ChargeSession {
    <<record>>
    +At : DateTimeOffset
    +Odometer : Graded~decimal~?
    +Kwh : Graded~decimal~
    +Cost : Graded~decimal~?
    +StartSoc : int?
    +EndSoc : int?
    +Charger : string
    +Type : ChargeType
    +Purpose : Purpose?
    +ReceiptNumber : string
  }
  class ChargeType {
    <<enumeration>>
    +Home
    +DcFast
  }
  class DeductionMethod {
    <<record>>
    +Vehicle : Result
    +ParkingAndTolls : decimal
    +Total : Result
  }
  class ExpenseCategory {
    <<enumeration>>
    +Phone
    +Parking
    +Tolls
    +Supplies
    +Vehicle
    +Other
  }
  class Grade {
    <<enumeration>>
    +Stated
    +Entered
    +Measured
    +Derived
  }
  class Purpose {
    <<enumeration>>
    +Work
    +Personal
  }
  class ShiftSpan {
    <<record>>
    +ClockMinutes : int
    +StartOdometer : decimal
    +EndOdometer : decimal
  }
  ChargeSession --> ChargeType
  ChargeSession --> Purpose
  ChargeImport ..> ChargeSession
```

Links outside this diagram:

- Calculations uses ShiftSpan (`GigLedger.Core`)
- ChargeSession holds Graded (`GigLedger.Core`)
- ChargeSessionRow holds ChargeType (`GigLedger.Data`)
- ChargeSessionRow holds Grade (`GigLedger.Data`)
- ChargeSessionRow holds Purpose (`GigLedger.Data`)
- CorrectChargeRequest holds ChargeSession (`GigLedger.Web`)
- CostedCharge holds ChargeSession (`GigLedger.Core`)
- DeductionMethod holds Result (`GigLedger.Core`)
- Drive holds Purpose (`GigLedger.Core`)
- DriveRow holds Grade (`GigLedger.Data`)
- DriveRow holds Purpose (`GigLedger.Data`)
- EnergyCalculations uses ChargeSession (`GigLedger.Core`)
- Expense holds ExpenseCategory (`GigLedger.Core`)
- ExpenseRow holds ExpenseCategory (`GigLedger.Data`)
- ExpenseRow holds Grade (`GigLedger.Data`)
- Graded holds Grade (`GigLedger.Core`)
- IChargeService uses ChargeSession (`GigLedger.Core`)
- ICorrectionService uses ChargeSession (`GigLedger.Core`)
- LedgerServices uses ChargeSession (`GigLedger.Data`)
- PayoutRow holds Grade (`GigLedger.Data`)
- ShiftCloseRow holds Grade (`GigLedger.Data`)
- ShiftRow holds Grade (`GigLedger.Data`)
- TaxSummary holds DeductionMethod (`GigLedger.Core`)
- TaxSummary holds ExpenseCategory (`GigLedger.Core`)
- TipRow holds Grade (`GigLedger.Data`)
- TripActualsRow holds Grade (`GigLedger.Data`)
- TripRow holds Grade (`GigLedger.Data`)

## GigLedger.Core (9 of 10)

```mermaid
classDiagram
  class AttachedTo {
    <<enumeration>>
    +ChargeSession
    +Tip
    +Expense
  }
  class AttachmentInfo {
    <<record>>
    +Id : Guid
    +Owner : AttachedTo
    +OwnerId : Guid
    +FileName : string
    +ContentType : string
    +Sha256 : string
    +RecordedAt : DateTimeOffset
  }
  class Csv {
    +LineEnd : string
    +Quote(field) string
    +Read(text) IReadOnlyList~IReadOnlyList of string~
  }
  class IAttachmentService {
    <<interface>>
    +Attach(owner, ownerId, fileName, contentType, content) Guid
    +Get(attachmentId) StoredAttachment
    +For(owner, ownerId) IReadOnlyList~AttachmentInfo~
    +Verify(attachmentId) bool
  }
  class IBackupService {
    <<interface>>
    +Backup(folder) string
  }
  class IExportService {
    <<interface>>
    +Export(zip)
  }
  class NotFoundException {
    <<Exception>>
  }
  class StoredAttachment {
    <<record>>
    +Info : AttachmentInfo
    +Content : Byte~~
  }
  AttachmentInfo --> AttachedTo
  StoredAttachment --> AttachmentInfo
  IAttachmentService ..> AttachedTo
  IAttachmentService ..> AttachmentInfo
  IAttachmentService ..> StoredAttachment
```

Links outside this diagram:

- AttachRequest holds AttachedTo (`GigLedger.Web`)
- AttachmentRow holds AttachedTo (`GigLedger.Data`)
- IAttachmentService is implemented by LedgerServices (`GigLedger.Data`)
- IBackupService is implemented by LedgerServices (`GigLedger.Data`)
- IExportService is implemented by LedgerServices (`GigLedger.Data`)
- LedgerServices uses AttachedTo (`GigLedger.Data`)
- LedgerServices uses AttachmentInfo (`GigLedger.Data`)

## GigLedger.Core (10 of 10)

```mermaid
classDiagram
  class Xlsx {
    +ReadSheet(xlsx, sheet) IReadOnlyList~IReadOnlyList of string~
  }
```

## GigLedger.Data (1 of 3)

```mermaid
classDiagram
  class LedgerContext {
    <<DbContext>>
    +Shifts : DbSet~ShiftRow~
    +ShiftCloses : DbSet~ShiftCloseRow~
    +Trips : DbSet~TripRow~
    +TripActuals : DbSet~TripActualsRow~
    +Settings : DbSet~SettingsRow~
    +ChargeSessions : DbSet~ChargeSessionRow~
    +Tips : DbSet~TipRow~
    +HomeRates : DbSet~HomeRateRow~
    +Drives : DbSet~DriveRow~
    +Expenses : DbSet~ExpenseRow~
    +Attachments : DbSet~AttachmentRow~
    +MileageRates : DbSet~MileageRateRow~
    +PlatformForms : DbSet~PlatformFormRow~
    +Payouts : DbSet~PayoutRow~
    +SaveChanges(acceptAllChangesOnSuccess) int
    +SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken) Task~int~
  }
  class LedgerRecord {
    +Id : Guid
    +RecordedAt : DateTimeOffset
    +SupersedesId : Guid?
    +CorrectionReason : string
  }
  class SettingsRow {
    +AcceptThreshold : decimal
    +DefaultMilesPerKwh : decimal
    +DefaultPricePerKwh : decimal
  }
  class ShiftCloseRow {
    +ShiftId : Guid
    +EndedAt : DateTimeOffset
    +EndOdometer : decimal
    +EndOdometerGrade : Grade
  }
  class ShiftRow {
    +Platform : string
    +StartedAt : DateTimeOffset
    +StartOdometer : decimal
    +StartOdometerGrade : Grade
  }
  class TipRow {
    +TripId : Guid
    +Amount : decimal
    +AmountGrade : Grade
    +PostedAt : DateTimeOffset
  }
  class TripActualsRow {
    +TripId : Guid
    +ElapsedMinutes : int
    +ElapsedMinutesGrade : Grade
    +RouteMiles : decimal
    +RouteMilesGrade : Grade
    +ReturnMiles : decimal
    +ReturnMilesGrade : Grade
  }
  class TripRow {
    +ShiftId : Guid
    +Pay : decimal
    +PayGrade : Grade
    +StatedMiles : decimal
    +StatedMilesGrade : Grade
    +Drops : int
    +Items : int
    +EstimatedMinutes : int
    +EstimatedMinutesGrade : Grade
    +OfferedAt : DateTimeOffset
    +ReturnMilesOverride : decimal?
    +AcceptedAt : DateTimeOffset
  }
  LedgerRecord <|-- SettingsRow
  LedgerRecord <|-- ShiftCloseRow
  LedgerRecord <|-- ShiftRow
  LedgerRecord <|-- TipRow
  LedgerRecord <|-- TripActualsRow
  LedgerRecord <|-- TripRow
  LedgerContext --> SettingsRow
  LedgerContext --> ShiftCloseRow
  LedgerContext --> ShiftRow
  LedgerContext --> TipRow
  LedgerContext --> TripActualsRow
  LedgerContext --> TripRow
```

Links outside this diagram:

- LedgerContext holds AttachmentRow (`GigLedger.Data`)
- LedgerContext holds ChargeSessionRow (`GigLedger.Data`)
- LedgerContext holds DriveRow (`GigLedger.Data`)
- LedgerContext holds ExpenseRow (`GigLedger.Data`)
- LedgerContext holds HomeRateRow (`GigLedger.Data`)
- LedgerContext holds MileageRateRow (`GigLedger.Data`)
- LedgerContext holds PayoutRow (`GigLedger.Data`)
- LedgerContext holds PlatformFormRow (`GigLedger.Data`)
- LedgerContextFactory uses LedgerContext (`GigLedger.Data`)
- LedgerDatabase uses LedgerContext (`GigLedger.Data`)
- LedgerRecord is inherited by AttachmentRow (`GigLedger.Data`)
- LedgerRecord is inherited by ChargeSessionRow (`GigLedger.Data`)
- LedgerRecord is inherited by DriveRow (`GigLedger.Data`)
- LedgerRecord is inherited by ExpenseRow (`GigLedger.Data`)
- LedgerRecord is inherited by HomeRateRow (`GigLedger.Data`)
- LedgerRecord is inherited by MileageRateRow (`GigLedger.Data`)
- LedgerRecord is inherited by PayoutRow (`GigLedger.Data`)
- LedgerRecord is inherited by PlatformFormRow (`GigLedger.Data`)
- LedgerServices uses LedgerContext (`GigLedger.Data`)
- ShiftCloseRow holds Grade (`GigLedger.Core`)
- ShiftRow holds Grade (`GigLedger.Core`)
- TipRow holds Grade (`GigLedger.Core`)
- TripActualsRow holds Grade (`GigLedger.Core`)
- TripRow holds Grade (`GigLedger.Core`)

## GigLedger.Data (2 of 3)

```mermaid
classDiagram
  class AttachmentRow {
    +Owner : AttachedTo
    +OwnerId : Guid
    +FileName : string
    +ContentType : string
    +Content : Byte~~
    +Sha256 : string
  }
  class ChargeSessionRow {
    +At : DateTimeOffset
    +Odometer : decimal?
    +OdometerGrade : Grade?
    +Kwh : decimal
    +KwhGrade : Grade
    +Cost : decimal?
    +CostGrade : Grade?
    +StartSoc : int?
    +EndSoc : int?
    +Charger : string
    +Type : ChargeType
    +Purpose : Purpose?
    +ReceiptNumber : string
  }
  class DriveRow {
    +Date : DateOnly
    +StartOdometer : decimal
    +StartOdometerGrade : Grade
    +EndOdometer : decimal
    +EndOdometerGrade : Grade
    +Purpose : Purpose
    +Description : string
    +ShiftId : Guid?
  }
  class ExpenseRow {
    +Date : DateOnly
    +Category : ExpenseCategory
    +Amount : decimal
    +AmountGrade : Grade
    +Description : string
  }
  class HomeRateRow {
    +PerKwh : decimal
    +EffectiveFrom : DateOnly
    +IsPlaceholder : bool
  }
  class MileageRateRow {
    +Year : int
    +PerMile : decimal
  }
  class PayoutRow {
    +Platform : string
    +TripId : string
    +At : DateTimeOffset
    +Zone : string
    +Type : PayoutType
    +Amount : decimal
    +AmountGrade : Grade
    +DepositStatus : string
    +DepositedOn : DateOnly?
  }
  class PlatformFormRow {
    +Year : int
    +Platform : string
    +Form : TaxForm
    +AnnualTotal : decimal
    +Monthly : string
  }
```

Links outside this diagram:

- AttachmentRow holds AttachedTo (`GigLedger.Core`)
- ChargeSessionRow holds ChargeType (`GigLedger.Core`)
- ChargeSessionRow holds Grade (`GigLedger.Core`)
- ChargeSessionRow holds Purpose (`GigLedger.Core`)
- DriveRow holds Grade (`GigLedger.Core`)
- DriveRow holds Purpose (`GigLedger.Core`)
- ExpenseRow holds ExpenseCategory (`GigLedger.Core`)
- ExpenseRow holds Grade (`GigLedger.Core`)
- LedgerContext holds AttachmentRow (`GigLedger.Data`)
- LedgerContext holds ChargeSessionRow (`GigLedger.Data`)
- LedgerContext holds DriveRow (`GigLedger.Data`)
- LedgerContext holds ExpenseRow (`GigLedger.Data`)
- LedgerContext holds HomeRateRow (`GigLedger.Data`)
- LedgerContext holds MileageRateRow (`GigLedger.Data`)
- LedgerContext holds PayoutRow (`GigLedger.Data`)
- LedgerContext holds PlatformFormRow (`GigLedger.Data`)
- LedgerRecord is inherited by AttachmentRow (`GigLedger.Data`)
- LedgerRecord is inherited by ChargeSessionRow (`GigLedger.Data`)
- LedgerRecord is inherited by DriveRow (`GigLedger.Data`)
- LedgerRecord is inherited by ExpenseRow (`GigLedger.Data`)
- LedgerRecord is inherited by HomeRateRow (`GigLedger.Data`)
- LedgerRecord is inherited by MileageRateRow (`GigLedger.Data`)
- LedgerRecord is inherited by PayoutRow (`GigLedger.Data`)
- LedgerRecord is inherited by PlatformFormRow (`GigLedger.Data`)
- PayoutRow holds Grade (`GigLedger.Core`)
- PayoutRow holds PayoutType (`GigLedger.Core`)
- PlatformFormRow holds TaxForm (`GigLedger.Core`)

## GigLedger.Data (3 of 3)

```mermaid
classDiagram
  class LedgerContextFactory {
    +CreateDbContext(args) LedgerContext
  }
  class LedgerDatabase {
    +Open(connection) LedgerContext
  }
  class LedgerServices {
    +HomeRateOn(date) HomeRate
    +SetHomeRate(perKwh, effectiveFrom)
    +Record(session) Guid
    +Import(csv) ImportResult
    +Between(from, to) IReadOnlyList~CostedCharge~
    +RecordTip(tripId, amount, postedAt)
    +Get() Settings
    +Set(settings)
    +Evaluate(offer) OfferEvaluation
    +Accept(shiftId, offer, acceptedAt) Guid
    +RecordActuals(tripId, actuals)
    +OnShift(shiftId) IReadOnlyList~StoredTrip~
    +Start(platform, startedAt, startOdometer) Guid
    +End(shiftId, endedAt, endOdometer)
    +Open() StoredShift
    +Summary(shiftId) ShiftSummary
    +Export(output)
    +Log(drive) Guid
    +Record(expense) Guid
    +CorrectActuals(tripId, corrected, reason)
    +ActualsHistory(tripId) IReadOnlyList~Version of GradedActuals~
    +CorrectCharge(chargeId, corrected, reason) Guid
    +ChargeHistory(chargeId) IReadOnlyList~Version of ChargeSession~
    +Attach(owner, ownerId, fileName, contentType, content) Guid
    +For(owner, ownerId) IReadOnlyList~AttachmentInfo~
    +Verify(attachmentId) bool
    +Backup(folder) string
    +Rows(from, to) IReadOnlyList~ReportRow~
    +Csv(from, to) string
    +SetMileageRate(year, perMile)
    +RateFor(year) MileageRate
    +RecordForm(form)
    +RecordedByMonth(year, platform) IReadOnlyList~decimal~
    +Reconcile(year, platform) Reconciliation
  }
```

Links outside this diagram:

- IAttachmentService is implemented by LedgerServices (`GigLedger.Core`)
- IBackupService is implemented by LedgerServices (`GigLedger.Core`)
- IChargeService is implemented by LedgerServices (`GigLedger.Core`)
- ICorrectionService is implemented by LedgerServices (`GigLedger.Core`)
- IExpenseService is implemented by LedgerServices (`GigLedger.Core`)
- IExportService is implemented by LedgerServices (`GigLedger.Core`)
- IMileageService is implemented by LedgerServices (`GigLedger.Core`)
- IOfferService is implemented by LedgerServices (`GigLedger.Core`)
- IPayoutService is implemented by LedgerServices (`GigLedger.Core`)
- IReportService is implemented by LedgerServices (`GigLedger.Core`)
- ISettingsService is implemented by LedgerServices (`GigLedger.Core`)
- IShiftService is implemented by LedgerServices (`GigLedger.Core`)
- ITaxService is implemented by LedgerServices (`GigLedger.Core`)
- ITripService is implemented by LedgerServices (`GigLedger.Core`)
- LedgerContextFactory uses LedgerContext (`GigLedger.Data`)
- LedgerDatabase uses LedgerContext (`GigLedger.Data`)
- LedgerServices uses AttachedTo (`GigLedger.Core`)
- LedgerServices uses AttachmentInfo (`GigLedger.Core`)
- LedgerServices uses ChargeSession (`GigLedger.Core`)
- LedgerServices uses CostedCharge (`GigLedger.Core`)
- LedgerServices uses Drive (`GigLedger.Core`)
- LedgerServices uses Expense (`GigLedger.Core`)
- LedgerServices uses Graded (`GigLedger.Core`)
- LedgerServices uses GradedActuals (`GigLedger.Core`)
- LedgerServices uses HomeRate (`GigLedger.Core`)
- LedgerServices uses ImportResult (`GigLedger.Core`)
- LedgerServices uses LedgerContext (`GigLedger.Data`)
- LedgerServices uses MileageRate (`GigLedger.Core`)
- LedgerServices uses Offer (`GigLedger.Core`)
- LedgerServices uses OfferEvaluation (`GigLedger.Core`)
- LedgerServices uses PlatformForm (`GigLedger.Core`)
- LedgerServices uses Reconciliation (`GigLedger.Core`)
- LedgerServices uses ReportRow (`GigLedger.Core`)
- LedgerServices uses Settings (`GigLedger.Core`)
- LedgerServices uses ShiftSummary (`GigLedger.Core`)
- LedgerServices uses StoredShift (`GigLedger.Core`)
- LedgerServices uses StoredTrip (`GigLedger.Core`)
- LedgerServices uses Version (`GigLedger.Core`)

## GigLedger.Web (1 of 3)

```mermaid
classDiagram
  class AcceptOfferRequest {
    <<record>>
    +Offer : Offer
    +AcceptedAt : DateTimeOffset
  }
  class AttachRequest {
    <<record>>
    +Owner : AttachedTo
    +OwnerId : Guid
    +FileName : string
    +ContentType : string
    +ContentBase64 : string
  }
  class BackupFolder {
    <<record>>
    +Path : string
  }
  class BackupResult {
    <<record>>
    +Path : string
  }
  class CorrectActualsRequest {
    <<record>>
    +Actuals : GradedActuals
    +Reason : string
  }
  class CorrectChargeRequest {
    <<record>>
    +Session : ChargeSession
    +Reason : string
  }
  class CorrectDriveRequest {
    <<record>>
    +Drive : Drive
    +Reason : string
  }
  class CorrectExpenseRequest {
    <<record>>
    +Expense : Expense
    +Reason : string
  }
```

Links outside this diagram:

- AcceptOfferRequest holds Offer (`GigLedger.Core`)
- AttachRequest holds AttachedTo (`GigLedger.Core`)
- CorrectActualsRequest holds GradedActuals (`GigLedger.Core`)
- CorrectChargeRequest holds ChargeSession (`GigLedger.Core`)
- CorrectDriveRequest holds Drive (`GigLedger.Core`)
- CorrectExpenseRequest holds Expense (`GigLedger.Core`)

## GigLedger.Web (2 of 3)

```mermaid
classDiagram
  class Created {
    <<record>>
    +Id : Guid
  }
  class Display {
    +Money(value) string
    +Rate(value) string
    +PerMile(value) string
    +Share(value) string
    +Miles(value) string
    +Hours(value) string
    +Time(value) string
    +Efficiency(value) string
    +PricePerKwh(value) string
  }
  class EndShiftRequest {
    <<record>>
    +EndedAt : DateTimeOffset
    +EndOdometer : Graded~decimal~
  }
  class HomeRateRequest {
    <<record>>
    +PerKwh : decimal
    +EffectiveFrom : DateOnly
  }
  class LedgerApi {
    +MapLedgerApi(app)
  }
  class LedgerApiRecord {
    +MapRecordApi(api)
  }
  class MileageRateRequest {
    <<record>>
    +PerMile : decimal
  }
  class Refusal {
    +IsRefusal(e) bool
    +Reason(e) string
  }
```

Links outside this diagram:

- EndShiftRequest holds Graded (`GigLedger.Core`)

## GigLedger.Web (3 of 3)

```mermaid
classDiagram
  class StartShiftRequest {
    <<record>>
    +Platform : string
    +StartedAt : DateTimeOffset
    +StartOdometer : Graded~decimal~
  }
  class TipRequest {
    <<record>>
    +Amount : decimal
    +PostedAt : DateTimeOffset
  }
  class Verification {
    <<record>>
    +Intact : bool
  }
```

Links outside this diagram:

- StartShiftRequest holds Graded (`GigLedger.Core`)

## GigLedger.Web.Components

```mermaid
classDiagram
  class App {
    <<ComponentBase>>
  }
  class Routes {
    <<ComponentBase>>
  }
  class _Imports
```

## GigLedger.Web.Components.Layout

```mermaid
classDiagram
  class MainLayout {
    <<LayoutComponentBase>>
  }
```

## GigLedger.Web.Components.Pages (1 of 2)

```mermaid
classDiagram
  class Backup {
    <<ComponentBase>>
  }
  class Charges {
    <<ComponentBase>>
  }
  class Error {
    <<ComponentBase>>
  }
  class Expenses {
    <<ComponentBase>>
  }
  class Home {
    <<ComponentBase>>
  }
  class Home_ActualsInput {
    +Minutes : int?
    +Route : decimal?
    +Return : decimal?
  }
  class Mileage {
    <<ComponentBase>>
  }
  class OfferPage {
    <<ComponentBase>>
  }
```

## GigLedger.Web.Components.Pages (2 of 2)

```mermaid
classDiagram
  class ReportsPage {
    <<ComponentBase>>
  }
  class Summary {
    <<ComponentBase>>
    +Id : Guid
  }
  class TaxPage {
    <<ComponentBase>>
  }
```
