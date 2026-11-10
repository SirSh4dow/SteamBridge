using System.Diagnostics;
using SteamBridge.Core;
using SteamBridge.Models;
using SteamBridge.Scanners;

namespace SteamBridge.UI;

public sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private SteamInfo _steamInfo;
    private readonly SteamGridDbClient _gridClient;

    // Header Controls
    private readonly Label _steamStatusLabel = new();
    private readonly Button _toggleSteamBtn = new();
    private readonly ComboBox _userCombo = new();

    // Navigation Buttons
    private readonly Panel _navPanel = new();
    private readonly Button _navLibraryBtn = new();
    private readonly Button _navArtBtn = new();
    private readonly Button _navCollectionsBtn = new();
    private readonly Button _navSettingsBtn = new();

    // Content Panel
    private readonly Panel _contentContainer = new();

    // Views
    private readonly LibraryView _libraryView;
    private readonly ArtView _artView;
    private readonly CollectionsView _collectionsView;
    private readonly SettingsView _settingsView;

    public MainForm()
    {
        _config = AppConfig.Load();
        _steamInfo = SteamLocator.FindSteam(_config.CustomSteamPath);
        _gridClient = new SteamGridDbClient(_config.SteamGridDbKey);

        // Inicializa Views antes dos eventos do header
        _libraryView = new LibraryView(this);
        _artView = new ArtView(this);
        _collectionsView = new CollectionsView(this);
        _settingsView = new SettingsView(this);

        Text = "SteamBridge";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        Size = new Size(1100, 750);
        BackColor = Color.FromArgb(245, 247, 251);
        Font = new Font("Segoe UI", 10F);

        // Header
        var header = CreateHeader();
        Controls.Add(header);

        // Sidebar Navigation
        CreateNavPanel();
        Controls.Add(_navPanel);

        // Content Area
        _contentContainer.Dock = DockStyle.Fill;
        _contentContainer.BackColor = Color.FromArgb(245, 247, 251);
        _contentContainer.Padding = new Padding(20);
        Controls.Add(_contentContainer);
        _contentContainer.BringToFront();

        // Seleciona a primeira aba
        SwitchView(_libraryView, _navLibraryBtn);

        // Timer para monitorar status da Steam (aberta/fechada)
        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        timer.Tick += (_, _) => UpdateSteamStatus();
        timer.Start();

        UpdateSteamStatus();
    }

    public SteamInfo SteamInfo => _steamInfo;
    public AppConfig Config => _config;
    public SteamGridDbClient GridClient => _gridClient;

    public void ReloadSteam()
    {
        _steamInfo = SteamLocator.FindSteam(_config.CustomSteamPath);
        PopulateUsers();
        UpdateSteamStatus();
    }

    private Panel CreateHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = Color.White,
            Padding = new Padding(24, 14, 24, 14)
        };

        var logoIcon = new Label
        {
            Text = "\u25b2",
            Font = new Font("Segoe UI Symbol", 20F, FontStyle.Bold),
            ForeColor = Color.FromArgb(67, 97, 238),
            AutoSize = true,
            Location = new Point(24, 22)
        };

        var title = new Label
        {
            Text = "SteamBridge",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 30, 44),
            AutoSize = true,
            Location = new Point(62, 16)
        };

        var subtitle = new Label
        {
            Text = "Integre Epic, Xbox e Jogos Locais ao Steam com arte oficial e coleções",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(105, 114, 132),
            AutoSize = true,
            Location = new Point(65, 48)
        };

        header.Controls.AddRange(new Control[] { logoIcon, title, subtitle });

        // User combo
        var userLabel = new Label
        {
            Text = "Conta Steam:",
            AutoSize = true,
            ForeColor = Color.FromArgb(105, 114, 132),
            Font = new Font("Segoe UI", 9F)
        };
        _userCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _userCombo.Width = 180;
        _userCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_userCombo.SelectedItem is SteamUser u)
            {
                _steamInfo.SelectedUser = u;
                OnUserChanged();
            }
        };

        PopulateUsers();

        // Steam Running Status
        _steamStatusLabel.AutoSize = true;
        _steamStatusLabel.Font = new Font("Segoe UI Semibold", 9.5F);

        _toggleSteamBtn.Size = new Size(110, 34);
        _toggleSteamBtn.FlatStyle = FlatStyle.Flat;
        _toggleSteamBtn.FlatAppearance.BorderSize = 0;
        _toggleSteamBtn.Font = new Font("Segoe UI Semibold", 9F);
        _toggleSteamBtn.Cursor = Cursors.Hand;
        _toggleSteamBtn.Click += (_, _) => ToggleSteam();

        void LayoutRightControls()
        {
            int right = header.ClientSize.Width - 24;
            _toggleSteamBtn.Location = new Point(right - _toggleSteamBtn.Width, 26);
            _steamStatusLabel.Location = new Point(_toggleSteamBtn.Left - _steamStatusLabel.PreferredWidth - 14, 34);
            _userCombo.Location = new Point(_steamStatusLabel.Left - _userCombo.Width - 20, 30);
            userLabel.Location = new Point(_userCombo.Left - userLabel.PreferredWidth - 8, 34);
        }

        header.Controls.AddRange(new Control[] { userLabel, _userCombo, _steamStatusLabel, _toggleSteamBtn });
        header.Resize += (_, _) => LayoutRightControls();
        LayoutRightControls();

        return header;
    }

    private void PopulateUsers()
    {
        _userCombo.Items.Clear();
        foreach (var u in _steamInfo.Users)
        {
            _userCombo.Items.Add(u);
        }
        if (_steamInfo.SelectedUser != null && _userCombo.Items.Contains(_steamInfo.SelectedUser))
        {
            _userCombo.SelectedItem = _steamInfo.SelectedUser;
        }
        else if (_userCombo.Items.Count > 0)
        {
            _userCombo.SelectedIndex = 0;
        }
    }

    private void CreateNavPanel()
    {
        _navPanel.Dock = DockStyle.Left;
        _navPanel.Width = 200;
        _navPanel.BackColor = Color.FromArgb(240, 243, 248);
        _navPanel.Padding = new Padding(12, 16, 12, 16);

        ConfigureNavButton(_navLibraryBtn, "  Adicionar Jogos", (_, _) => SwitchView(_libraryView, _navLibraryBtn));
        ConfigureNavButton(_navArtBtn, "  Artes & Capas", (_, _) => SwitchView(_artView, _navArtBtn));
        ConfigureNavButton(_navCollectionsBtn, "  Coleções", (_, _) => SwitchView(_collectionsView, _navCollectionsBtn));
        ConfigureNavButton(_navSettingsBtn, "  Configurações", (_, _) => SwitchView(_settingsView, _navSettingsBtn));

        _navPanel.Controls.Add(_navSettingsBtn);
        _navPanel.Controls.Add(_navCollectionsBtn);
        _navPanel.Controls.Add(_navArtBtn);
        _navPanel.Controls.Add(_navLibraryBtn);

        int top = 16;
        foreach (Control btn in _navPanel.Controls.Cast<Control>().Reverse())
        {
            btn.Location = new Point(12, top);
            top += 48;
        }
    }

    private void ConfigureNavButton(Button btn, string text, EventHandler onClick)
    {
        btn.Text = text;
        btn.Size = new Size(176, 42);
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 0;
        btn.Font = new Font("Segoe UI Semibold", 9.5F);
        btn.TextAlign = ContentAlignment.MiddleLeft;
        btn.ForeColor = Color.FromArgb(80, 90, 105);
        btn.BackColor = Color.Transparent;
        btn.Cursor = Cursors.Hand;
        btn.Click += onClick;
    }

    private void SwitchView(Control view, Button activeBtn)
    {
        foreach (Control c in _navPanel.Controls)
        {
            if (c is Button b)
            {
                b.BackColor = Color.Transparent;
                b.ForeColor = Color.FromArgb(80, 90, 105);
            }
        }
        activeBtn.BackColor = Color.FromArgb(226, 232, 240);
        activeBtn.ForeColor = Color.FromArgb(67, 97, 238);

        _contentContainer.Controls.Clear();
        view.Dock = DockStyle.Fill;
        _contentContainer.Controls.Add(view);

        if (view is ArtView art)
        {
            art.OnSteamUserChanged();
        }
        else if (view is CollectionsView col)
        {
            col.OnSteamUserChanged();
        }
    }

    private void UpdateSteamStatus()
    {
        bool running = SteamLocator.IsSteamRunning();
        if (running)
        {
            _steamStatusLabel.Text = "\u25cf Steam Aberto";
            _steamStatusLabel.ForeColor = Color.FromArgb(46, 125, 50); // Verde
            _toggleSteamBtn.Text = "Fechar Steam";
            _toggleSteamBtn.BackColor = Color.FromArgb(239, 68, 68); // Vermelho
            _toggleSteamBtn.ForeColor = Color.White;
        }
        else
        {
            _steamStatusLabel.Text = "\u25cb Steam Fechado";
            _steamStatusLabel.ForeColor = Color.FromArgb(120, 120, 120);
            _toggleSteamBtn.Text = "Abrir Steam";
            _toggleSteamBtn.BackColor = Color.FromArgb(67, 97, 238); // Azul
            _toggleSteamBtn.ForeColor = Color.White;
        }
    }

    private void ToggleSteam()
    {
        if (SteamLocator.IsSteamRunning())
        {
            _toggleSteamBtn.Enabled = false;
            _toggleSteamBtn.Text = "Fechando...";
            Task.Run(() =>
            {
                SteamLocator.CloseSteam();
                Invoke(UpdateSteamStatus);
                Invoke(() => _toggleSteamBtn.Enabled = true);
            });
        }
        else
        {
            SteamLocator.StartSteam(_steamInfo.ExePath);
            UpdateSteamStatus();
        }
    }

    private void OnUserChanged()
    {
        _libraryView?.OnSteamUserChanged();
        _artView?.OnSteamUserChanged();
        _collectionsView?.OnSteamUserChanged();
    }
}
