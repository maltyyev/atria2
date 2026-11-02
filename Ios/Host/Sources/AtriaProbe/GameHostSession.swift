import Foundation
import NativeCore

/// Owns session start, focus transitions, asynchronous stop and restart ordering.
@MainActor final class GameHostSession {
    static let shared = GameHostSession()
    private var owners = Set<UUID>()
    private var started = false
    private var stopTask: Task<Int32, Never>?

    /// Waits for an earlier stop to finish before acquiring the shared managed session.
    /// Acquires a view lease and starts the host if no active session exists.
    func start(owner: UUID, resources: String, storage: String) async -> Int32 {
        owners.insert(owner)
        if let pendingStop = stopTask {
            let result = await pendingStop.value
            self.stopTask = nil
            if result < 0 { return result }
        }
        guard owners.contains(owner) else { return -1 }
        if started { return 0 }
        let result = resources.withCString { root in storage.withCString { output in atria_start(root, output) } }
        started = result >= 0
        return result
    }

    /// Pauses host input and rendering while retaining the game session and logical state.
    func suspend(owner: UUID) {
        if owners.contains(owner) && started { atria_suspend() }
    }

    /// Resumes host input and rendering without rebasing simulation time.
    func resume(owner: UUID) {
        if owners.contains(owner) && started { atria_resume() }
    }

    /// Releases one view's ownership and requests shutdown only after the final owner leaves.
    /// Releases a view lease and reports whether it stopped the shared host.
    func stop(owner: UUID) -> Bool {
        owners.remove(owner)
        guard owners.isEmpty else { return false }
        if started {
            started = false
            _ = atria_stop()
            stopTask = Task.detached(priority: .userInitiated) { atria_wait_stopped() }
        }
        return true
    }

    /// Restarts the shared session after its worker and frame transport have been released.
    func restart(owner: UUID, resources: String, storage: String) async -> Int32 {
        guard owners == Set([owner]) else { return -1 }
        _ = stop(owner: owner)
        return await start(owner: owner, resources: resources, storage: storage)
    }
}
