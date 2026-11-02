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
    var onError: ((String) -> Void)?
    var onFirstFrame: ((Int32, Int32) -> Void)?
    var onFrame: (() -> Void)?

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
            if lastSequence == 0 { onFirstFrame?(frameWidth, frameHeight) }
            if texture?.width != Int(frameWidth) || texture?.height != Int(frameHeight) {
                let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm,
                    width: Int(frameWidth), height: Int(frameHeight), mipmapped: false)
                descriptor.storageMode = .shared
                descriptor.usage = .shaderRead
                texture = view.device?.makeTexture(descriptor: descriptor)
            }
            texture?.replace(region: MTLRegionMake2D(0, 0, Int(frameWidth), Int(frameHeight)), mipmapLevel: 0,
                withBytes: pixels, bytesPerRow: Int(frameWidth) * 4)
            lastSequence = sequence
        }
        if let texture, let pass = view.currentRenderPassDescriptor, let drawable = view.currentDrawable,
           let command = queue.makeCommandBuffer(), let encoder = command.makeRenderCommandEncoder(descriptor: pass) {
            let scale = min(view.drawableSize.width / Double(texture.width), view.drawableSize.height / Double(texture.height))
            let width = Double(texture.width) * scale, height = Double(texture.height) * scale
            encoder.setViewport(MTLViewport(originX: (view.drawableSize.width - width) / 2,
                originY: (view.drawableSize.height - height) / 2, width: width, height: height, znear: 0, zfar: 1))
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

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) { }

    isolated deinit { pixels.deallocate() }
}
