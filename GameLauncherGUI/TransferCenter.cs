using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace GameLauncherGUI;

internal enum TransferState { Queued,Running,Completed,Cancelled,Failed }
internal sealed record TransferView(string Id,string Title,TransferState State,long Received,long? Total,string Detail);
internal sealed class TransferCenter
{
    private sealed class Job
    {
        internal string Id=Guid.NewGuid().ToString("N"),Title="",Detail="В очереди";internal TransferState State;internal long Received;internal long? Total;internal CancellationTokenSource? Cancellation;
        internal Func<Job,CancellationToken,Task<byte[]>> Work=null!;
    }
    private sealed record CacheItem(string Hash,long Size);
    private readonly string folder;private readonly SemaphoreSlim queue=new(1,1);private readonly object gate=new();private readonly List<Job> jobs=new();
    private readonly Func<HttpClient> httpFactory;private readonly Func<bool> offline;
    internal TransferCenter(string folder,Func<HttpClient>? httpFactory=null,Func<bool>? offline=null){this.folder=Path.GetFullPath(folder);this.httpFactory=httpFactory??(()=>new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All}){Timeout=TimeSpan.FromMinutes(5)});this.offline=offline??(()=>Workspace.Current.Offline);}
    internal IReadOnlyList<TransferView> Items(){lock(gate)return jobs.AsEnumerable().Reverse().Select(j=>new TransferView(j.Id,j.Title,j.State,j.Received,j.Total,j.Detail)).ToArray();}
    private void Update(Job job,TransferState state,string text){lock(gate){job.State=state;job.Detail=text;}}
    internal static bool Https(Uri uri)=>uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo);
    internal Task<byte[]> Fetch(string title,Uri uri,long limit,CancellationToken cancellation,Func<Uri,bool>? allowed=null)
    {
        if(!Https(uri))throw new InvalidDataException("Для загрузки нужен HTTPS без встроенных паролей.");
        title=new string(title.Where(c=>!char.IsControl(c)).Take(160).ToArray());var job=new Job{Title=string.IsNullOrWhiteSpace(title)?"Загрузка":title};job.Work=(item,ct)=>Download(item,uri,limit,allowed??Https,ct);lock(gate){if(jobs.Count>100)jobs.RemoveAll(j=>j.State is TransferState.Completed or TransferState.Cancelled or TransferState.Failed);jobs.Add(job);}return Execute(job,cancellation);
    }
    private async Task<byte[]> Execute(Job job,CancellationToken outer)
    {
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(outer);lock(gate){job.Cancellation=cancellation;job.State=TransferState.Queued;job.Received=0;job.Total=null;job.Detail="В очереди";}
        bool entered=false;
        try{await queue.WaitAsync(cancellation.Token).ConfigureAwait(false);entered=true;Update(job,TransferState.Running,"Загружаем…");var bytes=await job.Work(job,cancellation.Token).ConfigureAwait(false);cancellation.Token.ThrowIfCancellationRequested();Update(job,TransferState.Completed,"Загружено в кеш. Установка выполняется исходным разделом отдельно.");return bytes;}
        catch(OperationCanceledException){Update(job,TransferState.Cancelled,"Загрузка отменена. Установка не выполнялась центром загрузок.");throw;}
        catch(Exception ex)when(ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException){Update(job,TransferState.Failed,ex is InvalidDataException?ex.Message:"Загрузка не удалась. Проверьте сеть и повторите.");throw;}
        finally{lock(gate)job.Cancellation=null;if(entered)queue.Release();}
    }
    internal void Cancel(string id){lock(gate)jobs.FirstOrDefault(j=>j.Id==id)?.Cancellation?.Cancel();}
    internal async Task Retry(string id)
    {
        Job? job;lock(gate){job=jobs.FirstOrDefault(j=>j.Id==id);if(job==null||job.State is not(TransferState.Cancelled or TransferState.Failed))return;job.State=TransferState.Queued;}
        try{await Execute(job,CancellationToken.None).ConfigureAwait(false);}catch(Exception ex)when(ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException){}
    }
    private async Task<byte[]> Download(Job job,Uri original,long limit,Func<Uri,bool> allowed,CancellationToken cancellation)
    {
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original.AbsoluteUri)));string path=Path.Combine(folder,key+".bin"),index=Path.Combine(folder,"index.json");
        var entries=LocalJson.Read(index,()=>new Dictionary<string,CacheItem>());
        if(entries.TryGetValue(key,out var entry)&&entry.Size<=limit&&File.Exists(path)){
            using var input=File.OpenRead(path);if(input.Length==entry.Size&&Convert.ToHexString(SHA256.HashData(input))==entry.Hash){var cached=await File.ReadAllBytesAsync(path,cancellation).ConfigureAwait(false);lock(gate){job.Received=cached.Length;job.Total=cached.Length;}return cached;}
        }
        if(offline())throw new IOException("Включён офлайн-режим. Нужного файла нет в проверенном кеше.");
        using var http=httpFactory();Uri uri=original;
        for(int redirects=0;redirects<6;redirects++){
            if(!Https(uri)||!allowed(uri))throw new InvalidDataException("Недопустимый адрес загрузки.");using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false);
            if(response.RequestMessage?.RequestUri is Uri final&&(!Https(final)||!allowed(final)))throw new InvalidDataException("Недопустимое перенаправление загрузки.");
            if((int)response.StatusCode is >=300 and <400){uri=new Uri(uri,response.Headers.Location??throw new IOException("Не указан адрес загрузки."));continue;}
            response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Файл превышает допустимый размер.");lock(gate)job.Total=response.Content.Headers.ContentLength;
            using var source=await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);using var result=new MemoryStream();byte[] buffer=new byte[81920];int read;
            while((read=await source.ReadAsync(buffer,cancellation).ConfigureAwait(false))>0){if(result.Length+read>limit)throw new InvalidDataException("Файл превышает допустимый размер.");result.Write(buffer,0,read);lock(gate)job.Received=result.Length;}
            cancellation.ThrowIfCancellationRequested();byte[] bytes=result.ToArray();Directory.CreateDirectory(folder);string temp=path+".tmp";
            try{await File.WriteAllBytesAsync(temp,bytes,cancellation).ConfigureAwait(false);cancellation.ThrowIfCancellationRequested();File.Move(temp,path,true);entries[key]=new(Convert.ToHexString(SHA256.HashData(bytes)),bytes.Length);LocalJson.Write(index,entries);}finally{if(File.Exists(temp))File.Delete(temp);}
            return bytes;
        }
        throw new IOException("Слишком много перенаправлений.");
    }
    internal long CacheSize(){try{return Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*.bin").Sum(p=>new FileInfo(p).Length):0;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return 0;}}
    internal void Forget(Uri uri)
    {
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)));string path=Path.Combine(folder,key+".bin"),index=Path.Combine(folder,"index.json");
        try{if(File.Exists(path))File.Delete(path);var entries=LocalJson.Read(index,()=>new Dictionary<string,CacheItem>());if(entries.Remove(key))LocalJson.Write(index,entries);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
    }
    internal void ClearCache()
    {
        if(!queue.Wait(0))throw new IOException("Дождитесь завершения загрузок или отмените их.");
        try{
        lock(gate){if(jobs.Any(j=>j.State is TransferState.Queued or TransferState.Running))throw new IOException("Дождитесь завершения загрузок или отмените их.");}
        if(!Directory.Exists(folder))return;var directory=new DirectoryInfo(folder);if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Кеш не должен быть папкой-ссылкой.");
        foreach(string file in Directory.EnumerateFiles(folder)){
            string name=Path.GetFileName(file);if(name!="index.json"&&!(name.Length==68&&name.EndsWith(".bin")&&name[..64].All(Uri.IsHexDigit))&&!(name.Length==72&&name.EndsWith(".bin.tmp")&&name[..64].All(Uri.IsHexDigit)))continue;
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Файл кеша является ссылкой.");File.Delete(file);
        }
        }finally{queue.Release();}
    }
}
internal sealed class DownloadsForm : StudioDialog
{
    private readonly TransferCenter center;private readonly ListBox list=new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White,BorderStyle=BorderStyle.None,IntegralHeight=false};
    private readonly FormattedDescription detail=new(){Dock=DockStyle.Fill};private readonly System.Windows.Forms.Timer timer=new(){Interval=400};private readonly ModernButton cancel=new("Отменить загрузку",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};private readonly ModernButton retry=new("Повторить загрузку",VisualTheme.Accent){Dock=DockStyle.Fill};
    private sealed record Row(TransferView Item){public override string ToString()=>$"{Item.Title} · {Label(Item.State)} · {Item.Received/1048576.0:N1} МБ";}
    internal static string Label(TransferState state)=>state switch{TransferState.Queued=>"В очереди",TransferState.Running=>"Загружается",TransferState.Completed=>"Загружено",TransferState.Cancelled=>"Отменено",_=>"Ошибка"};
    internal DownloadsForm(TransferCenter center)
    {
        this.center=center;Text="PLAYSPACE / Центр загрузок";Size=new Size(920,700);MinimumSize=new Size(800,580);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(28)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));layout.RowStyles.Add(new RowStyle(SizeType.Percent,55));layout.RowStyles.Add(new RowStyle(SizeType.Percent,45));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new Label{Text="Загрузки модов и компонентов",Dock=DockStyle.Fill,Font=new Font("Segoe UI",20,FontStyle.Bold)},0,0);layout.Controls.Add(list,0,1);layout.Controls.Add(detail,0,2);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));actions.Controls.Add(cancel,0,0);actions.Controls.Add(retry,1,0);var clear=new ModernButton("Очистить кеш…",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};actions.Controls.Add(clear,2,0);layout.Controls.Add(actions,0,3);
        cancel.Click+=(s,e)=>{if(list.SelectedItem is Row row)center.Cancel(row.Item.Id);};retry.Click+=async(s,e)=>{if(list.SelectedItem is Row row){await center.Retry(row.Item.Id);RefreshRows();}};clear.Click+=(s,e)=>{if(MessageBox.Show(this,"Удалить скачанные файлы из кеша? Установленные моды и компоненты останутся на месте.","Очистка кеша",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;try{center.ClearCache();RefreshRows();}catch(IOException ex){detail.Text=ex.Message;}};
        list.SelectedIndexChanged+=(s,e)=>Details();timer.Tick+=(s,e)=>RefreshRows();Shown+=(s,e)=>{RefreshRows();timer.Start();};
    }
    private void RefreshRows(){string? selected=(list.SelectedItem as Row)?.Item.Id;var rows=center.Items();list.BeginUpdate();list.Items.Clear();foreach(var item in rows)list.Items.Add(new Row(item));list.SelectedIndex=selected==null?(rows.Count>0?0:-1):rows.ToList().FindIndex(i=>i.Id==selected);list.EndUpdate();Details();}
    private void Details(){var item=(list.SelectedItem as Row)?.Item;cancel.Enabled=item?.State is TransferState.Queued or TransferState.Running;retry.Enabled=item?.State is TransferState.Cancelled or TransferState.Failed;detail.Text=item==null?"Здесь появятся загрузки Thunderstore, DLSS и WARP.\n\nПовтор загружает файл в кеш; установку нужно запустить из исходного раздела. Удаление кеша не удаляет установленные моды.":$"{item.Title}\n{Label(item.State)}\n{item.Received/1048576.0:N1} МБ"+(item.Total.HasValue?$" / {item.Total.Value/1048576.0:N1} МБ":"")+"\n\n"+item.Detail+$"\n\nКеш на диске: {center.CacheSize()/1048576.0:N1} МБ.";}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
