namespace Atria.Game.Persistence;

public static class AutosaveReason
{
    public const string BeforeBattle = "ui.autosave.beforeBattle";
    public const string AfterBattle = "ui.autosave.afterBattle";
    public const string QuestComplete = "ui.autosave.questComplete";
    public const string Location = "ui.autosave.location";
    public const string WorldArea = "ui.autosave.worldArea";

    // Pre-0.15.7 saves contain literal captions instead of IDs; never use editable translations as migration keys.
    public static string FromSaved(string value) => value switch
    {
        "Перед боем" or "Перад боем" => BeforeBattle,
        "После боя" or "Пасля бою" => AfterBattle,
        "Задание завершено" or "Заданне завершана" => QuestComplete,
        "Переход между локациями" or "Пераход паміж лакацыямі" => Location,
        "Новая область карты" or "Новая вобласць карты" or "Новая вобласць мапы" => WorldArea,
        _ => value
    };

    public static string Text(string id) => id is BeforeBattle or AfterBattle or QuestComplete or Location or WorldArea
        ? GameText.Get(id)
        : id;
}
