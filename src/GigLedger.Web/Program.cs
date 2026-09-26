using System.Text.Json.Serialization;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using GigLedger.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// One SQLite file, path from configuration (SDD 8, NFR-3).
builder.Services.AddDbContext<LedgerContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Ledger") ?? "Data Source=gigledger.db"));
builder.Services.AddSingleton(TimeProvider.System);

// The UI and the API resolve the same services (FR-34).
builder.Services.AddScoped<LedgerServices>();
builder.Services.AddScoped<ISettingsService>(sp => sp.GetRequiredService<LedgerServices>());
builder.Services.AddScoped<IOfferService>(sp => sp.GetRequiredService<LedgerServices>());
builder.Services.AddScoped<ITripService>(sp => sp.GetRequiredService<LedgerServices>());
builder.Services.AddScoped<IShiftService>(sp => sp.GetRequiredService<LedgerServices>());

// Grades travel by name in JSON, as they do in the database.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<LedgerContext>().Database.Migrate();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// In a container, TLS ends in front of the pod (ingress or load balancer) and the app serves
// plain HTTP on 8080, so there is no HTTPS port to redirect to. The official .NET images set
// DOTNET_RUNNING_IN_CONTAINER.
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
    app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapLedgerApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Visible to the API tests' WebApplicationFactory.</summary>
public partial class Program;
