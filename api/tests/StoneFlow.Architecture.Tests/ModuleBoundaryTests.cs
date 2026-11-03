using NetArchTest.Rules;

namespace StoneFlow.Architecture.Tests;

/// <summary>Niezmiennik 7: moduł nie sięga do typów (w tym encji) innego modułu.</summary>
public sealed class ModuleBoundaryTests
{
    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Module_does_not_reference_other_modules(string module)
    {
        string[] otherModules = [.. Modules.Names.Where(m => m != module).Select(m => $"StoneFlow.{m}")];

        var result = Types.InAssemblies(Modules.AssembliesOf(module))
            .ShouldNot()
            .HaveDependencyOnAny(otherModules)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Moduł {module} odwołuje się do innego modułu w: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
