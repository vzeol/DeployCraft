using System.Windows;

namespace DeployCraft.Console;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        UiLanguage.Init();
        Theme.Init(this);
        base.OnStartup(e);
    }
}
