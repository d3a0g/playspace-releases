namespace GameLauncherGUI
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if(args.Contains("--verify-package"))
            {
                using var icon=typeof(Form1).Assembly.GetManifestResourceStream("GameLauncherGUI.AppIcon");
                Environment.ExitCode=icon!=null&&RecommendedGame.All.Length==500&&UpdateService.BundledFeed()==UpdateService.DefaultFeed?0:2;
                return;
            }
            ApplicationConfiguration.Initialize();
            Application.SetCompatibleTextRenderingDefault(true);
            Application.Run(new LauncherApplicationContext());
        }
    }
}
