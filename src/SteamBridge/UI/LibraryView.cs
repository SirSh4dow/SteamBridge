using SteamBridge.Core;
using SteamBridge.Models;
using SteamBridge.Scanners;

namespace SteamBridge.UI;

public class LibraryView : UserControl
{
    private readonly MainForm _main;
    private readonly CheckBox _chkEpic = new() { Text = "Epic Games", Checked = true, AutoSize = true };
    private readonly CheckBox _chkXbox = new() { Text = "Xbox / Game Pass", Checked = true, AutoSize = true };
    private readonly CheckBox _chkFolders = new() { Text = "Pastas Locais", Checked = true, AutoSize = true };

    private readonly Button _scanBtn = new() { Text = "Escanear Jogos", AutoSize = true, BackColor = Color.FromArgb(67, 97, 238), ForeColor = Color.White };
    private readonly Button _applyBtn = new() { Text = "Aplicar Selecionados no Steam", AutoSize = true, BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White };
    private readonly Button _selectAllBtn = new() { Text = "Marcar Todos", AutoSize = true };
    private readonly Button _deselectAllBtn = new() { Text = "Desmarcar Todos", AutoSize = true };

    private readonly DataGridView _grid = new();
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(105, 114, 132) };
    private List<ScannedGame> _scannedGames = new();

    public LibraryView(MainForm main)
    {
        _main = main;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(245, 247, 251);

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Color.White, Padding = new Padding(16) };

        // Checkboxes fontes
        _chkEpic.Location = new Point(16, 16);
        _chkXbox.Location = new Point(130, 16);
        _chkFolders.Location = new Point(280, 16);

        _scanBtn.Location = new Point(410, 12);
        _scanBtn.Height = 34;
        _scanBtn.FlatStyle = FlatStyle.Flat;
        _scanBtn.FlatAppearance.BorderSize = 0;
        _scanBtn.Font = new Font("Segoe UI Semibold", 9.5F);
        _scanBtn.Cursor = Cursors.Hand;
        _scanBtn.Click += async (_, _) => await RunScanAsync();

        _applyBtn.Location = new Point(550, 12);
        _applyBtn.Height = 34;
        _applyBtn.FlatStyle = FlatStyle.Flat;
        _applyBtn.FlatAppearance.BorderSize = 0;
        _applyBtn.Font = new Font("Segoe UI Semibold", 9.5F);
        _applyBtn.Cursor = Cursors.Hand;
        _applyBtn.Click += async (_, _) => await ApplyToSteamAsync();

        _selectAllBtn.Location = new Point(16, 52);
        _selectAllBtn.Height = 28;
        _selectAllBtn.FlatStyle = FlatStyle.Flat;
        _selectAllBtn.Click += (_, _) => SetAllSelection(true);

        _deselectAllBtn.Location = new Point(126, 52);
        _deselectAllBtn.Height = 28;
        _deselectAllBtn.FlatStyle = FlatStyle.Flat;
        _deselectAllBtn.Click += (_, _) => SetAllSelection(false);

        _statusLabel.Location = new Point(260, 56);
        _statusLabel.Text = "Pronto para escanear.";

        topPanel.Controls.AddRange(new Control[] {
            _chkEpic, _chkXbox, _chkFolders, _scanBtn, _applyBtn,
            _selectAllBtn, _deselectAllBtn, _statusLabel
        });

        // Grid
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowTemplate.Height = 40;

        var colCheck = new DataGridViewCheckBoxColumn { HeaderText = "", Width = 40, DataPropertyName = "Selected" };
        var colBadge = new DataGridViewTextBoxColumn { HeaderText = "Origem", Width = 90, DataPropertyName = "Badge", ReadOnly = true };
        var colTitle = new DataGridViewTextBoxColumn { HeaderText = "Título do Jogo", Width = 280, DataPropertyName = "Title" };
        var colExe = new DataGridViewTextBoxColumn { HeaderText = "Executável / Destino", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DataPropertyName = "Exe", ReadOnly = true };
        var colStatus = new DataGridViewTextBoxColumn { HeaderText = "No Steam?", Width = 110, ReadOnly = true };

        _grid.Columns.AddRange(colCheck, colBadge, colTitle, colExe, colStatus);

        var gridContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 14, 0, 0) };
        gridContainer.Controls.Add(_grid);

        Controls.Add(gridContainer);
        Controls.Add(topPanel);
    }

    public void OnSteamUserChanged()
    {
        UpdateExistingStatus();
    }

    private void SetAllSelection(bool selected)
    {
        foreach (var g in _scannedGames)
        {
            g.Selected = selected;
        }
        _grid.Refresh();
    }

    private async Task RunScanAsync()
    {
        _scanBtn.Enabled = false;
        _statusLabel.Text = "Escaneando bibliotecas...";
        _scannedGames.Clear();
        _grid.DataSource = null;

        bool scanEpic = _chkEpic.Checked;
        bool scanXbox = _chkXbox.Checked;
        bool scanFolders = _chkFolders.Checked;
        var folderPaths = _main.Config.ScannedFolders.ToList();

        var games = await Task.Run(() =>
        {
            var list = new List<ScannedGame>();
            if (scanEpic)
            {
                try { list.AddRange(EpicScanner.Scan()); } catch { }
            }
            if (scanXbox)
            {
                try { list.AddRange(XboxScanner.Scan()); } catch { }
            }
            if (scanFolders)
            {
                foreach (var folder in folderPaths)
                {
                    try { list.AddRange(FolderScanner.ScanDirectory(folder)); } catch { }
                }
            }
            return list;
        });

        _scannedGames = games;
        UpdateExistingStatus();

        _grid.DataSource = _scannedGames;
        _grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _scannedGames.Count)
            {
                var g = _scannedGames[e.RowIndex];
                if (e.ColumnIndex == 4) // Status
                {
                    e.Value = g.AlreadyInSteam ? "✓ Já adicionado" : "Pendente";
                }
            }
        };

        _statusLabel.Text = $"Encontrados {_scannedGames.Count} jogos.";
        _scanBtn.Enabled = true;
    }

    private void UpdateExistingStatus()
    {
        var user = _main.SteamInfo.SelectedUser;
        if (user == null) return;

        string vdfPath = Path.Combine(_main.SteamInfo.InstallPath, "userdata", user.Id3, "config", "shortcuts.vdf");
        var existing = ShortcutsVdf.Read(vdfPath);

        foreach (var g in _scannedGames)
        {
            // Calcula o AppID que teria no Steam
            string targetExe = !string.IsNullOrEmpty(g.Uri) ? g.Uri : SteamUtils.QuoteIfNeeded(g.Exe);
            g.AppId = SteamUtils.CalculateShortcutAppId(targetExe, g.Title);

            g.AlreadyInSteam = existing.Any(e => e.AppId == g.AppId || e.AppName.Equals(g.Title, StringComparison.OrdinalIgnoreCase));
        }
        _grid.Refresh();
    }

    private async Task ApplyToSteamAsync()
    {
        var user = _main.SteamInfo.SelectedUser;
        if (user == null)
        {
            MessageBox.Show("Nenhuma conta Steam selecionada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var toApply = _scannedGames.Where(g => g.Selected).ToList();
        if (toApply.Count == 0)
        {
            MessageBox.Show("Nenhum jogo selecionado para aplicar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (SteamLocator.IsSteamRunning())
        {
            var res = MessageBox.Show(
                "O Steam precisa estar fechado para gravar novos atalhos com segurança.\nDeseja fechar o Steam agora?",
                "Steam em execução",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (res == DialogResult.Yes)
            {
                SteamLocator.CloseSteam();
            }
            else
            {
                return;
            }
        }

        _applyBtn.Enabled = false;
        _statusLabel.Text = "Gravando atalhos no Steam...";

        await Task.Run(() =>
        {
            string vdfPath = Path.Combine(_main.SteamInfo.InstallPath, "userdata", user.Id3, "config", "shortcuts.vdf");
            var current = ShortcutsVdf.Read(vdfPath);

            foreach (var g in toApply)
            {
                string targetExe = !string.IsNullOrEmpty(g.Uri) ? g.Uri : SteamUtils.QuoteIfNeeded(g.Exe);
                string startDir = SteamUtils.QuoteIfNeeded(g.Cwd);
                uint appId = SteamUtils.CalculateShortcutAppId(targetExe, g.Title);

                // Verifica se já existe
                var existing = current.FirstOrDefault(c => c.AppId == appId || c.AppName.Equals(g.Title, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new SteamShortcut
                    {
                        AppId = appId,
                        AppName = g.Title,
                        Exe = targetExe,
                        StartDir = startDir,
                        Icon = g.IconPath,
                        LaunchOptions = g.LaunchArgs,
                        Tags = new List<string> { g.Badge }
                    };
                    current.Add(existing);
                }
                else
                {
                    existing.Exe = targetExe;
                    existing.StartDir = startDir;
                    if (!string.IsNullOrEmpty(g.IconPath)) existing.Icon = g.IconPath;
                }
            }

            ShortcutsVdf.Write(vdfPath, current);
        });

        UpdateExistingStatus();
        _statusLabel.Text = $"{toApply.Count} jogos aplicados no shortcuts.vdf!";
        _applyBtn.Enabled = true;

        MessageBox.Show("Atalhos gravados com sucesso no Steam!", "Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
