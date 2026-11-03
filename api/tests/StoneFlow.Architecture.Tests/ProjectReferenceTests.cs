using System.Xml.Linq;

namespace StoneFlow.Architecture.Tests;

/// <summary>
/// Niezmiennik 7 na poziomie plików projektów: wykrywa niedozwoloną referencję także wtedy,
/// gdy nie jest jeszcze używana w kodzie (kompilator pomija nieużywane referencje w metadanych).
/// </summary>
public sealed class ProjectReferenceTests
{
    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Domain_references_only_shared_kernel(string module)
    {
        var project = Project($"api/src/Modules/{module}/StoneFlow.{module}.Domain/StoneFlow.{module}.Domain.csproj");

        Assert.Equal(["StoneFlow.Shared.Kernel"], project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.FrameworkReferences);
    }

    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Application_references_only_own_domain_and_shared_application(string module)
    {
        var project = Project($"api/src/Modules/{module}/StoneFlow.{module}.Application/StoneFlow.{module}.Application.csproj");

        Assert.Equal([$"StoneFlow.{module}.Domain", "StoneFlow.Shared.Application"], project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.FrameworkReferences);
    }

    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Infrastructure_references_only_own_application_and_shared_infrastructure(string module)
    {
        var project = Project($"api/src/Modules/{module}/StoneFlow.{module}.Infrastructure/StoneFlow.{module}.Infrastructure.csproj");

        Assert.Equal([$"StoneFlow.{module}.Application", "StoneFlow.Shared.Infrastructure"], project.ProjectReferences);
    }

    [Theory]
    [MemberData(nameof(Modules.All), MemberType = typeof(Modules))]
    public void Api_references_only_own_application(string module)
    {
        var project = Project($"api/src/Modules/{module}/StoneFlow.{module}.Api/StoneFlow.{module}.Api.csproj");

        Assert.Equal([$"StoneFlow.{module}.Application"], project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
    }

    [Fact]
    public void Shared_kernel_has_no_references()
    {
        var project = Project("api/src/Shared/StoneFlow.Shared.Kernel/StoneFlow.Shared.Kernel.csproj");

        Assert.Empty(project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.FrameworkReferences);
    }

    [Fact]
    public void Shared_application_references_only_shared_kernel()
    {
        var project = Project("api/src/Shared/StoneFlow.Shared.Application/StoneFlow.Shared.Application.csproj");

        Assert.Equal(["StoneFlow.Shared.Kernel"], project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.FrameworkReferences);
    }

    private static ProjectFile Project(string relativePath)
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot.Path, relativePath));

        string[] Includes(string element) =>
        [
            .. document.Descendants(element)
                .Select(e => (string?)e.Attribute("Include"))
                .OfType<string>()
                .Select(include => element == "ProjectReference"
                    ? Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))
                    : include)
                .Order(StringComparer.Ordinal),
        ];

        return new ProjectFile(Includes("ProjectReference"), Includes("PackageReference"), Includes("FrameworkReference"));
    }

    private sealed record ProjectFile(string[] ProjectReferences, string[] PackageReferences, string[] FrameworkReferences);
}
