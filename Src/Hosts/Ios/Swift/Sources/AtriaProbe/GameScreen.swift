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
@MainActor final class GameController: UIViewController, UIGestureRecognizerDelegate {
    private var renderer: MetalFrameRenderer!
    private let controllerInput = ControllerInput()
    private let session = GameHostSession.shared
    private let sessionOwner = UUID()
    private var startTask: Task<Void, Never>?
    private var touchButtons: MobileTouchButtons = []
    private var touchLeftX: Int16 = 0
    private var touchLeftY: Int16 = 0
    private var touchContextId: UInt64 = 0
    private var snapshotRevision: UInt64 = 0
    private var snapshotReadErrorShown = false
    private var mobileSnapshot: MobileUiSnapshot?
    private var resettingTouchControls = false
    private var touchOverlayLayout: TouchOverlayLayout?
    private let dpad = NativeTouchDirectionPad(labels: .init(up: "", left: "", down: "", right: ""))
    private let stick = NativeTouchStick(accessibilityLabel: "")
    private let confirmButton = NativeTouchButton(title: "A", accessibilityLabel: "", holds: true, compact: true)
    private let backButton = NativeTouchButton(title: "B", accessibilityLabel: "", holds: true, compact: true)
    private let attackButton = NativeTouchButton(title: "X", accessibilityLabel: "", holds: true, compact: true)
    private let jumpButton = NativeTouchButton(title: "A", accessibilityLabel: "", holds: true, compact: true)
    private let modifierButton = NativeTouchButton(title: "B", accessibilityLabel: "", holds: true, compact: true)
    private let languageButton = NativeTouchButton(title: "", accessibilityLabel: "", compact: true)
    private let profilesButton = NativeTouchButton(title: "", accessibilityLabel: "")
    private let navigationButtons = UIView()
    private let combatButtons = UIView()
    private let noticeView = UIView()
    private let noticeLabel = UILabel()
    private var noticeTopConstraint: NSLayoutConstraint?
    private var nativeMenuHost: UIHostingController<NativeMenuPanel>?
    private var nativeMenuVisible = false
    private let movieView = UIView()
    private let errorLabel = UILabel()
    private var observers: [NSObjectProtocol] = []
    private var active = UIApplication.shared.applicationState == .active
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
            startTask = Task { @MainActor [weak self] in
                guard let self, !Task.isCancelled else { return }
                let result = await self.session.start(owner: self.sessionOwner, resources: resources, storage: storage)
                guard !Task.isCancelled else { return }
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
        renderer.onContentViewportChanged = { [weak self] _ in
            self?.layoutTouchControls()
            self?.layoutProfilesButton()
        }
        renderer.onFrame = { [weak self] in
            self?.refreshMobileUI()
            self?.sendInput()
        }
        view.addSubview(renderer.view)
        renderer.view.addGestureRecognizer(UITapGestureRecognizer(target: self, action: #selector(tapGame(_:))))
        movieView.backgroundColor = .clear
        movieView.isUserInteractionEnabled = false
        view.addSubview(movieView)
        AppleMovies.container = movieView
    }

    private func setupControls() {
        navigationButtons.addSubview(confirmButton)
        navigationButtons.addSubview(backButton)
        combatButtons.addSubview(attackButton)
        combatButtons.addSubview(jumpButton)
        combatButtons.addSubview(modifierButton)

        dpad.onDirectionChanged = { [weak self] direction, down in
            guard let self else { return }
            let bit: MobileTouchButtons = switch direction {
            case .up: .up
            case .left: .left
            case .down: .down
            case .right: .right
            }
            self.setTouchButton(bit, down: down)
        }
        confirmButton.onHoldChanged = { [weak self] in self?.setTouchButton(.a, down: $0) }
        backButton.onHoldChanged = { [weak self] in self?.setTouchButton(.b, down: $0) }
        attackButton.onHoldChanged = { [weak self] in self?.setTouchButton(.x, down: $0) }
        jumpButton.onHoldChanged = { [weak self] in self?.setTouchButton(.a, down: $0) }
        modifierButton.onHoldChanged = { [weak self] in self?.setTouchButton(.b, down: $0) }
        stick.onChange = { [weak self] x, y in self?.setTouchAxes(x, y) }
        let touchActivity = { atria_touch_activity(0) }
        dpad.onTouchBegan = touchActivity
        stick.onTouchBegan = touchActivity
        for button in [confirmButton, backButton, attackButton, jumpButton, modifierButton, languageButton, profilesButton] {
            button.onTouchBegan = touchActivity
        }
        languageButton.onTap = { [weak self] in
            guard let snapshot = self?.mobileSnapshot else { return }
            self?.sendMobileCommand(contextId: snapshot.contextId, action: .cycleLanguage)
        }
        profilesButton.onTap = { [weak self] in
            guard let snapshot = self?.mobileSnapshot else { return }
            self?.sendMobileCommand(contextId: snapshot.contextId, action: .openProfiles)
        }

        view.addSubview(dpad)
        view.addSubview(stick)
        view.addSubview(navigationButtons)
        view.addSubview(combatButtons)
        view.addSubview(languageButton)
        view.addSubview(profilesButton)

        for control in [dpad, stick, navigationButtons, combatButtons, languageButton] {
            control.isHidden = true
        }
        profilesButton.isHidden = true
        noticeView.backgroundColor = UIColor(red: 0.10, green: 0.09, blue: 0.08, alpha: 1)
        noticeView.layer.borderColor = UIColor(red: 0.59, green: 0.42, blue: 0.25, alpha: 1).cgColor
        noticeView.layer.borderWidth = 2
        noticeView.layer.cornerRadius = 12
        noticeView.layer.masksToBounds = true
        noticeView.isHidden = true
        noticeView.translatesAutoresizingMaskIntoConstraints = false
        noticeLabel.font = .preferredFont(forTextStyle: .body)
        noticeLabel.adjustsFontForContentSizeCategory = true
        noticeLabel.textColor = UIColor(red: 1, green: 0.95, blue: 0.86, alpha: 1)
        noticeLabel.numberOfLines = 0
        noticeLabel.textAlignment = .center
        noticeLabel.isAccessibilityElement = true
        noticeLabel.accessibilityTraits = .staticText
        noticeLabel.translatesAutoresizingMaskIntoConstraints = false
        noticeView.addSubview(noticeLabel)
        view.addSubview(noticeView)
        let noticeTop = noticeView.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 12)
        noticeTopConstraint = noticeTop
        NSLayoutConstraint.activate([
            noticeTop,
            noticeView.centerXAnchor.constraint(equalTo: view.safeAreaLayoutGuide.centerXAnchor),
            noticeView.widthAnchor.constraint(lessThanOrEqualTo: view.safeAreaLayoutGuide.widthAnchor, constant: -32),
            noticeLabel.leadingAnchor.constraint(equalTo: noticeView.leadingAnchor, constant: 14),
            noticeLabel.trailingAnchor.constraint(equalTo: noticeView.trailingAnchor, constant: -14),
            noticeLabel.topAnchor.constraint(equalTo: noticeView.topAnchor, constant: 10),
            noticeLabel.bottomAnchor.constraint(equalTo: noticeView.bottomAnchor, constant: -10)
        ])
        errorLabel.isHidden = true
        view.addSubview(errorLabel)
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        renderer?.view.frame = view.bounds
        renderer?.updateContentViewport()
        movieView.frame = view.bounds
        AppleMovies.layer?.frame = movieView.bounds
        let area = view.safeAreaLayoutGuide.layoutFrame
        errorLabel.frame = area.insetBy(dx: 20, dy: 30)
        layoutTouchControls()
        layoutProfilesButton()
        updateFocus()
    }

    private func sendInput() {
        guard active && !interrupted else { return }
        for slot in 0..<4 {
            let pad = controllerInput.sample(slot)
            atria_pad(pad.slot, pad.connected, pad.buttons, pad.leftTrigger, pad.rightTrigger,
                pad.lx, pad.ly, pad.rx, pad.ry)
        }
        if errorLabel.isHidden { showCoreError() }
    }

    private func refreshMobileUI() {
        let revision = atria_mobile_ui_revision()
        guard revision != snapshotRevision, let next = readMobileSnapshot() else { return }
        let contextChanged = mobileSnapshot?.contextId != next.contextId
        let touchLayoutChanged = mobileSnapshot?.overlayKind != next.overlayKind
        snapshotRevision = next.revision
        touchContextId = next.contextId
        mobileSnapshot = next
        if contextChanged || touchLayoutChanged { resetTouchControls(publishNeutral: true) }
        dpad.updateAccessibilityLabels(.init(up: next.labels.up, left: next.labels.left,
            down: next.labels.down, right: next.labels.right))
        stick.updateAccessibilityLabel(next.labels.movement)
        confirmButton.updateAccessibilityLabel(next.labels.confirm)
        backButton.updateAccessibilityLabel(next.labels.back)
        attackButton.updateAccessibilityLabel(next.labels.attack)
        jumpButton.updateAccessibilityLabel(next.labels.jump)
        modifierButton.updateAccessibilityLabel(next.labels.modifier)
        languageButton.updateTitle(next.labels.language, accessibilityLabel: next.labels.language)
        profilesButton.updateTitle(next.labels.profiles, accessibilityLabel: next.labels.profiles)
        profilesButton.isHidden = !next.showProfilesButton
        noticeTopConstraint?.constant = next.showLanguageButton ? 72 : 12
        if next.menu == nil, next.textRequest == nil, let notice = next.notice, !notice.isEmpty {
            noticeLabel.text = notice
            noticeLabel.accessibilityLabel = notice
            noticeView.isHidden = false
        } else {
            noticeView.isHidden = true
        }
        layoutTouchControls()
        layoutProfilesButton()
        updateNativeMenu(next)
    }

    private func readMobileSnapshot() -> MobileUiSnapshot? {
        var capacity = atria_mobile_ui_copy(nil, 0)
        guard capacity > 0 else {
            reportSnapshotReadError("Invalid snapshot size: \(capacity).")
            return nil
        }
        for _ in 0..<3 {
            var bytes = [UInt8](repeating: 0, count: Int(capacity))
            let copied = bytes.withUnsafeMutableBufferPointer { buffer in
                atria_mobile_ui_copy(buffer.baseAddress, Int32(buffer.count))
            }
            if copied > capacity {
                capacity = copied
                continue
            }
            guard copied >= 0, copied <= capacity else {
                reportSnapshotReadError("Snapshot copy returned \(copied) bytes for capacity \(capacity).")
                return nil
            }
            do {
                let snapshot = try JSONDecoder().decode(MobileUiSnapshot.self, from: Data(bytes.prefix(Int(copied))))
                snapshotReadErrorShown = false
                return snapshot
            }
            catch {
                reportSnapshotReadError("Mobile UI snapshot decode failed: \(error).")
                return nil
            }
        }
        reportSnapshotReadError("Mobile UI snapshot changed size repeatedly while copying.")
        return nil
    }

    private func reportSnapshotReadError(_ detail: String) {
        guard !snapshotReadErrorShown else { return }
        snapshotReadErrorShown = true
        AppLog.write(detail)
        showError("The mobile interface could not be loaded. Close and reopen the game.")
    }

    private func updateNativeMenu(_ snapshot: MobileUiSnapshot) {
        guard snapshot.menu != nil || snapshot.textRequest != nil else {
            if nativeMenuVisible {
                view.endEditing(true)
                nativeMenuHost?.rootView = NativeMenuPanel(snapshot: snapshot) { [weak self] contextId, action, targetId, value, text in
                    self?.sendMobileCommand(contextId: contextId, action: action, targetId: targetId, value: value, text: text)
                }
                nativeMenuHost?.view.isHidden = true
                nativeMenuVisible = false
            }
            return
        }
        let panel = NativeMenuPanel(snapshot: snapshot) { [weak self] contextId, action, targetId, value, text in
            self?.sendMobileCommand(contextId: contextId, action: action, targetId: targetId, value: value, text: text)
        }
        if let nativeMenuHost {
            nativeMenuHost.rootView = panel
            nativeMenuHost.view.isHidden = false
        } else {
            let host = UIHostingController(rootView: panel)
            host.view.backgroundColor = .clear
            host.view.isOpaque = true
            addChild(host)
            view.addSubview(host.view)
            host.view.translatesAutoresizingMaskIntoConstraints = false
            let touchRecognizer = UILongPressGestureRecognizer(target: self, action: #selector(panelTouch(_:)))
            touchRecognizer.minimumPressDuration = 0
            touchRecognizer.cancelsTouchesInView = false
            touchRecognizer.delaysTouchesBegan = false
            touchRecognizer.delaysTouchesEnded = false
            touchRecognizer.delegate = self
            host.view.addGestureRecognizer(touchRecognizer)
            NSLayoutConstraint.activate([
                host.view.leadingAnchor.constraint(equalTo: view.leadingAnchor),
                host.view.trailingAnchor.constraint(equalTo: view.trailingAnchor),
                host.view.topAnchor.constraint(equalTo: view.topAnchor),
                host.view.bottomAnchor.constraint(equalTo: view.bottomAnchor)
            ])
            host.didMove(toParent: self)
            nativeMenuHost = host
        }
        nativeMenuVisible = true
        if !errorLabel.isHidden { view.bringSubviewToFront(errorLabel) }
    }

    private func layoutProfilesButton() {
        guard let metal = renderer?.view, renderer.contentViewport.width > 0 else { return }
        let content = view.convert(renderer.contentViewport, from: metal)
        let center = CGPoint(x: content.midX, y: content.minY + content.height * (1320.0 / 1440.0))
        let safe = view.safeAreaLayoutGuide.layoutFrame.insetBy(dx: 8, dy: 8)
        let availableWidth = max(1, min(safe.width, content.width * 0.72))
        let size = profilesButton.intrinsicContentSize
        let width = min(availableWidth, max(min(180, availableWidth), size.width))
        let height = min(max(1, safe.height), profilesButton.fittingHeight(forWidth: width))
        let x = min(max(center.x - width / 2, safe.minX), safe.maxX - width)
        let y = min(max(center.y - height / 2, safe.minY), safe.maxY - height)
        profilesButton.frame = CGRect(x: x, y: y, width: width, height: height)
    }

    /// Places controls in black side gutters; lower groups retain the bottom safe-area inset.
    private func layoutTouchControls() {
        guard let renderer else { return }
        let viewport = view.convert(renderer.contentViewport, from: renderer.view)
        let safe = view.safeAreaLayoutGuide.layoutFrame
        let rightGutter = TouchControlGeometry.rightGutter(viewport: viewport, safeArea: safe)
        let languageMaxWidth = max(0, rightGutter.width - TouchControlMetrics.gutterMargin * 2)
        let languageWidth = min(languageMaxWidth, max(TouchControlMetrics.buttonSide,
            languageButton.intrinsicContentSize.width))
        let languageHeight = languageButton.fittingHeight(forWidth: languageWidth)
        let overlayMode: TouchOverlayMode = switch mobileSnapshot?.overlayKind ?? .none {
        case .none: .none
        case .navigation: .navigation
        case .combat: .combat
        }
        let next = TouchControlGeometry.overlayLayout(viewport: viewport, screenBounds: view.bounds, safeArea: safe,
            overlayKind: overlayMode,
            showLanguageButton: mobileSnapshot?.showLanguageButton == true,
            languageSize: CGSize(width: languageWidth, height: languageHeight))
        if touchOverlayLayout != next {
            resetTouchControls(publishNeutral: true)
            touchOverlayLayout = next
            AppLog.write("Touch layout viewport=\(viewport), safe=\(safe), dpad=\(String(describing: next.directionPad)), stick=\(String(describing: next.stick)), actions=\(String(describing: next.navigationButtons ?? next.combatButtons)).")
        }
        dpad.frame = next.directionPad ?? .zero
        navigationButtons.frame = next.navigationButtons ?? .zero
        stick.frame = next.stick ?? .zero
        combatButtons.frame = next.combatButtons ?? .zero
        languageButton.frame = next.languageButton ?? .zero
        let faceFrames = TouchControlGeometry.faceButtonFrames(in: CGRect(origin: .zero,
            size: TouchControlGeometry.directionPadSize))
        confirmButton.frame = faceFrames.jump
        backButton.frame = faceFrames.modifier
        attackButton.frame = faceFrames.attack
        jumpButton.frame = faceFrames.jump
        modifierButton.frame = faceFrames.modifier
        dpad.isHidden = next.directionPad == nil
        navigationButtons.isHidden = next.navigationButtons == nil
        stick.isHidden = next.stick == nil
        combatButtons.isHidden = next.combatButtons == nil
        languageButton.isHidden = next.languageButton == nil
    }

    private func sendMobileCommand(contextId: UInt64, action: MobileUiAction, targetId: String? = nil,
                                   value: Int32 = 0, text: String? = nil) {
        guard active && !interrupted else { return }
        withUTF8CString(targetId) { target in
            withUTF8CString(text) { message in
                atria_mobile_ui_command(contextId, action.rawValue, target, value, message)
            }
        }
    }

    private func withUTF8CString<T>(_ value: String?, _ body: (UnsafePointer<UInt8>?) -> T) -> T {
        guard let value else { return body(nil) }
        var bytes = Array(value.utf8)
        bytes.append(0)
        return bytes.withUnsafeBufferPointer { body($0.baseAddress) }
    }

    private func setTouchButton(_ bit: MobileTouchButtons, down: Bool) {
        guard !resettingTouchControls else { return }
        if down { touchButtons.insert(bit) }
        else { touchButtons.remove(bit) }
        sendTouchSample()
    }

    private func setTouchAxes(_ x: Int16, _ y: Int16) {
        guard !resettingTouchControls else { return }
        touchLeftX = x
        touchLeftY = y
        sendTouchSample()
    }

    private func sendTouchSample(force: Bool = false) {
        guard mobileSnapshot != nil, force || active && !interrupted else { return }
        atria_mobile_touch_sample(touchContextId, touchLeftX, touchLeftY, touchButtons.rawValue)
    }

    private func resetTouchControls(publishNeutral: Bool) {
        resettingTouchControls = true
        touchButtons = []
        touchLeftX = 0
        touchLeftY = 0
        dpad.reset()
        stick.reset()
        confirmButton.reset()
        backButton.reset()
        attackButton.reset()
        jumpButton.reset()
        modifierButton.reset()
        languageButton.reset()
        profilesButton.reset()
        resettingTouchControls = false
        if publishNeutral { sendTouchSample(force: true) }
    }

    @objc private func panelTouch(_ gesture: UILongPressGestureRecognizer) {
        if gesture.state == .began, active && !interrupted { atria_touch_activity(0) }
    }

    func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer,
                           shouldRecognizeSimultaneouslyWith otherGestureRecognizer: UIGestureRecognizer) -> Bool {
        true
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
        if !focused { resetTouchControls(publishNeutral: true) }
        let size = renderer?.view.drawableSize ?? CGSize(width: 1280, height: 720)
        atria_surface(Int32(max(1, size.width)), Int32(max(1, size.height)), focused ? 1 : 0)
        if focused {
            session.resume(owner: sessionOwner)
            sendTouchSample()
        }
        else { session.suspend(owner: sessionOwner) }
        renderer?.view.isPaused = !focused
        AppleAudio.setActive(focused)
        AppleMovies.focus(focused)
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
        errorLabel.backgroundColor = UIColor(red: 0.10, green: 0.09, blue: 0.08, alpha: 1)
        errorLabel.layer.cornerRadius = 12
        errorLabel.layer.borderWidth = 2
        errorLabel.layer.borderColor = UIColor(red: 0.59, green: 0.42, blue: 0.25, alpha: 1).cgColor
        errorLabel.clipsToBounds = true
        errorLabel.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
        errorLabel.numberOfLines = 0
        errorLabel.text = text
        errorLabel.isHidden = false
        view.addSubview(errorLabel)
    }

    /// Releases this view's lease and asynchronously stops the final active host.
    func stopSession() {
        resetTouchControls(publishNeutral: true)
        active = false
        startTask?.cancel()
        startTask = nil
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
