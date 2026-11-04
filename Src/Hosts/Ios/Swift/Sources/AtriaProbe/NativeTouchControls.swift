import UIKit

enum MobileTouchDirection {
    case up, left, down, right
}

struct MobileTouchDirectionLabels {
    let up: String
    let left: String
    let down: String
    let right: String
}

/// A rounded opaque touch button that sends one tap or a balanced hold/release pair.
@MainActor final class NativeTouchButton: UIControl {
    private let titleLabel = UILabel()
    private let holds: Bool
    private let compact: Bool
    private var isHolding = false
    private var contactActive = false
    private var ignoreTap = false
    var onTap: (() -> Void)?
    var onTouchBegan: (() -> Void)?
    var onHoldChanged: ((Bool) -> Void)?

    init(title: String, accessibilityLabel: String, holds: Bool = false, compact: Bool = false) {
        self.holds = holds
        self.compact = compact
        super.init(frame: .zero)
        isMultipleTouchEnabled = true
        isAccessibilityElement = true
        isOpaque = true
        accessibilityTraits = .button
        self.accessibilityLabel = accessibilityLabel
        backgroundColor = TouchControlColor.surface
        layer.borderColor = TouchControlColor.edge.cgColor
        layer.borderWidth = compact ? 1.5 : 2
        layer.cornerRadius = compact ? 10 : 12

        titleLabel.text = title
        titleLabel.textColor = TouchControlColor.text
        titleLabel.textAlignment = .center
        titleLabel.numberOfLines = compact ? 1 : 0
        titleLabel.font = UIFontMetrics(forTextStyle: compact ? .body : .headline)
            .scaledFont(for: .systemFont(ofSize: 17, weight: .semibold))
        titleLabel.adjustsFontForContentSizeCategory = true
        titleLabel.adjustsFontSizeToFitWidth = compact
        titleLabel.minimumScaleFactor = 0.7
        titleLabel.isUserInteractionEnabled = false
        addSubview(titleLabel)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    override var intrinsicContentSize: CGSize {
        let title = titleLabel.intrinsicContentSize
        let minimum: CGFloat = compact ? TouchControlMetrics.buttonSide : 56
        let horizontalInset: CGFloat = compact ? 8 : 24
        let verticalInset: CGFloat = compact ? 8 : 16
        return CGSize(width: max(minimum, ceil(title.width) + horizontalInset),
                      height: max(minimum, ceil(title.height) + verticalInset))
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        titleLabel.frame = bounds.insetBy(dx: compact ? 2 : 3, dy: 2)
    }

    override func beginTracking(_ touch: UITouch, with event: UIEvent?) -> Bool {
        guard super.beginTracking(touch, with: event) else { return false }
        contactActive = true
        ignoreTap = false
        onTouchBegan?()
        setPressed(true)
        if holds {
            isHolding = true
            onHoldChanged?(true)
        }
        return true
    }

    override func endTracking(_ touch: UITouch?, with event: UIEvent?) {
        if holds {
            releaseHold()
        } else if !ignoreTap, let touch, bounds.contains(touch.location(in: self)) {
            onTap?()
        }
        contactActive = false
        ignoreTap = false
        setPressed(false)
        super.endTracking(touch, with: event)
    }

    override func cancelTracking(with event: UIEvent?) {
        contactActive = false
        ignoreTap = true
        releaseHold()
        setPressed(false)
        super.cancelTracking(with: event)
    }

    func reset() {
        if contactActive { ignoreTap = true }
        releaseHold()
        setPressed(false)
    }

    override func accessibilityActivate() -> Bool {
        onTouchBegan?()
        if holds {
            guard !isHolding else { return false }
            isHolding = true
            onHoldChanged?(true)
            releaseHold()
        } else {
            onTap?()
        }
        return true
    }

    func updateAccessibilityLabel(_ value: String) { accessibilityLabel = value }

    func updateTitle(_ value: String, accessibilityLabel: String) {
        titleLabel.text = value
        self.accessibilityLabel = accessibilityLabel
        invalidateIntrinsicContentSize()
        setNeedsLayout()
    }

    func fittingHeight(forWidth width: CGFloat) -> CGFloat {
        let horizontalInset: CGFloat = compact ? 8 : 6
        let verticalInset: CGFloat = compact ? 8 : 16
        let minimum: CGFloat = compact ? TouchControlMetrics.buttonSide : 56
        let labelSize = titleLabel.sizeThatFits(CGSize(width: max(1, width - horizontalInset), height: .greatestFiniteMagnitude))
        return max(minimum, ceil(labelSize.height) + verticalInset)
    }

    private func releaseHold() {
        guard isHolding else { return }
        isHolding = false
        onHoldChanged?(false)
    }

    private func setPressed(_ pressed: Bool) {
        backgroundColor = pressed ? TouchControlColor.pressed : TouchControlColor.surface
        layer.borderColor = pressed ? TouchControlColor.accent.cgColor : TouchControlColor.edge.cgColor
    }
}

/// An inverted-T held-state control whose edges are interpreted by Core.
@MainActor final class NativeTouchDirectionPad: UIView {
    private let up: NativeTouchButton
    private let left: NativeTouchButton
    private let down: NativeTouchButton
    private let right: NativeTouchButton
    var onTouchBegan: (() -> Void)? {
        didSet { bindActions() }
    }
    var onDirectionChanged: ((MobileTouchDirection, Bool) -> Void)? {
        didSet { bindActions() }
    }

