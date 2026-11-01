import Foundation
import CoreText
import CoreGraphics

@_cdecl("apple_text_create")
func textCreate(_ textPointer: UnsafePointer<CChar>?, _ familyPointer: UnsafePointer<CChar>?,
                _ size: Float, _ bold: Int32, _ wrap: Int32, _ metrics: UnsafeMutablePointer<Int32>?) -> UnsafeMutableRawPointer? {
    guard let textPointer, let familyPointer, let metrics,
          size.isFinite, size >= 1, size <= 128, wrap > 0, wrap <= 4096 else { return nil }
    let text = String(cString: textPointer)
    let family = String(cString: familyPointer)
    let fontName: String
    if family.contains("Palatino") { fontName = bold != 0 ? "Palatino-Bold" : "Palatino-Roman" }
    else if family.contains("Times") { fontName = bold != 0 ? "TimesNewRomanPS-BoldMT" : "TimesNewRomanPSMT" }
    else { fontName = bold != 0 ? "Helvetica-Bold" : "Helvetica" }
    let font = CTFontCreateWithName(fontName as CFString, CGFloat(size), nil)
    let attributes: [NSAttributedString.Key: Any] = [
        NSAttributedString.Key(kCTFontAttributeName as String): font,
        NSAttributedString.Key(kCTForegroundColorAttributeName as String): CGColor(gray: 1, alpha: 1)
    ]
    let string = NSAttributedString(string: text, attributes: attributes)
    let setter = CTFramesetterCreateWithAttributedString(string)
    let suggested = CTFramesetterSuggestFrameSizeWithConstraints(setter, CFRange(location: 0, length: 0), nil,
        CGSize(width: Int(wrap), height: 1024), nil)
    let padding = 4
    let advance = max(0, min(Int(wrap), Int(ceil(suggested.width))))
    let width = max(1, advance + padding * 2)
    let height = max(1, min(1024, Int(ceil(suggested.height))) + padding * 2)
    let data = UnsafeMutableRawPointer.allocate(byteCount: width * height, alignment: 16)
    data.initializeMemory(as: UInt8.self, repeating: 0, count: width * height)
    guard let context = CGContext(data: data, width: width, height: height, bitsPerComponent: 8,
        bytesPerRow: width, space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else {
        data.deallocate()
        return nil
    }
    let path = CGPath(rect: CGRect(x: padding, y: padding, width: Int(wrap), height: height - padding * 2), transform: nil)
    let frame = CTFramesetterCreateFrame(setter, CFRange(location: 0, length: 0), path, nil)
    CTFrameDraw(frame, context)
    metrics[0] = Int32(width)
    metrics[1] = Int32(height)
    metrics[2] = Int32(advance)
    metrics[3] = Int32(CFArrayGetCount(CTFrameGetLines(frame)))
    return data
}

@_cdecl("apple_text_free")
func textFree(_ pixels: UnsafeMutableRawPointer?) { pixels?.deallocate() }
