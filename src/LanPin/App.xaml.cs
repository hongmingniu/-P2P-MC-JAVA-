using Microsoft.UI.Xaml;
namespace LanPin.Desktop;
public partial class App:Application
{
    MainWindow? window;
    public App()
    {
        UnhandledException+=(_,e)=>
        {
            try
            {
                var command=Environment.GetCommandLineArgs();string folder=command.ElementAtOrDefault(1)=="--ui-check"?Path.GetFullPath(command.ElementAtOrDefault(2)??"ui-check"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LanPin");
                Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"unhandled-error.txt"),e.Exception.ToString());
            }
            catch{}
        };
        InitializeComponent();
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var command=Environment.GetCommandLineArgs();string mode=command.ElementAtOrDefault(1)??"";
        string? output=command.ElementAtOrDefault(2);
        try
        {
            if(mode=="--self-test"){await Task.Run(SelfTest.Run);Exit();return;}
            window=new MainWindow(mode=="--ui-check"?new AppearanceStore(Path.Combine(Path.GetFullPath(output??"ui-check"),"profile")):new AppearanceStore(),mode=="--ui-check");
            window.Activate();await window.Loaded;
            if(mode=="--smoke-check")
            {File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"smoke-result.txt"),"PASS WinUI 3 native window loaded\n"+typeof(Window).Assembly.FullName);Exit();}
            else if(mode=="--ui-check"){await UiChecks.Run(window,output??"ui-check");Exit();}
        }
        catch(Exception ex)
        {
            string folder=mode=="--ui-check"?Path.GetFullPath(output??"ui-check"):mode=="--self-test"?AppContext.BaseDirectory:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LanPin");
            Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,mode=="--ui-check"?"ui-check-result.txt":mode=="--self-test"?"self-test-result.txt":"last-startup-error.txt"),"\nFAIL\n"+ex);Environment.ExitCode=1;Exit();
        }
    }
}
