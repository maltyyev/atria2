import Foundation
#if canImport(Darwin)
import Darwin
#else
import Glibc
#endif

enum AppStorage {
    static func directory(in support: URL) throws -> String {
        // Resolve the OS-owned /var alias before adding our folders; keep checks on player directories intact.
        guard let physical = realpath(support.path, nil) else {
            throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno))
        }
        defer { free(physical) }
        return String(cString: physical) + "/Atria"
    }
}
