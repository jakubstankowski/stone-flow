using NetArchTest.Rules;

namespace StoneFlow.Architecture.Tests;

/// <summary>
/// Niezmiennik 7: zależności wskazują do środka, a domena nie zależy od HTTP, EF Core ani hostingu.
/// Sprawdza typy w skompilowanych zestawach.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly string[] Frameworks =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Data.SqlClient",
    ];

    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Domain_does_not_depend_on_http_ef_or_hosting(string module) =>
        AssertNoDependency(Types.InAssembly(Modules.Assembly(module, "Domain")), Frameworks);

    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Domain_does_not_depend_on_outer_layers(string module) =>
        AssertNoDependency(
            Types.InAssembly(Modules.Assembly(module, "Domain")),
            [$"StoneFlow.{module}.Application", $"StoneFlow.{module}.Infrastructure", $"StoneFlow.{module}.Api", "StoneFlow.Shared.Application", "StoneFlow.Shared.Infrastructure"]);

    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Application_does_not_depend_on_infrastructure_http_or_ef(string module) =>
        AssertNoDependency(
            Types.InAssembly(Modules.Assembly(module, "Application")),
            [.. Frameworks, $"StoneFlow.{module}.Infrastructure", $"StoneFlow.{module}.Api", "StoneFlow.Shared.Infrastructure"]);

    [Fact]
    public void Shared_kernel_has_no_dependencies() =>
        AssertNoDependency(
            Types.InAssembly(Shared.Kernel.AssemblyReference.Assembly),
            [.. Frameworks, "StoneFlow.Shared.Application", "StoneFlow.Shared.Infrastructure", .. Modules.Names.Select(m => $"StoneFlow.{m}")]);

    [Fact]
    public void Shared_application_does_not_depend_on_infrastructure_http_or_ef() =>
        AssertNoDependency(
            Types.InAssembly(Shared.Application.AssemblyReference.Assembly),
            [.. Frameworks, "StoneFlow.Shared.Infrastructure", .. Modules.Names.Select(m => $"StoneFlow.{m}")]);

    [Fact]
    public void Shared_does_not_depend_on_modules()
    {
        string[] modules = [.. Modules.Names.Select(m => $"StoneFlow.{m}")];

        AssertNoDependency(Types.InAssembly(Shared.Infrastructure.AssemblyReference.Assembly), modules);
    }

    private static void AssertNoDependency(Types types, string[] forbidden)
    {
        var result = types.ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Niedozwolone zależności w: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
