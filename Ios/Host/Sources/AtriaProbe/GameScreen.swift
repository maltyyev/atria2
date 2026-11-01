import SwiftUI
import UIKit
import MetalKit
import GameController
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
}

final class GameController: UIViewController, MTKViewDelegate {
    private let maximumPixels = 2560 * 1440
    private var metal: MTKView!
    private var queue: MTLCommandQueue!
    private var pipeline: MTLRenderPipelineState!
    private var texture: MTLTexture?
    private var pixels: UnsafeMutablePointer<UInt32>!
    private var lastSequence: Int32 = 0
    private var frameWidth: Int32 = 0
    private var frameHeight: Int32 = 0
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
            let result = resources.withCString { root in storage.withCString { output in atria_start(root, output) } }
            AppLog.write("C# session start returned \(result); storage: \(storage).")
            if result < 0 { showCoreError(fallback: "C# session could not start.") }
        } catch { showError(String(describing: error)) }
        UIApplication.shared.isIdleTimerDisabled = true
    }

    private func setupGraphics() throws {
        guard let device = MTLCreateSystemDefaultDevice(), let commandQueue = device.makeCommandQueue() else {
            throw NSError(domain: "Atria", code: 1, userInfo: [NSLocalizedDescriptionKey: "Metal is unavailable."])
        }
        queue = commandQueue
        metal = MTKView(frame: view.bounds, device: device)
        metal.colorPixelFormat = .bgra8Unorm
        metal.clearColor = MTLClearColorMake(0, 0, 0, 1)
        metal.preferredFramesPerSecond = 60
        metal.delegate = self
        view.addSubview(metal)
        metal.addGestureRecognizer(UITapGestureRecognizer(target:self,action:#selector(tapGame(_:))))
        let shader = """
        #include <metal_stdlib>
        using namespace metal;
        struct Vertex { float4 position [[position]]; float2 uv; };
        vertex Vertex atriaFrameVertex(uint id [[vertex_id]]) {
            const float2 p[4] = {float2(-1,1),float2(-1,-1),float2(1,1),float2(1,-1)};
            const float2 uv[4] = {float2(0,0),float2(0,1),float2(1,0),float2(1,1)};
            return {float4(p[id],0,1),uv[id]};
        }
        fragment float4 sampleFrame(Vertex v [[stage_in]], texture2d<float> image [[texture(0)]]) {
            constexpr sampler s(coord::normalized, address::clamp_to_edge, filter::nearest);
            return float4(image.sample(s,v.uv).rgb,1);
        }
        """
        let library = try device.makeLibrary(source: shader, options: nil)
        let descriptor = MTLRenderPipelineDescriptor()
        descriptor.vertexFunction = library.makeFunction(name: "atriaFrameVertex")
        descriptor.fragmentFunction = library.makeFunction(name: "sampleFrame")
        descriptor.colorAttachments[0].pixelFormat = metal.colorPixelFormat
        pipeline = try device.makeRenderPipelineState(descriptor: descriptor)
        pixels = .allocate(capacity: maximumPixels)
        movieView.backgroundColor = .clear
        movieView.isUserInteractionEnabled = false
        view.addSubview(movieView)
        AppleMovies.container = movieView
    }

    private func setupControls() {
        dpad.changed = { [weak self] bits in
            guard let self else { return }
            self.virtualButtons = (self.virtualButtons & ~UInt16(15)) | bits
            self.sendPad()
        }
        view.addSubview(dpad)
        let definitions: [(String, UInt16)] = [("A",0x1000),("B",0x2000),("X",0x4000),("Y",0x8000),
            ("LB",0x100),("RB",0x200),("☰",0x10),("▣",0x20),("LT",0),("RT",0)]
        for (title, bit) in definitions {
            let button = HoldButton(title: title)
            button.changed = { [weak self] down in
                guard let self else { return }
                if title == "LT" { self.leftTrigger = down ? 255 : 0 }
                else if title == "RT" { self.rightTrigger = down ? 255 : 0 }
                else if down { self.virtualButtons |= bit }
                else { self.virtualButtons &= ~bit }
                self.sendPad()
            }
            buttons[title] = button
            view.addSubview(button)
        }
        let keyboard = HoldButton(title: "⌨")
        keyboard.changed = { [weak self] down in if down { self?.showKeyboard() } }
        buttons["⌨"] = keyboard
        view.addSubview(keyboard)
        errorLabel.isHidden = true
        view.addSubview(errorLabel)
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        metal?.frame = view.bounds
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

    private func sendPad() {
        guard active && !interrupted else { return }
        var bits = virtualButtons
        var lt = leftTrigger, rt = rightTrigger
        // The existing bindings use the stick for walking and RT+D-pad for quick moves.
        var x: Int16 = rightTrigger == 0 ? (bits & 4 != 0 ? -32767 : bits & 8 != 0 ? 32767 : 0) : 0
        var y: Int16 = rightTrigger == 0 ? (bits & 1 != 0 ? 32767 : bits & 2 != 0 ? -32767 : 0) : 0
        if let pad = GCController.controllers().first?.extendedGamepad {
            let map: [(GCControllerButtonInput, UInt16)] = [(pad.dpad.up,1),(pad.dpad.down,2),(pad.dpad.left,4),(pad.dpad.right,8),
                (pad.buttonA,0x1000),(pad.buttonB,0x2000),(pad.buttonX,0x4000),(pad.buttonY,0x8000),
                (pad.leftShoulder,0x100),(pad.rightShoulder,0x200),(pad.buttonMenu,0x10)]
            for (button, bit) in map where button.isPressed { bits |= bit }
            if pad.buttonOptions?.isPressed == true { bits |= 0x20 }
            lt = max(lt, UInt8(max(0,min(1,pad.leftTrigger.value))*255))
            rt = max(rt, UInt8(max(0,min(1,pad.rightTrigger.value))*255))
            if abs(pad.leftThumbstick.xAxis.value) > 0.1 { x = Int16(max(-1,min(1,pad.leftThumbstick.xAxis.value))*32767) }
            if abs(pad.leftThumbstick.yAxis.value) > 0.1 { y = Int16(max(-1,min(1,pad.leftThumbstick.yAxis.value))*32767) }
        }
        atria_pad(bits,lt,rt,x,y)
    }

    func draw(in view: MTKView) {
        sendPad()
        guard pixels != nil else { return }
        let sequence = atria_frame(pixels,Int32(maximumPixels),&frameWidth,&frameHeight)
        if sequence > 0 && sequence != lastSequence {
            if lastSequence == 0 { AppLog.write("First C# frame: \(frameWidth)x\(frameHeight).") }
            if texture?.width != Int(frameWidth) || texture?.height != Int(frameHeight) {
                let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm,
                    width: Int(frameWidth),height: Int(frameHeight),mipmapped: false)
                descriptor.storageMode = .shared
                descriptor.usage = .shaderRead
                texture = view.device?.makeTexture(descriptor: descriptor)
            }
            texture?.replace(region:MTLRegionMake2D(0,0,Int(frameWidth),Int(frameHeight)),mipmapLevel:0,
                withBytes:pixels,bytesPerRow:Int(frameWidth)*4)
            lastSequence = sequence
        }
        if let texture, let pass = view.currentRenderPassDescriptor, let drawable = view.currentDrawable,
           let command = queue.makeCommandBuffer(), let encoder = command.makeRenderCommandEncoder(descriptor: pass) {
            let scale = min(view.drawableSize.width / Double(texture.width),view.drawableSize.height / Double(texture.height))
            let w = Double(texture.width)*scale, h = Double(texture.height)*scale
            encoder.setViewport(MTLViewport(originX:(view.drawableSize.width-w)/2,originY:(view.drawableSize.height-h)/2,
                width:w,height:h,znear:0,zfar:1))
            encoder.setRenderPipelineState(pipeline)
            encoder.setFragmentTexture(texture,index:0)
            encoder.drawPrimitives(type:.triangleStrip,vertexStart:0,vertexCount:4)
            encoder.endEncoding()
            command.present(drawable)
            command.commit()
            // Do not overwrite a shared texture while the GPU still samples it.
            command.waitUntilCompleted()
            if command.status == .error && errorLabel.isHidden {
                showError("Metal command failed: \(String(describing: command.error))")
            }
        }
        if errorLabel.isHidden { showCoreError() }
    }

    private func showCoreError(fallback: String? = nil) {
        let count = atria_error(nil,0)
        if count > 0 {
            var message = [CChar](repeating:0,count:Int(count)+1)
            _ = atria_error(&message,Int32(message.count))
            showError(String(decoding:message.prefix(Int(count)).map { UInt8(bitPattern:$0) },as:UTF8.self))
        } else if let fallback { showError(fallback) }
    }

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) { updateFocus() }

    private func observeLifecycle() {
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName:UIApplication.willResignActiveNotification,object:nil,queue:.main) { [weak self] _ in
            MainActor.assumeIsolated { AppLog.write("Application inactive."); self?.active = false; self?.updateFocus() }
        })
        observers.append(center.addObserver(forName:UIApplication.didBecomeActiveNotification,object:nil,queue:.main) { [weak self] _ in
            MainActor.assumeIsolated { AppLog.write("Application active."); self?.active = true; self?.updateFocus() }
        })
        observers.append(center.addObserver(forName:AVAudioSession.interruptionNotification,object:nil,queue:.main) { [weak self] note in
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
        }
        let size = metal?.drawableSize ?? CGSize(width:1280,height:720)
        atria_surface(Int32(max(1,size.width)),Int32(max(1,size.height)),focused ? 1 : 0)
        metal?.isPaused = !focused
        AppleAudio.setActive(focused)
        AppleMovies.focus(focused)
    }

    private func showKeyboard() {
        let alert = UIAlertController(title:nil,message:nil,preferredStyle:.alert)
        alert.addTextField { $0.autocorrectionType = .no; $0.placeholder = "Имя / Імя" }
        alert.addAction(UIAlertAction(title:"OK",style:.default) { _ in
            (alert.textFields?.first?.text ?? "").withCString { atria_text($0) }
        })
        alert.addAction(UIAlertAction(title:"×",style:.cancel))
        present(alert,animated:true)
    }

    @objc private func tapGame(_ gesture: UITapGestureRecognizer) {
        let position = gesture.location(in:metal)
        let scaleX = metal.drawableSize.width / metal.bounds.width
        let scaleY = metal.drawableSize.height / metal.bounds.height
        let x = Int32(position.x * scaleX), y = Int32(position.y * scaleY)
        atria_pointer(x,y,1)
        atria_pointer(x,y,0)
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

    isolated deinit {
        for observer in observers { NotificationCenter.default.removeObserver(observer) }
        pixels?.deallocate()
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
    private let label = UILabel()
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
        changed?(bits)
    }
    override func touchesBegan(_ touches:Set<UITouch>,with event:UIEvent?) { update(touches) }
    override func touchesMoved(_ touches:Set<UITouch>,with event:UIEvent?) { update(touches) }
    override func touchesEnded(_ touches:Set<UITouch>,with event:UIEvent?) { reset() }
    override func touchesCancelled(_ touches:Set<UITouch>,with event:UIEvent?) { reset() }
    func reset() { changed?(0) }
}
