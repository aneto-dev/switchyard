using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;
using Xunit;

namespace Switchyard.Ordering.Application.Tests;

public sealed class OrderPlacementPaymentTransitionTests
{
    [Fact]
    public void AuthorisedPaymentMovesThroughConfirmingBeforeConfirmationCompletes()
    {
        var now =
            new DateTimeOffset(
                2026,
                10,
                18,
                18,
                30,
                0,
                TimeSpan.Zero);

        var paymentRequestId =
            Guid.NewGuid();

        var process =
            OrderPlacementProcess.Rehydrate(
                new OrderId(
                    Guid.NewGuid()),
                OrderPlacementState.AwaitingPayment,
                paymentRequestId,
                now,
                now,
                new[]
                {
                    OrderPlacementLine.Rehydrate(
                        new OrderLineId(
                            Guid.NewGuid()),
                        Guid.NewGuid(),
                        "BIKE-001",
                        1,
                        OrderPlacementLineState.Reserved,
                        Guid.NewGuid(),
                        now.AddMinutes(15))
                });

        var transition =
            process.RecordPaymentAuthorised(
                paymentRequestId,
                now.AddSeconds(1));

        Assert.True(
            transition.Changed);
        Assert.True(
            transition.ConfirmOrder);
        Assert.False(
            transition.FailOrder);
        Assert.Empty(
            transition.Releases);
        Assert.Equal(
            OrderPlacementState.Confirming,
            process.State);

        process.CompleteConfirmation(
            now.AddSeconds(2));

        Assert.Equal(
            OrderPlacementState.Confirmed,
            process.State);
    }
}
