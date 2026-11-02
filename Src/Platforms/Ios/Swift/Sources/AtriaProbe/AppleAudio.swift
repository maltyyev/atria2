import Foundation
import AVFoundation

enum AppleAudio {
    static let queue = DispatchQueue(label: "Atria audio")
    // C ABI calls are synchronous on the game thread; every access is serialized by queue.
    nonisolated(unsafe) static var players: [Int64: AVAudioPlayer] = [:]
    nonisolated(unsafe) static var paused: Set<Int64> = []
    nonisolated(unsafe) static var nextID: Int64 = 1
    nonisolated(unsafe) static var active = true

    static func register(_ player: AVAudioPlayer) -> Int64 {
        guard player.prepareToPlay() else { return 0 }
        let id = nextID
        nextID += 1
        players[id] = player
        return id
    }

    static func setActive(_ value: Bool) {
        queue.sync {
            active = value
            if value {
                for id in paused { players[id]?.play() }
                paused.removeAll()
            } else {
                for (id, player) in players where player.isPlaying {
                    paused.insert(id)
                    player.pause()
                }
            }
        }
    }
}

@_cdecl("apple_sound_create")
func soundCreate(_ bytes: UnsafeRawPointer?, _ count: Int32) -> Int64 {
    guard let bytes, count > 0 else { return 0 }
    let data = Data(bytes: bytes, count: Int(count))
    return AppleAudio.queue.sync {
        do { return AppleAudio.register(try AVAudioPlayer(data: data)) }
        catch { AppLog.write("Audio decode failed: \(error)"); return 0 }
    }
}

@_cdecl("apple_music_open")
func musicOpen(_ path: UnsafePointer<CChar>?) -> Int64 {
    guard let path else { return 0 }
    let url = URL(fileURLWithPath: String(cString: path))
    return AppleAudio.queue.sync {
        do { return AppleAudio.register(try AVAudioPlayer(contentsOf: url)) }
        catch { AppLog.write("Music open failed: \(error)"); return 0 }
    }
}

@_cdecl("apple_sound_command")
func soundCommand(_ id: Int64, _ command: Int32, _ value: Float) -> Int32 {
    AppleAudio.queue.sync {
        guard let player = AppleAudio.players[id] else { return -1 }
        switch command {
        case 0:
            if !player.isPlaying {
                if AppleAudio.active {
                    guard player.play() else { return -1 }
                } else { AppleAudio.paused.insert(id) }
            }
        case 1:
            player.stop()
            player.currentTime = 0
            AppleAudio.paused.remove(id)
        case 2: player.volume = min(1, max(0, value))
        case 3: return player.isPlaying || AppleAudio.paused.contains(id) ? 1 : 0
        case 4:
            player.stop()
            AppleAudio.players.removeValue(forKey: id)
            AppleAudio.paused.remove(id)
        default: return -1
        }
        return 0
    }
}
