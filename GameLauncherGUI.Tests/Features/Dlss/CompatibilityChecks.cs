using System.Diagnostics;
using GameLauncherGUI;

internal static class CompatibilityChecks
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void Reject(Action action){try{action();}catch(Exception ex)when(ex is IOException or ArgumentException or Newtonsoft.Json.JsonException or OperationCanceledException){checks++;return;}throw new Exception("Invalid compatibility data accepted");}
    private static IEnumerable<Control> Children(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Children(c)));
    private static void Pump(Func<bool> ready){var clock=Stopwatch.StartNew();while(!ready()&&clock.Elapsed<TimeSpan.FromSeconds(8)){Application.DoEvents();Thread.Sleep(5);}Check(ready(),"UI operation timed out");}
    internal static void Run(bool ui)
    {
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpaceCompatibility-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            string gameRoot=Path.Combine(folder,"Game");Directory.CreateDirectory(gameRoot);string exe=Path.Combine(gameRoot,"game.exe");File.WriteAllText(exe,"fixture");var game=new GameInfo{Name="Fixture",SteamAppId="999999",ExePath=exe,InstallDirectory=gameRoot};
            string dll=Path.Combine(gameRoot,"Engine","Plugins","nvngx_dlss.dll");Directory.CreateDirectory(Path.GetDirectoryName(dll)!);File.WriteAllText(dll,"fixture, not executable");
            string fg=Path.Combine(gameRoot,"nvngx_dlssg.dll");File.WriteAllText(fg,"fixture");
            var snapshot=DlssSnapshot.Read(game,"NVIDIA GeForce RTX 3060 Ti",default);Check(snapshot.Files.Length==2,"DLSS files not found");Check(snapshot.Files.All(f=>!Path.IsPathRooted(f.Relative)),"Absolute paths exposed as relative");Check(snapshot.Features.Contains("NVIDIA FRAME GENERATION\nДля определённой видеокарты недоступна"),"RTX 30 native FG mislabelled");
            var ada=snapshot with{Adapter="RTX 4070"};Check(ada.Features.Contains("GPU поддерживается, библиотека найдена"),"RTX 40 FG classification");var blackwell=snapshot with{Adapter="RTX 5070"};Check(blackwell.Features.Contains("MULTI FRAME GENERATION\nGPU поддерживается"),"RTX 50 MFG classification");var unknown=snapshot with{Adapter="Unknown"};Check(unknown.Features.Contains("Совместимая GeForce RTX не определена"),"Unknown GPU accepted");
            using(var cts=new CancellationTokenSource()){cts.Cancel();Reject(()=>DlssSnapshot.Read(game,"RTX 3060",cts.Token));}
            string data=Path.Combine(folder,"data");var store=new DlssCheckStore(data,game);Check(store.Read().Length==0,"Invented user reports");store.Save(snapshot,DlssTestOutcome.Works,"Test notes");var report=store.Read().Single();Check(report.Context==snapshot.Context&&report.Adapter==snapshot.Adapter&&report.Outcome==DlssTestOutcome.Works,"Context not saved");Check(report.CheckedUtc.Offset==TimeSpan.Zero,"Non-UTC report time");
            store.Save(snapshot,DlssTestOutcome.Issues,"Updated note");Check(store.Read().Length==1&&store.Read()[0].Outcome==DlssTestOutcome.Issues,"Same configuration duplicated");
            File.AppendAllText(dll,"changed");var changed=DlssSnapshot.Read(game,snapshot.Adapter,default);Check(snapshot.Context!=changed.Context,"DLL change did not invalidate result");store.Save(changed,DlssTestOutcome.Fails,"New configuration");Check(store.Read().Length==2,"Previous result lost");Check(ada.Context!=snapshot.Context,"GPU not in context");
            string ini=Path.Combine(gameRoot,"OptiScaler.ini");File.WriteAllText(ini,"[FrameGen]\nEnabled=true");var configChanged=DlssSnapshot.Read(game,snapshot.Adapter,default);Check(configChanged.Context!=changed.Context,"OptiScaler settings not in context");
            Check(new DlssCheckStore(data,new(){SteamAppId="888888",ExePath=exe}).Read().Length==0,"Other game reports leaked");
            Check((snapshot with{Restricted=!snapshot.Restricted}).Context!=snapshot.Context,"Restriction not in report context");Check((snapshot with{Incomplete=!snapshot.Incomplete}).Context!=snapshot.Context,"Partial scan not in report context");
            Reject(()=>store.Save(snapshot,(DlssTestOutcome)400,"bad"));Reject(()=>store.Save(snapshot,DlssTestOutcome.Works,new string('x',601)));store.Remove(snapshot.Context);Check(store.Read().All(c=>c.Context!=snapshot.Context),"Delete failed");
            string reportFile=Directory.GetFiles(Path.Combine(data,"dlss-checks"),"*.json").Single();File.WriteAllText(reportFile,"broken JSON");Reject(()=>store.Save(snapshot,DlssTestOutcome.Works,"must not overwrite"));Check(File.ReadAllText(reportFile)=="broken JSON","Corrupt report overwritten");File.Delete(reportFile);
            for(int i=0;i<105;i++)store.Save(snapshot with{ConfigHash=i.ToString()},DlssTestOutcome.Works,"");Check(store.Read().Length==100,"Report history not bounded");
            foreach(string path in Directory.GetFiles(gameRoot,"nvngx_*.dll",SearchOption.AllDirectories))File.Delete(path);var empty=DlssSnapshot.Read(game,snapshot.Adapter,default);Check(empty.Files.Length==0&&empty.Libraries.Contains("не найдены"),"Missing DLLs not represented");
            var missing=DlssSnapshot.Read(new(){Name="Missing",ExePath=Path.Combine(folder,"missing.exe")},"Unknown",default);Check(missing.Incomplete,"Missing root reported as fully scanned");
            var entries=ChangeHistory.Load();Check(entries.Length>=3&&entries.Any(e=>e.Version==ChangeHistory.BuildVersion),"Current version missing from history");Check(!ChangeHistory.BuildVersion.Contains("preview")||entries[0].Stage.Contains("не опубликована"),"Preview marked published");Check(entries.All(e=>e.Changes.Length>0&&e.KnownIssues.Length>0),"Missing changes or known issues");
            if(ui){
                string uiData=Path.Combine(folder,"ui-data");using var form=new DlssCompatibilityForm(game,"RTX 3060",uiData);form.Show();
                var snapshotField=typeof(DlssCompatibilityForm).GetField("snapshot",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;Pump(()=>snapshotField.GetValue(form)!=null);
                Children(form).OfType<ModernButton>().Single(b=>b.Text=="Мои проверки").PerformClick();Application.DoEvents();Children(form).OfType<ModernToggle>().Single().Checked=true;Children(form).OfType<TextBox>().Single().Text="UI result";
                Children(form).OfType<ModernButton>().Single(b=>b.Text=="Сохранить мою проверку").PerformClick();var uiStore=new DlssCheckStore(uiData,game);Pump(()=>uiStore.Read().Length==1&&Children(form).OfType<ModernButton>().Single(b=>b.Text=="Удалить мою отметку").Enabled);Check(uiStore.Read()[0].Note=="UI result","UI save lost note");Application.DoEvents();
                Children(form).OfType<ModernButton>().Single(b=>b.Text=="Удалить мою отметку").PerformClick();Check(uiStore.Read().Length==0,"UI delete failed");
                Directory.CreateDirectory("artifacts/compatibility");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save("artifacts/compatibility/my-checks.png");}
                using var settings=new SettingsForm("123","key",(_,_,_,_)=>Task.CompletedTask,()=>{});settings.Show();Children(settings).OfType<ModernButton>().Single(b=>b.Text=="Что нового").PerformClick();Application.DoEvents();Check(Children(settings).OfType<WhatsNewPanel>().Any(p=>p.Visible),"What's new page not shown");
                Children(settings).OfType<ModernButton>().Single(b=>b.Text=="Профиль Steam").PerformClick();Application.DoEvents();Check(Children(settings).OfType<TextBox>().Any(t=>t.Text=="123"),"Settings tab switch lost Steam input");
            }
        }finally{Directory.Delete(folder,true);}
        Console.WriteLine($"PASS: {checks} compatibility/history checks: GPU conditions, DLL scan/cancellation, report context invalidation, bounded local persistence/corruption protection, version history, settings navigation and user save/delete UI. Offline fixtures only.");
    }
}
