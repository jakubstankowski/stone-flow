using System.Reflection;

namespace StoneFlow.Architecture.Tests;

/// <summary>Zestawy modułów i części wspólnej sprawdzane przez testy architektury.</summary>
internal static class Modules
{
    public static readonly string[] Names = ["Access", "Orders", "Expenses", "Reporting", "Integrations"];

    public static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api"];

    public static TheoryData<string> All => new(Names);

    public static Assembly Assembly(string module, string layer) => layer switch
    {
        "Domain" => DomainAssembly(module),
        "Application" => ApplicationAssembly(module),
        "Infrastructure" => InfrastructureAssembly(module),
        "Api" => ApiAssembly(module),
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Nieznana warstwa."),
    };

    public static IEnumerable<Assembly> AssembliesOf(string module) => Layers.Select(layer => Assembly(module, layer));

    private static Assembly DomainAssembly(string module) => module switch
    {
        "Access" => Access.Domain.AssemblyReference.Assembly,
        "Orders" => Orders.Domain.AssemblyReference.Assembly,
        "Expenses" => Expenses.Domain.AssemblyReference.Assembly,
        "Reporting" => Reporting.Domain.AssemblyReference.Assembly,
        "Integrations" => Integrations.Domain.AssemblyReference.Assembly,
        _ => throw Unknown(module),
    };

    private static Assembly ApplicationAssembly(string module) => module switch
    {
        "Access" => Access.Application.AssemblyReference.Assembly,
        "Orders" => Orders.Application.AssemblyReference.Assembly,
        "Expenses" => Expenses.Application.AssemblyReference.Assembly,
        "Reporting" => Reporting.Application.AssemblyReference.Assembly,
        "Integrations" => Integrations.Application.AssemblyReference.Assembly,
        _ => throw Unknown(module),
    };

    private static Assembly InfrastructureAssembly(string module) => module switch
    {
        "Access" => Access.Infrastructure.AssemblyReference.Assembly,
        "Orders" => Orders.Infrastructure.AssemblyReference.Assembly,
        "Expenses" => Expenses.Infrastructure.AssemblyReference.Assembly,
        "Reporting" => Reporting.Infrastructure.AssemblyReference.Assembly,
        "Integrations" => Integrations.Infrastructure.AssemblyReference.Assembly,
        _ => throw Unknown(module),
    };

    private static Assembly ApiAssembly(string module) => module switch
    {
        "Access" => Access.Api.AssemblyReference.Assembly,
        "Orders" => Orders.Api.AssemblyReference.Assembly,
        "Expenses" => Expenses.Api.AssemblyReference.Assembly,
        "Reporting" => Reporting.Api.AssemblyReference.Assembly,
        "Integrations" => Integrations.Api.AssemblyReference.Assembly,
        _ => throw Unknown(module),
    };

    private static ArgumentOutOfRangeException Unknown(string module) =>
        new(nameof(module), module, "Nieznany moduł.");
}
