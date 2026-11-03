using System.Reflection;

namespace StoneFlow.Integrations.Domain;

/// <summary>Znacznik zestawu używany przy rejestracji i w testach architektury.</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
