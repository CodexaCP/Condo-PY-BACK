using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Tests.Finance;

/// <summary>
/// Caracterizacion de las reglas que mandan un cobro de propietario a una cuenta y a un rubro del libro de Finanzas
/// (FinanceLedgerRules.ForPayment) y de como se elige la cuenta por defecto (LedgerAccounts.Resolve). Son reglas puras, sin base de datos.
/// Fijan el comportamiento actual antes de agregar un tipo de cuenta nuevo (cuenta en transito de los cobros online).
/// </summary>
public class FinanceLedgerPaymentRulesTests
{
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Fund = Guid.NewGuid();
    private static readonly Guid Bank = Guid.NewGuid();
    private static readonly LedgerAccounts All = new(Cash, Fund, Bank);

    // ── ForPayment ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Check)]
    [InlineData(PaymentMethod.Other)]
    public void UnCobroQueNoEsEfectivoVaALaCuentaPorDefecto(PaymentMethod method)
    {
        var result = FinanceLedgerRules.ForPayment(method, CollectionClass.Ordinary, All);

        Assert.Equal(Bank, result.AccountId);
        Assert.Equal(LedgerDirection.In, result.Direction);
        Assert.Equal(FinanceChartTemplate.CollectionOrdinary, result.RubroKey);
    }

    [Fact]
    public void UnCobroEnEfectivoVaALaCaja()
    {
        Assert.Equal(Cash, FinanceLedgerRules.ForPayment(PaymentMethod.Cash, CollectionClass.Ordinary, All).AccountId);
    }

