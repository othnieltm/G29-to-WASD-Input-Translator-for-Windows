using System.Configuration;
using System.Data;
using System.Windows;

namespace G29WASDTranslator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static readonly System.Threading.Mutex SingleInstance =
        new(initiallyOwned: true, @"Local\G29WASDTranslator-SingleInstance");

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (!SingleInstance.WaitOne(0, false))
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}

