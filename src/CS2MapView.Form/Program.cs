using CS2MapView.Beta;
using log4net;
namespace CS2MapView.Form;
internal static class Program
{
    private static readonly ILog Logger = LogManager.GetLogger(typeof(Program));
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            UiLocalization.Initialize(Config.UserSettings.LoadOrDefault(false), args);
            if (args.Contains("--help")) { Console.WriteLine(BetaRenderCommand.Usage); return 0; }
            if (args.Contains("--render") || args.Contains("--heightmap"))
                return BetaRenderCommand.RunAsync(args).GetAwaiter().GetResult();
            BetaSettings.Load();
            System.Windows.Forms.Application.Run(new MainForm(args.FirstOrDefault(a => !a.StartsWith('-'))));
            BetaSettings.Save();
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Application failed", ex);
            Console.Error.WriteLine(ex);
            if (!args.Contains("--render") && !args.Contains("--heightmap"))
                MessageBox.Show(ex.Message, ProjectInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
