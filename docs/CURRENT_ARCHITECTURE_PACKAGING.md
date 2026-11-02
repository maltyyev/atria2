# Shared asset inventory and Core checks

## Asset inventory

`Assets/PackageResources.props` is the source inventory for Game, Music, Interface, and Localization assets. Windows MSBuild consumes the same items that `Ios/Prepare-Assets.py` reads. Game and Music entries are explicit immutable inputs; build and staging validation fail if one is missing or if an unlisted file appears in either folder. Add each new file to the shared inventory before building or staging it.

Windows keeps the original `.jad` files and packages only the listed source assets. iOS stages the same originals, alongside platform-specific `.mp4` conversions under `movies/`. The conversion stamp stores the source `.jad` SHA-256, so changing a movie source reruns conversion. Staged source files are copied only when their SHA-256 differs, and the generated `manifest.json` records staged file hashes. Interface artwork follows the existing BMP/SPR patterns. Localization remains editable after publish; existing files are not replaced, and the build's localization upgrade flow remains in charge of updates.

## Core-only checks

`Core.Tests/Atria.Core.Tests.csproj` references only `Core/Atria.Core.csproj`. Its executable checks the Core assembly for Windows executable references and native imports, verifies input edge consumption/re-arming, and checks representative native save encoding plus invalid-schema rejection. `build.ps1 test` and `test-aot` run these checks before the existing Windows test runner. The Windows runner and its CLI arguments remain unchanged.

## Verification

- `dotnet build Atria.slnx -c Release -m:1 -nr:false -p:PublishAot=false -p:NuGetAudit=false`: passed with zero warnings/errors.
- `dotnet Core.Tests/bin/Release/net10.0/Atria.Core.Tests.dll`: passed.
- `python Ios/test_asset_manifest.py`: passed missing explicit input, unlisted-file, and recursive glob fixtures.
- `python Ios/Prepare-Assets.py`: staged and hash-checked 91 source assets and reused three converted movies with matching source stamps. No player files are included.
- The unchanged Windows test runner reached `PersistenceRuntimeTests` but its disposable junction fixture could not be created in the sandbox. Earlier suites in that run passed; the full runner needs an unsandboxed run.

No publish, installed build, device install, or player-data migration was performed.
