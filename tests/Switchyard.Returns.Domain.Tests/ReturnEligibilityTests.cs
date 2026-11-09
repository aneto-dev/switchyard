using Switchyard.Returns.Domain.Eligibility;
using Xunit;

namespace Switchyard.Returns.Domain.Tests;

public sealed class ReturnEligibilityTests
{
    [Fact]
    public void AuthoriseReturnReservesDeliveredQuantity()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var eligibility =
            ReturnEligibility.Create(
                orderId,
                "SW-RET-100");

        eligibility.RecordDelivery(
            Guid.NewGuid(),
            new[]
            {
                new DeliveredLine(
                    lineId,
                    2)
            },
            new DateTimeOffset(
                2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

        var changed =
            eligibility.AuthoriseReturn(
                Guid.NewGuid(),
                new[]
                {
                    new ReturnRequestLine(
                        lineId,
                        1)
                },
                new DateTimeOffset(
                    2026, 11, 9, 12, 5, 0, TimeSpan.Zero));

        Assert.True(changed);
        Assert.Equal(
            1,
            eligibility.GetReturnableQuantity(
                lineId));
        Assert.Equal(
            1,
            eligibility.Lines.Single()
                       .AuthorisedReturnQuantity);
        Assert.Equal(
            0,
            eligibility.Lines.Single()
                       .ReturnedQuantity);
    }

    [Fact]
    public void AuthoriseReturnRejectsQuantityBeyondEligibility()
    {
        var lineId = Guid.NewGuid();
        var eligibility =
            ReturnEligibility.Create(
                Guid.NewGuid(),
                "SW-RET-101");

        eligibility.RecordDelivery(
            Guid.NewGuid(),
            new[]
            {
                new DeliveredLine(
                    lineId,
                    1)
            },
            new DateTimeOffset(
                2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

        var exception =
            Assert.Throws<ReturnQuantityNotEligibleException>(
                () =>
                    eligibility.AuthoriseReturn(
                        Guid.NewGuid(),
                        new[]
                        {
                            new ReturnRequestLine(
                                lineId,
                                2)
                        },
                        new DateTimeOffset(
                            2026, 11, 9, 12, 5, 0, TimeSpan.Zero)));

        Assert.Equal(
            1,
            exception.ReturnableQuantity);
    }

    [Fact]
    public void ExactReturnRequestReplayDoesNotReserveQuantityTwice()
    {
        var lineId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var requestedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 12, 5, 0, TimeSpan.Zero);

        var eligibility =
            ReturnEligibility.Create(
                Guid.NewGuid(),
                "SW-RET-102");

        eligibility.RecordDelivery(
            Guid.NewGuid(),
            new[]
            {
                new DeliveredLine(
                    lineId,
                    2)
            },
            requestedAtUtc.AddMinutes(-5));

        var first =
            eligibility.AuthoriseReturn(
                requestId,
                new[]
                {
                    new ReturnRequestLine(
                        lineId,
                        1)
                },
                requestedAtUtc);

        var replay =
            eligibility.AuthoriseReturn(
                requestId,
                new[]
                {
                    new ReturnRequestLine(
                        lineId,
                        1)
                },
                requestedAtUtc);

        Assert.True(first);
        Assert.False(replay);
        Assert.Equal(
            1,
            eligibility.GetReturnableQuantity(
                lineId));
    }
    [Fact]
    public void ReusedReturnRequestIdWithDifferentQuantityConflicts()
    {
        var lineId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var requestedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 12, 5, 0, TimeSpan.Zero);

        var eligibility =
            ReturnEligibility.Create(
                Guid.NewGuid(),
                "SW-RET-103");

        eligibility.RecordDelivery(
            Guid.NewGuid(),
            new[]
            {
                new DeliveredLine(
                    lineId,
                    3)
            },
            requestedAtUtc.AddMinutes(-5));

        eligibility.AuthoriseReturn(
            requestId,
            new[]
            {
                new ReturnRequestLine(
                    lineId,
                    1)
            },
            requestedAtUtc);

        Assert.Throws<ReturnRequestConflictException>(
            () =>
                eligibility.AuthoriseReturn(
                    requestId,
                    new[]
                    {
                        new ReturnRequestLine(
                            lineId,
                            2)
                    },
                    requestedAtUtc));
    }

}
