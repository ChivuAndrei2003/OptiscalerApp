namespace OptiscalerApp.Models;

// public class Game
// {
//     private string GameIconPath;
//     private string GameName;
//     private string PathName;
// }

public enum GamePlatform
{
    Steam = 0,
    Epic = 1,
    GOG = 2,
    Xbox = 3,
    EA = 4,
    BattleNet = 5,
    Ubisoft = 6,
    Lutris = 7,
    Manual = 8,
    Custom = 9
}

public class Game
{
    public string Name { get; set; }

    public string Path { get; set; }

    public GamePlatform Platform { get; set; }

    public bool isManual => Platform == GamePlatform.Manual;

    public bool isCustom => Platform == GamePlatform.Custom;

    public string IconPath { get; set; }

    public string AppId { get; set; }

    public string? DlssVersion { get; set; }
    public string? DlssPath { get; set; }

    public string? DlssFrameGenVersion { get; set; }
    public string? DlssFrameGenPath { get; set; }

    public string? FsrVersion { get; set; }
    public string? FsrPath { get; set; }
    
    public string? XessVersion { get; set; }
    public string? XessPath { get; set; }
    
    public bool DlssViaOptiscaler { get; set; }
    public bool FsrViaOptiscaler { get; set; }
    public bool XessViaOptiscaler { get; set; }
    
    public bool DlssIsNative => DlssVersion != null && !DlssViaOptiscaler;
    public bool FsrIsNative => FsrVersion != null && !FsrViaOptiscaler;
    public bool XessIsNative => XessVersion != null && !XessViaOptiscaler;
    
    public bool IsOptiscalerInstalled { get; set; }
    public string? OptiscalerVersion { get; set; }
    public string? Fsr4ExtraVersion { get; set; }
    
    public bool HasUpscaler => DlssVersion != null || DlssFrameGenVersion != null || FsrVersion != null || XessVersion != null || IsOptiscalerInstalled;

    // UI customization (not set by scanner)
    public bool IsHidden { get; set; } = false;
    public bool IsFavorite { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;
}