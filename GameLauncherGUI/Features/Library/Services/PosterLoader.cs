using System.Net.Http;

namespace GameLauncherGUI;

internal static class PosterLoader
{
    private const int MaximumBytes=10*1024*1024;
    private static readonly HttpClient client=new(){Timeout=TimeSpan.FromSeconds(10)};

    internal static async Task<Bitmap?> LoadAsync(string dataFolder,string id,bool offline,CancellationToken token,bool wide=false)
    {
        string folder=Path.Combine(dataFolder,wide?"wide-posters":"posters"),file=Path.Combine(folder,id+".jpg");
        try{
            Directory.CreateDirectory(folder);
            byte[] bytes;
            if(File.Exists(file)){
                if(new FileInfo(file).Length>MaximumBytes)return null;
                bytes=await File.ReadAllBytesAsync(file,token).ConfigureAwait(false);
            }else{
                if(offline)return null;
                string asset=wide?"header.jpg":"library_600x900_2x.jpg";
                using var response=await client.GetAsync($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/{asset}",HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>MaximumBytes)return null;
                using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var buffer=new MemoryStream();var block=new byte[81920];int count;
                while((count=await input.ReadAsync(block,token).ConfigureAwait(false))>0){if(buffer.Length+count>MaximumBytes)return null;await buffer.WriteAsync(block.AsMemory(0,count),token).ConfigureAwait(false);}
                bytes=buffer.ToArray();
                // GDI+ decoding/resizing is synchronous CPU work; network I/O stays asynchronous.
                bytes=await Task.Run(()=>{
                    using var source=new MemoryStream(bytes);using var validated=Image.FromStream(source);
                    using var resized=new Bitmap(validated,wide?460:300,wide?215:450);using var encoded=new MemoryStream();
                    resized.Save(encoded,System.Drawing.Imaging.ImageFormat.Jpeg);return encoded.ToArray();
                },token).ConfigureAwait(false);
                string temporary=Path.Combine(folder,id+"."+Guid.NewGuid().ToString("N")+".tmp");
                try{await File.WriteAllBytesAsync(temporary,bytes,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();File.Move(temporary,file,true);}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            return await Task.Run(()=>{
                token.ThrowIfCancellationRequested();using var stream=new MemoryStream(bytes);using var source=Image.FromStream(stream);var image=new Bitmap(source);
                if(token.IsCancellationRequested){image.Dispose();token.ThrowIfCancellationRequested();}return image;
            },token).ConfigureAwait(false);
        }
        catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException){return null;}
    }
}