    init(labels: MobileTouchDirectionLabels) {
        up = NativeTouchButton(title: "↑", accessibilityLabel: labels.up, holds: true, compact: true)
        left = NativeTouchButton(title: "←", accessibilityLabel: labels.left, holds: true, compact: true)
        down = NativeTouchButton(title: "↓", accessibilityLabel: labels.down, holds: true, compact: true)
        right = NativeTouchButton(title: "→", accessibilityLabel: labels.right, holds: true, compact: true)
        super.init(frame: .zero)
        isAccessibilityElement = false
        for button in [up, left, down, right] { addSubview(button) }
        bindActions()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    override var intrinsicContentSize: CGSize { TouchControlGeometry.directionPadSize }

    override func layoutSubviews() {
        super.layoutSubviews()
        let frames = TouchControlGeometry.directionPadFrames(in: bounds)
        up.frame = frames.up
        left.frame = frames.left
        down.frame = frames.down
        right.frame = frames.right
    }

    func reset() { [up, left, down, right].forEach { $0.reset() } }

    func updateAccessibilityLabels(_ labels: MobileTouchDirectionLabels) {
        up.updateAccessibilityLabel(labels.up)
        left.updateAccessibilityLabel(labels.left)
        down.updateAccessibilityLabel(labels.down)
        right.updateAccessibilityLabel(labels.right)
    }

    private func bindActions() {
        [up, left, down, right].forEach { $0.onTouchBegan = { [weak self] in self?.onTouchBegan?() } }
        up.onHoldChanged = { [weak self] down in self?.onDirectionChanged?(.up, down) }
        left.onHoldChanged = { [weak self] down in self?.onDirectionChanged?(.left, down) }
        down.onHoldChanged = { [weak self] down in self?.onDirectionChanged?(.down, down) }
        right.onHoldChanged = { [weak self] down in self?.onDirectionChanged?(.right, down) }
    }
}

/// Sends raw normalized stick axes; deadzone and run thresholds stay in Core.
@MainActor final class NativeTouchStick: UIView {
    private let knob = UIView()
    private var trackedTouch: UITouch?
    private var lastSample: (x: Int16, y: Int16) = (0, 0)
    private var touchActivityReported = false
    var onChange: ((Int16, Int16) -> Void)?
    var onTouchBegan: (() -> Void)?

