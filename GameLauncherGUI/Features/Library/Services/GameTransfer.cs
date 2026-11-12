using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace GameLauncherGUI;

internal sealed record TransferFile(string Relative, long Bytes, string Sha256);
internal sealed record GameTransferPlan(string Source, string Target, TransferFile[] Files, long Bytes, string[]? Directories=null);
internal sealed record GameTransferJournal(string Id, string Source, string Target, string Backup, string Phase, TransferFile[] Files, TransferFile[]? TargetFiles=null);

internal static class GameTransfer
{
    private static FileStream Lock(string source)=>new(Full(source)+".playspace-transfer.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
    internal static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Inside(string child,string parent) => Full(child).StartsWith(Full(parent)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static void NoLinks(string path)
    {
        for(var item=new DirectoryInfo(path);item!=null;item=item.Parent)
            if(item.Exists && (item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Путь содержит ссылку или точку подключения: "+item.FullName);
    }
    private static void GameFolder(string path)
    {
        path=Full(path);NoLinks(path);
        if(path.StartsWith("\\\\") || path.Equals(Full(Path.GetPathRoot(path)!),StringComparison.OrdinalIgnoreCase))throw new IOException("Нужна локальная папка отдельной игры.");
        foreach(string special in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),AppContext.BaseDirectory})
            if(!string.IsNullOrEmpty(special) && (Full(special).Equals(path,StringComparison.OrdinalIgnoreCase)||Inside(special,path)))throw new IOException("Нельзя переносить общую или системную папку.");
        if(Inside(path,Environment.GetFolderPath(Environment.SpecialFolder.Windows)))throw new IOException("Нельзя переносить системные файлы.");
        if(new[]{"games","игры","steam","steamapps","common","steamlibrary","epic games","riot games"}.Contains(Path.GetFileName(path).ToLowerInvariant()))throw new IOException("Выберите папку одной игры, а не всю библиотеку.");
    }
    private static string Hash(string path){using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return Convert.ToHexString(SHA256.HashData(stream));}
    private static string FileAt(string root,string relative)
    {
        string path=Path.GetFullPath(Path.Combine(root,relative));if(!Inside(path,root))throw new InvalidDataException("Некорректный путь в плане переноса.");NoLinks(Path.GetDirectoryName(path)!);
        if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Файл является ссылкой.");return path;
    }
    private static TransferFile[] Snapshot(string root,CancellationToken token,IProgress<string>? progress)
    {
        var files=new List<TransferFile>();var pending=new Stack<string>();pending.Push(root);
        while(pending.TryPop(out var directory))foreach(string path in Directory.EnumerateFileSystemEntries(directory)){
            token.ThrowIfCancellationRequested();if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("В игре есть ссылки. Автоматический перенос остановлен.");
            if(Directory.Exists(path))pending.Push(path);else{if(files.Count>=500000)throw new IOException("Слишком много файлов для переноса.");progress?.Report("Проверка: "+Path.GetFileName(path));files.Add(new(Path.GetRelativePath(root,path),new FileInfo(path).Length,Hash(path)));}
        }
        return files.ToArray();
    }
    internal static GameTransferPlan Preview(string source,string target,CancellationToken token,IProgress<string>? progress=null)
    {
        source=Full(source);target=Full(target);GameFolder(source);GameFolder(target);
        if(!Directory.Exists(source)||Directory.Exists(target)||File.Exists(target)||Inside(target,source)||Inside(source,target))throw new IOException("Нужна новая папка вне исходной игры. Перезапись существующих папок запрещена.");
        if(!new DriveInfo(Path.GetPathRoot(source)!).DriveFormat.Equals("NTFS",StringComparison.OrdinalIgnoreCase))throw new IOException("Для сохранения старого пути исходный диск должен использовать NTFS.");
        var files=Snapshot(source,token,progress);long bytes=files.Sum(f=>f.Bytes);
        if(files.Any(f=>f.Relative.Replace('\\','/').EndsWith("/pending.json",StringComparison.OrdinalIgnoreCase)||f.Relative.EndsWith("profile-switch.json",StringComparison.OrdinalIgnoreCase)))throw new IOException("Сначала завершите или восстановите незавершённую установку модов.");
        if(new DriveInfo(Path.GetPathRoot(target)!).AvailableFreeSpace<bytes+256L*1024*1024)throw new IOException("На целевом диске недостаточно места с запасом 256 МБ.");
        return new(source,target,files,bytes,Directory.EnumerateDirectories(source,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(source,p)).ToArray());
    }
    internal static void Verify(string root,TransferFile[] files,CancellationToken token,bool allowExtra=false)
    {
        NoLinks(root);
        var current=Snapshot(root,token,null).ToDictionary(f=>f.Relative,StringComparer.OrdinalIgnoreCase);
        if((!allowExtra&&current.Count!=files.Length)||files.Any(f=>!current.TryGetValue(f.Relative,out var actual)||actual.Bytes!=f.Bytes||actual.Sha256!=f.Sha256))throw new IOException("Файлы изменены или копия неполная. Операция остановлена; исходные данные сохранены.");
    }
    internal static void EnsureClosed(GameInfo game)
    {
        DlssManager.EnsureClosed(game);
        foreach(string name in new[]{"steam","EpicGamesLauncher","UbisoftConnect","upc","RiotClientServices","EADesktop","GalaxyClient","Battle.net"})
            foreach(var process in Process.GetProcessesByName(name)){using(process)throw new IOException("Закройте игровые клиенты перед переносом: "+name);}
    }
    internal static GameTransferJournal Execute(GameTransferPlan plan,string journalFile,Action ensureClosed,CancellationToken token,IProgress<string>? progress=null,Action<string,string>? link=null)
    {
        using var guard=Lock(plan.Source);
        ensureClosed();GameFolder(plan.Source);GameFolder(plan.Target);
        if(Directory.Exists(plan.Target)||File.Exists(plan.Target))throw new IOException("Целевая папка уже существует.");
        if(File.Exists(journalFile))
        {
            var previous=LocalJson.Read<GameTransferJournal?>(journalFile,()=>null);
            if(previous?.Phase!="Завершено")throw new IOException("Есть незавершённый перенос. Сначала завершите его или верните исходную папку.");
            File.Move(journalFile,journalFile+".completed-"+Guid.NewGuid().ToString("N"));
        }
        Verify(plan.Source,plan.Files,token);
        string id=Guid.NewGuid().ToString("N");string backup=plan.Source+".playspace-backup-"+id;
        var journal=new GameTransferJournal(id,plan.Source,plan.Target,backup,"Копирование",plan.Files);LocalJson.Write(journalFile,journal);
        Directory.CreateDirectory(plan.Target);
        foreach(string directory in plan.Directories??Array.Empty<string>())Directory.CreateDirectory(FileAt(plan.Target,directory));
        // Native Windows copy retains timestamps, attributes and alternate streams, including internet-zone metadata.
        foreach(var file in plan.Files){token.ThrowIfCancellationRequested();progress?.Report("Копирование: "+file.Relative);string destination=FileAt(plan.Target,file.Relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(FileAt(plan.Source,file.Relative),destination,false);}
        progress?.Report("Проверка SHA-256 обеих копий…");Verify(plan.Target,plan.Files,token);Verify(plan.Source,plan.Files,token);
        var targetFiles=plan.Files.ToArray();
        // Named mod profiles are bound to their root. Rebind only metadata, after verifying the copied bytes.
        for(int i=0;i<targetFiles.Length;i++)
        {
            string[] parts=targetFiles[i].Relative.Replace('\\','/').Split('/');
            int offset=Array.IndexOf(parts,".playspace-mods");
            if(offset<0||parts.Length!=offset+4||parts[offset+1]!="profiles"||parts[offset+3]!="profile.json"||!Guid.TryParseExact(parts[offset+2],"N",out _))continue;
            string path=FileAt(plan.Target,targetFiles[i].Relative);
            var profile=Newtonsoft.Json.JsonConvert.DeserializeObject<ManagedProfile>(File.ReadAllText(path))??throw new IOException("Повреждён профиль модов.");
            string RootHash(string root)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
            string relativeRoot=Path.Combine(parts.Take(offset).ToArray());
            if(profile.RootHash!=RootHash(Path.Combine(plan.Source,relativeRoot)))throw new IOException("Профиль модов не принадлежит исходной папке.");
            File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(profile with{RootHash=RootHash(Path.Combine(plan.Target,relativeRoot))}));
            targetFiles[i]=targetFiles[i] with{Bytes=new FileInfo(path).Length,Sha256=Hash(path)};
        }
        journal=journal with{TargetFiles=targetFiles};LocalJson.Write(journalFile,journal);
        Verify(plan.Target,targetFiles,token);ensureClosed();token.ThrowIfCancellationRequested();
        journal=journal with{Phase="Подготовка переключения"};LocalJson.Write(journalFile,journal);
        Directory.Move(plan.Source,backup);
        try{(link??CreateJunction)(plan.Source,plan.Target);}
        catch{if(!Directory.Exists(plan.Source)&&!File.Exists(plan.Source))Directory.Move(backup,plan.Source);throw;}
        journal=journal with{Phase="Перенесено; исходная копия сохранена"};LocalJson.Write(journalFile,journal);return journal;
    }
    private static void CreateJunction(string source,string target)
    {
        string Quote(string p)=>"'"+p.Replace("'","''")+"'";
        string script="$ErrorActionPreference='Stop';New-Item -ItemType Junction -Path "+Quote(source)+" -Value "+Quote(target)+" | Out-Null";
        var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-EncodedCommand");start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process=Process.Start(start)??throw new IOException("Не удалось создать ссылку на игру.");var error=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();process.WaitForExit();Task.WhenAll(error,output).GetAwaiter().GetResult();if(process.ExitCode!=0)throw new IOException("Не удалось сохранить старый путь игры: "+error.Result);
        if(!Directory.Exists(source)||(File.GetAttributes(source)&FileAttributes.ReparsePoint)==0)throw new IOException("Ссылка на игру не создана.");
    }
    internal static void ReleaseBackup(GameTransferJournal journal,string journalFile,Action ensureClosed,CancellationToken token)
    {
        using var guard=Lock(journal.Source);
        ensureClosed();Validate(journal);if(!Directory.Exists(journal.Source)||(File.GetAttributes(journal.Source)&FileAttributes.ReparsePoint)==0)throw new IOException("Старый путь игры больше не является ссылкой.");
        var resolved=Directory.ResolveLinkTarget(journal.Source,true);if(resolved==null||!Full(resolved.FullName).Equals(Full(journal.Target),StringComparison.OrdinalIgnoreCase))throw new IOException("Ссылка указывает на другую папку.");
        Verify(journal.Target,journal.TargetFiles??journal.Files,token,true);
        if(journal.Phase=="Освобождение места"&&!Directory.Exists(journal.Backup)){LocalJson.Write(journalFile,journal with{Phase="Завершено"});return;}
        if(journal.Phase=="Освобождение места")
        {
            var known=journal.Files.ToDictionary(f=>f.Relative,StringComparer.OrdinalIgnoreCase);
            foreach(var actual in Snapshot(journal.Backup,token,null))if(!known.TryGetValue(actual.Relative,out var original)||actual.Sha256!=original.Sha256||actual.Bytes!=original.Bytes)throw new IOException("Страховочная копия изменена. Удаление остановлено.");
        }
        else Verify(journal.Backup,journal.Files,token);
        ensureClosed();token.ThrowIfCancellationRequested();LocalJson.Write(journalFile,journal with{Phase="Освобождение места"});
        // Delete only the verified manifest, never recursively follow links or delete unknown files.
        foreach(var file in journal.Files){File.Delete(FileAt(journal.Backup,file.Relative));}
        foreach(string directory in Directory.EnumerateDirectories(journal.Backup,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length)){NoLinks(directory);Directory.Delete(directory);}
        Directory.Delete(journal.Backup);LocalJson.Write(journalFile,journal with{Phase="Завершено"});
    }
    internal static void Restore(GameTransferJournal journal,string journalFile,Action ensureClosed)
    {
        using var guard=Lock(journal.Source);
        ensureClosed();Validate(journal);
        if(journal.Phase is "Освобождение места" or "Завершено")throw new IOException("Страховочная копия уже удалялась. Автоматический возврат недоступен.");
        if(!Directory.Exists(journal.Backup))
        {
            if(Directory.Exists(journal.Source)&&(File.GetAttributes(journal.Source)&FileAttributes.ReparsePoint)==0)
            {Verify(journal.Source,journal.Files,default);File.Move(journalFile,journalFile+".cancelled-"+Guid.NewGuid().ToString("N"));return;}
            throw new IOException("Страховочная папка не найдена. Проверьте пути из журнала вручную.");
        }
        Verify(journal.Backup,journal.Files,default);NoLinks(Path.GetDirectoryName(journal.Source)!);
        if(Directory.Exists(journal.Source))
        {
            if((File.GetAttributes(journal.Source)&FileAttributes.ReparsePoint)==0)throw new IOException("Исходный путь уже занят обычной папкой.");
            var resolved=Directory.ResolveLinkTarget(journal.Source,true);
            if(resolved==null||!Full(resolved.FullName).Equals(Full(journal.Target),StringComparison.OrdinalIgnoreCase))throw new IOException("Исходная ссылка была изменена.");
            Directory.Delete(journal.Source); // Remove the junction itself, never its contents.
        }
        Directory.Move(journal.Backup,journal.Source);
        File.Move(journalFile,journalFile+".restored-"+Guid.NewGuid().ToString("N"));
    }
    internal static void Validate(GameTransferJournal j)
    {
        if(!Guid.TryParseExact(j.Id,"N",out _)||j.Backup!=j.Source+".playspace-backup-"+j.Id||j.Files==null||j.Files.Any(f=>f.Bytes<0||f.Sha256.Length!=64))throw new InvalidDataException("Повреждён журнал переноса.");
        GameFolder(j.Target);GameFolder(j.Backup);if(Inside(j.Target,j.Backup)||Inside(j.Backup,j.Target))throw new InvalidDataException("Пересекающиеся пути переноса.");
        foreach(var file in j.Files){FileAt(j.Target,file.Relative);FileAt(j.Backup,file.Relative);}
    }
}
