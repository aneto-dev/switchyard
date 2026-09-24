using System.Reflection;
using Switchyard.IntegrationContracts.Inventory;
using Xunit;

namespace Switchyard.Architecture.Tests;

public sealed class IntegrationContractsBoundaryTests
{
    [Fact]
    public void IntegrationContractsDoNotReferenceBusinessContextImplementations()
    {
        var assembly =
            typeof(ReserveInventoryV1).Assembly;

        var references =
            assembly.GetReferencedAssemblies()
                    .Select(reference => reference.Name)
                    .Where(name => name is not null)
                    .Cast<string>()
                    .ToArray();

        Assert.DoesNotContain(
            references,
            reference =>
                reference.StartsWith(
                    "Switchyard.Ordering",
                    StringComparison.Ordinal) ||
                reference.StartsWith(
                    "Switchyard.Inventory",
                    StringComparison.Ordinal) ||
                reference.StartsWith(
                    "Switchyard.Payments",
                    StringComparison.Ordinal) ||
                reference.StartsWith(
                    "Microsoft.EntityFrameworkCore",
                    StringComparison.Ordinal) ||
                reference.StartsWith(
                    "Azure.Messaging",
                    StringComparison.Ordinal));
    }
}
