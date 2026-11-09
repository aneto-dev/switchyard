using System.Text.Json;
using Switchyard.IntegrationContracts.Payments;
using Switchyard.Messaging;
using Switchyard.Returns.Application.Commercial;
using Switchyard.Returns.Application.Eligibility;
using Switchyard.Returns.Application.Refunds;
using Switchyard.Returns.Application.Requests;
using Switchyard.Returns.Domain.Eligibility;

namespace Switchyard.Returns.Application.Receipts;

public sealed class ReceiveReturnHandler
{
    private readonly IReturnEligibilityRepository _repository;
    private readonly IReturnCommercialSnapshotRepository _commercialRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IReturnRefundRepository _refundRepository;
    private readonly IReturnsUnitOfWork _unitOfWork;

    public ReceiveReturnHandler(
        IReturnEligibilityRepository repository,
        IReturnCommercialSnapshotRepository commercialRepository,
        IOutboxWriter outboxWriter,
        IReturnRefundRepository refundRepository,
        IReturnsUnitOfWork unitOfWork)
    {
        _repository =
            repository ??
            throw new ArgumentNullException(
                nameof(repository));

        _commercialRepository =
            commercialRepository ??
            throw new ArgumentNullException(
                nameof(commercialRepository));

        _outboxWriter =
            outboxWriter ??
            throw new ArgumentNullException(
                nameof(outboxWriter));

        _refundRepository =
            refundRepository ??
            throw new ArgumentNullException(
                nameof(refundRepository));

        _unitOfWork =
            unitOfWork ??
            throw new ArgumentNullException(
                nameof(unitOfWork));
    }

    public async Task<ReceiveReturnResult> HandleAsync(
        ReceiveReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Lines);

        await using var transaction =
            await _unitOfWork.BeginTransactionAsync(
                cancellationToken);

        var eligibility =
            await _repository.GetByOrderIdForUpdateAsync(
                command.OrderId,
                cancellationToken) ??
            throw new ReturnOrderNotEligibleException(
                command.OrderId);

        var changed =
            eligibility.ReceiveReturn(
                command.ReturnRequestId,
                command.ReturnReceiptId,
                command.Lines.Select(
                    line =>
                        new ReturnReceiptLine(
                            line.OrderLineId,
                            line.Quantity)),
                command.ReceivedAtUtc);

        if (changed)
        {
            var commercial =
                await _commercialRepository.GetByOrderIdAsync(
                    command.OrderId,
                    cancellationToken) ??
                throw new ReturnCommercialSnapshotMissingException(
                    command.OrderId);

            var receipt =
                eligibility.ReturnReceipts.Single(
                    candidate =>
                        candidate.ReturnReceiptId ==
                        command.ReturnReceiptId);

            decimal refundAmount = 0;

            foreach (var receiptLine in receipt.Lines)
            {
                var commercialLine =
                    commercial.Lines.SingleOrDefault(
                        line =>
                            line.OrderLineId ==
                            receiptLine.OrderLineId) ??
                    throw new ReturnCommercialSnapshotMissingLineException(
                        command.OrderId,
                        receiptLine.OrderLineId);

                refundAmount =
                    checked(
                        refundAmount +
                        commercialLine.UnitPriceAmount *
                        receiptLine.Quantity);
            }

            if (refundAmount > 0)
            {
                var refund =
                    new RefundPaymentV1(
                        command.ReturnReceiptId,
                        command.OrderId,
                        commercial.PaymentId,
                        command.ReturnRequestId,
                        command.ReturnReceiptId,
                        refundAmount,
                        commercial.Currency);

                await _outboxWriter.AddAsync(
                    new IntegrationMessageEnvelope(
                        Guid.NewGuid(),
                        RefundPaymentV1.MessageType,
                        JsonSerializer.Serialize(
                            refund,
                            JsonSerializerOptions.Web),
                        command.ReceivedAtUtc,
                        command.OrderId,
                        command.ReturnReceiptId),
                    cancellationToken);

                await _refundRepository.CreatePendingAsync(
                    new ReturnRefundProjection(
                        command.OrderId,
                        command.ReturnRequestId,
                        command.ReturnReceiptId,
                        commercial.PaymentId,
                        refundAmount,
                        commercial.Currency,
                        ReturnRefundStatus.Pending,
                        null,
                        command.ReceivedAtUtc,
                        command.ReceivedAtUtc),
                    cancellationToken);
            }

            await _repository.SaveAsync(
                eligibility,
                cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        var request =
            eligibility.ReturnRequests.Single(
                candidate =>
                    candidate.ReturnRequestId ==
                    command.ReturnRequestId);

        return new ReceiveReturnResult(
            command.ReturnReceiptId,
            request.Status,
            Replayed: !changed);
    }
}

public sealed record ReceiveReturnCommand(
    Guid ReturnReceiptId,
    Guid ReturnRequestId,
    Guid OrderId,
    IReadOnlyList<ReceiveReturnLine> Lines,
    DateTimeOffset ReceivedAtUtc);

public sealed record ReceiveReturnLine(
    Guid OrderLineId,
    int Quantity);

public sealed record ReceiveReturnResult(
    Guid ReturnReceiptId,
    ReturnRequestStatus Status,
    bool Replayed);

public sealed class ReturnCommercialSnapshotMissingException : Exception
{
    public ReturnCommercialSnapshotMissingException(
        Guid orderId)
        : base(
            $"Commercial snapshot for order '{orderId}' is required before receiving a return.")
    {
    }
}

public sealed class ReturnCommercialSnapshotMissingLineException : Exception
{
    public ReturnCommercialSnapshotMissingLineException(
        Guid orderId,
        Guid orderLineId)
        : base(
            $"Commercial snapshot for order '{orderId}' does not contain order line '{orderLineId}'.")
    {
    }
}
