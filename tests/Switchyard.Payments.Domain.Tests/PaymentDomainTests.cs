using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Domain.Payments;
using Xunit;

namespace Switchyard.Payments.Domain.Tests;

public sealed class PaymentDomainTests
{
    [Fact]
    public void PaymentAmountNormalisesCurrency()
    {
        var amount = new PaymentAmount(1299.99m, " gbp ");

        Assert.Equal(1299.99m, amount.Amount);
        Assert.Equal("GBP", amount.Currency);
    }

    [Fact]
    public void PaymentAmountRejectsSubMinorUnitPrecision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaymentAmount(10.001m, "GBP"));
    }

    [Fact]
    public void DefiniteAuthorisationRequiresProviderReference()
    {
        Assert.Throws<ArgumentException>(
            () => new PaymentAuthorisationAttempt(
                Guid.NewGuid(),
                PaymentId.New(),
                "provider-key",
                null,
                PaymentAuthorisationStatus.Authorised,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void IndeterminateAuthorisationCanExistWithoutProviderReference()
    {
        var paymentId = PaymentId.New();

        var attempt = new PaymentAuthorisationAttempt(
            Guid.NewGuid(),
            paymentId,
            "provider-key",
            null,
            PaymentAuthorisationStatus.Indeterminate,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(PaymentAuthorisationStatus.Indeterminate, attempt.Status);
        Assert.Null(attempt.ProviderReference);
    }
}
