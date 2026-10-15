namespace Tailoring.Domain.Finance;

/// <summary>Pure money rules for invoices. Decimal only; every figure is rounded to 2 places, half away from zero, per line.</summary>
public static class InvoiceMath
{
    public readonly record struct LineAmounts(decimal Gross, decimal Net, decimal Tax, decimal Total);

    public static decimal R(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    public static string? ValidateLine(string description, decimal quantity, decimal unitPrice, decimal discount, decimal taxPercent)
    {
        if (string.IsNullOrWhiteSpace(description)) return "Every line needs a description.";
        if (quantity <= 0) return $"'{description}': quantity must be above zero.";
        if (R(quantity) != quantity) return $"'{description}': quantity can have at most 2 decimal places.";
        if (unitPrice < 0) return $"'{description}': price cannot be negative.";
        if (discount < 0) return $"'{description}': discount cannot be negative.";
        if (taxPercent is < 0 or > 100) return $"'{description}': tax must be between 0% and 100%.";
        if (R(unitPrice) != unitPrice || R(discount) != discount) return $"'{description}': amounts can have at most 2 decimal places.";
        if (discount > R(quantity * unitPrice)) return $"'{description}': the discount is larger than the line.";
        return null;
    }

    /// <summary>Net = quantity x price - discount. Tax is on the net. Total = net + tax. Call <see cref="ValidateLine"/> first.</summary>
    public static LineAmounts Line(decimal quantity, decimal unitPrice, decimal discount, decimal taxPercent)
    {
        var gross = R(quantity * unitPrice);
        var net = gross - discount;
        var tax = R(net * taxPercent / 100m);
        return new LineAmounts(gross, net, tax, net + tax);
    }

    /// <summary>
    /// Splits an order-level discount across lines in proportion to their value. Uses the largest-remainder method,
    /// so the shares add up to EXACTLY the discount and no share exceeds its line.
    /// </summary>
    public static decimal[] AllocateDiscount(IReadOnlyList<decimal> grossAmounts, decimal discount)
    {
        var shares = new decimal[grossAmounts.Count];
        var sum = grossAmounts.Sum();
        if (discount <= 0 || sum <= 0) return shares;
        if (discount > sum) throw new ArgumentException("Discount is larger than the total.", nameof(discount));

        var exact = grossAmounts.Select(g => g * discount / sum).ToArray();
        for (var i = 0; i < shares.Length; i++) shares[i] = Math.Floor(exact[i] * 100m) / 100m;

        var cents = (int)decimal.Round((discount - shares.Sum()) * 100m);
        foreach (var i in Enumerable.Range(0, shares.Length).OrderByDescending(i => exact[i] - shares[i]).ThenBy(i => i).Take(cents))
            shares[i] += 0.01m;
        return shares;
    }

    public static decimal Balance(decimal total, decimal verifiedPaid, decimal credited) => total - verifiedPaid - credited;

    /// <summary>Unpaid | PartPaid | Paid | Credited. Derived from verified payments and credit notes: there is no "mark as paid".</summary>
    public static string PaymentStatus(decimal total, decimal verifiedPaid, decimal credited)
    {
        if (Balance(total, verifiedPaid, credited) <= 0) return verifiedPaid > 0 ? "Paid" : "Credited";
        return verifiedPaid > 0 ? "PartPaid" : "Unpaid";
    }
}
