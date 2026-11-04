import SwiftUI

/// Renders Core's menu snapshot and submits only its semantic actions.
@MainActor struct NativeMenuPanel: View {
    let snapshot: MobileUiSnapshot
    let send: (UInt64, MobileUiAction, String?, Int32, String?) -> Void
    @State private var textValue: String
    @FocusState private var textFieldFocused: Bool

    init(snapshot: MobileUiSnapshot, send: @escaping (UInt64, MobileUiAction, String?, Int32, String?) -> Void) {
        self.snapshot = snapshot
        self.send = send
        _textValue = State(initialValue: snapshot.textRequest?.initialValue ?? "")
    }

    var body: some View {
        ZStack {
            NativePanelPalette.background.ignoresSafeArea()
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment: .leading, spacing: 16) {
                        if let menu = snapshot.menu {
                            HStack(alignment: .top, spacing: 12) {
                                if menu.modal == nil && snapshot.textRequest == nil {
                                    Button {
                                        command(.back)
                                    } label: {
                                        Label(snapshot.labels.back, systemImage: "chevron.backward")
                                            .fixedSize(horizontal: false, vertical: true)
                                    }
                                    .buttonStyle(OpaqueMenuButtonStyle())
                                    .fixedSize(horizontal: true, vertical: false)
                                }

                                Text(menu.title)
                                    .font(.largeTitle.weight(.semibold))
                                    .foregroundStyle(NativePanelPalette.text)
                                    .fixedSize(horizontal: false, vertical: true)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                            }

                            if let modal = menu.modal, modal.kind == .confirmation || modal.kind == .notice {
                                modalContent(modal)
                            } else {
                                if let notice = menu.notice, !notice.isEmpty {
                                    Text(notice)
                                        .font(.body)
                                        .foregroundStyle(NativePanelPalette.text)
                                        .fixedSize(horizontal: false, vertical: true)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                        .padding(14)
                                        .background(NativePanelPalette.surface, in: RoundedRectangle(cornerRadius: 12))
                                }
                                ForEach(menu.rows) { row in
                                    menuRow(row).id(row.id)
                                }
                                if let hint = menu.footerHint, !hint.isEmpty {
                                    Text(hint)
                                        .font(.footnote)
                                        .foregroundStyle(NativePanelPalette.secondaryText)
                                        .fixedSize(horizontal: false, vertical: true)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                }
                            }

                            if let request = snapshot.textRequest {
                                textEntry(request)
                            }
                        } else if let request = snapshot.textRequest {
                            Text(request.title)
                                .font(.largeTitle.weight(.semibold))
                                .foregroundStyle(NativePanelPalette.text)
                                .fixedSize(horizontal: false, vertical: true)
                            textEntry(request)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 22)
                    .padding(.vertical, 18)
                }
                .onChange(of: snapshot.menu?.autoScrollRowId) { _, rowId in
                    guard let rowId,
                          snapshot.menu?.rows.contains(where: { $0.id == rowId }) == true else { return }
                    withAnimation(.easeInOut(duration: 0.2)) {
                        proxy.scrollTo(rowId, anchor: .center)
                    }
                }
            }
        }
        .onAppear {
            if let request = snapshot.textRequest {
                textValue = request.initialValue
                textFieldFocused = true
            }
        }
        .onChange(of: snapshot.textRequest?.id) { _, id in
            guard id != nil, let request = snapshot.textRequest else {
                textFieldFocused = false
                textValue = ""
                return
            }
            textValue = request.initialValue
            textFieldFocused = true
        }
        .onChange(of: snapshot.contextId) { _, _ in
            guard let request = snapshot.textRequest else { return }
            textValue = request.initialValue
            textFieldFocused = true
        }
    }

    @ViewBuilder private func menuRow(_ row: MobileMenuRow) -> some View {
        switch row.kind {
        case .selector:
            HStack(spacing: 8) {
                rowText(row)
                    .frame(maxWidth: .infinity, alignment: .leading)
                adjustButton("−", row: row, value: -1)
                adjustButton("+", row: row, value: 1)
            }
            .padding(8)
            .background(row.selected ? NativePanelPalette.selectedSurface : NativePanelPalette.surface,
                in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(NativePanelPalette.edge, lineWidth: 2))
        case .binding:
            HStack(spacing: 8) {
                rowButton(row, action: .activate)
                if !isCaptureRow(row) {
                    adjustButton("−", row: row, value: -1)
                    adjustButton("+", row: row, value: 1)
                }
            }
        default:
            HStack(spacing: 8) {
                rowButton(row, action: .activate)
                if let options = row.options {
                    ForEach(options) { option in
                        Button {
                            send(snapshot.contextId, .activate, option.id, 0, nil)
                        } label: {
                            Text(option.label)
                                .multilineTextAlignment(.center)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(minWidth: 60, maxWidth: 140, minHeight: 52)
                        }
                        .buttonStyle(OpaqueMenuButtonStyle(selected: option.selected, disabled: !row.enabled))
                        .disabled(!row.enabled)
                    }
                }
            }
        }
    }

    private func rowButton(_ row: MobileMenuRow, action: MobileUiAction) -> some View {
        let capturing = isCaptureRow(row)
        let captureHint = snapshot.menu?.notice ?? snapshot.menu?.footerHint ?? row.value
        return Button {
            send(snapshot.contextId, action, row.id, 0, nil)
        } label: {
            rowText(row)
                .frame(maxWidth: .infinity, minHeight: 56, alignment: .leading)
                .contentShape(Rectangle())
        }
        .buttonStyle(OpaqueMenuButtonStyle(emphasized: capturing, selected: row.selected || capturing,
            disabled: !row.enabled))
        .disabled(!row.enabled)
        .accessibilityValue(capturing ? captureHint : row.value)
    }

    private func isCaptureRow(_ row: MobileMenuRow) -> Bool {
        snapshot.menu?.capturing == true && snapshot.menu?.captureRowId == row.id
    }

    private func rowText(_ row: MobileMenuRow) -> some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text(row.label)
                    .font(.body.weight(row.selected ? .semibold : .regular))
                    .foregroundStyle(NativePanelPalette.text)
                    .fixedSize(horizontal: false, vertical: true)
                if !row.value.isEmpty {
                    Text(row.value)
                        .font(.callout)
                        .foregroundStyle(NativePanelPalette.secondaryText)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
            badgeView(row)
        }
        .accessibilityElement(children: .combine)
        .accessibilityValue(row.value)
    }

    @ViewBuilder private func badgeView(_ row: MobileMenuRow) -> some View {
        if row.badge != .none, let label = row.badgeLabel, !label.isEmpty {
            Text(label)
                .font(.caption.weight(.semibold))
                .foregroundStyle(badgeColor(row.badge))
                .fixedSize(horizontal: false, vertical: true)
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(NativePanelPalette.background, in: Capsule())
                .accessibilityLabel(label)
        }
    }

    private func badgeColor(_ kind: MobileMenuBadgeKind) -> Color {
        switch kind {
        case .manual: return Color(red: Double(0x42) / 255, green: Double(0x85) / 255, blue: Double(0xF4) / 255)
        case .automatic: return Color(red: Double(0x34) / 255, green: Double(0xA8) / 255, blue: Double(0x53) / 255)
        case .none: return NativePanelPalette.surface
        }
    }

    private func adjustButton(_ title: String, row: MobileMenuRow, value: Int32) -> some View {
        Button {
            send(snapshot.contextId, .adjust, row.id, value, nil)
        } label: {
            Text(title)
                .font(.title2.weight(.semibold))
                .frame(width: 52, height: 52)
                .contentShape(Rectangle())
        }
        .buttonStyle(OpaqueMenuButtonStyle(disabled: !row.enabled))
        .disabled(!row.enabled)
        .accessibilityLabel("\(row.label) \(title)")
        .accessibilityValue(row.value)
    }

    @ViewBuilder private func modalContent(_ modal: MobileMenuModal) -> some View {
        VStack(alignment: .leading, spacing: 18) {
            Text(modal.title)
                .font(.title.weight(.semibold))
                .foregroundStyle(NativePanelPalette.text)
                .fixedSize(horizontal: false, vertical: true)
            Text(modal.message)
                .font(.body)
                .foregroundStyle(NativePanelPalette.text)
                .fixedSize(horizontal: false, vertical: true)
            if modal.kind == .notice {
                Button(modal.confirmLabel) {
                    command(.dismissNotice)
                }
                .buttonStyle(OpaqueMenuButtonStyle(emphasized: true))
            } else {
                HStack(spacing: 12) {
                    Button {
                        command(.confirmYes)
                    } label: {
                        Text(modal.confirmLabel)
                            .fixedSize(horizontal: false, vertical: true)
                            .frame(maxWidth: .infinity, minHeight: 56)
                    }
                    .buttonStyle(OpaqueMenuButtonStyle(emphasized: modal.choice != false, selected: modal.choice == true))
                    Button {
                        command(.confirmNo)
                    } label: {
                        Text(modal.cancelLabel)
                            .fixedSize(horizontal: false, vertical: true)
                            .frame(maxWidth: .infinity, minHeight: 56)
                    }
                    .buttonStyle(OpaqueMenuButtonStyle(emphasized: false, selected: modal.choice == false))
                }
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(NativePanelPalette.surface, in: RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(NativePanelPalette.edge, lineWidth: 2))
    }

    private func textEntry(_ request: MobileTextRequest) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            TextField(request.placeholder, text: $textValue)
                .font(.body)
                .textFieldStyle(.plain)
                .textContentType(.name)
                .textInputAutocapitalization(.words)
                .autocorrectionDisabled()
                .submitLabel(.done)
                .focused($textFieldFocused)
                .onSubmit { submitText(request) }
                .foregroundStyle(NativePanelPalette.text)
                .tint(NativePanelPalette.accent)
                .padding(14)
                .frame(minHeight: 56)
                .background(NativePanelPalette.surface, in: RoundedRectangle(cornerRadius: 10))
                .overlay(RoundedRectangle(cornerRadius: 10).stroke(NativePanelPalette.edge, lineWidth: 2))
                .accessibilityLabel(request.title)
            HStack(spacing: 12) {
                Button(request.submitLabel) { submitText(request) }
                    .buttonStyle(OpaqueMenuButtonStyle(emphasized: true))
                Button(request.cancelLabel) {
                    send(snapshot.contextId, .cancelText, request.id, 0, nil)
                    textFieldFocused = false
                }
                .buttonStyle(OpaqueMenuButtonStyle())
            }
        }
    }

    private func submitText(_ request: MobileTextRequest) {
        send(snapshot.contextId, .submitText, request.id, 0, textValue)
    }

    private func command(_ action: MobileUiAction) {
        send(snapshot.contextId, action, nil, 0, nil)
    }
}

