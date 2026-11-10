using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SteamBridge.Core;
using SteamBridge.Models;

namespace SteamBridge.Desktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private SteamInfo _steam;
    private readonly SteamGridDbClient _gridClient;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private List<GameItem> _games = new();
    private string _source = "all";
    private int _limit = 60;
    private bool _initialized;
    private bool _discovered;
    private bool _busy;
    private string? _loadedContext;
    private SteamUser? _selectedAccount;
    private readonly Dictionary<string, (string Title, string Exe, string Args, string Cwd)> _drafts = new();
    private string _status = "Preparando sua biblioteca...";
    private string _artStatus = "Selecione um jogo integrado para personalizar suas artes.";
    private string _collectionHeading = "Selecione uma coleção";
    private string _collectionDescription = "Crie uma coleção ou escolha uma existente na lista ao lado.";
    private ImageSource? _artPreview;
    private string? _optionsGameKey;
    private string? _optionsKind;
    private bool _loadingMatches;
    private ArtworkItem? _selectedArt;
    private string _studioTitle = "";
    private string _studioCaption = "Capa atual";
    public string StudioTitle { get => _studioTitle; private set { _studioTitle = value; Changed(); } }
    public string StudioCaption { get => _studioCaption; private set { _studioCaption = value; Changed(); } }
    public double ArtCardWidth { get; private set; } = 158;
    public double ArtCardHeight { get; private set; } = 224;
    public double ArtPreviewWidth { get; private set; } = 280;
    public double ArtPreviewHeight { get; private set; } = 420;
    private void ApplyArtFrame()
    {
        (ArtCardWidth, ArtCardHeight, ArtPreviewWidth, ArtPreviewHeight) = ArtKind switch
        {
            "wide" => (248d, 116d, 440d, 206d),
            "hero" => (286d, 104d, 480d, 174d),
            "logo" => (230d, 100d, 420d, 180d),
            "icon" => (132d, 132d, 240d, 240d),
            _ => (158d, 224d, 280d, 420d)
        };
        Changed(nameof(ArtCardWidth));
        Changed(nameof(ArtCardHeight));
        Changed(nameof(ArtPreviewWidth));
        Changed(nameof(ArtPreviewHeight));
    }
    public bool HasArtSelection => _selectedArt != null;
    public ObservableCollection<SteamUser> Accounts { get; } = new();
    public SteamUser? SelectedAccount { get => _selectedAccount; set { _selectedAccount = value; Changed(); } }
    public ObservableCollection<GameItem> VisibleGames { get; } = new();
    public ObservableCollection<GameItem> IntegratedGames { get; } = new();
    public ObservableCollection<GameItem> ArtGames { get; } = new();
    public ObservableCollection<SteamCollection> Collections { get; } = new();
    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<ArtworkItem> ArtworkOptions { get; } = new();
    public bool Busy { get => _busy; set { _busy = value; Changed(); Changed(nameof(Ready)); } }
    public bool Ready => !Busy;
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string ArtStatus { get => _artStatus; set { _artStatus = value; Changed(); } }
    public ImageSource? ArtPreview { get => _artPreview; set { _artPreview = value; Changed(); } }
    public string CollectionHeading { get => _collectionHeading; set { _collectionHeading = value; Changed(); } }
    public string CollectionDescription { get => _collectionDescription; set { _collectionDescription = value; Changed(); } }
    public int TotalCount => _games.Count;
    public int IntegratedCount => _games.Count(g => g.IsIntegrated);
    public int PendingCount => _games.Count(g => g.CanImport);
    public int PlatformCount => _games.Select(g => g.Source).Distinct().Count();
    public string SelectionLabel => $"{_games.Count(g => g.Selected)} selecionados";
    public bool HasImportSelection => _games.Any(g => g.Selected && g.CanImport);
    public bool IsEmpty => VisibleGames.Count == 0;
    public bool HasMore => Filtered().Count() > _limit;
    public string ResultsLabel => $"{Filtered().Count()} jogos · exibindo {VisibleGames.Count}";
    public string SteamStatus { get; private set; } = "Verificando Steam";
    public string SteamAction { get; private set; } = "Abrir Steam";
    public Brush SteamStatusBrush { get; private set; } = Brushes.Gray;

    public MainWindow()
    {
        _config = AppConfig.Load();
        _steam = SteamLocator.FindSteam(_config.CustomSteamPath);
        _gridClient = new SteamGridDbClient(_config.SteamGridDbKey);
        InitializeComponent();
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkFrame(this);
        Height = Math.Min(980, SystemParameters.WorkArea.Height - 24);
        Width = Math.Min(1400, SystemParameters.WorkArea.Width - 24);
        DataContext = this;
        foreach (var user in _steam.Users) Accounts.Add(user);
        SelectedAccount = _steam.SelectedUser;
        foreach (var folder in _config.ScannedFolders) Folders.Add(folder);
        SteamPathBox.Text = _config.CustomSteamPath;
        ApiKeyBox.Password = _config.SteamGridDbKey;
        _timer.Tick += (_, _) => UpdateSteamStatus();
        _timer.Start();
        UpdateSteamStatus();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _initialized = true;
        await RunBusy(() => LoadLibrary(false));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (Busy) { e.Cancel = true; Status = "Aguarde a operação terminar antes de fechar."; return; }
        _timer.Stop();
    }

    private async Task RunBusy(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        try { await action(); }
        catch (Exception ex)
        {
            Status = "Operação não concluída: " + ex.Message;
            MessageBox.Show(this, ex.Message, "SteamBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { Busy = false; UpdateSteamStatus(); }
    }

    private async Task LoadLibrary(bool discover)
    {
        Status = discover ? "Escaneando plataformas. Isso pode levar alguns segundos..." : "Lendo sua biblioteca Steam...";
        var progress = new Progress<string>(text => Status = text);
        var warnings = new List<string>();
        var folders = _config.ScannedFolders.ToArray();
        _loadedContext = null;
        var snapshot = _steam with { SelectedUser = _steam.SelectedUser };
        List<GameItem> loaded;
        List<SteamCollection> loadedCollections;
        try
        {
            (loaded, loadedCollections) = await Task.Run(() => (
                LibraryService.Load(snapshot, folders, discover, progress, warnings, _config.ManualExecutables, _config.ManualTitles),
                snapshot.SelectedUser == null ? new List<SteamCollection>() : CollectionsManager.LoadCollections(snapshot.InstallPath, snapshot.SelectedUser.Id3)));
        }
        catch
        {
            foreach (var game in _games) game.PropertyChanged -= GameChanged;
            _games.Clear(); VisibleGames.Clear(); IntegratedGames.Clear(); ArtGames.Clear(); Collections.Clear();
            ClearArtGallery();
            ArtPreview = null; CollectionGames.SelectedItems.Clear(); RefreshVisible(); RefreshCounts();
            throw;
        }
        foreach (var game in _games) game.PropertyChanged -= GameChanged;
        foreach (var game in loaded.Where(g => g.CanImport))
        {
            if (_drafts.TryGetValue(game.Game.Key, out var draft))
            { game.Game.Title = draft.Title; game.Game.Exe = draft.Exe; game.Game.LaunchArgs = draft.Args; game.Game.Cwd = draft.Cwd; }
        }
        _games = loaded;
        _discovered = discover;
        foreach (var game in _games) game.PropertyChanged += GameChanged;
        IntegratedGames.Clear();
        ArtGames.Clear();
        foreach (var game in _games)
        {
            ArtGames.Add(game);
            if (game.IsIntegrated) IntegratedGames.Add(game);
        }
        SelectPending.IsChecked = false;
        RefreshVisible(); RefreshCounts();
        Collections.Clear(); foreach (var collection in loadedCollections) Collections.Add(collection);
        CollectionHeading = "Selecione uma coleção"; CollectionDescription = "Selecione uma coleção ou crie uma nova.";
        CollectionGames.SelectedItems.Clear();
        _loadedContext = ContextKey();
        if (ArtGameCombo.SelectedIndex < 0 && ArtGames.Count > 0) ArtGameCombo.SelectedIndex = 0;
        ShowShelf();
        Status = warnings.Count > 0 ? $"Biblioteca carregada com {warnings.Count} aviso(s). " + string.Join(" | ", warnings)
            : $"{TotalCount} jogos carregados · " + (discover ? "Scan concluído. Nenhum arquivo do Steam foi alterado." : "Escaneie para descobrir jogos Epic, Xbox e locais.");
        if (_steam.SelectedUser == null) Status = "Nenhuma conta Steam encontrada. Entre no Steam uma vez ou configure a pasta de instalação.";
    }

    private void GameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameItem.Selected)) { Changed(nameof(SelectionLabel)); Changed(nameof(HasImportSelection)); }
    }
    private IEnumerable<GameItem> Filtered()
    {
        string query = SearchBox?.Text.Trim() ?? "";
        return _games.Where(g => (_source == "all" || g.Source == _source)
            && (query.Length == 0 || CultureInfo.CurrentCulture.CompareInfo.IndexOf(g.Title, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0));
    }
    private void RefreshVisible()
    {
        var visible = Filtered().Take(_limit).ToHashSet();
        foreach (var game in _games.Where(g => !visible.Contains(g))) game.ReleaseCover();
        VisibleGames.Clear(); foreach (var game in Filtered().Take(_limit)) VisibleGames.Add(game);
        Changed(nameof(IsEmpty)); Changed(nameof(HasMore)); Changed(nameof(ResultsLabel));
    }
    private void RefreshCounts()
    {
        foreach (string name in new[] { nameof(TotalCount), nameof(IntegratedCount), nameof(PendingCount), nameof(PlatformCount), nameof(SelectionLabel), nameof(HasImportSelection) }) Changed(name);
    }
    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        _source = (sender as RadioButton)?.Tag?.ToString() ?? "all";
        if (!_initialized) return;
        _limit = 60; RefreshVisible();
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (!_initialized) return; _limit = 60; RefreshVisible(); }
    private void LoadMore_Click(object sender, RoutedEventArgs e) { _limit += 60; RefreshVisible(); }
    private void SelectPending_Click(object sender, RoutedEventArgs e) { foreach (var game in Filtered().Where(g => g.CanImport)) game.Selected = SelectPending.IsChecked == true; }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) { foreach (var game in _games) game.Selected = false; SelectPending.IsChecked = false; }
    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunBusy(() => LoadLibrary(true));

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Selecione o executável ou o script",
            Filter = "Executável ou script|*.exe;*.cmd;*.bat|Executável (*.exe)|*.exe|Prompt de comando (*.cmd;*.bat)|*.cmd;*.bat",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var game = LibraryService.FromExecutable(picker.FileName);
            if (_games.Any(g => g.Game.Exe.Equals(game.Exe, StringComparison.OrdinalIgnoreCase)))
            {
                Status = "Esse arquivo já está na biblioteca.";
                return;
            }
            if (!_config.ManualExecutables.Any(p => Path.GetFullPath(p).Equals(game.Exe, StringComparison.OrdinalIgnoreCase)))
            {
                _config.ManualExecutables.Add(game.Exe);
                _config.Save();
            }
            var item = new GameItem(game) { Selected = true };
            item.PropertyChanged += GameChanged;
            _games.Add(item);
            ArtGames.Add(item);
            RefreshVisible();
            RefreshCounts();
            Status = game.Title + " adicionado. Selecione Integrar ao Steam para gravar o atalho.";
        }
        catch (Exception ex)
        {
            Status = "Não foi possível adicionar o jogo: " + ex.Message;
            MessageBox.Show(this, ex.Message, "SteamBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (LibraryPage == null) return;
        string page = (sender as RadioButton)?.Tag?.ToString() ?? "Library";
        LibraryPage.Visibility = page == "Library" ? Visibility.Visible : Visibility.Collapsed;
        ArtPage.Visibility = page == "Art" ? Visibility.Visible : Visibility.Collapsed;
        CollectionsPage.Visibility = page == "Collections" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "Collections" && _initialized)
        {
            try { LoadCollections(); } catch (Exception ex) { Status = ex.Message; }
        }
    }

    private async void AccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || Busy) return;
        if (AccountCombo.SelectedItem is SteamUser user) _steam.SelectedUser = user;
        ClearArtGallery();
        await RunBusy(() => LoadLibrary(_discovered));
    }

    private string? _steamTransition;

    private void UpdateSteamStatus()
    {
        if (_steamTransition != null)
        {
            SteamStatus = _steamTransition;
            SteamAction = _steamTransition;
            SteamStatusBrush = Brushes.Goldenrod;
            Changed(nameof(SteamStatus)); Changed(nameof(SteamAction)); Changed(nameof(SteamStatusBrush));
            return;
        }
        bool running = SteamLocator.IsSteamRunning();
        SteamStatus = running ? (SteamLocator.HasSteamInterface() ? "Steam em execução" : "Steam ativo · interface indisponível") : "Steam fechado";
        SteamAction = running ? "Fechar Steam" : "Abrir Steam";
        SteamStatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#75D7A7" : "#8992B1"));
        Changed(nameof(SteamStatus)); Changed(nameof(SteamAction)); Changed(nameof(SteamStatusBrush));
    }
    private async void ToggleSteam_Click(object sender, RoutedEventArgs e)
    {
        if (Busy) return;
        bool closing = SteamLocator.IsSteamRunning();
        if (closing && !Confirm("Fechar o Steam de forma normal?\n\nFinalize seus jogos e aguarde downloads antes de continuar. Nenhum processo será encerrado à força.")) return;
        await RunBusy(async () =>
        {
            _steamTransition = closing ? "Fechando Steam..." : "Abrindo Steam...";
            UpdateSteamStatus();
            Status = _steamTransition;
            try
            {
                if (closing)
                {
                    bool closed = await Task.Run(() => SteamLocator.CloseSteam(30, _steam.ExePath));
                    Status = closed ? "Steam fechado." : "O Steam não concluiu o encerramento em 30 segundos. Confira os avisos do cliente ou use Steam > Sair. Nenhum processo foi encerrado à força.";
                }
                else
                {
                    SteamLocator.StartSteam(_steam.ExePath);
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    while (elapsed.Elapsed < TimeSpan.FromSeconds(30) && !SteamLocator.HasSteamInterface())
                        await Task.Delay(250);
                    Status = SteamLocator.HasSteamInterface()
                        ? "Processos do Steam e da interface iniciados."
                        : "A abertura não foi confirmada em 30 segundos. Confira se há um aviso do Steam; não serão feitas novas tentativas automáticas.";
                }
            }
            finally { _steamTransition = null; }
        });
    }
    private bool Confirm(string text) => MessageBox.Show(this, text, "SteamBridge · confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private string ContextKey() => _steam.InstallPath + "|" + _steam.SelectedUser?.Id3;
    private void RequireAccount()
    {
        if (_steam.SelectedUser == null || !Directory.Exists(_steam.InstallPath)) throw new InvalidOperationException("Selecione uma conta Steam válida nas configurações.");
        if (_loadedContext != ContextKey()) throw new InvalidOperationException("Recarregue a biblioteca desta conta antes de modificar arquivos.");
    }
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var selected = _games.Where(g => g.Selected && g.CanImport).ToList();
        if (selected.Count == 0) return;
        if (!Confirm($"Adicionar {selected.Count} jogo(s) à conta {_steam.SelectedUser?.PersonaName}?\n\nO Steam precisa estar fechado. Será criado um backup dos atalhos existentes antes da gravação.")) return;
        await RunBusy(async () =>
        {
            RequireAccount(); LibraryService.EnsureSteamClosed();
            Status = "Salvando atalhos com backup...";
            int count = await Task.Run(() => LibraryService.Apply(_steam, selected));
            await LoadLibrary(_discovered);
            Status = $"{count} jogo(s) integrado(s). Abra o Steam para atualizar a biblioteca.";
        });
    }

    private void GameDetails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameItem item) return;
        string previous = item.Game.Title;
        var dialog = new GameDetailsWindow(item) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (item.Source != "steam" && item.IsIntegrated && !string.Equals(previous, item.Game.Title, StringComparison.Ordinal))
                LibraryService.RenameIntegrated(_steam, item);
            RememberTitle(item);
        }
        catch (Exception ex)
        {
            item.Game.Title = previous;
            MessageBox.Show(this, ex.Message, "SteamBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (item.CanImport) _drafts[item.Game.Key] = (item.Game.Title, item.Game.Exe, item.Game.LaunchArgs, item.Game.Cwd);
        item.Refresh();
        RefreshVisible();
    }

    private void RememberTitle(GameItem item)
    {
        if (item.Source == "steam" || string.IsNullOrWhiteSpace(item.Game.Exe) || string.IsNullOrWhiteSpace(item.Game.Title)) return;
        string? existingKey = _config.ManualTitles.Keys.FirstOrDefault(k => k.Equals(item.Game.Exe, StringComparison.OrdinalIgnoreCase));
        if (existingKey != null && !existingKey.Equals(item.Game.Exe, StringComparison.Ordinal)) _config.ManualTitles.Remove(existingKey);
        _config.ManualTitles[existingKey ?? item.Game.Exe] = item.Game.Title;
        _config.Save();
    }

    private string ArtKind => ArtFormatWide.IsChecked == true ? "wide"
        : ArtFormatHero.IsChecked == true ? "hero"
        : ArtFormatLogo.IsChecked == true ? "logo"
        : ArtFormatIcon.IsChecked == true ? "icon"
        : "portrait";
    private GameItem RequireArtGame() => ArtGameCombo.SelectedItem as GameItem ?? throw new InvalidOperationException("Selecione o jogo que vai receber a capa.");
    private void ClearArtGallery()
    {
        ArtworkOptions.Clear();
        _selectedArt = null;
        var game = ArtGameCombo?.SelectedItem as GameItem;
        ArtPreview = game?.Cover;
        StudioCaption = "Capa atual";
        if (ArtChoicesEmpty != null) ArtChoicesEmpty.Visibility = Visibility.Visible;
        Changed(nameof(HasArtSelection));
    }
    private void SelectArt(ArtworkItem? art)
    {
        foreach (var item in ArtworkOptions) item.IsSelected = item == art;
        _selectedArt = art;
        var game = ArtGameCombo?.SelectedItem as GameItem;
        if (art?.Preview != null)
        {
            ArtPreview = art.Preview;
            StudioCaption = "Nova capa";
        }
        else
        {
            ArtPreview = game?.Cover;
            StudioCaption = "Capa atual";
        }
        if (ArtChoicesEmpty != null) ArtChoicesEmpty.Visibility = ArtworkOptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Changed(nameof(HasArtSelection));
    }
    private void SelectArt_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ArtworkItem art) SelectArt(art);
    }
    private void ShowShelf()
    {
        if (ShelfView == null) return;
        ShelfView.Visibility = Visibility.Visible;
        CoverStudio.Visibility = Visibility.Collapsed;
    }
    private void StudioBack_Click(object sender, RoutedEventArgs e) => ShowShelf();
    private void ShelfPick_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameItem item) return;
        foreach (var game in ArtGames) game.IsShelfFocus = game == item;
        ArtGameCombo.SelectedItem = item;
        ArtSearchName.Text = item.Title;
        StudioTitle = item.Title;
        ArtPreview = item.Cover;
        StudioCaption = "Capa atual";
        ShelfView.Visibility = Visibility.Collapsed;
        CoverStudio.Visibility = Visibility.Visible;
        ApplyArtFrame();
        ArtChoicesEmpty.Text = _gridClient.HasApiKey ? "Buscando capas…" : "Importe uma imagem ou configure a chave SteamGridDB.";
        ArtChoicesEmpty.Visibility = Visibility.Visible;
        if (_gridClient.HasApiKey) SearchArt_Click(sender, e);
        else ArtStatus = "Sem chave do SteamGridDB. A importação de imagem continua disponível.";
    }
    private void ArtSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SearchArt_Click(sender, e);
        e.Handled = true;
    }
    private async void ArtFormat_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _loadingMatches || Busy) return;
        ApplyArtFrame();
        if (ArtMatchCombo.SelectedItem is GridGameMatch match)
            await RunBusy(() => LoadArtOptions(match.Id));
        else if (ArtGameCombo.SelectedItem is GameItem game)
        {
            ArtSearchName.Text = game.Title;
            SearchArt_Click(sender, e);
        }
    }
    private void ArtSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        ClearArtGallery();
        _optionsGameKey = null; _optionsKind = null;
        if (sender == ArtGameCombo)
        {
            ArtGameId.Text = "";
            _loadingMatches = true;
            ArtMatchCombo.ItemsSource = null;
            ArtMatchCombo.Visibility = Visibility.Collapsed;
            _loadingMatches = false;
        }
        if (ArtGameCombo.SelectedItem is GameItem game && sender == ArtGameCombo)
            ArtSearchName.Text = game.Title;
    }
    private async void ImportArt_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Escolha a arte", Filter = "Imagens PNG e JPEG|*.png;*.jpg;*.jpeg" };
        if (picker.ShowDialog(this) != true) return;
        await RunBusy(async () =>
        {
            RequireAccount(); var game = RequireArtGame();
            if (!Confirm($"Aplicar esta imagem a {game.Title}?\n\nA arte anterior será preservada em backup. O Steam precisa estar fechado.")) return;
            LibraryService.EnsureSteamClosed();
            byte[] bytes = await File.ReadAllBytesAsync(picker.FileName);
            ValidateImage(bytes);
            await SaveArt(game, ArtKind, bytes, Path.GetExtension(picker.FileName).ToLowerInvariant());
        });
    }
    private static void ValidateImage(byte[] bytes)
    {
        if (bytes.Length > 25 * 1024 * 1024) throw new InvalidOperationException("Escolha uma imagem de até 25 MB.");
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0 || (long)decoder.Frames[0].PixelWidth * decoder.Frames[0].PixelHeight > 45_000_000)
            throw new InvalidOperationException("Imagem inválida ou com resolução muito alta.");
    }
    private async Task SaveArt(GameItem game, string kind, byte[] bytes, string extension)
    {
        uint appId = EnsureArtAppId(game);
        string dir = SteamArtManager.GetGridDirectory(_steam.InstallPath, _steam.SelectedUser!.Id3);
        byte[] stored = bytes;
        string storedExt = extension;
        byte[]? jpeg = null;
        if (kind == "icon" && TryEncodeIcon(bytes, out byte[] png, out byte[] jpg))
        {
            stored = png;
            storedExt = ".png";
            jpeg = jpg;
        }
        await Task.Run(() =>
        {
            LibraryService.EnsureSteamClosed();
            SteamArtManager.SaveArt(dir, appId, kind, stored, storedExt);
            if (jpeg != null)
            {
                SteamArtManager.PublishLibraryIcon(_steam.InstallPath, appId, stored, jpeg);
                RememberShortcutIcon(appId, Path.Combine(dir, appId.ToString(CultureInfo.InvariantCulture) + "_icon.png"));
            }
        });
        ArtPreview = CoverLoader.Load(SteamArtManager.FindExistingArt(dir, appId, kind), 600);
        if (kind != "icon") game.Cover = ArtPreview ?? CoverLoader.Load(CoverLoader.Find(_steam, appId));
        game.Refresh();
        StudioCaption = "Capa atual";
        ArtStatus = kind == "icon"
            ? "Ícone aplicado em " + game.Title + ". Feche e abra o Steam para ele aparecer na lista."
            : "Capa aplicada em " + game.Title + ". Abra o Steam de novo para ver na biblioteca.";
        Status = ArtStatus;
    }
    private static bool TryEncodeIcon(byte[] source, out byte[] png, out byte[] jpeg)
    {
        png = Array.Empty<byte>();
        jpeg = Array.Empty<byte>();
        try
        {
            using var input = new MemoryStream(source);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;
            png = EncodeFrame(decoder.Frames[0], new PngBitmapEncoder());
            jpeg = EncodeFrame(decoder.Frames[0], new JpegBitmapEncoder { QualityLevel = 90 });
            return png.Length > 8 && jpeg.Length > 8;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException)
        {
            return false;
        }
    }
    private static byte[] EncodeFrame(BitmapFrame frame, BitmapEncoder encoder)
    {
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
    private void RememberShortcutIcon(uint appId, string iconPath)
    {
        if (_steam.SelectedUser == null) return;
        string vdf = LibraryService.ShortcutPath(_steam);
        if (!File.Exists(vdf)) return;
        var shortcuts = ShortcutsVdf.Read(vdf);
        var shortcut = shortcuts.FirstOrDefault(item => item.AppId == appId);
        if (shortcut == null) return;
        shortcut.Icon = iconPath;
        ShortcutsVdf.Write(vdf, shortcuts);
    }
    private uint EnsureArtAppId(GameItem game)
    {
        if (game.Game.AppId != 0) return game.Game.AppId;
        game.Game.AppId = LibraryService.CreateShortcut(game.Game).AppId;
        return game.Game.AppId;
    }
    private async void SearchArt_Click(object sender, RoutedEventArgs e)
    {
        await RunBusy(async () =>
        {
            if (!_gridClient.HasApiKey) throw new InvalidOperationException("Configure sua chave SteamGridDB primeiro. A importação de imagens locais funciona sem chave.");
            string query = ArtSearchName.Text.Trim();
            if (string.IsNullOrWhiteSpace(query) && ArtGameCombo.SelectedItem is GameItem selected) query = selected.Title;
            ClearArtGallery();
            int id;
            if (string.IsNullOrWhiteSpace(query))
            {
                if (!int.TryParse(ArtGameId.Text.Trim(), out id) || id <= 0) throw new InvalidOperationException("Digite o nome do jogo para buscar as capas.");
            }
            else
            {
                ArtStatus = "Buscando jogos no SteamGridDB...";
                var matches = await _gridClient.SearchGamesAsync(query);
                if (matches.Count == 0) { ArtStatus = "Nenhum jogo encontrado com esse nome. Confira a chave, a conexão ou o texto da busca."; return; }
                var exact = matches.FirstOrDefault(m => m.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase)) ?? matches[0];
                _loadingMatches = true;
                ArtMatchCombo.ItemsSource = matches;
                ArtMatchCombo.SelectedItem = exact;
                ArtMatchCombo.Visibility = matches.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                _loadingMatches = false;
                id = exact.Id;
                ArtGameId.Text = id.ToString();
            }
            await LoadArtOptions(id);
        });
    }
    private async void ArtMatch_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMatches || !_initialized || Busy) return;
        if (ArtMatchCombo.SelectedItem is not GridGameMatch match) return;
        ArtGameId.Text = match.Id.ToString();
        await RunBusy(() => LoadArtOptions(match.Id));
    }
    private async Task LoadArtOptions(int id)
    {
        var game = ArtGameCombo.SelectedItem as GameItem;
        ArtStatus = "Carregando capas...";
        var options = await _gridClient.GetArtOptionsAsync(id, ArtKind);
        _optionsGameKey = game?.Game.Key;
        _optionsKind = ArtKind;
        ArtworkOptions.Clear();
        foreach (var option in options.Take(18))
        {
            var data = await _gridClient.DownloadImageAsync(option.ThumbUrl);
            ImageSource? preview = null;
            if (data != null)
            {
                try { using var stream = new MemoryStream(data); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 300; image.StreamSource = stream; image.EndInit(); image.Freeze(); preview = image; }
                catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException or System.Runtime.InteropServices.COMException) { }
            }
            ArtworkOptions.Add(new ArtworkItem(option, preview));
        }
        SelectArt(null);
        if (ArtChoicesEmpty != null)
        {
            ArtChoicesEmpty.Visibility = ArtworkOptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ArtChoicesEmpty.Text = ArtworkOptions.Count == 0 ? "Nenhuma capa neste formato." : "";
        }
        ArtStatus = ArtworkOptions.Count > 0
            ? ArtworkOptions.Count + " capas. Clique em uma para ver ao lado."
            : "Nenhuma capa neste formato.";
    }
    private async void UseArt_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedArt == null) { ArtStatus = "Escolha uma das alternativas, ou importe uma imagem."; return; }
        var art = _selectedArt;
        await RunBusy(async () =>
        {
            RequireAccount(); var game = RequireArtGame();
            if (_optionsGameKey != game.Game.Key || _optionsKind != ArtKind) throw new InvalidOperationException("Busque novamente para o jogo e formato selecionados.");
            if (!Confirm($"Trocar a capa de {game.Title} por esta?\n\nA arte anterior fica em backup. O Steam precisa estar fechado.")) return;
            LibraryService.EnsureSteamClosed();
            byte[] bytes = await _gridClient.DownloadImageAsync(art.Option.Url) ?? throw new IOException("Falha ao baixar a imagem.");
            ValidateImage(bytes);
            string ext = art.Option.Mime switch { "image/jpeg" => ".jpg", "image/png" => ".png", "image/x-icon" or "image/vnd.microsoft.icon" => ".ico", _ => Path.GetExtension(new Uri(art.Option.Url).AbsolutePath).ToLowerInvariant() };
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".ico")) throw new InvalidOperationException("Formato não suportado. Use uma imagem PNG ou JPEG.");
            await SaveArt(game, ArtKind, bytes, ext);
        });
    }

    private void LoadCollections()
    {
        Collections.Clear();
        if (_steam.SelectedUser == null) return;
        foreach (var collection in CollectionsManager.LoadCollections(_steam.InstallPath, _steam.SelectedUser.Id3)) Collections.Add(collection);
        CollectionHeading = "Selecione uma coleção";
        CollectionDescription = "Selecione os jogos e salve. As alterações são aplicadas com o Steam fechado.";
        CollectionGames.SelectedItems.Clear();
    }
    private void CollectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionGames == null) return;
        CollectionGames.SelectedItems.Clear();
        if (CollectionsList.SelectedItem is not SteamCollection collection) return;
        CollectionHeading = collection.Name;
        CollectionDescription = collection.IsDynamic ? "Coleção dinâmica · somente leitura. Edite os filtros pelo Steam." : "Clique nos jogos para incluir ou remover. Depois salve a seleção.";
        CollectionGames.IsEnabled = !collection.IsDynamic;
        foreach (var game in IntegratedGames.Where(g => collection.Added.Contains(g.Game.AppId) && !collection.Removed.Contains(g.Game.AppId))) CollectionGames.SelectedItems.Add(game);
    }
    private async void CreateCollection_Click(object sender, RoutedEventArgs e)
    {
        string name = NewCollectionName.Text.Trim();
        if (name.Length == 0) { Status = "Digite o nome da nova coleção."; return; }
        if (!Confirm($"Criar a coleção \"{name}\" no Steam? Um backup será criado antes da alteração.")) return;
        await RunBusy(() =>
        {
            RequireAccount(); LibraryService.EnsureSteamClosed();
            var collection = new SteamCollection("uc-" + Guid.NewGuid().ToString("N"), name, new HashSet<uint>(), new HashSet<uint>());
            CollectionsManager.SaveCollection(_steam.InstallPath, _steam.SelectedUser!.Id3, collection);
            NewCollectionName.Clear(); LoadCollections(); CollectionsList.SelectedItem = Collections.FirstOrDefault(c => c.Id == collection.Id);
            Status = "Coleção criada. Abra o Steam para sincronizar."; return Task.CompletedTask;
        });
    }
    private async void SaveCollection_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionsList.SelectedItem is not SteamCollection collection || collection.IsDynamic) { Status = "Selecione uma coleção estática."; return; }
        if (!Confirm($"Salvar os jogos de \"{collection.Name}\"? Os dados atuais serão copiados para backup.")) return;
        await RunBusy(() =>
        {
            RequireAccount(); LibraryService.EnsureSteamClosed();
            var selected = CollectionGames.SelectedItems.Cast<GameItem>().Select(g => g.Game.AppId).ToHashSet();
            var known = IntegratedGames.Select(g => g.Game.AppId).ToHashSet();
            // Preserve membership for games that are not installed or visible in this app.
            var added = collection.Added.Where(id => !known.Contains(id)).Concat(selected).ToHashSet();
            var removed = collection.Removed.Concat(collection.Added.Where(id => known.Contains(id) && !selected.Contains(id))).Except(selected).ToHashSet();
            CollectionsManager.SaveCollection(_steam.InstallPath, _steam.SelectedUser!.Id3, new SteamCollection(collection.Id, collection.Name, added, removed));
            LoadCollections(); CollectionsList.SelectedItem = Collections.FirstOrDefault(c => c.Id == collection.Id);
            Status = "Coleção salva com backup."; return Task.CompletedTask;
        });
    }
    private async void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionsList.SelectedItem is not SteamCollection collection || collection.IsDynamic) return;
        if (!Confirm($"Excluir apenas a coleção \"{collection.Name}\"?\n\nNenhum jogo será desinstalado. Um backup será preservado.")) return;
        await RunBusy(() => { RequireAccount(); LibraryService.EnsureSteamClosed(); CollectionsManager.DeleteCollection(_steam.InstallPath, _steam.SelectedUser!.Id3, collection.Id); LoadCollections(); Status = "Coleção excluída com backup."; return Task.CompletedTask; });
    }

    private void BrowseSteam_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Pasta que contém steam.exe" };
        if (picker.ShowDialog(this) == true) SteamPathBox.Text = picker.FolderName;
    }
    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Pasta de jogos locais" };
        if (picker.ShowDialog(this) == true && !Folders.Contains(picker.FolderName, StringComparer.OrdinalIgnoreCase)) Folders.Add(picker.FolderName);
    }
    private void RemoveFolder_Click(object sender, RoutedEventArgs e) { if (FoldersList.SelectedItem is string folder) Folders.Remove(folder); }
    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        await RunBusy(async () =>
        {
            string path = SteamPathBox.Text.Trim().Trim('"');
            if (path.Length > 0 && !File.Exists(Path.Combine(path, "steam.exe"))) throw new InvalidOperationException("A pasta selecionada não contém steam.exe.");
            if (Folders.Any(f => !Directory.Exists(f))) throw new InvalidOperationException("Uma das pastas de jogos não está disponível. Conecte o disco ou remova a pasta da lista.");
            _config.CustomSteamPath = path; _config.SteamGridDbKey = ApiKeyBox.Password.Trim(); _config.ScannedFolders = Folders.ToList();
            _config.Save(); _gridClient.SetApiKey(_config.SteamGridDbKey);
            _steam = SteamLocator.FindSteam(path);
            var detectedAccount = _steam.SelectedUser;
            Accounts.Clear(); foreach (var user in _steam.Users) Accounts.Add(user);
            SelectedAccount = detectedAccount;
            await LoadLibrary(false);
            Status = "Configurações salvas. O arquivo da versão anterior permanece intacto.";
        });
    }
    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireAccount();
            string dir = Path.Combine(Path.GetDirectoryName(LibraryService.ShortcutPath(_steam))!, "backups_steambridge");
            if (!Directory.Exists(dir)) { Status = "Ainda não há backups de atalhos. Eles serão criados antes da primeira alteração em arquivos existentes."; return; }
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { dir } });
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async void RestoreShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Restaurar backup dos atalhos", Filter = "Backup de atalhos Steam|*.vdf" };
        if (_steam.SelectedUser != null) picker.InitialDirectory = Path.Combine(Path.GetDirectoryName(LibraryService.ShortcutPath(_steam))!, "backups_steambridge");
        if (picker.ShowDialog(this) != true) return;
        if (!Confirm("Restaurar todos os atalhos deste backup na conta selecionada?\n\nOs atalhos atuais serão copiados para outro backup antes da restauração. O Steam precisa estar fechado.")) return;
        await RunBusy(async () =>
        {
            RequireAccount(); LibraryService.EnsureSteamClosed();
            var restored = ShortcutsVdf.Read(picker.FileName);
            ShortcutsVdf.Write(LibraryService.ShortcutPath(_steam), restored);
            await LoadLibrary(_discovered); Status = "Atalhos restaurados. A versão anterior à restauração também foi preservada.";
        });
    }
    private async void RemoveShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var selected = _games.Where(g => g.Selected && g.IsShortcut).Select(g => g.Game.AppId).ToHashSet();
        if (selected.Count == 0) { Status = "Selecione atalhos integrados na biblioteca primeiro. Jogos nativos do Steam não podem ser removidos aqui."; return; }
        if (!Confirm($"Remover {selected.Count} atalho(s) do Steam?\n\nNenhum jogo será desinstalado. Um backup dos atalhos será criado.")) return;
        await RunBusy(async () =>
        {
            RequireAccount(); LibraryService.EnsureSteamClosed();
            string path = LibraryService.ShortcutPath(_steam);
            var current = ShortcutsVdf.Read(path); current.RemoveAll(s => selected.Contains(s.AppId));
            ShortcutsVdf.Write(path, current); await LoadLibrary(_discovered); Status = "Atalhos removidos com backup. Os arquivos dos jogos não foram alterados.";
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
