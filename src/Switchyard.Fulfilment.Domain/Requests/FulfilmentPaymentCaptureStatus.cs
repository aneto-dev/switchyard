namespace Switchyard.Fulfilment.Domain.Requests;

public enum FulfilmentPaymentCaptureStatus
{
    NotRequested = 0,
    Pending = 1,
    Captured = 2,
    Indeterminate = 3,
    NotApplied = 4
}
