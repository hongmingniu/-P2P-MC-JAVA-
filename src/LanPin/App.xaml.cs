using System.Windows;

namespace LanPin.Desktop;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--self-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await Task.Run(SelfTest.Run); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-result.txt"),"FAIL\n"+ex); Shutdown(1); }
            return;
        }
        if (e.Args.FirstOrDefault() == "--ui-check")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output=e.Args.ElementAtOrDefault(1) ?? Path.Combine(AppContext.BaseDirectory,"ui-check");
            try { await UiChecks.Run(output); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(output);File.AppendAllText(Path.Combine(output,"ui-check-result.txt"),"\nFAIL\n"+ex);Shutdown(1); }
            return;
        }
        var window = new MainWindow(); MainWindow = window; window.Show();
    }
}
