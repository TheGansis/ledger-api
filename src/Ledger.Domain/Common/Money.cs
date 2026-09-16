namespace Ledger.Domain.Common;

/// <summary>
/// Денежная сумма с валютой. decimal, а не double — банковские суммы не терпят
/// двоичной погрешности. Точность 4 знака после запятой (как в SQL money/numeric(19,4)).
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public const int Scale = 4;

    public static Money Of(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException("currency.invalid", "Currency must be a 3-letter ISO code.");
        if (decimal.Round(amount, Scale) != amount)
            throw new DomainException("amount.scale", $"Amount must have at most {Scale} decimal places.");
        return new Money(amount, currency.ToUpperInvariant());
    }

    public bool IsPositive => Amount > 0;

    public Money Add(Money other) => Same(other) with { Amount = Amount + other.Amount };
    public Money Subtract(Money other) => Same(other) with { Amount = Amount - other.Amount };

    private Money Same(Money other) =>
        other.Currency == Currency
            ? this
            : throw new DomainException("currency.mismatch", $"Currency mismatch: {Currency} vs {other.Currency}.");

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
