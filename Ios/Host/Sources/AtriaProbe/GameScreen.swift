import SwiftUI
import UIKit
import MetalKit
import AVFoundation
import NativeCore

@main
struct AtriaApp: App {
    init() {
        do {
            let support = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask,
                appropriateFor: nil, create: true)
            try AppLog.start(in: support.appendingPathComponent("Atria/logs", isDirectory: true))
            AppLog.write("Build \(Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") ?? "unknown"); iOS \(UIDevice.current.systemVersion); \(UIDevice.current.model).")
        } catch { NSLog("Atria cannot initialize diagnostics: %@", String(describing: error)) }
    }
    var body: some Scene { WindowGroup { GameScreen().ignoresSafeArea().statusBarHidden() } }
}

struct GameScreen: UIViewControllerRepresentable {
    func makeUIViewController(context: Context) -> GameController { GameController() }
    func updateUIViewController(_ controller: GameController, context: Context) { }
    static func dismantleUIViewController(_ controller: GameController, coordinator: ()) { controller.stopSession() }
}

/// Coordinates UIKit lifecycle, touch controls and the shared game session.
@MainActor final class GameController: UIViewController {
    private var renderer: MetalFrameRenderer!
    private let controllerInput = ControllerInput()
    private let session = GameHostSession.shared
    private let sessionOwner = UUID()
    private var virtualButtons: UInt16 = 0
    private var leftTrigger: UInt8 = 0
    private var rightTrigger: UInt8 = 0
    private var buttons: [String: HoldButton] = [:]
    private let dpad = DirectionPad()
    private let movieView = UIView()
    private let errorLabel = UILabel()
    private var observers: [NSObjectProtocol] = []
    private var active = true
    private var interrupted = false

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        view.isMultipleTouchEnabled = true
        do {
            AppLog.write("Initializing Metal.")
            try setupGraphics()
            AppLog.write("Metal ready; initializing audio.")
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
            try AVAudioSession.sharedInstance().setActive(true)
            setupControls()
            observeLifecycle()
            guard let resourceURL = Bundle.module.url(forResource: "assets", withExtension: nil) else {
                throw CocoaError(.fileReadNoSuchFile, userInfo: [NSLocalizedDescriptionKey: "Bundled assets are missing."])
            }
            let resources = resourceURL.path
            let support = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask,
                appropriateFor: nil, create: true)
            let storage = try AppStorage.directory(in: support)
            Task { @MainActor [weak self] in
                guard let self else { return }
                let result = await self.session.start(owner: self.sessionOwner, resources: resources, storage: storage)
                AppLog.write("C# session start returned \(result); storage: \(storage).")
                self.updateFocus()
                if result < 0 { self.showCoreError(fallback: "C# session could not start.") }
            }
        } catch { showError(String(describing: error)) }
        UIApplication.shared.isIdleTimerDisabled = true
    }

    private func setupGraphics() throws {
        renderer = try MetalFrameRenderer(frame: view.bounds)
        renderer.onFirstFrame = { AppLog.write("First C# frame: \($0)x\($1).") }
        renderer.onError = { [weak self] in self?.showError($0) }
        renderer.onFrame = { [weak self] in self?.sendInput() }
        view.addSubview(renderer.view)
        renderer.view.addGestureRecognizer(UITapGestureRecognizer(target: self, action: #selector(tapGame(_:))))
        movieView.backgroundColor = .clear
        movieView.isUserInteractionEnabled = false
        view.addSubview(movieView)
        AppleMovies.container = movieView
    }

    private func setupControls() {
        dpad.changed = { [weak self] bits in
            guard let self else { return }
            self.virtualButtons = (self.virtualButtons & ~UInt16(15)) | bits
            self.sendInput()
        }
        dpad.touched = { atria_touch_activity(0) }
        view.addSubview(dpad)
        let definitions: [(String, UInt16)] = [("A",0x1000),("B",0x2000),("X",0x4000),("Y",0x8000),
            ("LB",0x100),("RB",0x200),("☰",0x10),("▣",0x20),("LT",0),("RT",0)]
        for (title, bit) in definitions {
            let button = HoldButton(title: title)
            button.changed = { [weak self] down in
                guard let self else { return }
                if down { atria_touch_activity(0) }
                if title == "LT" { self.leftTrigger = down ? 255 : 0 }
                else if title == "RT" { self.rightTrigger = down ? 255 : 0 }
                else if down { self.virtualButtons |= bit }
                else { self.virtualButtons &= ~bit }
                self.sendInput()
            }
            buttons[title] = button
            view.addSubview(button)
        }
        let keyboard = HoldButton(title: "⌨")
        keyboard.changed = { [weak self] down in
            guard down, let self else { return }
            atria_touch_activity(0)
            self.showKeyboard()
        }
        buttons["⌨"] = keyboard
        view.addSubview(keyboard)
        errorLabel.isHidden = true
        view.addSubview(errorLabel)
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        renderer?.view.frame = view.bounds
        movieView.frame = view.bounds
        AppleMovies.layer?.frame = movieView.bounds
        let area = view.safeAreaLayoutGuide.layoutFrame
        let diameter: CGFloat = min(44, area.height / 7)
        let gap: CGFloat = 5
        let left = area.minX + 8
        let right = area.maxX - 8
        let centerY = area.maxY - diameter * 1.8
        dpad.frame = CGRect(x: left, y: centerY - diameter * 1.5, width: diameter * 3, height: diameter * 3)
        let centers: [String: CGPoint] = [
            "A": CGPoint(x:right-diameter*1.5,y:centerY+diameter), "Y": CGPoint(x:right-diameter*1.5,y:centerY-diameter),
            "X": CGPoint(x:right-diameter*2.5,y:centerY), "B": CGPoint(x:right-diameter*0.5,y:centerY),
            "LT": CGPoint(x:left+diameter*0.5,y:area.minY+diameter*0.6),
            "LB": CGPoint(x:left+diameter*1.6+gap,y:area.minY+diameter*0.6),
            "RT": CGPoint(x:right-diameter*0.5,y:area.minY+diameter*0.6),
            "RB": CGPoint(x:right-diameter*1.6-gap,y:area.minY+diameter*0.6),
            "☰": CGPoint(x:area.midX+diameter*0.7,y:area.minY+diameter*0.6),
            "▣": CGPoint(x:area.midX-diameter*0.7,y:area.minY+diameter*0.6),
            "⌨": CGPoint(x:area.midX,y:area.maxY-diameter*0.5)
        ]
        for (name, point) in centers {
            buttons[name]?.frame = CGRect(x:point.x-diameter/2,y:point.y-diameter/2,width:diameter,height:diameter)
        }
        errorLabel.frame = area.insetBy(dx: 20, dy: 30)
        updateFocus()
    }

    private func sendInput() {
        guard active && !interrupted else { return }
        // Touch controls travel separately; they never occupy or mask a physical controller slot.
        let x: Int16 = rightTrigger == 0 ? (virtualButtons & 4 != 0 ? -32767 : virtualButtons & 8 != 0 ? 32767 : 0) : 0
        let y: Int16 = rightTrigger == 0 ? (virtualButtons & 1 != 0 ? 32767 : virtualButtons & 2 != 0 ? -32767 : 0) : 0
        atria_touch_pad(virtualButtons, leftTrigger, rightTrigger, x, y)
        for slot in 0..<4 {
            let pad = controllerInput.sample(slot)
            atria_pad(pad.slot, pad.connected, pad.buttons, pad.leftTrigger, pad.rightTrigger,
                pad.lx, pad.ly, pad.rx, pad.ry)
        }
        if errorLabel.isHidden { showCoreError() }
    }

    private func showCoreError(fallback: String? = nil) {
        let count = atria_error(nil, 0)
        if count > 0 {
            var message = [CChar](repeating: 0, count: Int(count) + 1)
            _ = atria_error(&message, Int32(message.count))
            showError(String(decoding: message.prefix(Int(count)).map { UInt8(bitPattern: $0) }, as: UTF8.self))
        } else if let fallback { showError(fallback) }
    }

    private func observeLifecycle() {
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: UIApplication.willResignActiveNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                AppLog.write("Application inactive.")
                self?.active = false
                self?.updateFocus()
            }
        })
        observers.append(center.addObserver(forName: UIApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                AppLog.write("Application active.")
                self?.active = true
                self?.updateFocus()
            }
        })
        observers.append(center.addObserver(forName: AVAudioSession.interruptionNotification, object: nil, queue: .main) { [weak self] note in
            let kind = note.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt
            MainActor.assumeIsolated {
                self?.interrupted = kind == AVAudioSession.InterruptionType.began.rawValue
                if self?.interrupted == false { try? AVAudioSession.sharedInstance().setActive(true) }
                self?.updateFocus()
            }
        })
    }

    private func updateFocus() {
        let focused = active && !interrupted
        if !focused {
            virtualButtons = 0; leftTrigger = 0; rightTrigger = 0
            dpad.reset()
            for button in buttons.values { button.reset() }
            atria_touch_pad(0, 0, 0, 0, 0)
        }
        let size = renderer?.view.drawableSize ?? CGSize(width: 1280, height: 720)
        atria_surface(Int32(max(1, size.width)), Int32(max(1, size.height)), focused ? 1 : 0)
        if focused { session.resume(owner: sessionOwner) }
        else { session.suspend(owner: sessionOwner) }
        renderer?.view.isPaused = !focused
        AppleAudio.setActive(focused)
        AppleMovies.focus(focused)
    }

    private func showKeyboard() {
        let alert = UIAlertController(title: nil, message: nil, preferredStyle: .alert)
        alert.addTextField { $0.autocorrectionType = .no; $0.placeholder = "Имя / Імя" }
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in
            (alert.textFields?.first?.text ?? "").withCString { atria_text($0) }
        })
        alert.addAction(UIAlertAction(title: "×", style: .cancel))
        present(alert, animated: true)
    }

    @objc private func tapGame(_ gesture: UITapGestureRecognizer) {
        guard let metal = renderer?.view else { return }
        atria_touch_activity(0)
        let position = gesture.location(in: metal)
        let scaleX = metal.drawableSize.width / metal.bounds.width
        let scaleY = metal.drawableSize.height / metal.bounds.height
        let x = Int32(position.x * scaleX), y = Int32(position.y * scaleY)
        atria_pointer(x, y, 1)
        atria_pointer(x, y, 0)
    }

    private func showError(_ text: String) {
        AppLog.write("ERROR: \(text)")
        errorLabel.textColor = .white
        errorLabel.backgroundColor = UIColor.black.withAlphaComponent(0.9)
        errorLabel.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
        errorLabel.numberOfLines = 0
        errorLabel.text = text
        errorLabel.isHidden = false
        view.addSubview(errorLabel)
    }

    /// Releases this view's lease and asynchronously stops the final active host.
    func stopSession() {
        for observer in observers { NotificationCenter.default.removeObserver(observer) }
        observers.removeAll()
        renderer?.view.isPaused = true
        if session.stop(owner: sessionOwner) {
            AppleAudio.setActive(false)
            AppleMovies.stop()
            try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
        }
    }
}

