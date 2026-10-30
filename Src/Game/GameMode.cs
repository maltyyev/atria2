namespace Atria.Game;


/// <summary>Persisted original mode IDs. Names do not change the save or scenario ABI.</summary>
internal static class GameMode
{
    public const int MainMenu = 200;
    public const int Save = 220;
    public const int Load = 230;
    public const int Credits = 240;
    public const int Settings = 250;
    public const int World = 300;
    public const int WorldEntry = 310;
    public const int Dungeon = 330;
    public const int Town = 400;
    public const int Interior = 410;
    public const int Inn = 420;
    public const int Shop = 430;
    public const int Status = 500;
    public const int Ending = 1000;
    public const int Defeat = 1100;
    public const int QuitConfirmation = 9999;

    public static bool CanSnapshot(int mode) => mode is World or Dungeon or Town or Interior or Inn or Shop;
}
