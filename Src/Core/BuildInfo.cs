using System.Reflection;
using Atria.Core.Game.Progression;
using Atria.Core.Resources;

namespace Atria.Core;

/// <summary>Build identity selected by the executable or native library host.</summary>
internal static class BuildInfo
{
    private static string version = typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    /// <summary>Version selected by the executable or native host, with the Core version as a test fallback.</summary>
    public static string Version => version;

    /// <summary>Reads the product version from the host assembly before any version badge or log is created.</summary>
    public static void UseHostAssembly(Assembly host)
    {
        ArgumentNullException.ThrowIfNull(host);
        version = host.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? host.GetName().Version?.ToString()
            ?? throw new InvalidOperationException("The host assembly has no product version.");
    }
}
