using System.Reflection;
using Switchyard.Messaging;
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
