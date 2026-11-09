using Switchyard.Returns.Domain.Eligibility;
using Xunit;

namespace Switchyard.Returns.Domain.Tests;

public sealed class ReturnReceiptTests
{
    [Fact]
    public void PartialReceiptMovesAuthorisedQuantityToReturnedQuantity()
    {
        var lineId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var eligibility =
            CreateAuthorisedReturn(
                lineId,
                requestId,
                deliveredQuantity: 2,
                authorisedQuantity: 2);

        var changed =
            eligibility.ReceiveReturn(
                requestId,
                Guid.NewGuid(),
                new[]
                {
                    new ReturnReceiptLine(
                        lineId,
                        1)
                },
                new DateTimeOffset(
                    2026, 11, 9, 13, 0, 0, TimeSpan.Zero));

        Assert.True(changed);

        var line =
            eligibility.Lines.Single();

        Assert.Equal(
            1,
            line.AuthorisedReturnQuantity);

        Assert.Equal(
            1,
            line.ReturnedQuantity);

        Assert.Equal(
            0,
            line.ReturnableQuantity);

        Assert.Equal(
            ReturnRequestStatus.PartiallyReceived,
            eligibility.ReturnRequests.Single()
                       .Status);
    }

    [Fact]
    public void FinalReceiptMarksRequestReceived()
    {
        var lineId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var eligibility =
            CreateAuthorisedReturn(
                lineId,
                requestId,
                deliveredQuantity: 2,
                authorisedQuantity: 2);

        eligibility.ReceiveReturn(
            requestId,
            Guid.NewGuid(),
            new[]
            {
                new ReturnReceiptLine(
                    lineId,
                    1)
            },
            new DateTimeOffset(
                2026, 11, 9, 13, 0, 0, TimeSpan.Zero));

        eligibility.ReceiveReturn(
            requestId,
            Guid.NewGuid(),
            new[]
            {
                new ReturnReceiptLine(
                    lineId,
                    1)
            },
            new DateTimeOffset(
                2026, 11, 9, 13, 5, 0, TimeSpan.Zero));

        var line =
            eligibility.Lines.Single();

        Assert.Equal(
            0,
            line.AuthorisedReturnQuantity);

        Assert.Equal(
            2,
            line.ReturnedQuantity);

        Assert.Equal(
            ReturnRequestStatus.Received,
            eligibility.ReturnRequests.Single()
                       .Status);
    }

    [Fact]
    public void ExactReceiptReplayDoesNotMoveQuantityTwice()
    {
        var lineId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var receivedAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 13, 0, 0, TimeSpan.Zero);

        var eligibility =
            CreateAuthorisedReturn(
                lineId,
                requestId,
                deliveredQuantity: 2,
                authorisedQuantity: 2);

        var first =
            eligibility.ReceiveReturn(
                requestId,
                receiptId,
                new[]
                {
                    new ReturnReceiptLine(
                        lineId,
                        1)
                },
                receivedAtUtc);

        var replay =
            eligibility.ReceiveReturn(
                requestId,
                receiptId,
                new[]
                {
                    new ReturnReceiptLine(
                        lineId,
                        1)
                },
                receivedAtUtc);

        Assert.True(first);
        Assert.False(replay);
        Assert.Equal(
            1,
            eligibility.Lines.Single()
                       .ReturnedQuantity);
    }

    [Fact]
    public void ReceiptCannotExceedOutstandingAuthorisedQuantity()
    {
        var lineId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var eligibility =
            CreateAuthorisedReturn(
                lineId,
                requestId,
                deliveredQuantity: 2,
                authorisedQuantity: 1);

        var exception =
            Assert.Throws<ReturnReceiptQuantityNotAuthorisedException>(
                () =>
                    eligibility.ReceiveReturn(
                        requestId,
                        Guid.NewGuid(),
                        new[]
                        {
                            new ReturnReceiptLine(
                                lineId,
                                2)
                        },
                        new DateTimeOffset(
                            2026, 11, 9, 13, 0, 0, TimeSpan.Zero)));

        Assert.Equal(
            1,
            exception.OutstandingQuantity);
    }

    private static ReturnEligibility CreateAuthorisedReturn(
        Guid lineId,
        Guid requestId,
        int deliveredQuantity,
        int authorisedQuantity)
    {
        var eligibility =
            ReturnEligibility.Create(
                Guid.NewGuid(),
                "SW-RET-300");

        var deliveredAtUtc =
            new DateTimeOffset(
                2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        eligibility.RecordDelivery(
            Guid.NewGuid(),
            new[]
            {
                new DeliveredLine(
                    lineId,
                    deliveredQuantity)
            },
            deliveredAtUtc);

        eligibility.AuthoriseReturn(
            requestId,
            new[]
            {
                new ReturnRequestLine(
                    lineId,
                    authorisedQuantity)
            },
            deliveredAtUtc.AddMinutes(5));

        return eligibility;
    }
}
