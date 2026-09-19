using System.Reflection;
using Switchyard.Payments.Application.Authorisation;
using Switchyard.Payments.Domain.Authorisation;
using Switchyard.Payments.Infrastructure.Persistence;
using Xunit;

namespace Switchyard.Architecture.Tests;

public sealed class PaymentBoundaryTests
{
    [Fact]
    public void PaymentsDomainDoesNotReferenceHigherLayersOrOtherContexts()
    {
        AssertDoesNotReference(
            typeof(PaymentAuthorisationAttempt).Assembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Payments.Application",
            "Switchyard.Payments.Infrastructure",
            "Switchyard.Ordering",
            "Switchyard.Inventory");
    }

    [Fact]
    public void PaymentsApplicationReferencesDomainButNotInfrastructureOrOtherContexts()
    {
        var applicationAssembly = typeof(AuthorisePaymentHandler).Assembly;
        var references = GetReferenceNames(applicationAssembly);

        Assert.Contains("Switchyard.Payments.Domain", references);
        AssertDoesNotReference(
            applicationAssembly,
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "Switchyard.Api",
            "Switchyard.Payments.Infrastructure",
            "Switchyard.Ordering",
            "Switchyard.Inventory");
    }

    [Fact]
    public void PaymentsInfrastructureReferencesItsApplicationAndDomainButNotOtherContexts()
    {
        var infrastructureAssembly = typeof(PaymentsDbContext).Assembly;
        var references = GetReferenceNames(infrastructureAssembly);

        Assert.Contains("Switchyard.Payments.Application", references);
        Assert.Contains("Switchyard.Payments.Domain", references);
        AssertDoesNotReference(
            infrastructureAssembly,
            "Microsoft.AspNetCore",
            "Switchyard.Api",
            "Switchyard.Ordering",
            "Switchyard.Inventory");
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
