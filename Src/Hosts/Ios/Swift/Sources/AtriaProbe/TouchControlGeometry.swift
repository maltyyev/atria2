import Foundation

/// Normalized game-axis vector for a drag inside the analog-stick bounds.
struct TouchStickVector: Equatable {
    let x: Double
    let y: Double
}

struct TouchStickMetrics: Equatable {
    let diameter: CGFloat
    let knobDiameter: CGFloat
    let edgeInset: CGFloat
    let travelRadius: CGFloat
}

struct TouchDirectionPadFrames: Equatable {
    let up: CGRect
    let left: CGRect
    let down: CGRect
    let right: CGRect
}

struct TouchFaceButtonFrames: Equatable {
    let attack: CGRect
    let modifier: CGRect
    let jump: CGRect
}

struct TouchOverlayLayout: Equatable {
    let viewport: CGRect
    let safeArea: CGRect
    let directionPad: CGRect?
    let stick: CGRect?
    let navigationButtons: CGRect?
    let combatButtons: CGRect?
    let languageButton: CGRect?
}

enum TouchOverlayMode {
    case none, navigation, combat
}

enum TouchControlMetrics {
    static let buttonSide: CGFloat = 42
    static let buttonGap: CGFloat = 6
    static let gutterMargin: CGFloat = 8
    static let stickMaximumDiameter: CGFloat = 120
    static let stickMinimumDiameter: CGFloat = 56
    static let stickReferenceDiameter: CGFloat = 144
    static let stickReferenceKnobDiameter: CGFloat = 44
    static let stickReferenceEdgeInset: CGFloat = 8
}

/// Converts UIKit touch positions to raw signed axes without applying game thresholds.
enum TouchControlGeometry {
    static var directionPadSize: CGSize {
        CGSize(width: TouchControlMetrics.buttonSide * 3 + TouchControlMetrics.buttonGap * 2,
               height: TouchControlMetrics.buttonSide * 2 + TouchControlMetrics.buttonGap)
    }

    static func directionPadFrames(in bounds: CGRect) -> TouchDirectionPadFrames {
        let side = TouchControlMetrics.buttonSide
        let gap = TouchControlMetrics.buttonGap
        let originX = bounds.minX + (bounds.width - directionPadSize.width) / 2
        let originY = bounds.minY + (bounds.height - directionPadSize.height) / 2
        return TouchDirectionPadFrames(
            up: CGRect(x: originX + side + gap, y: originY, width: side, height: side),
            left: CGRect(x: originX, y: originY + side + gap, width: side, height: side),
            down: CGRect(x: originX + side + gap, y: originY + side + gap, width: side, height: side),
            right: CGRect(x: originX + (side + gap) * 2, y: originY + side + gap, width: side, height: side))
    }

    static func faceButtonFrames(in bounds: CGRect) -> TouchFaceButtonFrames {
        let side = TouchControlMetrics.buttonSide
        let gap = TouchControlMetrics.buttonGap
        let topY = bounds.minY
        let bottomY = topY + side + gap
        return TouchFaceButtonFrames(
            attack: CGRect(x: bounds.minX, y: topY, width: side, height: side),
            modifier: CGRect(x: bounds.maxX - side, y: topY, width: side, height: side),
            jump: CGRect(x: bounds.midX - side / 2, y: bottomY, width: side, height: side))
    }

    static func stickMetrics(diameter: CGFloat) -> TouchStickMetrics {
        let knob = diameter * TouchControlMetrics.stickReferenceKnobDiameter / TouchControlMetrics.stickReferenceDiameter
        let inset = diameter * TouchControlMetrics.stickReferenceEdgeInset / TouchControlMetrics.stickReferenceDiameter
        return TouchStickMetrics(diameter: diameter, knobDiameter: knob, edgeInset: inset,
            travelRadius: max(0, diameter / 2 - knob / 2 - inset))
    }

    static func leftGutter(viewport: CGRect, safeArea: CGRect) -> CGRect {
        CGRect(x: safeArea.minX, y: safeArea.minY,
            width: max(0, min(safeArea.maxX, viewport.minX) - safeArea.minX), height: safeArea.height)
    }

    static func rightGutter(viewport: CGRect, safeArea: CGRect) -> CGRect {
        CGRect(x: max(safeArea.minX, min(safeArea.maxX, viewport.maxX)), y: safeArea.minY,
            width: max(0, safeArea.maxX - max(safeArea.minX, viewport.maxX)), height: safeArea.height)
    }