final class HoldButton: UIControl {
    var changed: ((Bool)->Void)?
    private let title = UILabel()
    init(title text:String) {
        super.init(frame:.zero)
        backgroundColor = UIColor(white:0.12,alpha:0.65)
        layer.borderWidth = 1
        layer.borderColor = UIColor(white:0.8,alpha:0.7).cgColor
        title.text = text
        title.textColor = .white
        title.textAlignment = .center
        title.font = .systemFont(ofSize:16,weight:.semibold)
        title.isUserInteractionEnabled = false
        addSubview(title)
        accessibilityLabel = text
        accessibilityTraits = .button
    }
    required init?(coder:NSCoder) { fatalError("init(coder:) is not supported") }
    override func layoutSubviews() { super.layoutSubviews(); title.frame = bounds; layer.cornerRadius = bounds.width/2 }
    override func beginTracking(_ touch:UITouch,with event:UIEvent?) -> Bool { alpha = 0.5; changed?(true); return true }
    override func endTracking(_ touch:UITouch?,with event:UIEvent?) { reset() }
    override func cancelTracking(with event:UIEvent?) { reset() }
    func reset() { alpha = 1; changed?(false) }
}

final class DirectionPad: UIView {
    var changed: ((UInt16)->Void)?
    var touched: (() -> Void)?
    private let label = UILabel()
    private var lastBits: UInt16 = 0
    override init(frame:CGRect) {
        super.init(frame:frame)
        backgroundColor = UIColor(white:0.12,alpha:0.6)
        label.text = "      ↑\n←      →\n      ↓"
        label.numberOfLines = 3
        label.textAlignment = .center
        label.textColor = .white
        label.font = .systemFont(ofSize:25,weight:.semibold)
        addSubview(label)
        accessibilityLabel = "D-pad"
    }
    required init?(coder:NSCoder) { fatalError("init(coder:) is not supported") }
    override func layoutSubviews() { super.layoutSubviews(); layer.cornerRadius = bounds.width/2; label.frame = bounds }
    private func update(_ touches:Set<UITouch>) {
        guard let point = touches.first?.location(in:self) else { return }
        let dx = point.x-bounds.midX, dy = point.y-bounds.midY
        let deadZone = bounds.width*0.12
        var bits: UInt16 = 0
        if dx < -deadZone { bits |= 4 }
        if dx > deadZone { bits |= 8 }
        if dy < -deadZone { bits |= 1 }
        if dy > deadZone { bits |= 2 }
        if bits != lastBits && bits != 0 { touched?() }
        lastBits = bits
        changed?(bits)
    }
    override func touchesBegan(_ touches:Set<UITouch>,with event:UIEvent?) { touched?(); update(touches) }
    override func touchesMoved(_ touches:Set<UITouch>,with event:UIEvent?) { update(touches) }
    override func touchesEnded(_ touches:Set<UITouch>,with event:UIEvent?) { reset() }
    override func touchesCancelled(_ touches:Set<UITouch>,with event:UIEvent?) { reset() }
    func reset() { lastBits = 0; changed?(0) }
}
