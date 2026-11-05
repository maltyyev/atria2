import Foundation
import NativeCore

/// Supplies deterministic native bridge calls for the production session coordinator.
private final class SessionBridge: @unchecked Sendable {
    static let shared = SessionBridge()
    private let lock = NSLock()
    private var starts = 0
    func start() -> Int32 { lock.withLock { starts += 1; return 1 } }
    var startCount: Int { lock.withLock { starts } }
}

@_cdecl("atria_start")
func testStart(_ resources: UnsafePointer<CChar>?, _ storage: UnsafePointer<CChar>?) -> Int32 {
    SessionBridge.shared.start()
}
@_cdecl("atria_suspend") func testSuspend() { }
@_cdecl("atria_resume") func testResume() { }
@_cdecl("atria_stop") func testStop() -> Int32 { 0 }
@_cdecl("atria_wait_stopped") func testWaitStopped() -> Int32 {
    Thread.sleep(forTimeInterval: 0.02)
    return 0
}

@main struct SessionChecks {
    @MainActor static func main() async {
        let centeredStick = TouchControlGeometry.normalizedStick(dx: 0, dy: 0, radius: 100)
        precondition(centeredStick == TouchStickVector(x: 0, y: 0))
        let upwardStick = TouchControlGeometry.sample(TouchControlGeometry.normalizedStick(dx: 0, dy: -50, radius: 100))
        precondition(upwardStick.x == 0 && upwardStick.y == 16384, "Touch stick up must map to positive game Y.")
        let cappedStick = TouchControlGeometry.normalizedStick(dx: 100, dy: -100, radius: 100)
        precondition(abs(hypot(cappedStick.x, cappedStick.y) - 1) < 0.000001,
            "Touch stick diagonals must stay inside the unit circle.")
        let zeroRadiusStick = TouchControlGeometry.sample(TouchControlGeometry.normalizedStick(dx: 1, dy: 1, radius: 0))
        precondition(zeroRadiusStick.x == 0 && zeroRadiusStick.y == 0,
            "A zero-size touch stick must stay neutral.")

        let fittedGameplay = RenderedViewportGeometry.fit(containerWidth: 852, containerHeight: 393,
            contentWidth: 640, contentHeight: 480).cgRect
        precondition(fittedGameplay == CGRect(x: 164, y: 0, width: 524, height: 393),
            "The renderer viewport must match the centered 4:3 drawable rect.")
        precondition(TouchControlGeometry.directionPadSize == CGSize(width: 138, height: 90) &&
                     TouchControlMetrics.buttonSide == 42 && TouchControlMetrics.buttonGap == 6,
            "Gameplay controls must keep the approved 42-point buttons and 6-point gaps.")
        precondition(TouchOverlayMode.combatSelection.rawValue == 3 &&
                     TouchOverlayMode.experienceAllocation.rawValue == 4,
            "New Swift overlay modes must match the appended Core contract IDs.")
        let fittedMenu = RenderedViewportGeometry.fit(containerWidth: 852, containerHeight: 393,
            contentWidth: 2560, contentHeight: 1440).cgRect
        precondition(abs(fittedMenu.width - 698.6666666667) < 0.001 && fittedMenu.minX > 76,
            "The main-menu aspect-fit viewport changed unexpectedly.")
        let safeAreas = [
            CGRect(x: 59, y: 0, width: 772, height: 393),
            CGRect(x: 21, y: 0, width: 772, height: 393),
            CGRect(x: 59, y: 0, width: 734, height: 372)
        ]
        let screenBounds = CGRect(x: 0, y: 0, width: 852, height: 393)
        for safeArea in safeAreas {
            let navigationLayout = TouchControlGeometry.overlayLayout(viewport: fittedGameplay,
                screenBounds: screenBounds, safeArea: safeArea, overlayKind: .navigation,
                showLanguageButton: true, languageSize: CGSize(width: 70, height: 42))
            let combatLayout = TouchControlGeometry.overlayLayout(viewport: fittedGameplay,
                screenBounds: screenBounds, safeArea: safeArea, overlayKind: .combat,
                showLanguageButton: true, languageSize: CGSize(width: 70, height: 42))
            let selectionLayout = TouchControlGeometry.overlayLayout(viewport: fittedGameplay,
                screenBounds: screenBounds, safeArea: safeArea, overlayKind: .combatSelection,
                showLanguageButton: true, languageSize: CGSize(width: 70, height: 42))
            let experienceLayout = TouchControlGeometry.overlayLayout(viewport: fittedGameplay,
                screenBounds: screenBounds, safeArea: safeArea, overlayKind: .experienceAllocation,
                showLanguageButton: true, languageSize: CGSize(width: 70, height: 42))
            precondition(navigationLayout.directionPad != nil && navigationLayout.navigationButtons != nil,
                "The 4:3 navigation controls must fit in both landscape safe-area orientations.")
            precondition(combatLayout.stick != nil && combatLayout.combatButtons != nil,
                "The 4:3 combat controls must fit in both landscape safe-area orientations.")
            precondition(combatLayout.stick?.width == 120,
                "The combat stick should use the available black gutter without approaching the notch.")
            precondition(selectionLayout.directionPad != nil && selectionLayout.combatButtons != nil &&
                         selectionLayout.navigationButtons == nil && selectionLayout.stick == nil &&
                         selectionLayout.languageButton == nil,
                "Pre-battle selection shows arrows and X/A only, with no stick or language button.")
            precondition(experienceLayout.directionPad != nil && experienceLayout.navigationButtons != nil &&
                         experienceLayout.combatButtons == nil && experienceLayout.stick == nil &&
                         experienceLayout.languageButton == nil,
                "Experience allocation shows arrows and A/B only, with no stick or language button.")
            precondition(navigationLayout.navigationButtons?.size == TouchControlGeometry.directionPadSize &&
                         combatLayout.combatButtons?.size == TouchControlGeometry.directionPadSize &&
                         selectionLayout.combatButtons?.size == TouchControlGeometry.directionPadSize,
                "Face buttons must fit the 138-by-90 gutter group.")
            let navigationFaces = TouchControlGeometry.faceButtonFrames(in: CGRect(origin: .zero,
                size: navigationLayout.navigationButtons!.size))
            let combatFaces = TouchControlGeometry.faceButtonFrames(in: CGRect(origin: .zero,
                size: combatLayout.combatButtons!.size))
            let selectionFaces = TouchControlGeometry.faceButtonFrames(in: CGRect(origin: .zero,
                size: selectionLayout.combatButtons!.size))
            precondition(navigationFaces.jump == combatFaces.jump && navigationFaces.modifier == combatFaces.modifier,
                "Navigation A/B must keep the same positions as combat A/B.")
            precondition(selectionFaces.attack == combatFaces.attack && selectionFaces.jump == combatFaces.jump &&
                         experienceLayout.navigationButtons?.origin == navigationLayout.navigationButtons?.origin,
                "Selection X/A and XP A/B must keep their agreed positions across scenes.")
            precondition(navigationFaces.jump.midX == navigationLayout.navigationButtons!.midX - navigationLayout.navigationButtons!.minX &&
                         navigationFaces.jump.minY > navigationFaces.modifier.minY &&
                         combatFaces.attack.minX < combatFaces.jump.minX &&
                         combatFaces.modifier.minX > combatFaces.jump.minX &&
                         combatFaces.modifier.minY == combatFaces.attack.minY,
                "Face buttons must place A centered below, B right/above, and combat X on the left.")
            let lowerFrames = [navigationLayout.directionPad, navigationLayout.navigationButtons,
                               combatLayout.stick, combatLayout.combatButtons].compactMap({ $0 })
            for frame in lowerFrames {
                precondition(frame.minX >= screenBounds.minX && frame.maxX <= screenBounds.maxX &&
                             frame.minY >= safeArea.minY && frame.maxY <= safeArea.maxY,
                    "A lower game control escaped screen or vertical safe-area bounds.")
                precondition(!frame.intersects(fittedGameplay), "A game control overlapped rendered game art.")
            }
            if let language = navigationLayout.languageButton {
                precondition(language.minX >= safeArea.minX && language.maxX <= safeArea.maxX &&
                             language.minY >= safeArea.minY && language.maxY <= safeArea.maxY &&
                             !language.intersects(fittedGameplay),
                    "The language control must remain within the full safe area and black gutter.")
            }
            precondition(navigationLayout.directionPad!.minX < safeArea.minX,
                "Lower controls should use the wider black gutter while avoiding the notch vertically.")
            precondition(TouchControlGeometry.visibleActionButtons(for: .combatSelection) == [.a, .x] &&
                         TouchControlGeometry.visibleActionButtons(for: .experienceAllocation) == [.a, .b] &&
                         TouchControlGeometry.visibleActionButtons(for: .combat) == [.a, .b, .x] &&
                         TouchControlGeometry.visibleActionButtons(for: .navigation) == [.a, .b],
                "Each scene must expose only its Core-defined face buttons.")
            let dpad = TouchControlGeometry.directionPadFrames(in: CGRect(origin: .zero,
                size: TouchControlGeometry.directionPadSize))
            precondition(dpad.left.minY == dpad.down.minY && dpad.down.minY == dpad.right.minY &&
                         dpad.up.midX == dpad.down.midX && dpad.up.maxY < dpad.down.minY,
                "D-pad arrows must keep the inverted-T arrangement.")
        }
        let narrowLayout = TouchControlGeometry.overlayLayout(viewport: fittedMenu,
            screenBounds: screenBounds, safeArea: safeAreas[2], overlayKind: .none,
            showLanguageButton: false, languageSize: CGSize(width: 70, height: 42))
        precondition(narrowLayout.directionPad == nil && narrowLayout.stick == nil &&
                     narrowLayout.navigationButtons == nil && narrowLayout.combatButtons == nil &&
                     narrowLayout.languageButton == nil,
            "A 16:9 main-menu scene must keep gameplay controls hidden.")
        let zeroLayout = TouchControlGeometry.overlayLayout(viewport: .zero, screenBounds: screenBounds,
            safeArea: safeAreas[0], overlayKind: .navigation, showLanguageButton: true,
            languageSize: CGSize(width: 70, height: 42))
        precondition(zeroLayout.directionPad == nil && zeroLayout.navigationButtons == nil &&
                     zeroLayout.languageButton == nil,
            "Controls must stay hidden until renderer geometry is valid.")
        let stickMetrics = TouchControlGeometry.stickMetrics(diameter: 72)
        precondition(abs(stickMetrics.knobDiameter - 22) < 0.0001 && stickMetrics.travelRadius < 36,
            "Stick knob and travel must scale with its available gutter size.")

        let session = GameHostSession()
        let first = UUID(), second = UUID(), cancelled = UUID()
        let initial = await session.start(owner: first, resources: "assets", storage: "output")
        let shared = await session.start(owner: second, resources: "assets", storage: "output")
        precondition(initial == 1 && shared == 0 && SessionBridge.shared.startCount == 1)
        precondition(!session.stop(owner: first), "One view stopped another view's session.")
        precondition(session.stop(owner: second))
        var entered = false
        let pending = Task { @MainActor in
            entered = true
            return await session.start(owner: cancelled, resources: "assets", storage: "output")
        }
        while !entered { await Task.yield() }
        pending.cancel()
        _ = session.stop(owner: cancelled)
        let cancelledResult = await pending.value
        precondition(cancelledResult == -1 && SessionBridge.shared.startCount == 1,
            "A cancelled view started a game after it had been dismantled.")
        let restarted = await session.start(owner: first, resources: "assets", storage: "output")
        precondition(restarted == 1 && SessionBridge.shared.startCount == 2)
        precondition(session.stop(owner: first))
        let firstRestart = Task { @MainActor in
            await session.start(owner: first, resources: "assets", storage: "output")
        }
        let secondRestart = Task { @MainActor in
            await session.start(owner: second, resources: "assets", storage: "output")
        }
        let results = await [firstRestart.value, secondRestart.value]
        precondition(results.sorted() == [0, 1] && SessionBridge.shared.startCount == 3,
            "Concurrent views created duplicate workers while awaiting the same stop.")
        precondition(!session.stop(owner: first) && session.stop(owner: second))
        print("Swift session checks PASS: shared leases, asynchronous stop, cancelled start and restart.")
    }
}
