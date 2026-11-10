using System.Windows;
using System.Windows.Threading;

namespace SteamBridge.Desktop;

public partial class App : Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, "Local\\SteamBridge.Desktop", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("O SteamBridge já está aberto. Procure a janela na barra de tarefas.", "SteamBridge");
            Shutdown(); return;
        }
        DispatcherUnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Não foi possível concluir a operação.\n\n" + e.Exception.Message,
            "SteamBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