    init(frame: CGRect = .zero, accessibilityLabel: String) {
        super.init(frame: frame)
        isMultipleTouchEnabled = true
        isOpaque = true
        backgroundColor = TouchControlColor.surface
        layer.cornerRadius = 72
        layer.borderColor = TouchControlColor.edge.cgColor
        layer.borderWidth = 2
        self.accessibilityLabel = accessibilityLabel
        accessibilityTraits = .allowsDirectInteraction
        isAccessibilityElement = true

        knob.isUserInteractionEnabled = false
        knob.isOpaque = true
        knob.backgroundColor = TouchControlColor.accent
        knob.layer.cornerRadius = 22
        knob.layer.borderWidth = 2
        knob.layer.borderColor = TouchControlColor.text.cgColor
        addSubview(knob)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    override var intrinsicContentSize: CGSize {
        CGSize(width: TouchControlMetrics.stickReferenceDiameter, height: TouchControlMetrics.stickReferenceDiameter)
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        layer.cornerRadius = min(bounds.width, bounds.height) / 2
        let metrics = TouchControlGeometry.stickMetrics(diameter: min(bounds.width, bounds.height))
        knob.bounds = CGRect(x: 0, y: 0, width: metrics.knobDiameter, height: metrics.knobDiameter)
        knob.layer.cornerRadius = metrics.knobDiameter / 2
        knob.layer.borderWidth = metrics.knobDiameter * 2 / TouchControlMetrics.stickReferenceKnobDiameter
        if trackedTouch == nil { knob.center = CGPoint(x: bounds.midX, y: bounds.midY) }
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard trackedTouch == nil, let touch = touches.first else { return }
        trackedTouch = touch
        update(touch)
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let trackedTouch, touches.contains(where: { $0 === trackedTouch }) else { return }
        update(trackedTouch)
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let trackedTouch, touches.contains(where: { $0 === trackedTouch }) else { return }
        reset()
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let trackedTouch, touches.contains(where: { $0 === trackedTouch }) else { return }
        reset()
    }

    func reset() {
        trackedTouch = nil
        touchActivityReported = false
        knob.center = CGPoint(x: bounds.midX, y: bounds.midY)
        publish((0, 0))
    }

    func updateAccessibilityLabel(_ value: String) { accessibilityLabel = value }

    private func update(_ touch: UITouch) {
        let point = touch.location(in: self)
        let radius = max(1, TouchControlGeometry.stickMetrics(diameter: min(bounds.width, bounds.height)).travelRadius)
        let vector = TouchControlGeometry.normalizedStick(dx: Double(point.x - bounds.midX),
            dy: Double(point.y - bounds.midY), radius: Double(radius))
        let sample = TouchControlGeometry.sample(vector)
        if !touchActivityReported && (sample.x != 0 || sample.y != 0) {
            touchActivityReported = true
            onTouchBegan?()
        }
        knob.center = CGPoint(x: bounds.midX + CGFloat(vector.x) * radius,
            y: bounds.midY - CGFloat(vector.y) * radius)
        publish(sample)
    }

    private func publish(_ sample: (x: Int16, y: Int16)) {
        guard sample.x != lastSample.x || sample.y != lastSample.y else { return }
        lastSample = sample
        onChange?(sample.x, sample.y)
    }
}

private enum TouchControlColor {
    static let surface = UIColor(red: 0.20, green: 0.16, blue: 0.12, alpha: 1)
    static let pressed = UIColor(red: 0.34, green: 0.24, blue: 0.15, alpha: 1)
    static let edge = UIColor(red: 0.59, green: 0.42, blue: 0.25, alpha: 1)
    static let accent = UIColor(red: 0.73, green: 0.53, blue: 0.32, alpha: 1)
    static let text = UIColor(red: 1, green: 0.95, blue: 0.86, alpha: 1)
}
