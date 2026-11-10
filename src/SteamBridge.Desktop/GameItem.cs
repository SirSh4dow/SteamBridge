using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SteamBridge.Core;
using SteamBridge.Models;

namespace SteamBridge.Desktop;

public sealed class GameItem : INotifyPropertyChanged
{
    public ScannedGame Game { get; }
    public bool IsShortcut { get; set; }
    public string Title => Game.Title;
    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? "Jogo" : Title;
    public string Source => Game.Source;
    public string Badge => Game.Badge;
    public string Initial => string.IsNullOrWhiteSpace(Title) ? "SB" : Title[..1].ToUpperInvariant();
    public string Subtitle => Game.AlreadyInSteam ? (Source == "steam" ? "Instalado no Steam" : "Integrado ao Steam") : "Pronto para integrar";
    public bool CanImport => Source != "steam" && !Game.AlreadyInSteam;
    public bool IsIntegrated => Game.AlreadyInSteam;
    public string Location => string.IsNullOrEmpty(Game.InstallDir) ? Game.Exe : Game.InstallDir;
    public Brush SourceBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Source switch
    {
        "steam" => "#69CFF8", "epic" => "#DBD5F4", "xbox" => "#8BDF98", _ => "#C5A1FF"
    }));
    public Brush CoverBrush { get; }
    public Func<ImageSource?>? CoverFactory { get; set; }
    private ImageSource? _cover;
    private bool _coverLoaded;
    public ImageSource? Cover
    {
        get { if (!_coverLoaded) { _coverLoaded = true; _cover = CoverFactory?.Invoke(); } return _cover; }
        set { _cover = value; _coverLoaded = true; }
    }
    public void ReleaseCover() { _cover = null; _coverLoaded = false; }
    private bool _selected;
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(); } }
    private bool _shelfFocus;
    public bool IsShelfFocus { get => _shelfFocus; set { if (_shelfFocus == value) return; _shelfFocus = value; Changed(); } }
    public GameItem(ScannedGame game)
    {
        Game = game;
        uint hash = SteamUtils.ComputeCrc32(System.Text.Encoding.UTF8.GetBytes(game.Title));
        string[] colors = ["#3C2A6A", "#1B465C", "#5A2A52", "#1A4840", "#5A3E2C", "#2A315C"];
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(colors[hash % colors.Length]), Color.FromRgb(12, 14, 20), 70);
        brush.Freeze(); CoverBrush = brush;
    }
    public void Refresh() { Changed(nameof(Title)); Changed(nameof(Subtitle)); Changed(nameof(CanImport)); Changed(nameof(IsIntegrated)); Changed(nameof(Cover)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class CoverLoader
{
    public static ImageSource? Load(string? path, int width = 360)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width; image.StreamSource = stream; image.EndInit(); image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException) { return null; }
    }

    public static string? Find(SteamInfo steam, uint appId, string kind = "portrait")
    {
        if (steam.SelectedUser != null)
        {
            var local = SteamArtManager.FindExistingArt(SteamArtManager.GetGridDirectory(steam.InstallPath, steam.SelectedUser.Id3), appId, kind);
            if (local != null) return local;
        }
        var cache = Path.Combine(steam.InstallPath, "appcache", "librarycache");
        string name = kind switch { "hero" => "library_hero", "logo" => "logo", "wide" => "header", "icon" => "icon", _ => "library_600x900" };
        foreach (var ext in new[] { ".jpg", ".png" })
        {
            string flat = Path.Combine(cache, $"{appId}_{name}{ext}");
            if (File.Exists(flat)) return flat;
            string nested = Path.Combine(cache, appId.ToString(), name + ext);
            if (File.Exists(nested)) return nested;
        }
        string folder = Path.Combine(cache, appId.ToString());
        if (Directory.Exists(folder))
        {
            var result = Directory.EnumerateFiles(folder, "*" + name + "*").FirstOrDefault(p => p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            if (result != null) return result;
        }
        if (kind == "portrait") return Find(steam, appId, "wide");
        return null;
    }
}

public sealed class ArtworkItem : System.ComponentModel.INotifyPropertyChanged
{
    public ArtworkItem(ArtOption option, ImageSource? preview)
    {
        Option = option;
        Preview = preview;
    }

    public ArtOption Option { get; }
    public ImageSource? Preview { get; }
    public string Label => $"{Option.Width} × {Option.Height}  ·  {Option.Style}";
    private bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
