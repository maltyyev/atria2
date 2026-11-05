namespace Atria.Core.Contracts.Host;

/// <summary>Touch layout selected by Core for the current scene.</summary>
public enum MobileOverlayKind
{
    None = 0,
    Navigation = 1,
    Combat = 2,
    CombatSelection = 3,
    ExperienceAllocation = 4
}

/// <summary>Native-menu action IDs shared by mobile adapters and the Core session.</summary>
public enum MobileUiAction
{
    Activate = 0,
    Adjust = 1,
    Select = 2,
    Back = 3,
    ConfirmYes = 4,
    ConfirmNo = 5,
    DismissNotice = 6,
    SubmitText = 7,
    CancelText = 8,
    OpenProfiles = 9,
    CycleLanguage = 10
}

/// <summary>Stable touch button bits. Values are part of the native mobile ABI.</summary>
[Flags]
public enum MobileTouchButtons : ushort
{
    None = 0,
    Up = 0x0001,
    Down = 0x0002,
    Left = 0x0004,
    Right = 0x0008,
    A = 0x0010,
    B = 0x0020,
    X = 0x0040
}

public enum MobileMenuRowKind
{
    Action = 0,
    Selector = 1,
    Binding = 2,
    Slot = 3,
    Profile = 4
}

public enum MobileMenuBadgeKind
{
    None = 0,
    Manual = 1,
    Automatic = 2
}

public enum MobileMenuModalKind
{
    None = 0,
    Confirmation = 1,
    Notice = 2
}

/// <summary>Immutable data for one native menu row; Id remains stable while values change.</summary>
public sealed record MobileMenuOption(string Id, string Label, bool Selected);

public sealed record MobileMenuRow(
    string Id,
    string Label,
    string Value,
    MobileMenuRowKind Kind,
    bool Enabled,
    bool Selected,
    IReadOnlyList<MobileMenuOption>? Options = null,
    MobileMenuBadgeKind Badge = MobileMenuBadgeKind.None,
    string? BadgeLabel = null);

public sealed record MobileMenuModal(
    MobileMenuModalKind Kind,
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel,
    bool? Choice = null);

/// <summary>Core-owned description of the menu or confirmation currently shown by the mobile adapter.</summary>
public sealed record MobileMenuSnapshot(
    string Scene,
    string Title,
    IReadOnlyList<MobileMenuRow> Rows,
    string? Notice = null,
    MobileMenuModal? Modal = null,
    string? FooterHint = null,
    string? AutoScrollRowId = null,
    bool Capturing = false,
    string? CaptureRowId = null,
    string? ContextKey = null);

/// <summary>Requests native text entry only while a Core profile-name editor is active.</summary>
public sealed record MobileTextRequest(
    string Id,
    string Title,
    string InitialValue,
    string Placeholder,
    string SubmitLabel,
    string CancelLabel);

/// <summary>Localized accessibility labels for visible touch controls.</summary>
public sealed record MobileTouchLabels(
    string Up,
    string Down,
    string Left,
    string Right,
    string Confirm,
    string Back,
    string Attack,
    string Jump,
    string Modifier,
    string Language,
    string Movement,
    string Profiles);

/// <summary>One immutable UI snapshot. ContextId guards actions; Revision only tracks display changes.</summary>
public sealed record MobileUiSnapshot(
    ulong ContextId,
    ulong Revision,
    string Scene,
    MobileOverlayKind OverlayKind,
    bool ShowLanguageButton,
    MobileTouchLabels Labels,
    MobileMenuSnapshot? Menu = null,
    MobileTextRequest? TextRequest = null,
    bool ShowProfilesButton = false,
    string? Notice = null);

/// <summary>A typed action raised by the native mobile menu.</summary>
public readonly record struct MobileUiCommand(
    ulong ContextId,
    MobileUiAction Action,
    string? TargetId = null,
    int Value = 0,
    string? Text = null,
    bool InvalidText = false);

/// <summary>Raw mobile touch controls. They are interpreted by Core and never use Xbox remappings.</summary>
public readonly record struct MobileTouchState(
    ulong ContextId,
    short LeftX,
    short LeftY,
    MobileTouchButtons Buttons)
{
    public bool IsNeutral => Buttons == MobileTouchButtons.None && LeftX == 0 && LeftY == 0;
}

/// <summary>Optional, platform-neutral mobile UI/input bridge implemented by mobile game windows.</summary>
public interface IMobileUiChannel
{
    ulong UiRevision { get; }
    ulong UiContextId { get; }
    int CopyUiSnapshot(Span<byte> destination);
    void PublishUiSnapshot(MobileUiSnapshot snapshot);
    void SubmitUiCommand(MobileUiCommand command);
    bool TryDequeueUiCommand(out MobileUiCommand command);
    void SubmitTouchState(MobileTouchState state);
    bool TryDequeueTouchState(out MobileTouchState state);
}
