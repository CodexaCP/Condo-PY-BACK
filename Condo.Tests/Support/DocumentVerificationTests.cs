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
