using SteamBridge.Core;
using SteamBridge.Models;

namespace SteamBridge.UI;

public class CollectionsView : UserControl
{
    private readonly MainForm _main;
    private readonly ListBox _collectionsList = new();
    private readonly CheckedListBox _gamesCheckedList = new();
    private readonly TextBox _newColNameBox = new();
    private readonly Button _createColBtn = new() { Text = "Criar Coleção", AutoSize = true };
    private readonly Button _saveChangesBtn = new() { Text = "Salvar Alterações", AutoSize = true, BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White };
    private readonly Button _deleteColBtn = new() { Text = "Excluir Coleção", AutoSize = true, BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White };
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(105, 114, 132) };

    private List<SteamCollection> _collections = new();
    private List<ScannedGame> _allGames = new();
    private SteamCollection? _selectedCollection;

    public CollectionsView(MainForm main)
    {
        _main = main;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(245, 247, 251);

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.White, Padding = new Padding(16, 12, 16, 12) };

        var lblNew = new Label { Text = "Nova coleção:", AutoSize = true, Location = new Point(16, 18) };
        _newColNameBox.Location = new Point(116, 15);
        _newColNameBox.Width = 200;
        _newColNameBox.PlaceholderText = "Ex: Jogos Xbox...";

        _createColBtn.Location = new Point(326, 13);
        _createColBtn.Height = 32;
        _createColBtn.FlatStyle = FlatStyle.Flat;
        _createColBtn.Click += (_, _) => CreateCollection();

        _saveChangesBtn.Location = new Point(446, 13);
        _saveChangesBtn.Height = 32;
        _saveChangesBtn.FlatStyle = FlatStyle.Flat;
        _saveChangesBtn.FlatAppearance.BorderSize = 0;
        _saveChangesBtn.Click += (_, _) => SaveChanges();

        _deleteColBtn.Location = new Point(586, 13);
        _deleteColBtn.Height = 32;
        _deleteColBtn.FlatStyle = FlatStyle.Flat;
        _deleteColBtn.FlatAppearance.BorderSize = 0;
        _deleteColBtn.Click += (_, _) => DeleteCollection();

        _statusLabel.Location = new Point(716, 18);

        topPanel.Controls.AddRange(new Control[] {
            lblNew, _newColNameBox, _createColBtn, _saveChangesBtn, _deleteColBtn, _statusLabel
        });

        // Split container para coleções à esquerda e jogos à direita
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 280,
            Padding = new Padding(16)
        };

        var leftBox = new GroupBox { Text = "Coleções do Steam", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 9F) };
        _collectionsList.Dock = DockStyle.Fill;
        _collectionsList.BorderStyle = BorderStyle.None;
        _collectionsList.Font = new Font("Segoe UI", 10F);
        _collectionsList.SelectedIndexChanged += (_, _) => OnCollectionSelected();
        leftBox.Controls.Add(_collectionsList);
        split.Panel1.Controls.Add(leftBox);

        var rightBox = new GroupBox { Text = "Jogos na Coleção (Marque para incluir)", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 9F) };
        _gamesCheckedList.Dock = DockStyle.Fill;
        _gamesCheckedList.BorderStyle = BorderStyle.None;
        _gamesCheckedList.Font = new Font("Segoe UI", 9.5F);
        _gamesCheckedList.CheckOnClick = true;
        rightBox.Controls.Add(_gamesCheckedList);
        split.Panel2.Controls.Add(rightBox);

        Controls.Add(split);
        Controls.Add(topPanel);
    }

    public void OnSteamUserChanged()
    {
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _statusLabel.Text = "Carregando coleções...";
        _collectionsList.Items.Clear();
        _gamesCheckedList.Items.Clear();
        _allGames.Clear();

        var user = _main.SteamInfo.SelectedUser;
        if (user == null)
        {
            _statusLabel.Text = "Nenhuma conta selecionada.";
            return;
        }

        var steamPath = _main.SteamInfo.InstallPath;
        var libraries = _main.SteamInfo.Libraries;

        await Task.Run(() =>
        {
            // Coleções
            _collections = CollectionsManager.LoadCollections(steamPath, user.Id3);

            // Jogos Steam + atalhos
            var native = SteamInstalledScanner.ScanInstalledGames(libraries);
            _allGames.AddRange(native);

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
                    Exe = s.Exe
                });
            }
        });

        foreach (var col in _collections)
        {
            _collectionsList.Items.Add(col.Name);
        }

        if (_collectionsList.Items.Count > 0)
        {
            _collectionsList.SelectedIndex = 0;
        }

        _statusLabel.Text = $"{_collections.Count} coleções carregadas.";
    }

    private void OnCollectionSelected()
    {
        int idx = _collectionsList.SelectedIndex;
        if (idx < 0 || idx >= _collections.Count)
        {
            _selectedCollection = null;
            _gamesCheckedList.Items.Clear();
            return;
        }

        _selectedCollection = _collections[idx];
        _gamesCheckedList.BeginUpdate();
        _gamesCheckedList.Items.Clear();

        for (int i = 0; i < _allGames.Count; i++)
        {
            var g = _allGames[i];
            bool isInCollection = _selectedCollection.Added.Contains(g.AppId) && !_selectedCollection.Removed.Contains(g.AppId);
            _gamesCheckedList.Items.Add($"[{g.Badge}] {g.Title}", isInCollection);
        }
        _gamesCheckedList.EndUpdate();
    }

    private void CreateCollection()
    {
        string name = _newColNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Informe um nome para a coleção.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var user = _main.SteamInfo.SelectedUser;
        if (user == null) return;

        string colId = Guid.NewGuid().ToString("N")[..12];
        var newCol = new SteamCollection(colId, name, new HashSet<uint>(), new HashSet<uint>());

        CollectionsManager.SaveCollection(_main.SteamInfo.InstallPath, user.Id3, newCol);
        _newColNameBox.Clear();
        _ = LoadDataAsync();
    }

    private void SaveChanges()
    {
        if (_selectedCollection == null) return;
        var user = _main.SteamInfo.SelectedUser;
        if (user == null) return;

        if (SteamLocator.IsSteamRunning())
        {
            var res = MessageBox.Show(
                "O Steam precisa estar fechado para alterar coleções.\nDeseja fechar o Steam agora?",
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

        for (int i = 0; i < _allGames.Count; i++)
        {
            var g = _allGames[i];
            bool isChecked = _gamesCheckedList.GetItemChecked(i);
            if (isChecked)
            {
                _selectedCollection.Added.Add(g.AppId);
                _selectedCollection.Removed.Remove(g.AppId);
            }
            else
            {
                _selectedCollection.Added.Remove(g.AppId);
                _selectedCollection.Removed.Add(g.AppId);
            }
        }

        CollectionsManager.SaveCollection(_main.SteamInfo.InstallPath, user.Id3, _selectedCollection);
        MessageBox.Show("Coleção salva com sucesso!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void DeleteCollection()
    {
        if (_selectedCollection == null) return;
        var user = _main.SteamInfo.SelectedUser;
        if (user == null) return;

        var res = MessageBox.Show(
            $"Deseja realmente excluir a coleção '{_selectedCollection.Name}'?\n(Os jogos continuarão no Steam)",
            "Confirmar exclusão",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning
        );

        if (res == DialogResult.Yes)
        {
            CollectionsManager.DeleteCollection(_main.SteamInfo.InstallPath, user.Id3, _selectedCollection.Id);
            _ = LoadDataAsync();
        }
    }
}
