using System.Reflection;
using Switchyard.Fulfilment.Application.Requests;
using Switchyard.Fulfilment.Domain.Requests;
using Switchyard.Fulfilment.Infrastructure.Persistence;
using Xunit;

namespace Switchyard.Architecture.Tests;

public sealed class FulfilmentBoundaryTests
{
    [Fact]
    public void FulfilmentDomainDoesNotReferenceHigherLayersOrOtherContexts()
    {
        AssertDoesNotReference(
            typeof(FulfilmentRequest).Assembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Fulfilment.Application",
            "Switchyard.Fulfilment.Infrastructure",
            "Switchyard.Inventory",
            "Switchyard.Ordering",
            "Switchyard.Payments");
    }

    [Fact]
    public void FulfilmentApplicationReferencesDomainButNotInfrastructureOrOtherContexts()
    {
        var applicationAssembly =
            typeof(CreateFulfilmentHandler).Assembly;

        var references =
            GetReferenceNames(
                applicationAssembly);

        Assert.Contains(
            "Switchyard.Fulfilment.Domain",
            references);

        AssertDoesNotReference(
            applicationAssembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Fulfilment.Infrastructure",
            "Switchyard.Inventory",
            "Switchyard.Ordering",
            "Switchyard.Payments");
    }

    [Fact]
    public void FulfilmentInfrastructureReferencesItsLayersButNotBusinessContexts()
    {
        var infrastructureAssembly =
            typeof(FulfilmentDbContext).Assembly;

        var references =
            GetReferenceNames(
                infrastructureAssembly);

        Assert.Contains(
            "Switchyard.Fulfilment.Application",
            references);
        Assert.Contains(
            "Switchyard.Fulfilment.Domain",
            references);

        AssertDoesNotReference(
            infrastructureAssembly,
            "Microsoft.AspNetCore",
            "Switchyard.Api",
            "Switchyard.Inventory",
            "Switchyard.Ordering",
            "Switchyard.Payments");
    }

    private static void AssertDoesNotReference(
        Assembly assembly,
        params string[] forbiddenPrefixes)
    {
        var references =
            GetReferenceNames(
                assembly);

        foreach (var forbiddenPrefix in
                 forbiddenPrefixes)
        {
            Assert.DoesNotContain(
                references,
                reference =>
                    reference.StartsWith(
                        forbiddenPrefix,
                        StringComparison.Ordinal));
        }
    }

    private static string[] GetReferenceNames(
        Assembly assembly) =>
        assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();
}
