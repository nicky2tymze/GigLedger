using Bunit;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using GigLedger.Web.Components.Pages;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>Slice 3a screens, written before the pages.</summary>
public sealed class RecordPageTests : TestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateOnly Today = new(2026, 9, 25);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(Now);
    private readonly string _backups = Directory.CreateTempSubdirectory("gigledger-ui-backup-").FullName;

    public RecordPageTests()
    {
        _connection.Open();
        _ledger = new LedgerServices(LedgerDatabase.Open(_connection), _clock);
        JSInterop.Mode = JSRuntimeMode.Loose; // InputFile calls into JS on render
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IMileageService>(_ledger);
        Services.AddSingleton<IExpenseService>(_ledger);
        Services.AddSingleton<IAttachmentService>(_ledger);
        Services.AddSingleton<IBackupService>(_ledger);
        Services.AddSingleton(new BackupFolder(_backups));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _connection.Dispose();
        Directory.Delete(_backups, recursive: true);
    }

    private IMileageService Mileage => _ledger;
    private IExpenseService Expenses => _ledger;
    private IAttachmentService Attachments => _ledger;

    private static Drive DriveIn(decimal end = 17_958m) =>
        new(Today, new(17_948m, Grade.Measured), new(end, Grade.Measured), Purpose.Work, "Home to Walmart 1102");

    // ---- Mileage ----

    [Fact]
    public void UI_Mileage_LogsADriveWithWhereAndWhy()
    {
        var page = RenderComponent<Mileage>();
        page.Find("#drive-start").Change("17948");
        page.Find("#drive-end").Change("17958");
        page.Find("#drive-purpose").Change("Personal");
        page.Find("#drive-description").Change("Walmart for binders");
        page.Find("#log-drive").Click();

        var drive = Assert.Single(Mileage.Year(2026)).Drive;
        Assert.Equal((Purpose.Personal, "Walmart for binders", Today), (drive.Purpose, drive.Description, drive.Date));
        Assert.Equal(Display.Miles(10m), page.Find("#personal-miles").TextContent.Trim());
    }

    [Fact]
    public void UI_Mileage_ADriveWithoutWhereAndWhyShowsTheReason()
    {
        var page = RenderComponent<Mileage>();
        page.Find("#drive-start").Change("17948");
        page.Find("#drive-end").Change("17958");
        page.Find("#log-drive").Click();
        Assert.Contains("where", page.Find(".error").TextContent);
        Assert.Empty(Mileage.Year(2026));
    }

    [Fact]
    public void UI_Mileage_CorrectsADriveWithAReason()
    {
        Mileage.Log(DriveIn(end: 17_985m));
        var page = RenderComponent<Mileage>();
        page.Find("#correct-0").Click();
        page.Find("#drive-end").Change("17958");
        page.Find("#correction-reason").Change("typed 17,958 as 17,985");
        page.Find("#save-correction").Click();

        var current = Assert.Single(Mileage.Year(2026));
        Assert.Equal(10m, RecordKeeping.Miles(current.Drive));
        Assert.Equal(2, Mileage.History(current.Id).Count);
        Assert.Contains("corrected", page.Find(".drive").TextContent);
    }

    // ---- Expenses ----

    [Fact]
    public void UI_Expenses_RecordsAnExpense()
    {
        var page = RenderComponent<Expenses>();
        page.Find("#expense-category").Change("Supplies");
        page.Find("#expense-amount").Change("18.47");
        page.Find("#expense-description").Change("Binders");
        page.Find("#record-expense").Click();

        var expense = Assert.Single(Expenses.Year(2026)).Expense;
        Assert.Equal((ExpenseCategory.Supplies, 18.47m, "Binders"), (expense.Category, expense.Amount.Value, expense.Description));
    }

    [Fact]
    public void UI_Expenses_AttachesAReceipt()
    {
        var id = Expenses.Record(new Expense(Today, ExpenseCategory.Supplies, new(18.47m, Grade.Measured), "Binders"));
        var page = RenderComponent<Expenses>();
        var content = "%PDF receipt"u8.ToArray();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(content, "walmart.pdf", contentType: "application/pdf"));

        var info = Assert.Single(Attachments.For(AttachedTo.Expense, id));
        Assert.Equal("walmart.pdf", info.FileName);
        Assert.Equal(RecordKeeping.Sha256(content), info.Sha256);
        Assert.Contains("walmart.pdf", page.Find(".expense").TextContent);
    }

    // ---- Backup ----

    [Fact]
    public void UI_Backup_WritesADatedCopyAndSaysWhere()
    {
        var page = RenderComponent<Backup>();
        page.Find("#backup-now").Click();
        var written = Assert.Single(Directory.GetFiles(_backups));
        Assert.Contains(Path.GetFileName(written), page.Find("#backup-result").TextContent);
    }
}
