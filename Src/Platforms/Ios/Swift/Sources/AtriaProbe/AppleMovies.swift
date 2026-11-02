import UIKit
import AVFoundation

@MainActor enum AppleMovies {
    nonisolated static let lock = NSLock()
    // Only status crosses the C ABI; the lock protects every read/write.
    nonisolated(unsafe) static var status: Int32 = 1
    static var player: AVPlayer?
    static var layer: AVPlayerLayer?
    static var observation: NSKeyValueObservation?
    static var completion: NSObjectProtocol?
    static weak var container: UIView?
    static var active = true

    nonisolated static func setStatus(_ value: Int32) { lock.lock(); status = value; lock.unlock() }
    static func stop() {
        player?.pause()
        layer?.removeFromSuperlayer()
        observation = nil
        if let completion { NotificationCenter.default.removeObserver(completion) }
        completion = nil
        player = nil
        layer = nil
    }
    static func focus(_ value: Bool) {
        active = value
        if value { player?.play() } else { player?.pause() }
    }
}

@_cdecl("apple_movie_start")
func movieStart(_ path: UnsafePointer<CChar>?, _ volume: Float) {
    guard let path else { AppleMovies.setStatus(-1); return }
    let url = URL(fileURLWithPath: String(cString: path))
    AppleMovies.setStatus(0)
    DispatchQueue.main.async {
        AppleMovies.stop()
        guard let view = AppleMovies.container else { AppleMovies.setStatus(-1); return }
        let player = AVPlayer(url: url)
        player.volume = volume
        let layer = AVPlayerLayer(player: player)
        layer.videoGravity = .resizeAspect
        layer.frame = view.bounds
        layer.backgroundColor = UIColor.black.cgColor
        view.layer.addSublayer(layer)
        AppleMovies.player = player
        AppleMovies.layer = layer
        if let item = player.currentItem {
            AppleMovies.observation = item.observe(\.status, options: [.new]) { item, _ in
                if item.status == .failed {
                    AppLog.write("Movie failed: \(String(describing: item.error))")
                    AppleMovies.setStatus(-1)
                }
            }
            AppleMovies.completion = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime,
                object: item, queue: .main) { _ in AppleMovies.setStatus(1) }
        }
        if AppleMovies.active { player.play() }
    }
}

@_cdecl("apple_movie_status")
func movieStatus() -> Int32 {
    AppleMovies.lock.lock()
    defer { AppleMovies.lock.unlock() }
    return AppleMovies.status
}

@_cdecl("apple_movie_stop")
func movieStop() { DispatchQueue.main.async { AppleMovies.stop() } }
