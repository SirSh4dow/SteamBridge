using SteamBridge.Core;
using SteamBridge.Models;

namespace SteamBridge.UI;

public class ArtView : UserControl
{
    private readonly MainForm _main;
    private readonly FlowLayoutPanel _flowGames = new();
    private readonly TextBox _searchBox = new();
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(105, 114, 132) };
    private readonly Button _refreshBtn = new() { Text = "Recarregar Lista", AutoSize = true };

    private List<ScannedGame> _allGames = new();

    public ArtView(MainForm main)
    {
        _main = main;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(245, 247, 251);

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.White, Padding = new Padding(16, 12, 16, 12) };

        var lblSearch = new Label { Text = "Buscar jogo:", AutoSize = true, Location = new Point(16, 18) };
        _searchBox.Location = new Point(106, 15);
        _searchBox.Width = 260;
        _searchBox.PlaceholderText = "Filtrar por nome...";
        _searchBox.TextChanged += (_, _) => FilterCards();

        _refreshBtn.Location = new Point(380, 13);
        _refreshBtn.Height = 32;
        _refreshBtn.FlatStyle = FlatStyle.Flat;
        _refreshBtn.Click += async (_, _) => await LoadAllGamesAsync();

        _statusLabel.Location = new Point(510, 18);

        topPanel.Controls.AddRange(new Control[] { lblSearch, _searchBox, _refreshBtn, _statusLabel });

        _flowGames.Dock = DockStyle.Fill;
        _flowGames.AutoScroll = true;
        _flowGames.Padding = new Padding(16);
        _flowGames.BackColor = Color.FromArgb(245, 247, 251);

        Controls.Add(_flowGames);
        Controls.Add(topPanel);
    }

    public void OnSteamUserChanged()
    {
        _ = LoadAllGamesAsync();
    }

    private async Task LoadAllGamesAsync()
    {
        _statusLabel.Text = "Carregando jogos instalados e atalhos...";
        _refreshBtn.Enabled = false;
        _flowGames.Controls.Clear();
        _allGames.Clear();

        var user = _main.SteamInfo.SelectedUser;
        var steamPath = _main.SteamInfo.InstallPath;
        var libraries = _main.SteamInfo.Libraries;

        await Task.Run(() =>
        {
            // 1. Jogos nativos da Steam (ACF)
            var nativeSteam = SteamInstalledScanner.ScanInstalledGames(libraries);
            _allGames.AddRange(nativeSteam);

            // 2. Atalhos não-Steam do shortcuts.vdf
            if (user != null)
            {
                string vdfPath = Path.Combine(steamPath, "userdata", user.Id3, "config", "shortcuts.vdf");
                var shortcuts = ShortcutsVdf.Read(vdfPath);
                foreach (var s in shortcuts)
                {
                    _allGames.Add(new ScannedGame
                    {
                        Key = $"shortcut:{s.AppId}",
                        Title = s.AppName,
                        Source = s.Tags.FirstOrDefault()?.ToLowerInvariant() ?? "custom",
                        AppId = s.AppId,
                        Exe = s.Exe,
                        AlreadyInSteam = true
                    });
                }
            }
        });

        _statusLabel.Text = $"{_allGames.Count} jogos disponíveis para troca de artes.";
        _refreshBtn.Enabled = true;
        FilterCards();
    }

    private void FilterCards()
    {
        _flowGames.SuspendLayout();
        _flowGames.Controls.Clear();

        string q = _searchBox.Text.Trim().ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(q)
            ? _allGames
            : _allGames.Where(g => g.Title.ToLowerInvariant().Contains(q)).ToList();

        var user = _main.SteamInfo.SelectedUser;
        string gridDir = user != null ? SteamArtManager.GetGridDirectory(_main.SteamInfo.InstallPath, user.Id3) : "";

        foreach (var g in filtered)
        {
            var card = CreateGameCard(g, gridDir);
            _flowGames.Controls.Add(card);
        }

        _flowGames.ResumeLayout();
    }

    private Control CreateGameCard(ScannedGame game, string gridDir)
    {
        var panel = new Panel
        {
            Size = new Size(160, 260),
            BackColor = Color.White,
            Margin = new Padding(10),
            Cursor = Cursors.Hand
        };

        var pic = new PictureBox
        {
            Dock = DockStyle.Top,
            Height = 200,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(235, 238, 244)
        };

        string? existingCover = SteamArtManager.FindExistingArt(gridDir, game.AppId, "portrait");
        if (!string.IsNullOrEmpty(existingCover))
        {
            try
            {
                using var fs = new FileStream(existingCover, FileMode.Open, FileAccess.Read);
                pic.Image = Image.FromStream(fs);
            }
            catch { }
        }

        var lblTitle = new Label
        {
            Text = game.Title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 8.5F),
            ForeColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(4)
        };

        panel.Controls.Add(lblTitle);
        panel.Controls.Add(pic);

        // Click abre modal para trocar capas
        void OpenModal(object? s, EventArgs e)
        {
            using var modal = new ArtSelectorModal(_main, game);
            if (modal.ShowDialog(this) == DialogResult.OK)
            {
                // Atualiza a capa atual do card
                string? newCover = SteamArtManager.FindExistingArt(gridDir, game.AppId, "portrait");
                if (!string.IsNullOrEmpty(newCover))
                {
                    try
                    {
                        using var fs = new FileStream(newCover, FileMode.Open, FileAccess.Read);
                        pic.Image?.Dispose();
                        pic.Image = Image.FromStream(fs);
                    }
                    catch { }
                }
            }
        }

        panel.Click += OpenModal;
        pic.Click += OpenModal;
        lblTitle.Click += OpenModal;

        return panel;
    }
}
