using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Atria.Core.Contracts.Audio;
using Atria.Core.Contracts.Input;
using Atria.Core.Contracts.Presentation;
using Atria.Core.Contracts.Video;
using Atria.Core.Contracts.Window;
using Atria.Platforms.Ios;
using Atria.Platforms.Windows.Audio;
using Atria.Platforms.Windows.Render;
using Atria.Platforms.Windows.Win32;

namespace Tests.Contracts;

public static class ContractChecks
{
    private static readonly string[] ProductionProjects =
    [
        "Src/Core/Atria.Core.csproj",
        "Src/Platforms/Windows/Atria.Platforms.Windows.csproj",
        "Src/Platforms/Ios/Atria.Platforms.Ios.csproj",
        "Src/Hosts/Windows/Atria.Windows.csproj",
        "Src/Hosts/Ios/Atria.Ios.csproj",
    ];

    private static readonly string[] TestProjects =
    [
        "Tests/Core/Atria.Core.Tests.csproj",
        "Tests/Contracts/Atria.Contracts.Tests.csproj",
        "Tests/Windows/Atria.Tests.csproj",
        "Tests/Ios/Checks.csproj",
    ];

    private static readonly Dictionary<string, string[]> ExpectedReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Src/Core/Atria.Core.csproj"] = [],
        ["Src/Platforms/Windows/Atria.Platforms.Windows.csproj"] = ["Src/Core/Atria.Core.csproj"],
        ["Src/Platforms/Ios/Atria.Platforms.Ios.csproj"] = ["Src/Core/Atria.Core.csproj"],
        ["Src/Hosts/Windows/Atria.Windows.csproj"] = ["Src/Core/Atria.Core.csproj", "Src/Platforms/Windows/Atria.Platforms.Windows.csproj"],
        ["Src/Hosts/Ios/Atria.Ios.csproj"] = ["Src/Core/Atria.Core.csproj", "Src/Platforms/Ios/Atria.Platforms.Ios.csproj"],
        ["Tests/Core/Atria.Core.Tests.csproj"] = ["Src/Core/Atria.Core.csproj"],
        ["Tests/Contracts/Atria.Contracts.Tests.csproj"] =
        [
            "Src/Core/Atria.Core.csproj",
            "Src/Platforms/Windows/Atria.Platforms.Windows.csproj",
            "Src/Platforms/Ios/Atria.Platforms.Ios.csproj",
            "Tests/Core/Atria.Core.Tests.csproj",
        ],
        ["Tests/Windows/Atria.Tests.csproj"] =
        [
            "Src/Core/Atria.Core.csproj",
            "Src/Platforms/Windows/Atria.Platforms.Windows.csproj",
            "Src/Hosts/Windows/Atria.Windows.csproj",
            "Tests/Core/Atria.Core.Tests.csproj",
            "Tests/Contracts/Atria.Contracts.Tests.csproj",
        ],
        ["Tests/Ios/Checks.csproj"] = ["Src/Hosts/Ios/Atria.Ios.csproj"],
    };

    private static readonly HashSet<string> NamespaceExemptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Src/Core/AssemblyInfo.cs",
        "Src/Core/GlobalUsings.cs",
        "Src/Platforms/Windows/AssemblyInfo.cs",
        "Src/Platforms/Windows/GlobalUsings.cs",
        "Tests/Windows/GlobalUsings.cs",
        "Tests/Windows/Program.cs",
        "Tests/Contracts/Program.cs",
        "Tests/Ios/Program.cs",
    };

    public static void Run(string repositoryRoot)
    {
        string root = Path.GetFullPath(repositoryRoot);
        VerifySourceNamespaces(root, "Src", "Atria", ["Core", "Platforms/Windows", "Platforms/Ios", "Hosts/Windows", "Hosts/Ios"]);
        VerifySourceNamespaces(root, "Tests", "Tests", ["Core", "Contracts", "Windows", "Ios"]);
        VerifyProjectOwnership(root);
        VerifyBoundaryImports(root);
        VerifyResourceIdentities(root);
        VerifyPlatformContracts();
        Console.WriteLine("Architecture layout and public contract checks passed.");
    }

    private static void VerifySourceNamespaces(string root, string top, string namespaceRoot, string[] allowedTrees)
    {
        string baseDirectory = Path.Combine(root, top);
        foreach (string file in Directory.EnumerateFiles(baseDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !Path.GetRelativePath(baseDirectory, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Any(segment => segment is "bin" or "obj")))
        {
            string relative = Relative(root, file);
            string? tree = allowedTrees.FirstOrDefault(candidate =>
                relative.StartsWith(top + "/" + candidate.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
            Require(tree is not null, $"C# source has no project-owned {top} boundary: {relative}");

            string[] pieces = Path.GetRelativePath(baseDirectory, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string expected = namespaceRoot + "." + string.Join('.', pieces[..^1]);
            string source = File.ReadAllText(file);
            MatchCollection declarations = Regex.Matches(source,
                @"(?m)^\s*namespace\s+([A-Za-z_][A-Za-z_0-9]*(?:\.[A-Za-z_][A-Za-z_0-9]*)*)\s*[;{]");

            if (NamespaceExemptions.Contains(relative) && declarations.Count == 0)
                continue;

            Require(declarations.Count == 1, $"Expected one namespace declaration in {relative}; found {declarations.Count}.");
            Require(declarations[0].Groups[1].Value == expected,
                $"Namespace mismatch in {relative}: expected {expected}, found {declarations[0].Groups[1].Value}.");
        }
    }

    private static void VerifyProjectOwnership(string root)
    {
        foreach (string projectPath in ProductionProjects.Concat(TestProjects))
        {
            string fullPath = Path.Combine(root, projectPath.Replace('/', Path.DirectorySeparatorChar));
            Require(File.Exists(fullPath), $"Missing project: {projectPath}");
            XDocument project = XDocument.Load(fullPath);
            string projectDirectory = Path.GetDirectoryName(fullPath)!;
            var references = project.Descendants("ProjectReference")
                .Select(reference => Relative(root, Path.GetFullPath(Path.Combine(projectDirectory, (string?)reference.Attribute("Include") ?? ""))))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var expected = ExpectedReferences[projectPath].Order(StringComparer.OrdinalIgnoreCase).ToArray();
            Require(references.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase),
                $"Unexpected project ownership in {projectPath}: [{string.Join(", ", references)}].");

            Require(!project.Descendants("Compile").Any(),
                $"{projectPath} explicitly includes C# files; projects must compile their owned source tree by default.");
        }

        var assemblyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Src/Core/Atria.Core.csproj"] = "Atria.Core",
            ["Src/Platforms/Windows/Atria.Platforms.Windows.csproj"] = "Atria.Platforms.Windows",
            ["Src/Platforms/Ios/Atria.Platforms.Ios.csproj"] = "Atria.Platforms.Ios",
            ["Src/Hosts/Windows/Atria.Windows.csproj"] = "Atria",
            ["Src/Hosts/Ios/Atria.Ios.csproj"] = "Atria.Ios",
            ["Tests/Core/Atria.Core.Tests.csproj"] = "Atria.Core.Tests",
            ["Tests/Contracts/Atria.Contracts.Tests.csproj"] = "Atria.Contracts.Tests",
            ["Tests/Windows/Atria.Tests.csproj"] = "Atria.Tests",
        };
        foreach ((string projectPath, string expectedName) in assemblyNames)
        {
            XDocument project = XDocument.Load(Path.Combine(root, projectPath.Replace('/', Path.DirectorySeparatorChar)));
            string actual = project.Descendants("AssemblyName").Single().Value;
            Require(actual == expectedName, $"Assembly identity changed in {projectPath}: {actual}.");
        }
    }

    private static void VerifyBoundaryImports(string root)
    {
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "Src/Core"), "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            Require(!Regex.IsMatch(source, @"\bAtria\.(?:Platforms|Hosts)\."),
                $"Core source references a platform or host namespace: {Relative(root, file)}");
            Require(!Regex.IsMatch(source, @"\b(?:DllImport|LibraryImport|NativeLibrary)\b"),
                $"Core source contains a native import: {Relative(root, file)}");
        }

        foreach (string platformTree in new[] { "Src/Platforms/Windows", "Src/Platforms/Ios" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, platformTree.Replace('/', Path.DirectorySeparatorChar)), "*.cs", SearchOption.AllDirectories))
            {
                Require(!Regex.IsMatch(File.ReadAllText(file), @"\bAtria\.Hosts\."),
                    $"Platform source references a host namespace: {Relative(root, file)}");
            }
        }
    }

    private static void VerifyResourceIdentities(string root)
    {
        var expected = Directory.EnumerateFiles(Path.Combine(root, "Assets/Localization/ru-RU"), "*.json")
            .Select(file => "Atria.Localization.Defaults." + Path.GetFileName(file))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Src/Core/Resources/Content/Atria2"), "*.json")
                .Where(file => Path.GetFileName(file) != "OriginalNames.json")
                .Select(file => "Atria.Content.Atria2." + Path.GetFileName(file)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actual = typeof(GamepadState).Assembly.GetManifestResourceNames().Order(StringComparer.Ordinal).ToArray();
        Require(actual.SequenceEqual(expected, StringComparer.Ordinal),
            $"Atria.Core manifest resource identities changed. Expected [{string.Join(", ", expected)}], found [{string.Join(", ", actual)}].");

        Assembly coreTests = typeof(Tests.Core.CoreProbeChecks).Assembly;
        Require(coreTests.GetManifestResourceNames().Contains("Atria.Tests.OriginalNames.json", StringComparer.Ordinal),
            "The test fixture's stable Atria.Tests.OriginalNames.json identity is missing.");
    }

    private static void VerifyPlatformContracts()
    {
        Require(typeof(IGameWindow).IsAssignableFrom(typeof(NativeWindow)), "Windows NativeWindow no longer implements IGameWindow.");
        Require(typeof(IFramePresenter).IsAssignableFrom(typeof(D3D11Presenter)), "Windows D3D11Presenter no longer implements IFramePresenter.");
        Require(typeof(ITextRasterizer).IsAssignableFrom(typeof(DirectWriteRasterizer)), "Windows DirectWriteRasterizer no longer implements ITextRasterizer.");
        Require(typeof(IGameAudio).IsAssignableFrom(typeof(AudioDevice)), "Windows AudioDevice no longer implements IGameAudio.");
        Require(typeof(IGameWindow).IsAssignableFrom(typeof(MobileWindow)), "iOS MobileWindow no longer implements IGameWindow.");
        Require(typeof(IFramePresenter).IsAssignableFrom(typeof(MobilePresenter)), "iOS MobilePresenter no longer implements IFramePresenter.");
        Require(typeof(ITextRasterizer).IsAssignableFrom(typeof(AppleText)), "iOS AppleText no longer implements ITextRasterizer.");
        Require(typeof(IGameAudio).IsAssignableFrom(typeof(AppleAudio)), "iOS AppleAudio no longer implements IGameAudio.");
        Require(typeof(IFileMusicTransport).IsAssignableFrom(typeof(AppleMusic)), "iOS AppleMusic no longer implements IFileMusicTransport.");
        _ = typeof(MoviePlaybackResult);
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
