using System.Reflection;
using Switchyard.Returns.Application.Eligibility;
using Switchyard.Returns.Domain.Eligibility;
using Switchyard.Returns.Infrastructure.Persistence;
using Xunit;

namespace Switchyard.Architecture.Tests;

public sealed class ReturnsBoundaryTests
{
    [Fact]
    public void ReturnsDomainDoesNotReferenceHigherLayersOrOtherContexts()
    {
        AssertDoesNotReference(
            typeof(ReturnEligibility).Assembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Returns.Application",
            "Switchyard.Returns.Infrastructure",
            "Switchyard.Fulfilment",
            "Switchyard.Inventory",
            "Switchyard.Ordering",
            "Switchyard.Payments");
    }

    [Fact]
    public void ReturnsApplicationReferencesDomainButNotInfrastructureOrOtherContexts()
    {
        var assembly =
            typeof(ApplyShipmentDeliveredHandler).Assembly;

        Assert.Contains(
            "Switchyard.Returns.Domain",
            GetReferenceNames(assembly));

        AssertDoesNotReference(
            assembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Returns.Infrastructure",
            "Switchyard.Fulfilment",
            "Switchyard.Inventory",
            "Switchyard.Ordering",
            "Switchyard.Payments");
    }

    [Fact]
    public void ReturnsInfrastructureReferencesItsLayersButNotOtherBusinessContexts()
    {
        var assembly =
            typeof(ReturnsDbContext).Assembly;

        var references = GetReferenceNames(assembly);

        Assert.Contains(
            "Switchyard.Returns.Application",
            references);

        Assert.Contains(
            "Switchyard.Returns.Domain",
            references);

        AssertDoesNotReference(
            assembly,
            "Microsoft.AspNetCore",
            "Switchyard.Api",
            "Switchyard.Fulfilment",
            "Switchyard.Inventory",
            "Switchyard.Ordering",
            "Switchyard.Payments");
    }

    private static void AssertDoesNotReference(
        Assembly assembly,
        params string[] forbiddenPrefixes)
    {
        var references = GetReferenceNames(assembly);

        foreach (var forbiddenPrefix in forbiddenPrefixes)
        {
            Assert.DoesNotContain(
                references,
                reference =>
                    reference.StartsWith(
                        forbiddenPrefix,
                        StringComparison.Ordinal));
        }
    }

    private static string[] GetReferenceNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();
}
