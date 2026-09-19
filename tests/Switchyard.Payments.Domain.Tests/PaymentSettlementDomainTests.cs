using Switchyard.Payments.Domain.Payments;
using Switchyard.Payments.Domain.Settlement;
using Xunit;

namespace Switchyard.Payments.Domain.Tests;

public sealed class PaymentSettlementDomainTests
{
    [Fact]
    public void SuccessfulSettlementRequiresProviderReference()
    {
        Assert.Throws<ArgumentException>(
            () => new PaymentSettlementAttempt(
                Guid.NewGuid(),
                PaymentId.New(),
                PaymentSettlementAction.Capture,
                PaymentSettlementStatus.Succeeded,
                "provider-key",
                null,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void IndeterminateSettlementCanExistWithoutProviderReference()
    {
        var attempt = new PaymentSettlementAttempt(
            Guid.NewGuid(),
            PaymentId.New(),
            PaymentSettlementAction.Void,
            PaymentSettlementStatus.Indeterminate,
            "provider-key",
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(PaymentSettlementStatus.Indeterminate, attempt.Status);
        Assert.Null(attempt.ProviderReference);
    }

    [Fact]
    public void PendingSettlementCannotHaveResolvedTime()
    {
        Assert.Throws<ArgumentException>(
            () => new PaymentSettlementAttempt(
                Guid.NewGuid(),
                PaymentId.New(),
                PaymentSettlementAction.Capture,
                PaymentSettlementStatus.Pending,
                "provider-key",
                null,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch));
    }
}
