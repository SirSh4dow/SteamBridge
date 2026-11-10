using SteamBridge.Core;
using SteamBridge.Models;

namespace SteamBridge.UI;

public class ArtSelectorModal : Form
{
    private readonly MainForm _main;
    private readonly ScannedGame _game;
    private readonly ComboBox _kindCombo = new();
    private readonly FlowLayoutPanel _flowOptions = new();
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(105, 114, 132) };
    private int? _sgdbGameId;

    public ArtSelectorModal(MainForm main, ScannedGame game)
    {
        _main = main;
        _game = game;

        Text = $"Trocar Arte — {game.Title}";
        Size = new Size(880, 680);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(245, 247, 251);

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.White, Padding = new Padding(16, 12, 16, 12) };

        var lblKind = new Label { Text = "Tipo de Arte:", AutoSize = true, Location = new Point(16, 18) };
        _kindCombo.Location = new Point(110, 15);
        _kindCombo.Width = 160;
        _kindCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _kindCombo.Items.AddRange(new object[] { "Capa Vertical", "Capa Horizontal", "Hero (Fundo)", "Logo", "Ícone" });
        _kindCombo.SelectedIndex = 0;
        _kindCombo.SelectedIndexChanged += async (_, _) => await LoadOptionsAsync();

        var customBtn = new Button { Text = "Escolher Arquivo Local...", AutoSize = true, Location = new Point(290, 13), Height = 32 };
        customBtn.FlatStyle = FlatStyle.Flat;
        customBtn.Click += (_, _) => ChooseLocalFile();

        _statusLabel.Location = new Point(480, 18);

        topPanel.Controls.AddRange(new Control[] { lblKind, _kindCombo, customBtn, _statusLabel });

        _flowOptions.Dock = DockStyle.Fill;
        _flowOptions.AutoScroll = true;
        _flowOptions.Padding = new Padding(16);
        _flowOptions.BackColor = Color.FromArgb(245, 247, 251);

        Controls.Add(_flowOptions);
        Controls.Add(topPanel);

        Load += async (_, _) =>
        {
            if (!_main.GridClient.HasApiKey)
            {
                _statusLabel.Text = "Chave da SteamGridDB não configurada. Vá em Configurações.";
                return;
            }

            _statusLabel.Text = "Buscando jogo no SteamGridDB...";
            _sgdbGameId = await _main.GridClient.SearchGameIdAsync(_game.Title);
            if (_sgdbGameId == null)
            {
                _statusLabel.Text = "Jogo não encontrado no SteamGridDB.";
                return;
            }

            await LoadOptionsAsync();
        };
    }

    private string SelectedKindKey => _kindCombo.SelectedIndex switch
    {
        1 => "wide",
        2 => "hero",
        3 => "logo",
        4 => "icon",
        _ => "portrait"
    };

    private async Task LoadOptionsAsync()
    {
        if (_sgdbGameId == null) return;

        _flowOptions.SuspendLayout();
        _flowOptions.Controls.Clear();
        _statusLabel.Text = "Buscando opções...";

        string kind = SelectedKindKey;
        var options = await _main.GridClient.GetArtOptionsAsync(_sgdbGameId.Value, kind);

        if (options.Count == 0)
        {
            _statusLabel.Text = "Nenhuma arte encontrada para este formato.";
            _flowOptions.ResumeLayout();
            return;
        }

        _statusLabel.Text = $"{options.Count} artes encontradas. Clique para aplicar.";

        foreach (var opt in options)
        {
            var card = CreateOptionCard(opt, kind);
            _flowOptions.Controls.Add(card);
        }

        _flowOptions.ResumeLayout();
    }

    private Control CreateOptionCard(ArtOption opt, string kind)
    {
        int cardW = kind == "wide" || kind == "hero" ? 240 : 140;
        int cardH = kind == "wide" ? 140 : (kind == "hero" ? 110 : 210);

        var panel = new Panel
        {
            Size = new Size(cardW, cardH + 34),
            BackColor = Color.White,
            Margin = new Padding(8),
            Cursor = Cursors.Hand
        };

        var pic = new PictureBox
        {
            Dock = DockStyle.Top,
            Height = cardH,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(235, 238, 244)
        };

        var lblDim = new Label
        {
            Text = $"{opt.Width}x{opt.Height} (Score: {opt.Score})",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 7.5F),
            ForeColor = Color.FromArgb(100, 110, 125)
        };

        panel.Controls.Add(lblDim);
        panel.Controls.Add(pic);

        // Baixa a miniatura em background
        _ = Task.Run(async () =>
        {
            var bytes = await _main.GridClient.DownloadImageAsync(opt.ThumbUrl);
            if (bytes != null)
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    var img = Image.FromStream(ms);
                    if (!IsDisposed && pic.IsHandleCreated)
                    {
                        Invoke(() => pic.Image = img);
                    }
                }
                catch { }
            }
        });

        async void ApplyArt(object? s, EventArgs e)
        {
            panel.Enabled = false;
            _statusLabel.Text = "Baixando arte em alta resolução...";

            var bytes = await _main.GridClient.DownloadImageAsync(opt.Url);
            if (bytes != null)
            {
                var user = _main.SteamInfo.SelectedUser;
                if (user != null)
                {
                    string gridDir = SteamArtManager.GetGridDirectory(_main.SteamInfo.InstallPath, user.Id3);
                    SteamArtManager.SaveArt(gridDir, _game.AppId, kind, bytes);
                    MessageBox.Show("Arte aplicada com sucesso no Steam!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            else
            {
                MessageBox.Show("Falha ao baixar imagem.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                panel.Enabled = true;
            }
        }

        panel.Click += ApplyArt;
        pic.Click += ApplyArt;
        lblDim.Click += ApplyArt;

        return panel;
    }

    private void ChooseLocalFile()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Selecione uma imagem para arte do jogo",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Todos os arquivos (*.*)|*.*"
        };

        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(ofd.FileName);
                string ext = Path.GetExtension(ofd.FileName).ToLowerInvariant();
                var user = _main.SteamInfo.SelectedUser;
                if (user != null)
                {
                    string gridDir = SteamArtManager.GetGridDirectory(_main.SteamInfo.InstallPath, user.Id3);
                    SteamArtManager.SaveArt(gridDir, _game.AppId, SelectedKindKey, bytes, ext);
                    MessageBox.Show("Arte local gravada com sucesso!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar arquivo: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
