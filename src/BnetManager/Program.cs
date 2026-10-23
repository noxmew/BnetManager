using BnetManager.Cli;

namespace BnetManager;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            CliConsole.Initialize();
            return CliRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
