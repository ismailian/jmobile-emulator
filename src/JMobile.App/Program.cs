using JMobile.App.Hosting;
using JMobile.App.Views;

namespace JMobile.App;

/// <summary>Application entry point.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // A crash inside a game must not take the shell down with it: the emulator catches
        // guest failures, but a defect on the UI thread deserves a readable report rather than
        // the default Windows Forms exception box.
        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        string? dataRoot = ReadDataRootOption(args);

        using var controller = new EmulatorController(dataRoot);
        using var form = new MainForm(controller);

        // A path on the command line is imported and launched, so a JAR can be opened directly
        // from the shell or associated with the emulator.
        string? startupJar = args.FirstOrDefault(
            argument => argument.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));

        if (startupJar is not null && File.Exists(startupJar))
        {
            form.Shown += (_, _) =>
            {
                try
                {
                    controller.Launch(controller.AddGame(startupJar));
                }
                catch (Exception error)
                {
                    MessageBox.Show(form, error.Message, "Could not open that game",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
        }

        Application.Run(form);
    }

    /// <summary>Allows the data folder to be redirected, which makes a portable install possible.</summary>
    private static string? ReadDataRootOption(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--data", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void ReportFatal(Exception? error)
    {
        if (error is null) return;

        MessageBox.Show(
            error.ToString(),
            "JMobile has hit an unexpected error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
