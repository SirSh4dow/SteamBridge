using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SteamBridge.Desktop;

public sealed class GameDetailsWindow : Window
{
    public GameDetailsWindow(GameItem item)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = item.Title + " · SteamBridge";
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkFrame(this);
        Width = 680; Height = 560; MinHeight = 520; MinWidth = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var accent = new Border { Background = item.CoverBrush };
        root.Children.Add(accent);
        var body = new Grid { Margin = new Thickness(28, 22, 28, 22) };
        Grid.SetRow(body, 1);
        Grid.SetRowSpan(body, 3);
        root.Children.Add(body);
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = item.Badge + "  ·  " + item.Subtitle, Foreground = item.SourceBrush, FontSize = 11, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = item.Title, FontSize = 26, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 18) });
        body.Children.Add(heading);
        var form = new StackPanel();
        var scroller = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroller, 1);
        body.Children.Add(scroller);
        TextBox Field(string label, string text, bool readOnly)
        {
            form.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 0, 0, 7), FontSize = 11 });
            var box = new TextBox { Text = text, IsReadOnly = readOnly, Margin = new Thickness(0, 0, 0, 15) };
            form.Children.Add(box); return box;
        }
        var title = Field("NOME NO STEAM", item.Title, item.Source == "steam");
        bool launchLocked = item.IsIntegrated || !string.IsNullOrEmpty(item.Game.Aumid);
        var executable = Field("EXECUTÁVEL / DESTINO", item.Game.Exe, launchLocked);
        var arguments = Field("ARGUMENTOS DE INICIALIZAÇÃO", item.Game.LaunchArgs, launchLocked);
        var workingDirectory = Field("DIRETÓRIO DE TRABALHO", item.Game.Cwd, launchLocked);
        if (!string.IsNullOrEmpty(item.Game.Aumid)) Field("IDENTIDADE XBOX / UWP", item.Game.Aumid, true);
        form.Children.Add(new TextBlock
        {
            Text = item.Source == "steam"
                ? "Este jogo pertence à biblioteca Steam. O nome é definido pela própria Steam."
                : item.IsIntegrated
                    ? "Você pode corrigir o nome. Feche o Steam antes de salvar para gravar o atalho. O executável de um jogo já integrado não muda por aqui."
                    : "Altere o nome que vai aparecer no Steam. Os ajustes são gravados quando você integrar o jogo.",
            Foreground = new SolidColorBrush(Color.FromRgb(151, 157, 181)), TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        Grid.SetRow(actions, 2);
        body.Children.Add(actions);
        var play = new Button { Content = "Jogar", Margin = new Thickness(0, 0, 10, 0) };
        play.Click += (_, _) =>
        {
            try { LibraryService.Launch(item); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Não foi possível iniciar", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        actions.Children.Add(play);
        var save = new Button { Content = item.Source == "steam" ? "Fechar" : "Salvar ajustes", Style = (Style)FindResource("PrimaryButton"), IsDefault = true };
        save.Click += (_, _) =>
        {
            if (item.Source != "steam")
            {
                if (string.IsNullOrWhiteSpace(title.Text)) { MessageBox.Show(this, "Informe o título do jogo."); return; }
                if (!launchLocked)
                {
                    if (string.IsNullOrEmpty(item.Game.Aumid) && !File.Exists(executable.Text.Trim().Trim('"'))) { MessageBox.Show(this, "Selecione um executável existente."); return; }
                    string cwd = workingDirectory.Text.Trim().Trim('"');
                    if (cwd.Length > 0 && !Directory.Exists(cwd)) { MessageBox.Show(this, "O diretório de trabalho não existe."); return; }
                    item.Game.Exe = executable.Text.Trim().Trim('"');
                    item.Game.LaunchArgs = arguments.Text;
                    item.Game.Cwd = cwd;
                }
                item.Game.Title = title.Text.Trim();
            }
            DialogResult = true;
        };
        actions.Children.Add(save); Content = root;
    }
}
