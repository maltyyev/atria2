import Foundation
import NativeCore

/// Owns session start, focus transitions, asynchronous stop and restart ordering.
@MainActor final class GameHostSession {
    static let shared = GameHostSession()
    private var owners = Set<UUID>()
    private var started = false
    private var stopTask: Task<Int32, Never>?
    private var stopGeneration = 0

    /// Acquires a view lease after any earlier asynchronous stop has completed.
    func start(owner: UUID, resources: String, storage: String) async -> Int32 {
        guard !Task.isCancelled else { return -1 }
        owners.insert(owner)
        while let pendingStop = stopTask {
            let generation = stopGeneration
            let result = await pendingStop.value
            if generation != stopGeneration { continue }
            if result < 0 {
                owners.remove(owner)
                return result
            }
            stopTask = nil
        }
        guard !Task.isCancelled, owners.contains(owner) else {
            owners.remove(owner)
            return -1
        }
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

    /// Releases a view lease and requests shutdown after the final owner leaves.
    func stop(owner: UUID) -> Bool {
        owners.remove(owner)
        guard owners.isEmpty else { return false }
        if started {
            started = false
            _ = atria_stop()
            stopGeneration += 1
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
