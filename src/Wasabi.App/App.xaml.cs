using System.Windows;
using Wasabi.Core.Diagnostics;

namespace Wasabi.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            WasabiLog.Error("Eccezione non gestita nel thread UI.", e.Exception);
            e.Handled = true;
            MessageBox.Show(
                $"WASABI ha rilevato un errore. Il dettaglio è in:{Environment.NewLine}{WasabiLog.FilePath}",
                "Errore WASABI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WasabiLog.Error("Eccezione non gestita nel processo.", e.ExceptionObject as Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        WasabiLog.Info($"WASABI avviata. Argomenti: {string.Join(" ", e.Args)}");
        base.OnStartup(e);
    }
}
