using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Condo.Tests.Support;

public class DocumentVerificationTests
{
    private static ExpenseSettlementSummaryDto Summary(ExpensePeriod period, decimal expenses = 1_000_000m) => new()
    {
        ExpensePeriodId = period.Id,
        BuildingId = period.BuildingId,
        ExpensePeriodName = "Octubre 2026",
        BuildingName = "Torre Sol",
        TotalBuildingExpenses = expenses,
        GeneratedAtUtc = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc),
        IsCalculated = true,
        ExpenseLines = [new() { Supplier = "ANDE", Description = "Consumo", Amount = expenses, Category = "Ande" }]
    };

    private static (TestDb Db, DocumentVerificationService Service, ExpensePeriod Period) Setup()
    {
        var db = new TestDb();
        var company = db.AddCompany("Gestiones SA");
        var building = db.AddBuilding(company, "Torre Sol");
        var period = new ExpensePeriod
        {
            CompanyId = company.Id, BuildingId = building.Id, Year = 2026, Month = 10, Name = "Octubre 2026",
            Status = ExpensePeriodStatus.Published
        };
        db.Db.ExpensePeriods.Add(period);
        db.Db.SaveChanges();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Frontend:BaseUrl"] = "https://expenses.test" }).Build();
        return (db, new DocumentVerificationService(db.Db, config), period);
    }

    private static string TokenOf(string url) => url[(url.LastIndexOf('/') + 1)..];

    [Fact]
    public async Task Same_content_reuses_the_same_token()
    {
        var (db, service, period) = Setup();
        using var _ = db;

        var first = await service.IssueSettlementAsync(period, Summary(period), "Admin", default);
        var second = await service.IssueSettlementAsync(period, Summary(period), "Admin", default);

        Assert.Equal(first, second);
        Assert.StartsWith("https://expenses.test/v/", first);
        Assert.Equal(22, TokenOf(first).Length);
        Assert.Equal(1, await db.Db.DocumentVerifications.CountAsync());
    }

    [Fact]
    public async Task Changed_content_supersedes_the_previous_paper()
    {
        var (db, service, period) = Setup();
        using var _ = db;

        var oldUrl = await service.IssueSettlementAsync(period, Summary(period, 1_000_000m), "Admin", default);
        var newUrl = await service.IssueSettlementAsync(period, Summary(period, 1_250_000m), "Admin", default);

        Assert.NotEqual(oldUrl, newUrl);
        Assert.Equal("superseded", (await service.LookupAsync(TokenOf(oldUrl), default)).Status);
        var current = await service.LookupAsync(TokenOf(newUrl), default);
        Assert.Equal("valid", current.Status);
        Assert.Equal("Torre Sol", current.BuildingName);
        Assert.Equal("Octubre 2026", current.PeriodName);
        Assert.Equal("Admin", current.AdministratorName);
        Assert.False(string.IsNullOrEmpty(current.TotalFormatted));
    }

    [Fact]
    public async Task Draft_period_is_reported_as_draft_and_publishing_replaces_it()
    {
        var (db, service, period) = Setup();
        using var _ = db;
        period.Status = ExpensePeriodStatus.Draft;

        var draftUrl = await service.IssueSettlementAsync(period, Summary(period), null, default);
        Assert.Equal("draft", (await service.LookupAsync(TokenOf(draftUrl), default)).Status);
        // Sin administrador cargado en el edificio, muestra el nombre de la empresa.
        Assert.Equal("Gestiones SA", (await service.LookupAsync(TokenOf(draftUrl), default)).AdministratorName);

        period.Status = ExpensePeriodStatus.Published;
        var publishedUrl = await service.IssueSettlementAsync(period, Summary(period), null, default);

        Assert.NotEqual(draftUrl, publishedUrl);
        Assert.Equal("superseded", (await service.LookupAsync(TokenOf(draftUrl), default)).Status);
        Assert.Equal("valid", (await service.LookupAsync(TokenOf(publishedUrl), default)).Status);
    }

    [Fact]
    public async Task Deleted_period_and_revoked_token_are_not_valid()
    {
        var (db, service, period) = Setup();
        using var _ = db;

        var url = await service.IssueSettlementAsync(period, Summary(period), null, default);
        period.IsDeleted = true;
        await db.Db.SaveChangesAsync();
        Assert.Equal("unavailable", (await service.LookupAsync(TokenOf(url), default)).Status);

        period.IsDeleted = false;
        var row = await db.Db.DocumentVerifications.SingleAsync();
        row.RevokedAtUtc = DateTime.UtcNow;
        await db.Db.SaveChangesAsync();
        Assert.Equal("revoked", (await service.LookupAsync(TokenOf(url), default)).Status);
    }

    [Fact]
    public async Task Valid_document_carries_the_buttons_data_and_bank_accounts_only_when_the_switch_is_on()
    {
        var (db, service, period) = Setup();
        using var _ = db;
        var building = await db.Db.Buildings.SingleAsync();
        building.WhatsAppPhone = "+595981123456";
        db.Db.BuildingBankAccounts.Add(new BuildingBankAccount
        {
            CompanyId = period.CompanyId, BuildingId = building.Id, BankName = "Banco Demo", AccountNumber = "123-456",
            HolderName = "Consorcio Torre Sol", Alias = "torresol"
        });
        period.DueDate = new DateOnly(2026, 11, 15);
        await db.Db.SaveChangesAsync();

        var url = await service.IssueSettlementAsync(period, Summary(period), null, default);
        var off = await service.LookupAsync(TokenOf(url), default);

        Assert.Equal("valid", off.Status);
        Assert.Equal("2026-11-15", off.DueDate);
        Assert.StartsWith("https://wa.me/595981123456?text=", off.WhatsAppUrl);
        Assert.Contains("Octubre%202026", off.WhatsAppUrl);
        Assert.Empty(off.BankAccounts); // apagado por defecto

        building.QrShowBankAccounts = true;
        await db.Db.SaveChangesAsync();
        var on = await service.LookupAsync(TokenOf(url), default);

        var account = Assert.Single(on.BankAccounts);
        Assert.Equal("Banco Demo", account.BankName);
        Assert.Equal("torresol", account.Alias);
    }

    [Fact]
    public async Task WhatsApp_goes_to_the_active_building_manager_before_the_building_number()
    {
        var (db, service, period) = Setup();
        using var _ = db;
        var building = await db.Db.Buildings.SingleAsync();
        building.WhatsAppPhone = "+595981000000";
        var company = await db.Db.Companies.SingleAsync();
        // Un encargado sin telefono no tapa al siguiente; el 0 inicial del numero local se descarta.
        var noPhone = db.AddUser(company, UserRole.BuildingManager, "SinTelefono");
        var manager = db.AddUser(company, UserRole.BuildingManager, "Encargado");
        manager.PhonePrefix = "+595";
        manager.Phone = "0971 222 333";
        var inactive = db.AddUser(company, UserRole.BuildingManager, "Inactivo");
        inactive.WhatsAppPhone = "+595999999999";
        foreach (var u in new[] { noPhone, manager, inactive })
            db.Db.UserBuildingAccesses.Add(new UserBuildingAccess { CompanyId = company.Id, ApplicationUserId = u.Id, BuildingId = building.Id, IsActive = u != inactive });
        await db.Db.SaveChangesAsync();

        var url = await service.IssueSettlementAsync(period, Summary(period), null, default);
        var result = await service.LookupAsync(TokenOf(url), default);

        Assert.StartsWith("https://wa.me/595971222333?text=", result.WhatsAppUrl);
        Assert.Equal("manager", result.WhatsAppContact);

        // Sin encargados con acceso activo, vuelve al WhatsApp del edificio.
        foreach (var access in db.Db.UserBuildingAccesses) access.IsActive = false;
        await db.Db.SaveChangesAsync();
        var fallback = await service.LookupAsync(TokenOf(url), default);
        Assert.StartsWith("https://wa.me/595981000000?text=", fallback.WhatsAppUrl);
        Assert.Equal("administration", fallback.WhatsAppContact);
    }

    [Fact]
    public async Task Draft_and_replaced_documents_do_not_expose_the_buttons_data()
    {
        var (db, service, period) = Setup();
        using var _ = db;
        var building = await db.Db.Buildings.SingleAsync();
        building.WhatsAppPhone = "+595981123456";
        building.QrShowBankAccounts = true;
        period.Status = ExpensePeriodStatus.Draft;
        await db.Db.SaveChangesAsync();

        var draftUrl = await service.IssueSettlementAsync(period, Summary(period), null, default);
        var draft = await service.LookupAsync(TokenOf(draftUrl), default);
        Assert.Equal("draft", draft.Status);
        Assert.Null(draft.WhatsAppUrl);
        Assert.Empty(draft.BankAccounts);

        period.Status = ExpensePeriodStatus.Published;
        await service.IssueSettlementAsync(period, Summary(period), null, default);
        var replaced = await service.LookupAsync(TokenOf(draftUrl), default);
        Assert.Equal("superseded", replaced.Status);
        Assert.Null(replaced.WhatsAppUrl);
        Assert.Empty(replaced.BankAccounts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corto")]
    [InlineData("../../etc/passwd")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Unknown_or_malformed_tokens_give_the_same_empty_answer(string? token)
    {
        var (db, service, _) = Setup();
        using var _ = db;

        var result = await service.LookupAsync(token, default);

        Assert.Equal("notfound", result.Status);
        Assert.Null(result.BuildingName);
        Assert.Null(result.TotalFormatted);
    }

    [Fact]
    public void Qr_svg_is_generated()
    {
        var svg = QrCodeImage.Svg("https://expenses.com.py/v/AAAAAAAAAAAAAAAAAAAAAA");
        Assert.Contains("<svg", svg);
        Assert.Equal("expenses.com.py/v/AAAAAAAAAAAAAAAAAAAAAA", QrCodeImage.ShortText("https://expenses.com.py/v/AAAAAAAAAAAAAAAAAAAAAA"));
    }
}
