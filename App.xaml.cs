using System;
using System.Windows;

namespace Companion;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch any startup errors and show them in a message box
        this.DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(args.Exception.Message + "\n\n" + args.Exception.StackTrace, "App Crash");
            Application.Current.Shutdown();
        };
    }
}