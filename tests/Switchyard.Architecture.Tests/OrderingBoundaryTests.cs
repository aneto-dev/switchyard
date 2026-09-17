using Switchyard.Ordering.Domain.Orders;
using Xunit;

namespace Switchyard.Architecture.Tests;

public sealed class OrderingBoundaryTests
{
    private static readonly string[] ForbiddenReferences =
    {
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Switchyard.Api"
    };

    [Fact]
    public void OrderingDomainDoesNotReferenceApiOrInfrastructureFrameworks()
    {
        var referencedAssemblies = typeof(Order)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        foreach (var forbiddenReference in ForbiddenReferences)
        {
            Assert.DoesNotContain(
                referencedAssemblies,
                reference => reference.StartsWith(forbiddenReference, StringComparison.Ordinal));
        }
    }
}
