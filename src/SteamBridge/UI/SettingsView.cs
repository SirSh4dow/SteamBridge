using SteamBridge.Core;

namespace SteamBridge.UI;

public class SettingsView : UserControl
{
    private readonly MainForm _main;
    private readonly TextBox _keyBox = new();
    private readonly TextBox _steamPathBox = new();
    private readonly ListBox _foldersList = new();
    private readonly Button _saveBtn = new() { Text = "Salvar Configurações", AutoSize = true, BackColor = Color.FromArgb(67, 97, 238), ForeColor = Color.White };
    private readonly Button _browseSteamBtn = new() { Text = "Procurar...", AutoSize = true };
    private readonly Button _addFolderBtn = new() { Text = "Adicionar Pasta", AutoSize = true };
    private readonly Button _removeFolderBtn = new() { Text = "Remover Pasta", AutoSize = true };

    public SettingsView(MainForm main)
    {
        _main = main;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(245, 247, 251);
        AutoScroll = true;

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(30),
            BackColor = Color.White
        };

        int top = 20;

        // Chave SteamGridDB
        var lblKeyTitle = new Label { Text = "Chave da API SteamGridDB", AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5F), Location = new Point(20, top) };
        top += 26;
        var lblKeySub = new Label { Text = "Necessária para buscar capas, heroes e logos automaticamente (obtenha grátis em steamgriddb.com/profile/preferences/api).", AutoSize = true, ForeColor = Color.FromArgb(105, 114, 132), Location = new Point(20, top) };
        top += 24;
        _keyBox.Location = new Point(20, top);
        _keyBox.Width = 450;
        _keyBox.PasswordChar = '•';
        _keyBox.Text = _main.Config.SteamGridDbKey;
        panel.Controls.AddRange(new Control[] { lblKeyTitle, lblKeySub, _keyBox });

        top += 45;

        // Pasta personalizada do Steam
        var lblSteamTitle = new Label { Text = "Diretório de Instalação do Steam", AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5F), Location = new Point(20, top) };
        top += 26;
        var lblSteamSub = new Label { Text = "Deixe vazio para detecção automática pelo Registro do Windows.", AutoSize = true, ForeColor = Color.FromArgb(105, 114, 132), Location = new Point(20, top) };
        top += 24;
        _steamPathBox.Location = new Point(20, top);
        _steamPathBox.Width = 450;
        _steamPathBox.Text = string.IsNullOrEmpty(_main.Config.CustomSteamPath) ? _main.SteamInfo.InstallPath : _main.Config.CustomSteamPath;

        _browseSteamBtn.Location = new Point(480, top - 2);
        _browseSteamBtn.Height = 30;
        _browseSteamBtn.FlatStyle = FlatStyle.Flat;
        _browseSteamBtn.Click += (_, _) => BrowseSteamPath();

        panel.Controls.AddRange(new Control[] { lblSteamTitle, lblSteamSub, _steamPathBox, _browseSteamBtn });

        top += 45;

        // Pastas locais para escaneamento
        var lblFoldersTitle = new Label { Text = "Pastas de Jogos Monitoradas", AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5F), Location = new Point(20, top) };
        top += 26;
        var lblFoldersSub = new Label { Text = "Diretórios onde o SteamBridge buscará jogos locais instalados.", AutoSize = true, ForeColor = Color.FromArgb(105, 114, 132), Location = new Point(20, top) };
        top += 24;

        _foldersList.Location = new Point(20, top);
        _foldersList.Size = new Size(450, 140);
        foreach (var f in _main.Config.ScannedFolders) _foldersList.Items.Add(f);

        _addFolderBtn.Location = new Point(480, top);
        _addFolderBtn.Height = 30;
        _addFolderBtn.FlatStyle = FlatStyle.Flat;
        _addFolderBtn.Click += (_, _) => AddFolder();

        _removeFolderBtn.Location = new Point(480, top + 38);
        _removeFolderBtn.Height = 30;
        _removeFolderBtn.FlatStyle = FlatStyle.Flat;
        _removeFolderBtn.Click += (_, _) => RemoveFolder();

        panel.Controls.AddRange(new Control[] { lblFoldersTitle, lblFoldersSub, _foldersList, _addFolderBtn, _removeFolderBtn });

        top += 160;

        _saveBtn.Location = new Point(20, top);
        _saveBtn.Height = 36;
        _saveBtn.FlatStyle = FlatStyle.Flat;
        _saveBtn.FlatAppearance.BorderSize = 0;
        _saveBtn.Font = new Font("Segoe UI Semibold", 9.5F);
        _saveBtn.Cursor = Cursors.Hand;
        _saveBtn.Click += (_, _) => SaveSettings();
        panel.Controls.Add(_saveBtn);

        Controls.Add(panel);
    }

    private void BrowseSteamPath()
    {
        using var fbd = new FolderBrowserDialog { Description = "Selecione a pasta de instalação do Steam" };
        if (fbd.ShowDialog(this) == DialogResult.OK)
        {
            _steamPathBox.Text = fbd.SelectedPath;
        }
    }

    private void AddFolder()
    {
        using var fbd = new FolderBrowserDialog { Description = "Selecione a pasta onde ficam seus jogos" };
        if (fbd.ShowDialog(this) == DialogResult.OK)
        {
            if (!_foldersList.Items.Contains(fbd.SelectedPath))
            {
                _foldersList.Items.Add(fbd.SelectedPath);
            }
        }
    }

    private void RemoveFolder()
    {
        if (_foldersList.SelectedIndex >= 0)
        {
            _foldersList.Items.RemoveAt(_foldersList.SelectedIndex);
        }
    }

    private void SaveSettings()
    {
        _main.Config.SteamGridDbKey = _keyBox.Text.Trim();
        _main.Config.CustomSteamPath = _steamPathBox.Text.Trim();

        _main.Config.ScannedFolders.Clear();
        foreach (var item in _foldersList.Items)
        {
            if (item != null) _main.Config.ScannedFolders.Add(item.ToString()!);
        }

        _main.Config.Save();
        _main.GridClient.SetApiKey(_main.Config.SteamGridDbKey);
        _main.ReloadSteam();

        MessageBox.Show("Configurações salvas com sucesso!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
