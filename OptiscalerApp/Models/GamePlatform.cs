namespace OptiscalerApp.Models;

public enum GamePlatform
{
    Steam = 0,
    Epic = 1,
    Gog = 2,
    Xbox = 3,
    Ea = 4,
    BattleNet = 5,
    Ubisoft = 6,
    Lutris = 7,
    Manual = 8,
    Custom = 9
}

public static class GamePlatformNames
{
    /// <summary>The name users know a store by; enum names such as "Gog" or "BattleNet" read as typos.</summary>
    public static string DisplayName(this GamePlatform platform)
    {
        return platform switch
        {
            GamePlatform.Gog => "GOG",
            GamePlatform.Ea => "EA",
            GamePlatform.BattleNet => "Battle.net",
            GamePlatform.Custom => "Custom folder",
            _ => platform.ToString()
        };
    }
}
