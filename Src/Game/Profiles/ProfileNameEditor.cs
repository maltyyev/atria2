using System.Globalization;
using Atria.Game.Controls;
using Atria.Render.Codecs;
using Atria.Render.UI;
using Atria.Resources;

namespace Atria.Game.Profiles;

internal sealed class ProfileNameEditor
{
    private const int Columns = 10;
    private const int CellWidth = 54;
    private const int CellHeight = 36;
    private int alphabet;
    private int selection;
    private bool uppercase, replaceOnType = true;
    public string Text { get; private set; }
    public bool Closed { get; private set; }
    public string? Result { get; private set; }
    public string Error { get; private set; } = "";
    private string Characters => GameText.Get(alphabet switch
    {
        0 => "ui.profiles.lettersRu", 1 => "ui.profiles.lettersBe", _ => "ui.profiles.lettersLatin"
    });

    public ProfileNameEditor(string name)
    {
        Text = name;
        selection = Characters.Length + 3;
    }

    internal void Type(string value)
    {
        if (value.Length == 0) return;
        string candidate = (replaceOnType ? "" : Text) + value;
        if (new StringInfo(candidate).LengthInTextElements <= PlayerProfileStore.MaximumNameLength)
        {
            Text = candidate;
            replaceOnType = false;
            Error = "";
        }
    }

    internal void Erase()
    {
        if (replaceOnType) Text = "";
        else if (Text.Length > 0) Text = Text[..StringInfo.ParseCombiningCharacters(Text)[^1]];
        replaceOnType = false;
    }

    internal void Accept()
    {
        try { Result = PlayerProfileStore.ValidateName(Text); Closed = true; }
        catch (ArgumentException) { Error = GameText.Get("ui.profiles.invalidName"); }
    }

    public void Update(InputRouter input, string typed)
    {
        Type(typed);
        if (input.Take(1) || input.PadButtonPressed(1)) { Closed = true; return; }
        if (input.Take(0x1c) || input.PadButtonPressed(7)) { Accept(); return; }
        if (input.Take(0x0e, true) || input.PadButtonPressed(2)) Erase();
        if (input.PadButtonPressed(3)) { alphabet = (alphabet + 1) % 3; selection = 0; }
        if (input.Take(0xc8, true) || input.PadButtonPressed(10, true) || input.PadButtonPressed(18, true)) selection -= Columns;
        if (input.Take(0xd0, true) || input.PadButtonPressed(12, true) || input.PadButtonPressed(19, true)) selection += Columns;
        if (input.Take(0xcb, true) || input.PadButtonPressed(13, true) || input.PadButtonPressed(16, true)) selection--;
        if (input.Take(0xcd, true) || input.PadButtonPressed(11, true) || input.PadButtonPressed(17, true)) selection++;
        selection = Math.Clamp(selection, 0, Characters.Length + 4);
        bool activate = input.PadButtonPressed(0);
        if (input.Take(256) && input.MouseX is >= 50 and < 590 && input.MouseY is >= 145 and < 325)
        {
            int clicked = (input.MouseY - 145) / CellHeight * Columns + (input.MouseX - 50) / CellWidth;
            if (clicked <= Characters.Length + 4) { selection = clicked; activate = true; }
        }
        if (!activate) return;
        if (selection < Characters.Length)
        {
            string letter = Characters[selection].ToString();
            Type(uppercase ? letter.ToUpperInvariant() : letter);
        }
        else switch (selection - Characters.Length)
        {
            case 0: uppercase = !uppercase; break;
            case 1: alphabet = (alphabet + 1) % 3; selection = 0; break;
            case 2: Erase(); break;
            case 3: Accept(); break;
            case 4: Closed = true; break;
        }
    }

    public void Draw(Span<ushort> pixels, SpriteFile tiles, BitmapFont font)
    {
        for (int y = 24; y < 452; y++)
            pixels.Slice(y * 640 + 24, 592).Clear();
        UiRaster.DrawPanel(tiles, 16, 16, 17, 12, pixels, arrows: false);
        ControlPanel.Text(font, pixels, GameText.Get("ui.profiles.nameTitle"), 48, 42, 0xffe0);
        ControlPanel.Text(font, pixels, Text + "|", 50, 88, replaceOnType ? (ushort)0xffe0 : (ushort)0xffff, 66);
        for (int i = 0; i < Characters.Length + 5; i++)
        {
            int x = 50 + i % Columns * CellWidth, y = 145 + i / Columns * CellHeight;
            for (int line = y; line < y + CellHeight - 3; line++)
                pixels.Slice(line * 640 + x, CellWidth - 3).Fill(i == selection ? (ushort)0x52aa : (ushort)0x2104);
            string label = i < Characters.Length ? Characters[i].ToString() : GameText.Get((i - Characters.Length) switch
            {
                0 => "ui.profiles.case", 1 => "ui.profiles.alphabet", 2 => "ui.profiles.erase",
                3 => "ui.profiles.accept", _ => "ui.profiles.cancel"
            });
            if (label == " ") label = GameText.Get("ui.profiles.space");
            if (uppercase && i < Characters.Length) label = label.ToUpperInvariant();
            ControlPanel.Text(font, pixels, label, x + 3, y + 9, 0xffff, 6);
        }
        ControlPanel.Text(font, pixels, Error, 48, 355, 0xffe0, 68);
        ControlPanel.Text(font, pixels, GameText.Get("ui.profiles.typingHelp"), 48, 390, 0xbdf7, 68);
        ControlPanel.Text(font, pixels, GameText.Get("ui.profiles.padTypingHelp"), 48, 416, 0xbdf7, 68);
    }
}
