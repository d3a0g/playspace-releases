using GameLauncherGUI;
using System.Reflection;
using System.Net;
using System.Net.Http;

internal static class LibraryPresentationChecks
{
    private static int checks;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    private static IEnumerable<Control> Children(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
    private sealed class WaitingHttp:HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){await Task.Delay(Timeout.Infinite,token).ConfigureAwait(false);return new(HttpStatusCode.OK);}
    }
    internal static void Run()
    {
        string folder=Path.Combine(Path.GetTempPath(),"PlaySpacePresentation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var state=new BrowseState();state.Save("all","a",350);state.Save("filtered","b",70);Check(state.Read("all")==new BrowsePosition("a",350),"Browse context lost");for(int i=0;i<100;i++)state.Save("key"+i,null,i);Check(state.Read("key99")!=null&&state.Read("all")==null,"Browse history is not bounded");
        using(var grid=new CustomCheckListBox{Size=new(650,300)}){for(int i=0;i<30;i++)grid.Items.Add(new("Game "+i,false,""));grid.SelectItem(20);int offset=grid.ViewportOffset;grid.ResetView();grid.RestoreViewport(20,offset);Check(grid.SelectedIndex==20&&grid.ViewportOffset==offset,"Grid viewport restore failed");int profile=-1;grid.ProfileRequested+=i=>profile=i;grid.ShowProfileButtons=false;grid.Navigate(Keys.Alt|Keys.Enter);Check(profile==20,"Hidden gear removed keyboard profile access");grid.Items.Clear();grid.RestoreViewport(20,offset);Check(grid.SelectedIndex==-1&&grid.ViewportOffset==0,"Viewport did not clamp removed games");}
        var navigation=new SectionNavigationState();
        using(var host=new Form{Size=new(850,600)}){var section=new SectionNavigation(navigation);host.Controls.Add(section);section.Add("One",SettingsPage.Create("One",""));section.Add("Two",SettingsPage.Create("Two",""));host.Show();section.Select(1);Application.DoEvents();Check(navigation.Section=="Two","Section selection not stored");host.Close();}
        using(var section=new SectionNavigation(navigation)){section.Add("One",SettingsPage.Create("One",""));section.Add("Two",SettingsPage.Create("Two",""));Check(Children(section).OfType<ModernButton>().Single(b=>b.Text=="Two").IsActive,"Section selection not restored");}
        new LauncherPreferences{OfflineMode=true,OnboardingCompleted=true,HomeBanner=true}.Save(folder);
        var games=Enumerable.Range(0,35).Select(i=>new GameInfo{Name="Game "+i.ToString("D2"),ExePath=Path.Combine(folder,"game"+i+".exe"),SteamAppId=""}).ToList();GameLibraryStore.Save(Path.Combine(folder,"games.json"),games);
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        using(var main=new Form1(folder,false))
        {
            main.Show();Application.DoEvents();var grid=(CustomCheckListBox)typeof(Form1).GetField("listBoxGames",flags)!.GetValue(main)!;var search=(TextBox)typeof(Form1).GetField("searchGames",flags)!.GetValue(main)!;
            grid.SelectItem(20);int offset=grid.ViewportOffset;Check(offset>0,"Fixture did not scroll");search.Text="Game 03";Application.DoEvents();search.Clear();Application.DoEvents();Check(grid.SelectedIndex==20&&grid.ViewportOffset==offset,"Library search return lost position or selection");typeof(Form1).GetMethod("ApplyWorkspaceSettings",flags)!.Invoke(main,null);Check(grid.SelectedIndex==20&&grid.ViewportOffset==offset,"Applying appearance lost library position");
            var menu=(ContextMenuStrip)typeof(Form1).GetField("gameMenu",flags)!.GetValue(main)!;Check(menu.Items.Count==4,"Context menu is not compact");Check(menu.Items[0].Text=="Играть"&&menu.Items[1].Text=="Профиль"&&menu.Items[2].Text=="Открыть папку","Daily actions missing");var more=(ToolStripMenuItem)menu.Items[3];Check(more.DropDownItems.Count>10&&more.DropDownItems.Cast<ToolStripItem>().Any(i=>i.Text=="Своя иконка…"),"Advanced actions were removed");Check(!grid.ShowProfileButtons,"Rare profile button still clutters list");
            grid.SelectItem(0);grid.RestoreViewport(0,0);bool extraWindow=false;using var watchdog=new System.Windows.Forms.Timer{Interval=100};watchdog.Tick+=(s,e)=>{foreach(Form form in Application.OpenForms.Cast<Form>().Where(f=>f!=main).ToArray()){extraWindow=true;form.Close();}};watchdog.Start();var card=grid.RectangleToClient(grid.AccessibilityObject.GetChild(0)!.Bounds);typeof(CustomCheckListBox).GetMethod("OnMouseDown",flags)!.Invoke(grid,new object[]{new MouseEventArgs(MouseButtons.Left,1,card.Left+100,card.Top+card.Height/2,0)});Application.DoEvents();watchdog.Stop();Check(!extraWindow,"A single click still opens a game card");
            string shots=Path.GetFullPath("artifacts/library-presentation-previews");Directory.CreateDirectory(shots);using var image=new Bitmap(main.Width,main.Height);main.DrawToBitmap(image,main.ClientRectangle);image.Save(Path.Combine(shots,"library.png"));main.Dispose();
        }
        var center=new TransferCenter(Path.Combine(folder,"transfer-cache"),()=>new HttpClient(new WaitingHttp()),()=>false);
        using(var strip=new OperationStrip(center))using(var cancel=new CancellationTokenSource())
        {
            var activity=new List<bool>();strip.ActivityChanged+=active=>activity.Add(active);var transfer=center.Fetch("Fixture download",new Uri("https://example.invalid/component"),100,cancel.Token);strip.RefreshOperation();Check(activity.SequenceEqual(new[]{true}),"Operations strip did not reveal active download");Check(Children(strip).OfType<Label>().Any(l=>l.Text.Contains("Fixture download")),"Download status missing");cancel.Cancel();try{transfer.GetAwaiter().GetResult();}catch(OperationCanceledException){}strip.RefreshOperation();Check(activity.SequenceEqual(new[]{true,false}),"Operations strip did not collapse");
        }
        var recState=new RecommendationBrowseState{Category="Шутеры",Query="Counter"};using(var rec=new RecommendationsForm(folder,_=>Task.FromResult<Bitmap?>(null),browseState:recState)){Check(Children(rec).OfType<TextBox>().Any(t=>t.Text=="Counter"),"Recommendation search not restored");}
        Console.WriteLine($"PASS: {checks} library presentation checks: no game detail popup, compact menu with retained commands, viewport/filter/section persistence, bounded memory, operation strip and recommendation state. Offline fixtures only.");
    }
}
