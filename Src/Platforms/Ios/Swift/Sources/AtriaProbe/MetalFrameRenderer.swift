import Foundation
import MetalKit
import NativeCore

/// Owns the Metal pipeline and shared BGRA upload texture used to present C# frames.
@MainActor final class MetalFrameRenderer: NSObject, MTKViewDelegate {
    static let maximumPixels = 2560 * 1440
    let view: MTKView
    private let queue: MTLCommandQueue
    private let pipeline: MTLRenderPipelineState
    private var texture: MTLTexture?
    private var pixels: UnsafeMutablePointer<UInt32>
    private var lastSequence: Int32 = 0
    private var frameWidth: Int32 = 0
    private var frameHeight: Int32 = 0
    private var drawableViewport = RenderedViewportRect.zero
    private(set) var contentViewport = CGRect.zero
    var onError: ((String) -> Void)?
    var onFirstFrame: ((Int32, Int32) -> Void)?
    var onFrame: (() -> Void)?
    var onContentViewportChanged: ((CGRect) -> Void)?

    /// Builds the Metal view and pipeline before the managed session starts.
    init(frame: CGRect) throws {
        guard let device = MTLCreateSystemDefaultDevice(), let commandQueue = device.makeCommandQueue() else {
            throw NSError(domain: "Atria", code: 1, userInfo: [NSLocalizedDescriptionKey: "Metal is unavailable."])
        }
        view = MTKView(frame: frame, device: device)
        view.colorPixelFormat = .bgra8Unorm
        view.clearColor = MTLClearColorMake(0, 0, 0, 1)
        view.preferredFramesPerSecond = 60
        queue = commandQueue
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
        descriptor.colorAttachments[0].pixelFormat = view.colorPixelFormat
        pipeline = try device.makeRenderPipelineState(descriptor: descriptor)
        pixels = .allocate(capacity: Self.maximumPixels)
        super.init()
        view.delegate = self
    }

    /// Copies the latest managed frame and presents it without changing game state.
    func draw(in view: MTKView) {
        onFrame?()
        let sequence = atria_frame(pixels, Int32(Self.maximumPixels), &frameWidth, &frameHeight)
        if sequence > 0 && sequence != lastSequence {
            if texture?.width != Int(frameWidth) || texture?.height != Int(frameHeight) {
                let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm,
                    width: Int(frameWidth), height: Int(frameHeight), mipmapped: false)
                descriptor.storageMode = .shared
                descriptor.usage = .shaderRead
                texture = view.device?.makeTexture(descriptor: descriptor)
            }
            updateContentViewport()
            if lastSequence == 0 { onFirstFrame?(frameWidth, frameHeight) }
            texture?.replace(region: MTLRegionMake2D(0, 0, Int(frameWidth), Int(frameHeight)), mipmapLevel: 0,
                withBytes: pixels, bytesPerRow: Int(frameWidth) * 4)
            lastSequence = sequence
        }
        updateContentViewport()
        if let texture, let pass = view.currentRenderPassDescriptor, let drawable = view.currentDrawable,
           let command = queue.makeCommandBuffer(), let encoder = command.makeRenderCommandEncoder(descriptor: pass) {
            let viewport = drawableViewport
            encoder.setViewport(MTLViewport(originX: viewport.x, originY: viewport.y,
                width: viewport.width, height: viewport.height, znear: 0, zfar: 1))
            encoder.setRenderPipelineState(pipeline)
            encoder.setFragmentTexture(texture, index: 0)
            encoder.drawPrimitives(type: .triangleStrip, vertexStart: 0, vertexCount: 4)
            encoder.endEncoding()
            command.present(drawable)
            command.commit()
            // Reuse the shared texture only after Metal has finished sampling it.
            command.waitUntilCompleted()
            if command.status == .error { onError?("Metal command failed: \(String(describing: command.error))") }
        }
    }

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) { updateContentViewport(drawableSize: size) }

    /// Recomputes after drawable, frame, or source-texture changes; callbacks run only on geometry changes.
    func updateContentViewport(drawableSize: CGSize? = nil) {
        let drawable = drawableSize ?? view.drawableSize
        let sourceWidth = texture.map { Double($0.width) } ?? 0
        let sourceHeight = texture.map { Double($0.height) } ?? 0
        drawableViewport = RenderedViewportGeometry.fit(containerWidth: Double(drawable.width),
            containerHeight: Double(drawable.height), contentWidth: sourceWidth, contentHeight: sourceHeight)
        let scaleX = drawable.width > 0 ? view.bounds.width / drawable.width : 0
        let scaleY = drawable.height > 0 ? view.bounds.height / drawable.height : 0
        let next = CGRect(x: CGFloat(drawableViewport.x) * scaleX, y: CGFloat(drawableViewport.y) * scaleY,
            width: CGFloat(drawableViewport.width) * scaleX, height: CGFloat(drawableViewport.height) * scaleY)
        guard next != contentViewport else { return }
        contentViewport = next
        onContentViewportChanged?(next)
    }

    isolated deinit { pixels.deallocate() }
}