    static func overlayLayout(viewport: CGRect, screenBounds: CGRect, safeArea: CGRect, overlayKind: TouchOverlayMode,
                              showLanguageButton: Bool, languageSize: CGSize) -> TouchOverlayLayout {
        guard viewport.width > 0, viewport.height > 0, screenBounds.width > 0,
              safeArea.width > 0, safeArea.height > 0 else {
            return TouchOverlayLayout(viewport: viewport, safeArea: safeArea, directionPad: nil, stick: nil,
                navigationButtons: nil, combatButtons: nil, languageButton: nil)
        }
        let margin = TouchControlMetrics.gutterMargin
        let buttonSide = TouchControlMetrics.buttonSide
        let lowerArea = CGRect(x: screenBounds.minX, y: safeArea.minY,
                               width: screenBounds.width, height: safeArea.height)
        let left = leftGutter(viewport: viewport, safeArea: lowerArea)
        let right = rightGutter(viewport: viewport, safeArea: lowerArea)
        let languageGutter = rightGutter(viewport: viewport, safeArea: safeArea)
        let usableHeight = safeArea.height - margin * 2
        let dpadSize = directionPadSize
        let canPlaceLeftControls = left.width - margin * 2 >= dpadSize.width && usableHeight >= dpadSize.height
        let canPlaceRightButtons = right.width - margin * 2 >= directionPadSize.width && usableHeight >= directionPadSize.height
        let directionPad = overlayKind == .navigation && canPlaceLeftControls
            ? CGRect(x: left.maxX - margin - dpadSize.width, y: safeArea.maxY - margin - dpadSize.height,
                     width: dpadSize.width, height: dpadSize.height) : nil
        let stickDiameter = min(TouchControlMetrics.stickMaximumDiameter,
            min(left.width - margin * 2, usableHeight))
        let stickFrame = overlayKind == .combat && stickDiameter >= TouchControlMetrics.stickMinimumDiameter
            ? CGRect(x: left.maxX - margin - stickDiameter, y: safeArea.maxY - margin - stickDiameter,
                     width: stickDiameter, height: stickDiameter) : nil
        let navigationFrame = overlayKind == .navigation && canPlaceRightButtons
            ? CGRect(x: right.maxX - margin - directionPadSize.width,
                     y: safeArea.maxY - margin - directionPadSize.height, width: directionPadSize.width,
                     height: directionPadSize.height) : nil
        let combatFrame = overlayKind == .combat && canPlaceRightButtons
            ? CGRect(x: right.maxX - margin - directionPadSize.width,
                     y: safeArea.maxY - margin - directionPadSize.height, width: directionPadSize.width,
                     height: directionPadSize.height) : nil
        let languageMaxWidth = languageGutter.width - margin * 2
        let languageHeight = min(max(buttonSide, languageSize.height), usableHeight)
        let languageWidth = min(languageMaxWidth, max(buttonSide, languageSize.width))
        var languageFrame: CGRect? = nil
        if showLanguageButton, languageWidth >= buttonSide, languageHeight >= buttonSide {
            let proposed = CGRect(x: languageGutter.maxX - margin - languageWidth, y: safeArea.minY + margin,
                                  width: languageWidth, height: languageHeight)
            if combatFrame.map({ !$0.intersects(proposed) }) ?? true,
               navigationFrame.map({ !$0.intersects(proposed) }) ?? true {
                languageFrame = proposed
            }
        }
        return TouchOverlayLayout(viewport: viewport, safeArea: safeArea, directionPad: directionPad,
            stick: stickFrame, navigationButtons: navigationFrame, combatButtons: combatFrame,
            languageButton: languageFrame)
    }

    static func normalizedStick(dx: Double, dy: Double, radius: Double) -> TouchStickVector {
        guard radius > 0 else { return TouchStickVector(x: 0, y: 0) }
        var x = dx / radius
        var y = -dy / radius
        let length = hypot(x, y)
        if length > 1 {
            x /= length
            y /= length
        }
        return TouchStickVector(x: x, y: y)
    }

    static func axis(_ value: Double) -> Int16 {
        Int16((min(1, max(-1, value)) * 32767).rounded())
    }

    static func sample(_ vector: TouchStickVector) -> (x: Int16, y: Int16) {
        (axis(vector.x), axis(vector.y))
    }
}
