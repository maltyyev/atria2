import Foundation
import NativeCore

/// Supplies deterministic native bridge calls for the production session coordinator.
private final class SessionBridge: @unchecked Sendable {
    static let shared = SessionBridge()
    private let lock = NSLock()
    private var starts = 0
    func start() -> Int32 { lock.withLock { starts += 1; return 1 } }
    var startCount: Int { lock.withLock { starts } }
}

@_cdecl("atria_start")
func testStart(_ resources: UnsafePointer<CChar>?, _ storage: UnsafePointer<CChar>?) -> Int32 {
    SessionBridge.shared.start()
}
@_cdecl("atria_suspend") func testSuspend() { }
@_cdecl("atria_resume") func testResume() { }
@_cdecl("atria_stop") func testStop() -> Int32 { 0 }
@_cdecl("atria_wait_stopped") func testWaitStopped() -> Int32 {
    Thread.sleep(forTimeInterval: 0.02)
    return 0
}

@main struct SessionChecks {
    @MainActor static func main() async {
        let session = GameHostSession()
        let first = UUID(), second = UUID(), cancelled = UUID()
        let initial = await session.start(owner: first, resources: "assets", storage: "output")
        let shared = await session.start(owner: second, resources: "assets", storage: "output")
        precondition(initial == 1 && shared == 0 && SessionBridge.shared.startCount == 1)
        precondition(!session.stop(owner: first), "One view stopped another view's session.")
        precondition(session.stop(owner: second))
        var entered = false
        let pending = Task { @MainActor in
            entered = true
            return await session.start(owner: cancelled, resources: "assets", storage: "output")
        }
        while !entered { await Task.yield() }
        pending.cancel()
        _ = session.stop(owner: cancelled)
        let cancelledResult = await pending.value
        precondition(cancelledResult == -1 && SessionBridge.shared.startCount == 1,
            "A cancelled view started a game after it had been dismantled.")
        let restarted = await session.start(owner: first, resources: "assets", storage: "output")
        precondition(restarted == 1 && SessionBridge.shared.startCount == 2)
        precondition(session.stop(owner: first))
        print("Swift session checks PASS: shared leases, asynchronous stop, cancelled start and restart.")
    }
}
