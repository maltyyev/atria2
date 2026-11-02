import Foundation
import GameController

/// Shared button bits match the managed gamepad contract for physical and touch controls.
enum PadButton {
    static let up: UInt16 = 0x0001
    static let down: UInt16 = 0x0002
    static let left: UInt16 = 0x0004
    static let right: UInt16 = 0x0008
    static let menu: UInt16 = 0x0010
    static let options: UInt16 = 0x0020
    static let leftThumb: UInt16 = 0x0040
    static let rightThumb: UInt16 = 0x0080
    static let leftShoulder: UInt16 = 0x0100
    static let rightShoulder: UInt16 = 0x0200
    static let a: UInt16 = 0x1000
    static let b: UInt16 = 0x2000
    static let x: UInt16 = 0x4000
    static let y: UInt16 = 0x8000
}

/// Contains one controller's identity, complete controls, and physical slot.
struct PhysicalPadSample {
    let slot: Int32
    let connected: Int32
    let buttons: UInt16
    let leftTrigger: UInt8
    let rightTrigger: UInt8
    let lx: Int16
    let ly: Int16
    let rx: Int16
    let ry: Int16
}

/// Maps connected controller identities to stable shared gamepad slots.
@MainActor final class ControllerInput {
    private var slotControllers = [GCController?](repeating: nil, count: 4)
    private var pendingDisconnect = [Bool](repeating: false, count: 4)
    private var rosterNeedsRefresh = false
    private var observers: [NSObjectProtocol] = []

    init() {
        refreshRoster()
        let center = NotificationCenter.default
        for name in [Notification.Name.GCControllerDidConnect, Notification.Name.GCControllerDidDisconnect] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.refreshRoster() }
            })
        }
    }

    /// Returns a complete sample while preserving the slot's disconnect transition.
    func sample(_ slot: Int) -> PhysicalPadSample {
        if rosterNeedsRefresh {
            rosterNeedsRefresh = false
            refreshRoster()
        }
        if pendingDisconnect[slot] {
            pendingDisconnect[slot] = false
            rosterNeedsRefresh = true
            return disconnected(slot)
        }
        guard let pad = slotControllers[slot]?.extendedGamepad else { return disconnected(slot) }
        var buttons: UInt16 = 0
        if pad.dpad.up.isPressed { buttons |= PadButton.up }
        if pad.dpad.down.isPressed { buttons |= PadButton.down }
        if pad.dpad.left.isPressed { buttons |= PadButton.left }
        if pad.dpad.right.isPressed { buttons |= PadButton.right }
        if pad.buttonMenu.isPressed { buttons |= PadButton.menu }
        if pad.buttonOptions?.isPressed == true { buttons |= PadButton.options }
        if pad.leftThumbstickButton?.isPressed == true { buttons |= PadButton.leftThumb }
        if pad.rightThumbstickButton?.isPressed == true { buttons |= PadButton.rightThumb }
        if pad.leftShoulder.isPressed { buttons |= PadButton.leftShoulder }
        if pad.rightShoulder.isPressed { buttons |= PadButton.rightShoulder }
        if pad.buttonA.isPressed { buttons |= PadButton.a }
        if pad.buttonB.isPressed { buttons |= PadButton.b }
        if pad.buttonX.isPressed { buttons |= PadButton.x }
        if pad.buttonY.isPressed { buttons |= PadButton.y }
        return PhysicalPadSample(slot: Int32(slot), connected: 1, buttons: buttons,
            leftTrigger: trigger(pad.leftTrigger.value), rightTrigger: trigger(pad.rightTrigger.value),
            lx: axis(pad.leftThumbstick.xAxis.value), ly: axis(pad.leftThumbstick.yAxis.value),
            rx: axis(pad.rightThumbstick.xAxis.value), ry: axis(pad.rightThumbstick.yAxis.value))
    }

    private func refreshRoster() {
        let controllers = GCController.controllers()
        let connected = Set(controllers.map(ObjectIdentifier.init))
        for slot in slotControllers.indices {
            if let controller = slotControllers[slot], !connected.contains(ObjectIdentifier(controller)) {
                slotControllers[slot] = nil
                pendingDisconnect[slot] = true
            }
        }
        for controller in controllers where !slotControllers.contains(where: { $0 === controller }) {
            let preferred = slot(controller.playerIndex)
            if let preferred, slotControllers[preferred] == nil, !pendingDisconnect[preferred] {
                slotControllers[preferred] = controller
            } else if let free = slotControllers.indices.first(where: { slotControllers[$0] == nil && !pendingDisconnect[$0] }) {
                slotControllers[free] = controller
            }
        }
    }

    private func slot(_ player: GCControllerPlayerIndex) -> Int? {
        switch player {
        case .index1: return 0
        case .index2: return 1
        case .index3: return 2
        case .index4: return 3
        case .indexUnset: return nil
        @unknown default: return nil
        }
    }

    private func disconnected(_ slot: Int) -> PhysicalPadSample {
        PhysicalPadSample(slot: Int32(slot), connected: 0, buttons: 0,
            leftTrigger: 0, rightTrigger: 0, lx: 0, ly: 0, rx: 0, ry: 0)
    }

    private func axis(_ value: Float) -> Int16 { Int16(max(-1, min(1, value)) * 32767) }
    private func trigger(_ value: Float) -> UInt8 { UInt8(max(0, min(1, value)) * 255) }

    isolated deinit {
        for observer in observers { NotificationCenter.default.removeObserver(observer) }
    }
}
