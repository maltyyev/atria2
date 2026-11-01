import Foundation

enum AppLog {
    private static let queue = DispatchQueue(label: "Atria diagnostics")
    // All access, including startup and failures on the audio thread, goes through queue.
    nonisolated(unsafe) private static var file: FileHandle?
    private static let retainedSessions = 10

    static func start(in directory: URL) throws {
        try queue.sync {
            guard file == nil else { return }
            let manager = FileManager.default
            try manager.createDirectory(at: directory, withIntermediateDirectories: true)
            let date = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "-")
            let url = directory.appendingPathComponent("host-\(date)-\(UUID().uuidString.prefix(8)).log")
            guard manager.createFile(atPath: url.path, contents: nil) else {
                throw CocoaError(.fileWriteUnknown, userInfo: [NSFilePathErrorKey: url.path])
            }
            file = try FileHandle(forWritingTo: url)
            let sessions = try manager.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
                .filter { $0.lastPathComponent.hasPrefix("host-") && $0.pathExtension == "log" }
                .sorted { $0.lastPathComponent > $1.lastPathComponent }
            for old in sessions.dropFirst(retainedSessions) where old != url { try manager.removeItem(at: old) }
        }
        write("Application launch; native diagnostics ready.")
    }

    static func write(_ message: String) {
        queue.sync {
            let line = "\(ISO8601DateFormatter().string(from: Date())) \(message)\n"
            do {
                guard let file else { NSLog("Atria: %@", line); return }
                try file.write(contentsOf: Data(line.utf8))
                try file.synchronize()
            } catch {
                NSLog("Atria log write failed: %@; message: %@", String(describing: error), line)
            }
        }
    }

    static func close() {
        queue.sync {
            do { try file?.close() }
            catch { NSLog("Atria log close failed: %@", String(describing: error)) }
            file = nil
        }
    }
}
