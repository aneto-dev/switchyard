using System.Reflection;
using Switchyard.Inventory.Application.Reservations;
using Switchyard.Inventory.Domain.Reservations;
using Switchyard.Inventory.Infrastructure.Persistence;
using Xunit;

namespace Switchyard.Architecture.Tests;

public sealed class InventoryBoundaryTests
{
    [Fact]
    public void InventoryDomainDoesNotReferenceHigherLayersOrOtherContexts()
    {
        AssertDoesNotReference(
            typeof(StockReservation).Assembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Inventory.Application",
            "Switchyard.Inventory.Infrastructure",
            "Switchyard.Ordering");
    }

    [Fact]
    public void InventoryApplicationReferencesDomainButNotInfrastructureOrOrdering()
    {
        var applicationAssembly = typeof(ReserveInventoryHandler).Assembly;
        var references = GetReferenceNames(applicationAssembly);

        Assert.Contains("Switchyard.Inventory.Domain", references);
        AssertDoesNotReference(
            applicationAssembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Inventory.Infrastructure",
            "Switchyard.Ordering");
    }

    [Fact]
    public void InventoryInfrastructureReferencesItsApplicationAndDomainButNotApiOrOrdering()
    {
        var infrastructureAssembly = typeof(InventoryDbContext).Assembly;
        var references = GetReferenceNames(infrastructureAssembly);

        Assert.Contains("Switchyard.Inventory.Application", references);
        Assert.Contains("Switchyard.Inventory.Domain", references);
        AssertDoesNotReference(
            infrastructureAssembly,
            "Microsoft.AspNetCore",
            "Switchyard.Api",
            "Switchyard.Ordering");
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
        assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();
}