    [Fact]
    public void SinCajaElEfectivoVaALaCuentaPorDefecto()
    {
        var result = FinanceLedgerRules.ForPayment(PaymentMethod.Cash, CollectionClass.Ordinary, new LedgerAccounts(null, Fund, Bank));

        Assert.Equal(Bank, result.AccountId);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.Card)]
    public void LoAplicadoAlFondoDeReservaVaALaCuentaDelFondoSeaCualSeaElMetodo(PaymentMethod method)
    {
        var result = FinanceLedgerRules.ForPayment(method, CollectionClass.ReserveFund, All);

        Assert.Equal(Fund, result.AccountId);
        Assert.Equal(FinanceChartTemplate.CollectionReserveFund, result.RubroKey);
    }

    [Fact]
    public void SinCuentaDelFondoElAporteQuedaEnLaCuentaPorDefecto()
    {
        var result = FinanceLedgerRules.ForPayment(PaymentMethod.BankTransfer, CollectionClass.ReserveFund, new LedgerAccounts(Cash, null, Bank));

        Assert.Equal(Bank, result.AccountId);
    }

    [Fact]
    public void SinNingunaCuentaElCobroQuedaSinCuentaPeroConSuRubro()
    {
        var result = FinanceLedgerRules.ForPayment(PaymentMethod.BankTransfer, CollectionClass.Extraordinary, new LedgerAccounts(null, null, null));

        Assert.Null(result.AccountId);
        Assert.Equal(FinanceChartTemplate.CollectionExtraordinary, result.RubroKey);
    }

    [Theory]
    [InlineData(CollectionClass.Ordinary, "Ordinary")]
    [InlineData(CollectionClass.Extraordinary, "Extraordinary")]
    [InlineData(CollectionClass.IndividualAdjustment, "IndividualAdjustment")]
    [InlineData(CollectionClass.LateFee, "LateFee")]
    [InlineData(CollectionClass.ReserveFund, "ReserveFund")]
    public void CadaClaseDeCobroTieneSuPropioRubro(CollectionClass cls, string name)
    {
        var expected = name switch
        {
            "Ordinary" => FinanceChartTemplate.CollectionOrdinary,
            "Extraordinary" => FinanceChartTemplate.CollectionExtraordinary,
            "IndividualAdjustment" => FinanceChartTemplate.CollectionIndividualAdjustment,
            "LateFee" => FinanceChartTemplate.CollectionLateFee,
            _ => FinanceChartTemplate.CollectionReserveFund
        };

        Assert.Equal(expected, FinanceLedgerRules.CollectionKey(cls));
        Assert.Equal(expected, FinanceLedgerRules.ForPayment(PaymentMethod.BankTransfer, cls, All).RubroKey);
    }

    [Theory]
    [InlineData(ExpenseChargeType.Ordinary, false, CollectionClass.Ordinary)]
    [InlineData(ExpenseChargeType.ReserveFund, false, CollectionClass.ReserveFund)]
    [InlineData(ExpenseChargeType.Extraordinary, false, CollectionClass.Extraordinary)]
    [InlineData(ExpenseChargeType.Individual, false, CollectionClass.IndividualAdjustment)]
    [InlineData(ExpenseChargeType.Adjustment, false, CollectionClass.IndividualAdjustment)]
    [InlineData(ExpenseChargeType.Ordinary, true, CollectionClass.LateFee)]
    [InlineData(ExpenseChargeType.Adjustment, true, CollectionClass.LateFee)]
    public void LaClaseDeCobroSaleDelTipoDeCargoYLaMoraTienePrioridad(ExpenseChargeType type, bool isLateFee, CollectionClass expected)
    {
        Assert.Equal(expected, FinanceLedgerRules.ClassifyCharge(type, isLateFee));
    }

    // ── Cuenta por defecto (LedgerAccounts.Resolve) ──────────────────────────

    private static FinancialAccount Account(FinancialAccountType type, bool active = true, bool deleted = false) =>
        new() { Name = type.ToString(), Type = type, IsActive = active, IsDeleted = deleted };

    [Fact]
    public void ElUnicoBancoActivoEsLaCuentaPorDefecto()
    {
        var bank = Account(FinancialAccountType.Bank);

        var resolved = LedgerAccounts.Resolve([Account(FinancialAccountType.Cash), bank, Account(FinancialAccountType.ReserveFund)], null);

        Assert.Equal(bank.Id, resolved.DefaultId);
    }

    [Fact]
    public void CajaYFondoSonLaUnicaActivaDeCadaTipo()
    {
        var cash = Account(FinancialAccountType.Cash);
        var fund = Account(FinancialAccountType.ReserveFund);

        var resolved = LedgerAccounts.Resolve([cash, fund, Account(FinancialAccountType.Bank)], null);

        Assert.Equal(cash.Id, resolved.CashId);
        Assert.Equal(fund.Id, resolved.FundId);
    }

    [Fact]
    public void ConDosBancosSinConfigurarNoHayCuentaPorDefecto()
    {
        var resolved = LedgerAccounts.Resolve([Account(FinancialAccountType.Bank), Account(FinancialAccountType.Bank)], null);

        Assert.Null(resolved.DefaultId);
    }

    [Fact]
    public void LaCuentaPorDefectoConfiguradaGanaSiSigueActivaYNoEsElFondo()
    {
        var one = Account(FinancialAccountType.Bank);
        var two = Account(FinancialAccountType.Bank);
        var fund = Account(FinancialAccountType.ReserveFund);

        Assert.Equal(two.Id, LedgerAccounts.Resolve([one, two, fund], two.Id).DefaultId);
        Assert.Null(LedgerAccounts.Resolve([one, two, fund], fund.Id).DefaultId);   // el fondo nunca es la cuenta por defecto
        Assert.Null(LedgerAccounts.Resolve([one, two], Guid.NewGuid()).DefaultId);  // configurada pero inexistente: se ignora
    }

    [Fact]
    public void SinBancosLaUnicaCuentaOperativaActivaEsLaPorDefecto()
    {
        var cash = Account(FinancialAccountType.Cash);

        var resolved = LedgerAccounts.Resolve([cash, Account(FinancialAccountType.ReserveFund)], null);

        Assert.Equal(cash.Id, resolved.DefaultId);
    }

    [Fact]
    public void LasCuentasInactivasOEliminadasNoCuentan()
    {
        var bank = Account(FinancialAccountType.Bank);

        var resolved = LedgerAccounts.Resolve(
            [bank, Account(FinancialAccountType.Bank, active: false), Account(FinancialAccountType.Bank, deleted: true)], null);

        Assert.Equal(bank.Id, resolved.DefaultId);
    }
}
