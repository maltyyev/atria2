import Foundation

/// Aspect-fits a source frame into a drawable; renderer and touch layout share this exact rect.
struct RenderedViewportRect: Equatable {
    let x: Double
    let y: Double
    let width: Double
    let height: Double

    static let zero = RenderedViewportRect(x: 0, y: 0, width: 0, height: 0)
    var cgRect: CGRect { CGRect(x: x, y: y, width: width, height: height) }

}

enum RenderedViewportGeometry {
    static func fit(containerWidth: Double, containerHeight: Double,
                    contentWidth: Double, contentHeight: Double) -> RenderedViewportRect {
        guard containerWidth > 0, containerHeight > 0, contentWidth > 0, contentHeight > 0 else { return .zero }
        let scale = min(containerWidth / contentWidth, containerHeight / contentHeight)
        let width = contentWidth * scale
        let height = contentHeight * scale
        return RenderedViewportRect(x: (containerWidth - width) / 2,
            y: (containerHeight - height) / 2, width: width, height: height)
    }
}
