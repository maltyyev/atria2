using System.Reflection;

namespace Atria;

internal static class BuildInfo
{
    // The project Version property also supplies the executable's Windows version metadata.
    public static readonly string Version = typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
