using SteamBridge.UI;

namespace SteamBridge;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");
        try
        {
            File.AppendAllText(logPath, $"[1] Main iniciado em {DateTime.Now}\n");
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            File.AppendAllText(logPath, "[2] SetHighDpiMode ok\n");
            Application.SetCompatibleTextRenderingDefault(false);
            File.AppendAllText(logPath, "[3] SetCompatibleTextRenderingDefault ok\n");
            
            var form = new MainForm();
            File.AppendAllText(logPath, "[4] MainForm instanciado ok\n");
            
            form.Shown += (_, _) => File.AppendAllText(logPath, "[5] Form exibido na tela (Shown)!\n");
            form.FormClosed += (s, e) => File.AppendAllText(logPath, $"[6] Form fechado com razão: {e.CloseReason}\n");

            Application.Run(form);
            File.AppendAllText(logPath, "[7] Application.Run finalizado normalmente\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"[ERRO FATAL] {ex}\n");
            MessageBox.Show($"Erro ao iniciar SteamBridge:\n\n{ex}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
