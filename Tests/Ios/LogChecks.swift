import Foundation
import Dispatch

@main struct LogChecks {
    static func main() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("Atria-log-check-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { AppLog.close(); try? FileManager.default.removeItem(at: directory) }
        for index in 0..<12 {
            try Data("old".utf8).write(to: directory.appendingPathComponent(String(format: "host-2000-%02d.log", index)))
        }
        let unrelated = directory.appendingPathComponent("user-notes.txt")
        try Data("preserve".utf8).write(to: unrelated)
        try AppLog.start(in: directory)
        let diagnostic = "ERROR: Metal\nДобрый день / Добры дзень\nstack: line 2"
        AppLog.write(diagnostic)
        DispatchQueue.concurrentPerform(iterations: 20) { index in AppLog.write("worker-\(index)") }
        // Read while the logger is open, as the desktop reader does with a running game.
        let sessions = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
            .filter { $0.pathExtension == "log" }.sorted { $0.lastPathComponent > $1.lastPathComponent }
        precondition(sessions.count == 10)
        let text = try String(contentsOf: sessions[0], encoding: .utf8)
        precondition(text.contains(diagnostic))
        for index in 0..<20 { precondition(text.contains("worker-\(index)\n")) }
        let preserved = try String(contentsOf: unrelated, encoding: .utf8)
        precondition(preserved == "preserve")
        let support = directory.appendingPathComponent("Support")
        let alias = directory.appendingPathComponent("OSAlias")
        try FileManager.default.createDirectory(at: support, withIntermediateDirectories: false)
        try FileManager.default.createSymbolicLink(at: alias, withDestinationURL: support)
        let physical = try AppStorage.directory(in: alias)
        precondition(physical == support.path + "/Atria")
        try FileManager.default.createSymbolicLink(at: support.appendingPathComponent("Atria"), withDestinationURL: directory)
        let withPlayerLink = try AppStorage.directory(in: alias)
        precondition(withPlayerLink == physical, "Player-owned links must remain visible to the shared safety checks.")
        print("Log PASS: immediate UTF-8 persistence, multiline errors, concurrent writes, retention, unrelated files preserved.")
        print("Storage PASS: OS alias resolved; player directory links remain subject to shared validation.")
    }
}
