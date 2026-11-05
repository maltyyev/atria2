import Foundation

enum MobileOverlayKind: Int, Decodable {
    case none = 0
    case navigation = 1
    case combat = 2
    case combatSelection = 3
    case experienceAllocation = 4
}

enum MobileUiAction: Int32 {
    case activate = 0
    case adjust = 1
    case select = 2
    case back = 3
    case confirmYes = 4
    case confirmNo = 5
    case dismissNotice = 6
    case submitText = 7
    case cancelText = 8
    case openProfiles = 9
    case cycleLanguage = 10
}

enum MobileMenuRowKind: Int, Decodable {
    case action = 0
    case selector = 1
    case binding = 2
    case slot = 3
    case profile = 4
}

enum MobileMenuModalKind: Int, Decodable {
    case none = 0
    case confirmation = 1
    case notice = 2
}

enum MobileMenuBadgeKind: Int, Decodable {
    case none = 0
    case manual = 1
    case automatic = 2
}

struct MobileTouchButtons: OptionSet {
    let rawValue: UInt16
    static let up = Self(rawValue: 0x0001)
    static let down = Self(rawValue: 0x0002)
    static let left = Self(rawValue: 0x0004)
    static let right = Self(rawValue: 0x0008)
    static let a = Self(rawValue: 0x0010)
    static let b = Self(rawValue: 0x0020)
    static let x = Self(rawValue: 0x0040)
}

struct MobileMenuOption: Decodable, Identifiable {
    let id: String
    let label: String
    let selected: Bool
}

struct MobileMenuRow: Decodable, Identifiable {
    let id: String
    let label: String
    let value: String
    let kind: MobileMenuRowKind
    let enabled: Bool
    let selected: Bool
    let options: [MobileMenuOption]?
    let badge: MobileMenuBadgeKind
    let badgeLabel: String?
}

struct MobileMenuModal: Decodable {
    let kind: MobileMenuModalKind
    let title: String
    let message: String
    let confirmLabel: String
    let cancelLabel: String
    let choice: Bool?
}

struct MobileMenuSnapshot: Decodable {
    let scene: String
    let title: String
    let rows: [MobileMenuRow]
    let notice: String?
    let modal: MobileMenuModal?
    let footerHint: String?
    let autoScrollRowId: String?
    let capturing: Bool
    let captureRowId: String?
}

struct MobileTextRequest: Decodable {
    let id: String
    let title: String
    let initialValue: String
    let placeholder: String
    let submitLabel: String
    let cancelLabel: String
}

struct MobileTouchLabels: Decodable {
    let up: String
    let down: String
    let left: String
    let right: String
    let confirm: String
    let back: String
    let attack: String
    let jump: String
    let modifier: String
    let language: String
    let movement: String
    let profiles: String
}

struct MobileUiSnapshot: Decodable {
    let contextId: UInt64
    let revision: UInt64
    let scene: String
    let overlayKind: MobileOverlayKind
    let showLanguageButton: Bool
    let labels: MobileTouchLabels
    let menu: MobileMenuSnapshot?
    let textRequest: MobileTextRequest?
    let showProfilesButton: Bool
    let notice: String?
}
