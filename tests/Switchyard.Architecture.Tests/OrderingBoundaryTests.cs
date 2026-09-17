using System.Reflection;
using Switchyard.Ordering.Application.Orders;
using Switchyard.Ordering.Domain.Orders;
using Xunit;

namespace Switchyard.Architecture.Tests;

public sealed class OrderingBoundaryTests
{
    [Fact]
    public void OrderingDomainDoesNotReferenceHigherLayersOrInfrastructureFrameworks()
    {
        AssertDoesNotReference(
            typeof(Order).Assembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Ordering.Application",
            "Switchyard.Ordering.Infrastructure");
    }

    [Fact]
    public void OrderingApplicationReferencesDomainButNotInfrastructure()
    {
        var applicationAssembly = typeof(CreatePendingOrderHandler).Assembly;
        var references = GetReferenceNames(applicationAssembly);

        Assert.Contains("Switchyard.Ordering.Domain", references);
        AssertDoesNotReference(
            applicationAssembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Ordering.Infrastructure");
    }

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var references = GetReferenceNames(assembly);

        foreach (var forbiddenPrefix in forbiddenPrefixes)
        {
            Assert.DoesNotContain(
                references,
                reference => reference.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
        }
    }

    private static string[] GetReferenceNames(Assembly assembly) =>
        assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
}
