// swift-tools-version: 6.0

import PackageDescription
import Foundation

let iosRoot = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().path
let runtimeRoot = ProcessInfo.processInfo.environment["ATRIA_NATIVEAOT_ROOT"]
    ?? iosRoot + "/../.packages/microsoft.netcore.app.runtime.nativeaot.ios-arm64/10.0.13/runtimes/ios-arm64/native"

let package = Package(
    name: "AtriaProbe",
    platforms: [
        .iOS(.v17),
        .macOS(.v14),
    ],
    products: [
        // An xtool project should contain exactly one library product,
        // representing the main app.
        .library(
            name: "AtriaProbe",
            targets: ["AtriaProbe"]
        ),
    ],
    targets: [
        .target(
            name: "AtriaProbe",
            dependencies: ["NativeCore"],
            resources: [.copy("Resources/assets")]
        ),
        .target(
            name: "NativeCore",
            linkerSettings: [
                // Match NativeAOT's Apple runtime/bootstrap libraries; no unresolved-symbol fallback.
                .unsafeFlags([
                    "-Xlinker", iosRoot + "/Game.ios-arm64.o",
                    "-Xlinker", runtimeRoot + "/libbootstrapperdll.o",
                    "-Xlinker", runtimeRoot + "/libSystem.Native.a",
                    "-Xlinker", runtimeRoot + "/libSystem.IO.Compression.Native.a",
                    "-Xlinker", runtimeRoot + "/libSystem.Net.Security.Native.a",
                    "-Xlinker", runtimeRoot + "/libSystem.Security.Cryptography.Native.Apple.a",
                    "-L" + runtimeRoot,
                ]),
                .linkedLibrary("Runtime.WorkstationGC"),
                .linkedLibrary("eventpipe-disabled"),
                .linkedLibrary("standalonegc-disabled"),
                .linkedLibrary("aotminipal"),
                .linkedLibrary("stdc++compat"),
                .linkedLibrary("brotlienc"),
                .linkedLibrary("brotlidec"),
                .linkedLibrary("brotlicommon"),
                .linkedLibrary("z"),
                .linkedLibrary("dl"),
                .linkedLibrary("objc"),
                .linkedLibrary("icucore"),
                .linkedFramework("CoreFoundation"),
                .linkedFramework("Foundation"),
                .linkedFramework("CryptoKit"),
                .linkedFramework("Network"),
                .linkedFramework("Security"),
                .linkedFramework("GSS"),
            ]
        ),
    ]
)
