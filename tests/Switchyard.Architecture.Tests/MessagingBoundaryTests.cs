using System.Reflection;
using Switchyard.Messaging;
using Switchyard.Messaging.ServiceBus;
using Xunit;

namespace Switchyard.Architecture.Tests;

public sealed class MessagingBoundaryTests
{
    [Fact]
    public void MessagingDoesNotReferenceBusinessContextsOrInfrastructureFrameworks()
    {
        AssertDoesNotReference(
            typeof(OutboxDispatcher).Assembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Ordering",
            "Switchyard.Inventory",
            "Switchyard.Payments");
    }

    [Fact]
    public void ServiceBusAdapterReferencesMessagingButNotBusinessContexts()
    {
        var assembly = typeof(ServiceBusMessageTransport).Assembly;
        var references = assembly.GetReferencedAssemblies()
                                 .Select(reference => reference.Name)
                                 .Where(name => name is not null)
                                 .Cast<string>()
                                 .ToArray();

        Assert.Contains("Switchyard.Messaging", references);
        Assert.DoesNotContain(
            references,
            reference =>
                reference.StartsWith("Switchyard.Ordering", StringComparison.Ordinal) ||
                reference.StartsWith("Switchyard.Inventory", StringComparison.Ordinal) ||
                reference.StartsWith("Switchyard.Payments", StringComparison.Ordinal) ||
                reference.StartsWith("Switchyard.Api", StringComparison.Ordinal));
    }

    private static void AssertDoesNotReference(
        Assembly assembly,
        params string[] forbiddenPrefixes)
    {
        var references = assembly.GetReferencedAssemblies()
                                 .Select(reference => reference.Name)
                                 .Where(name => name is not null)
                                 .Cast<string>()
                                 .ToArray();

        foreach (var forbiddenPrefix in forbiddenPrefixes)
        {
            Assert.DoesNotContain(
                references,
                reference => reference.StartsWith(
                    forbiddenPrefix,
                    StringComparison.Ordinal));
        }
    }
}
