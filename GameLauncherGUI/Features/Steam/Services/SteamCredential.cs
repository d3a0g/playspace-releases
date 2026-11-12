using System.Runtime.InteropServices;
using System.Text;

namespace GameLauncherGUI;

// Windows encrypts this credential for the current user; it never enters games.json.
internal static class SteamCredential
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct Credential {
        public uint Flags,Type; public string TargetName,Comment; public long LastWritten;
        public uint BlobSize; public IntPtr Blob; public uint Persist,AttributeCount;
        public IntPtr Attributes; public string TargetAlias,UserName;
    }
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool Write(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool Read(string target,uint type,uint flags,out IntPtr pointer);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr pointer);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool Delete(string target,uint type,uint flags);
    public static void Save(string target,string user,string key)
    {
        byte[] bytes=Encoding.Unicode.GetBytes(key);IntPtr blob=Marshal.AllocHGlobal(bytes.Length);
        try {
            Marshal.Copy(bytes,0,blob,bytes.Length);
            var credential=new Credential {Type=1,TargetName=target,Comment="PLAYSPACE Steam achievements",BlobSize=(uint)bytes.Length,Blob=blob,Persist=2,UserName=user,TargetAlias=""};
            if(!Write(ref credential,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        } finally { for(int i=0;i<bytes.Length;i++)Marshal.WriteByte(blob,i,0);Marshal.FreeHGlobal(blob);Array.Clear(bytes); }
    }
    public static (string User,string Key)? Load(string target)
    {
        if(!Read(target,1,0,out var pointer))return null;
        try { var credential=Marshal.PtrToStructure<Credential>(pointer);return (credential.UserName,Marshal.PtrToStringUni(credential.Blob,(int)credential.BlobSize/2)??""); }
        finally { CredFree(pointer); }
    }
    public static void Remove(string target) { if(!Delete(target,1,0) && Marshal.GetLastWin32Error()!=1168)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
}
