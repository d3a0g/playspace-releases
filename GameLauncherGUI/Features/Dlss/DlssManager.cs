using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record DlssLibrary(string Label,string Tag)
{
    public string Url=>$"https://raw.githubusercontent.com/NVIDIA/DLSS/{Tag}/lib/Windows_x86_64/rel/nvngx_dlss.dll";
    public static readonly DlssLibrary[] All={new("DLSS Super Resolution — SDK 310.9.16","v310.9.16"),new("DLSS 4.5 Super Resolution — SDK 310.5.3","v310.5.3")};
    public override string ToString()=>Label;
}
internal sealed class DlssManager
{
    private sealed record State(string OriginalHash,string UpdatedHash);
    private readonly string target;
    private string Backup=>target+".playspace-original";
    private string Ledger=>target+".playspace-update.json";
    public DlssManager(string target){if(!Path.GetFileName(target).Equals("nvngx_dlss.dll",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Поддерживается только существующий DLSS Super Resolution.");this.target=Path.GetFullPath(target);}
    internal static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
    public static string[] Scan(GameInfo game)
    {
        string? root=Directory.Exists(game.InstallDirectory)?game.InstallDirectory:File.Exists(game.ExePath)?Path.GetDirectoryName(game.ExePath):null;
        if(root==null)return Array.Empty<string>();var files=new List<string>();var pending=new Queue<(string Directory,int Depth)>();pending.Enqueue((root,0));int count=0;
        while(pending.Count>0&&count++<10000){var item=pending.Dequeue();try{foreach(string file in Directory.EnumerateFiles(item.Directory,"nvngx_dlss.dll"))files.Add(file);if(item.Depth<7)foreach(var directory in Directory.EnumerateDirectories(item.Directory)){if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)==0)pending.Enqueue((directory,item.Depth+1));}}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}}
        return files.ToArray();
    }
    public static bool Blocked(GameInfo game)
    {
        if(new[]{"730","570","1172470","252490","578080","359550","594650","1240440","1085660","1938090"}.Contains(game.SteamAppId))return true;
        if(new[]{"valorant","fortnite","rainbowsix","rainbow six","destiny 2","apex legends","escape from tarkov","call of duty","warzone"}.Any(name=>(game.Name+" "+Path.GetFileNameWithoutExtension(game.ExePath)).Contains(name,StringComparison.OrdinalIgnoreCase)))return true;
        string? root=game.InstallDirectory;if(!Directory.Exists(root))root=Path.GetDirectoryName(game.ExePath);
        if(root==null||!Directory.Exists(root))return false;
        try{
            ManagedPaths.NoLinks(root);var pending=new Queue<(string Path,int Depth)>();pending.Enqueue((root,0));int count=0;
            while(pending.TryDequeue(out var item))foreach(var path in Directory.EnumerateFileSystemEntries(item.Path)){
                if(++count>30000)return true;
                string name=Path.GetFileName(path).ToLowerInvariant().Replace("_","").Replace("-","");
                if(new[]{"easyanticheat","battleye","anticheat","vanguard"}.Any(name.Contains))return true;
                var attributes=File.GetAttributes(path);if(item.Depth<4&&(attributes&FileAttributes.Directory)!=0&&(attributes&FileAttributes.ReparsePoint)==0)pending.Enqueue((path,item.Depth+1));
            }
            return false;
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return true;}
    }
    public static void EnsureClosed(GameInfo game)
    {
        string? root=game.InstallDirectory;if(!Directory.Exists(root))root=File.Exists(game.ExePath)?Path.GetDirectoryName(game.ExePath):null;if(root==null)return;string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        bool running=false;foreach(var process in Process.GetProcesses()){using(process)try{if(process.MainModule?.FileName is string file&&file.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))running=true;}catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException){}}
        if(running)throw new IOException("Сначала закрой игру и её вспомогательные процессы.");
    }
    internal void ApplyVerified(string source,string? expectedHash=null)
    {
        using var guard=new FileStream(Ledger+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(expectedHash!=null&&Hash(source)!=expectedHash)throw new IOException("Библиотека изменилась после проверки подписи.");
        if(!File.Exists(target))throw new FileNotFoundException("DLSS-файл игры не найден.");string original=Hash(target),updated=Hash(source);
        if(File.Exists(Ledger)){
            var previous=JsonConvert.DeserializeObject<State>(File.ReadAllText(Ledger))??throw new InvalidDataException("Повреждён журнал DLSS.");
            if(!File.Exists(Backup)||Hash(Backup)!=previous.OriginalHash||original!=previous.UpdatedHash&&original!=previous.OriginalHash)throw new IOException("Файлы изменились вне лаунчера. Сначала проверь обновление игры.");original=previous.OriginalHash;
        }else {if(File.Exists(Backup))throw new IOException("Найдена резервная копия без журнала; автоматическая перезапись отменена.");File.Copy(target,Backup,false);}
        string prepared=Ledger+".tmp";File.WriteAllText(prepared,JsonConvert.SerializeObject(new State(original,updated)));File.Move(prepared,Ledger,true);
        string temp=target+".playspace-"+Guid.NewGuid().ToString("N")+".tmp";
        try{File.Copy(source,temp,false);if(Hash(temp)!=updated)throw new IOException("Исходная библиотека изменилась.");File.Move(temp,target,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Restore()
    {
        using var guard=new FileStream(Ledger+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(!File.Exists(Ledger)||!File.Exists(Backup))throw new IOException("Нет резервной копии LauncherPlaySpace.");var state=JsonConvert.DeserializeObject<State>(File.ReadAllText(Ledger))??throw new InvalidDataException("Повреждён журнал DLSS.");
        if(Hash(Backup)!=state.OriginalHash||File.Exists(target)&&Hash(target)!=state.UpdatedHash&&Hash(target)!=state.OriginalHash)throw new IOException("Файлы изменены; автоматическое восстановление отменено.");
        string temp=target+".restore.tmp";try{File.Copy(Backup,temp,false);File.Move(temp,target,true);File.Delete(Ledger);File.Delete(Backup);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static async Task VerifyNvidia(string file,CancellationToken cancellation=default)
    {
        string escaped=file.Replace("'","''");string script="$s=Get-AuthenticodeSignature -LiteralPath '"+escaped+"';if($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match 'NVIDIA CORPORATION'){exit 0}else{exit 1}";
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-EncodedCommand");start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
        using var process=Process.Start(start)??throw new IOException("Не удалось проверить подпись NVIDIA.");using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(40));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){try{process.Kill();}catch(InvalidOperationException){}if(cancellation.IsCancellationRequested)throw;throw new IOException("Истекло время проверки подписи.");}if(process.ExitCode!=0)throw new InvalidDataException("Библиотека не имеет действительной подписи NVIDIA.");
    }
    public static async Task<string> Download(DlssLibrary library,string folder,CancellationToken cancellation)
    {
        if(!DlssLibrary.All.Contains(library))throw new InvalidDataException("Неизвестный источник DLSS.");Directory.CreateDirectory(folder);string path=Path.Combine(folder,library.Tag+"-nvngx_dlss.dll"),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{byte[] bytes=await Workspace.Current.Transfers.Fetch("NVIDIA DLSS SR · "+library.Tag,new Uri(library.Url),100L*1024*1024,cancellation);await File.WriteAllBytesAsync(temp,bytes,cancellation);
            await VerifyNvidia(temp,cancellation);cancellation.ThrowIfCancellationRequested();File.Move(temp,path,true);return path;
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
internal sealed class DlssForm : StudioDialog
{
    private readonly ThemedComboBox files=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White};
    private readonly ThemedComboBox version=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White};
    private readonly FormattedDescription status=new(){Dock=DockStyle.Fill,Padding=new Padding(4,4,16,4),Font=new Font("Segoe UI",9),BackColor=Color.FromArgb(14,17,27)};private readonly ModernButton apply=new("Скачать и обновить DLSS SR",Color.FromArgb(115,94,244)){Dock=DockStyle.Fill};private readonly ModernButton restore=new("Восстановить оригинал",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};private readonly CancellationTokenSource cancellation=new();private bool busy;private bool disposed;
    public DlssForm(GameInfo game)
    {
        var gpu=RtxCompatibility.Detect(PcSnapshot.Read().Gpu);Text="PLAYSPACE / DLSS · "+game.Name;Size=new Size(840,640);MinimumSize=new Size(760,560);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28),ColumnCount=1,RowCount=7};foreach(float height in new[]{65f,130,40,40,48,48})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));ContentHost.Controls.Add(layout);
        var heading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));heading.Controls.Add(new SmoothLabel{Text="Библиотеки DLSS",Dock=DockStyle.Fill,Font=new Font("Segoe UI",22,FontStyle.Bold)},0,0);var experimental=new ModernButton("DLSS 5 · эксперимент",Color.FromArgb(75,59,120)){Dock=DockStyle.Fill,Margin=new Padding(0,4,0,14)};experimental.Click+=(s,e)=>{using var dialog=new UniversalDlss5Form(game);dialog.ShowDialog(this);};var rtx=new ModernButton("RTX 30 · FSR кадры",Color.FromArgb(35,48,54)){Dock=DockStyle.Fill,Margin=new Padding(0,4,4,14)};rtx.Click+=(s,e)=>{using var dialog=new RtxOptionsForm(game);dialog.ShowDialog(this);};heading.Controls.Add(rtx,1,0);heading.Controls.Add(experimental,2,0);layout.Controls.Add(heading,0,0);
        var compatibility=new DlssCompatibilityCard(game,gpu.Adapter);layout.Controls.Add(compatibility,0,1);
        layout.Controls.Add(files,0,2);version.Items.AddRange(DlssLibrary.All);version.SelectedIndex=0;layout.Controls.Add(version,0,3);layout.Controls.Add(apply,0,4);layout.Controls.Add(restore,0,5);layout.Controls.Add(status,0,6);
        Shown+=async(s,e)=>{try{var detectedSnapshot=await Task.Run(()=>DlssSnapshot.Read(game,gpu.Adapter,cancellation.Token),cancellation.Token);bool blocked=detectedSnapshot.Restricted;var detected=detectedSnapshot.Files.Where(f=>f.Kind=="Super Resolution").Select(f=>f.Path).ToArray();if(IsDisposed)return;compatibility.UpdateSummary(detectedSnapshot);files.Items.AddRange(detected);if(files.Items.Count>0)files.SelectedIndex=0;restore.Enabled=detected.Length>0;apply.Enabled=restore.Enabled&&!blocked&&gpu.SuperResolution;status.Text=(!gpu.SuperResolution?gpu.Summary+"\n":"")+(blocked?"Для сетевой игры/обнаруженного античита автоматическая замена отключена.":detected.Length==0?"В игре не найдена встроенная библиотека DLSS SR. Универсальная установка не поддерживается.":"Выбери библиотеку. Перед обновлением закрой игру.");}catch(Exception ex){if(!IsDisposed)status.Text=ex.Message;}};
        async Task Run(bool undo){if(busy||files.SelectedItem is not string target)return;busy=true;apply.Enabled=restore.Enabled=false;try{if(!undo&&DlssManager.Blocked(game))throw new IOException("Изменение отключено для этой игры.");if(!undo&&!gpu.SuperResolution)throw new IOException(gpu.Summary);DlssManager.EnsureClosed(game);if(undo){await Task.Run(()=>new DlssManager(target).Restore());status.Text="Оригинальная библиотека восстановлена.";}else{status.Text="Загружаем и проверяем подпись NVIDIA…";string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameLauncherPRO","dlss");string source=await DlssManager.Download((DlssLibrary)version.SelectedItem!,folder,cancellation.Token);string verifiedHash=DlssManager.Hash(source);await DlssManager.VerifyNvidia(source,cancellation.Token);DlssManager.EnsureClosed(game);await Task.Run(()=>new DlssManager(target).ApplyVerified(source,verifiedHash));status.Text="Библиотека обновлена. Проверь DLSS в настройках игры; оригинал можно восстановить.";}try{var current=await Task.Run(()=>DlssSnapshot.Read(game,gpu.Adapter,cancellation.Token),cancellation.Token);if(!IsDisposed)compatibility.UpdateSummary(current);}catch(OperationCanceledException){}catch(Exception refreshError)when(refreshError is IOException or UnauthorizedAccessException or ArgumentException){if(!IsDisposed)status.Text+=" Открой карточку совместимости, чтобы повторить сканирование.";}}catch(Exception ex){if(!IsDisposed)status.Text="Не удалось изменить DLSS: "+ex.Message;}finally{busy=false;if(!IsDisposed){restore.Enabled=files.Items.Count>0;apply.Enabled=restore.Enabled&&!DlssManager.Blocked(game)&&gpu.SuperResolution;}}}
        apply.Click+=async(s,e)=>await Run(false);restore.Click+=async(s,e)=>await Run(true);FormClosing+=(s,e)=>{if(busy){cancellation.Cancel();e.Cancel=true;status.Text="Завершаем операцию…";}};
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
