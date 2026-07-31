using System.Runtime.InteropServices;
using SDProjectEdit.App.Cli;
using SDProjectEdit.App.Ui;

namespace SDProjectEdit.App;

internal static class Program
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [STAThread]
    private static int Main(string[] args)
    {
        // Sin argumentos, con 'gui', con --gui, o con un path suelto (SDProjectEdit.exe X:\MiSolution)
        // se abre la ventana. Con un comando conocido, modo CLI. Cualquier otra cosa es un error de uso.
        var explicitGui = args.Length == 0
            || args[0].Equals("gui", StringComparison.OrdinalIgnoreCase)
            || args.Any(a => a.Equals("--gui", StringComparison.OrdinalIgnoreCase));

        var wantsGui = explicitGui
            || (!CommandLine.IsCommand(args[0]) && !args[0].StartsWith('-')
                && (Directory.Exists(args[0]) || File.Exists(args[0])));

        if (!wantsGui) return CommandLine.Run(args);

        // El exe es de subsistema consola para que el modo CLI se comporte como cualquier
        // herramienta de build (redirección, exit codes). Al abrir la ventana soltamos la consola.
        FreeConsole();

        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);

        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                LogCrash(e.Exception);
                MessageBox.Show(e.Exception.ToString(), "Error inesperado", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            var initialPath = args.FirstOrDefault(a =>
                !a.StartsWith('-')
                && !a.Equals("gui", StringComparison.OrdinalIgnoreCase)
                && !CommandLine.IsCommand(a));
            Application.Run(new MainForm(initialPath));
            return 0;
        }
        catch (Exception ex)
        {
            var path = LogCrash(ex);
            MessageBox.Show($"{ex.Message}\n\nDetalle en:\n{path}", "SDProjectEdit no pudo arrancar",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>Deja el detalle del fallo en %LOCALAPPDATA%\SDProjectEdit\crash.log.</summary>
    private static string LogCrash(Exception? ex)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SDProjectEdit");
        var path = Path.Combine(directory, "crash.log");
        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"--- {DateTime.Now:u} ---{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* si ni siquiera podemos loguear, no hay mucho más que hacer */ }
        return path;
    }
}