private struct OpaqueMenuButtonStyle: ButtonStyle {
    var emphasized = false
    var selected = false
    var disabled = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(emphasized || selected ? .semibold : .regular))
            .foregroundStyle(disabled ? NativePanelPalette.secondaryText : NativePanelPalette.text)
            .frame(minHeight: 56)
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .background(configuration.isPressed && !disabled ? NativePanelPalette.pressed : selected ? NativePanelPalette.selectedSurface : NativePanelPalette.surface,
                in: RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(emphasized ? NativePanelPalette.accent : NativePanelPalette.edge, lineWidth: 2))
            .clipShape(RoundedRectangle(cornerRadius: 12))
            .contentShape(RoundedRectangle(cornerRadius: 12))
    }
}

private enum NativePanelPalette {
    static let background = Color(red: 0.10, green: 0.09, blue: 0.08)
    static let surface = Color(red: 0.20, green: 0.16, blue: 0.12)
    static let selectedSurface = Color(red: 0.28, green: 0.21, blue: 0.15)
    static let pressed = Color(red: 0.34, green: 0.24, blue: 0.15)
    static let edge = Color(red: 0.59, green: 0.42, blue: 0.25)
    static let accent = Color(red: 0.73, green: 0.53, blue: 0.32)
    static let text = Color(red: 1, green: 0.95, blue: 0.86)
    static let secondaryText = Color(red: 0.83, green: 0.76, blue: 0.66)
}
