using Atria.Render;
using Atria.Render.Codecs;

namespace Atria.Game.Combat;

/// <summary>407fe0 text composition. Combat entry43ce90(1) selects ENG1,8x16/advance8.</summary>
public sealed class CombatGlobalUiRaster(CombatGlobalUiAssets assets)
{
    private const ushort NoticeColor = 0xffff;
    private const ushort ShadowColor = 0x7bef;
    private const ushort HighlightColor = 0xffe0;
    private const ushort SettingsShadowColor = 0x4208;
    private const int ShadowOffset = 1;
    private static readonly (int X, int Y) AutomationPosition = (10, 50);
    private static readonly (int X, int Y) PausePosition = (120, 200);
    private static readonly (int X, int Y) SettingsPosition = (250, 50);
    public void Draw(CombatGlobalUiRuntime ui, bool automationEnabled, bool paused, Span<ushort> target)
    {
        if (target.Length != LogicalScreen.PixelCount)
        {
            throw new ArgumentException("Incomplete native combat global UI assets/raster.");
        }

        if (ui.NoticePhase != 0)
        {
            Text(ui.NoticeText.Span, ui.NoticeX + ShadowOffset, ui.NoticeY + ShadowOffset, ShadowColor, target);
            Text(ui.NoticeText.Span, ui.NoticeX, ui.NoticeY, NoticeColor, target);
        }

        if (automationEnabled)
        {
            Text(assets.Messages.Automation.Span, AutomationPosition.X + ShadowOffset, AutomationPosition.Y + ShadowOffset, ShadowColor, target);
            Text(assets.Messages.Automation.Span, AutomationPosition.X, AutomationPosition.Y, HighlightColor, target);
        }

        if (paused)
        {
            Text(assets.Messages.Pause.Span, PausePosition.X + ShadowOffset, PausePosition.Y + ShadowOffset, ShadowColor, target);
            Text(assets.Messages.Pause.Span, PausePosition.X, PausePosition.Y, HighlightColor, target);
        }

        if (ui.SettingsNoticeVisible)
        {
            string text = FormatSettings(ui.VisualDetail, ui.Schedule.Period);
            //The native order/offset is intentionally unusual: dark at250,50 then yellow at251,51.
            Text(text, SettingsPosition.X, SettingsPosition.Y, SettingsShadowColor, target);
            Text(text, SettingsPosition.X + ShadowOffset, SettingsPosition.Y + ShadowOffset, HighlightColor, target);
        }
    }

    public static string FormatSettings(int detail, int period) =>
        GameText.Format("ui.combat.settings", ("detail", detail), ("period", period));

    private void Text(string text, int x, int y, ushort color, Span<ushort> target) => assets.Font.DrawText(text, target, LogicalScreen.Width, LogicalScreen.Height, LogicalScreen.Width, x, y, color);

    private void Text(ReadOnlySpan<byte> text, int x, int y, ushort color, Span<ushort> target) => assets.Font.DrawText(text, target, LogicalScreen.Width, LogicalScreen.Height, LogicalScreen.Width, x, y, color);
}
