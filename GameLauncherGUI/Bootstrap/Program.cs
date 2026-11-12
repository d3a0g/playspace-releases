namespace GameLauncherGUI
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if(args.Length==2&&args[0]=="--cleanup-update")LauncherUpdater.Cleanup(args[1]);
            if(args.Contains("--verify-package"))
            {
                using var icon=typeof(Form1).Assembly.GetManifestResourceStream("GameLauncherGUI.AppIcon");
                Environment.ExitCode=icon!=null&&RecommendedGame.All.Length==500&&UpdateService.BundledFeed()==UpdateService.DefaultFeed?0:2;
                return;
            }
            ApplicationConfiguration.Initialize();
            Application.SetCompatibleTextRenderingDefault(true);
            if(args.Length==2&&args[0]=="--apply-update")
            {
                Application.Run(new LauncherUpdateForm(args[1]));
                return;
            }
            using var instance=new Mutex(false,@"Local\LauncherPlaySpace.Launcher");
            bool acquired;try{acquired=instance.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired){MessageBox.Show("Лаунчер уже работает. Откройте его через значок в трее.","LauncherPlaySpace");return;}
            string marker=Path.Combine(LauncherSession.DataFolder,"session-running.flag");
            try{
                LauncherSession.Configure(args);
                if(!LauncherSession.SafeMode&&File.Exists(marker)&&MessageBox.Show("Предыдущий сеанс завершился некорректно. Запустить безопасный режим без сетевых загрузок и применения профилей?", "Восстановление запуска",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)LauncherSession.Configure(new[]{"--safe-mode"});
                Directory.CreateDirectory(LauncherSession.DataFolder);File.WriteAllText(marker,DateTimeOffset.UtcNow.ToString("O"));
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException+=(s,e)=>{LauncherSession.Record(LauncherSession.DataFolder,"Ошибка интерфейса",e.Exception);MessageBox.Show("Произошла ошибка интерфейса. Сохраните отчёт через «Моё пространство → Отчёт об ошибке». Если ошибка повторяется, перезапустите безопасный режим.","LauncherPlaySpace");};
                AppDomain.CurrentDomain.UnhandledException+=(s,e)=>LauncherSession.Record(LauncherSession.DataFolder,"Ошибка фоновой задачи",e.ExceptionObject as Exception);
                Application.Run(new LauncherApplicationContext());
                if(!LauncherSession.StartupFailed&&File.Exists(marker))File.Delete(marker);
            }catch(Exception ex){LauncherSession.Record(LauncherSession.DataFolder,"Ошибка приложения",ex);MessageBox.Show("Не удалось запустить лаунчер. Попробуйте ярлык безопасного режима или параметр --safe-mode. Библиотека и настройки не удалены.","LauncherPlaySpace");}
            finally{instance.ReleaseMutex();}
        }
    }
}
