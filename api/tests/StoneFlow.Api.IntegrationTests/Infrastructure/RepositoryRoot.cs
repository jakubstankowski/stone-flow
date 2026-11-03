namespace StoneFlow.Api.IntegrationTests.Infrastructure;

internal static class RepositoryRoot
{
    private const string Marker = "stone-flow.slnx";

    public static string Path { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Nie znaleziono katalogu głównego repozytorium ({Marker}).");
    }
}
