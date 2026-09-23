namespace Switchyard.Messaging.ServiceBus;

public sealed record ServiceBusReceiveOptions
{
    public ServiceBusReceiveOptions(
        string subscriptionName,
        int maxConcurrentCalls = 4,
        TimeSpan? maxAutoLockRenewalDuration = null,
        TimeSpan? retryBaseDelay = null,
        TimeSpan? retryMaxDelay = null,
        double retryJitterRatio = 0.20)
    {
        if (string.IsNullOrWhiteSpace(subscriptionName))
        {
            throw new ArgumentException(
                "Service Bus subscription name is required.",
                nameof(subscriptionName));
        }

        if (maxConcurrentCalls <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentCalls),
                "Maximum concurrent calls must be positive.");
        }

        var lockRenewal =
            maxAutoLockRenewalDuration ?? TimeSpan.FromMinutes(5);

        var retryBase =
            retryBaseDelay ?? TimeSpan.FromMilliseconds(500);

        var retryMax =
            retryMaxDelay ?? TimeSpan.FromSeconds(30);

        if (lockRenewal < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAutoLockRenewalDuration),
                "Maximum lock renewal duration cannot be negative.");
        }

        if (retryBase < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryBaseDelay),
                "Retry base delay cannot be negative.");
        }

        if (retryMax < retryBase)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryMaxDelay),
                "Retry maximum delay cannot be less than the base delay.");
        }

        if (retryJitterRatio < 0 || retryJitterRatio > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryJitterRatio),
                "Retry jitter ratio must be between zero and one.");
        }

        SubscriptionName = subscriptionName.Trim();
        MaxConcurrentCalls = maxConcurrentCalls;
        MaxAutoLockRenewalDuration = lockRenewal;
        RetryBaseDelay = retryBase;
        RetryMaxDelay = retryMax;
        RetryJitterRatio = retryJitterRatio;
    }

    public string SubscriptionName { get; }
    public int MaxConcurrentCalls { get; }
    public TimeSpan MaxAutoLockRenewalDuration { get; }
    public TimeSpan RetryBaseDelay { get; }
    public TimeSpan RetryMaxDelay { get; }
    public double RetryJitterRatio { get; }
}
