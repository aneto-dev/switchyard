using Switchyard.Ordering.Application.Placement;
using Switchyard.Ordering.Domain.Orders;
using Xunit;

namespace Switchyard.Ordering.Application.Tests;

public sealed class OrderPaymentAuthorisationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid PaymentId = Guid.NewGuid();

    [Fact]
    public void NonTerminalOutcomesMoveForwardToTerminalOutcome()
    {
        var authorisation = OrderPaymentAuthorisation.Start(
            new OrderId(Guid.NewGuid()), Observe(PaymentAuthorisationOutcome.Indeterminate, Now), Now);

        Assert.True(authorisation.Apply(
            Observe(PaymentAuthorisationOutcome.NeedsAttention, Now.AddMinutes(5), reason: "ReconciliationExhausted",
                    attempts: 3),
            Now.AddMinutes(5)));

        Assert.Equal(PaymentAuthorisationOutcome.NeedsAttention, authorisation.Outcome);
        Assert.Equal(3, authorisation.ReconciliationAttemptCount);

        Assert.True(authorisation.Apply(
            Observe(PaymentAuthorisationOutcome.Authorised, Now.AddMinutes(9), providerReference: "sim-auth-1"),
            Now.AddMinutes(10)));

        Assert.Equal(PaymentAuthorisationOutcome.Authorised, authorisation.Outcome);
        Assert.True(authorisation.IsTerminal);
        Assert.Equal(PaymentId, authorisation.PaymentId);
        Assert.Equal("sim-auth-1", authorisation.ProviderReference);
        Assert.Null(authorisation.Reason);
        Assert.Equal(3, authorisation.ReconciliationAttemptCount);
        Assert.Equal(Now.AddMinutes(9), authorisation.OutcomeAtUtc);
        Assert.Equal(Now.AddMinutes(10), authorisation.RecordedAtUtc);
    }

    [Theory]
    [InlineData(PaymentAuthorisationOutcome.NeedsAttention, PaymentAuthorisationOutcome.Indeterminate)]
    [InlineData(PaymentAuthorisationOutcome.NeedsAttention, PaymentAuthorisationOutcome.NeedsAttention)]
    [InlineData(PaymentAuthorisationOutcome.Authorised, PaymentAuthorisationOutcome.Indeterminate)]
    [InlineData(PaymentAuthorisationOutcome.Authorised, PaymentAuthorisationOutcome.NeedsAttention)]
    [InlineData(PaymentAuthorisationOutcome.Authorised, PaymentAuthorisationOutcome.Authorised)]
    [InlineData(PaymentAuthorisationOutcome.Failed, PaymentAuthorisationOutcome.Indeterminate)]
    [InlineData(PaymentAuthorisationOutcome.Failed, PaymentAuthorisationOutcome.Failed)]
    public void ReplayedAndStaleOutcomesDoNotOverwriteLaterOutcome(
        PaymentAuthorisationOutcome current, PaymentAuthorisationOutcome late)
    {
        var authorisation = OrderPaymentAuthorisation.Start(
            new OrderId(Guid.NewGuid()), Observe(current, Now.AddMinutes(5)), Now.AddMinutes(5));

        Assert.False(authorisation.Apply(Observe(late, Now), Now.AddMinutes(6)));

        Assert.Equal(current, authorisation.Outcome);
        Assert.Equal(Now.AddMinutes(5), authorisation.OutcomeAtUtc);
        Assert.Equal(Now.AddMinutes(5), authorisation.RecordedAtUtc);
    }

    [Theory]
    [InlineData(PaymentAuthorisationOutcome.Authorised, PaymentAuthorisationOutcome.Failed)]
    [InlineData(PaymentAuthorisationOutcome.Failed, PaymentAuthorisationOutcome.Authorised)]
    public void ConflictingTerminalOutcomeIsRejected(
        PaymentAuthorisationOutcome current, PaymentAuthorisationOutcome conflicting)
    {
        var authorisation = OrderPaymentAuthorisation.Start(
            new OrderId(Guid.NewGuid()), Observe(current, Now), Now);

        Assert.Throws<OrderPlacementPaymentOutcomeException>(
            () => authorisation.Apply(Observe(conflicting, Now.AddMinutes(1)), Now.AddMinutes(1)));

        Assert.Equal(current, authorisation.Outcome);
    }

    [Fact]
    public void DifferentPaymentOrRequestIdentityIsRejected()
    {
        var authorisation = OrderPaymentAuthorisation.Start(
            new OrderId(Guid.NewGuid()), Observe(PaymentAuthorisationOutcome.Indeterminate, Now), Now);

        Assert.Throws<OrderPlacementPaymentOutcomeException>(
            () => authorisation.Apply(
                Observe(PaymentAuthorisationOutcome.Authorised, Now) with { PaymentId = Guid.NewGuid() }, Now));

        Assert.Throws<OrderPlacementPaymentOutcomeException>(
            () => authorisation.Apply(
                Observe(PaymentAuthorisationOutcome.Authorised, Now) with { RequestId = Guid.NewGuid() }, Now));

        Assert.Equal(PaymentAuthorisationOutcome.Indeterminate, authorisation.Outcome);
    }

    [Fact]
    public void FailureBeforePaymentCreationKeepsNoPaymentIdentity()
    {
        var authorisation = OrderPaymentAuthorisation.Start(
            new OrderId(Guid.NewGuid()),
            Observe(PaymentAuthorisationOutcome.Failed, Now, reason: "ReservationExpired") with { PaymentId = null },
            Now);

        Assert.Null(authorisation.PaymentId);
        Assert.Equal("ReservationExpired", authorisation.Reason);
        Assert.True(authorisation.IsTerminal);
    }

    private static PaymentAuthorisationObservation Observe(
        PaymentAuthorisationOutcome outcome,
        DateTimeOffset occurredAtUtc,
        string? providerReference = null,
        string? reason = null,
        int? attempts = null) =>
        new(RequestId, PaymentId, outcome, 1399.98m, "GBP", providerReference, reason, attempts, occurredAtUtc);
}
