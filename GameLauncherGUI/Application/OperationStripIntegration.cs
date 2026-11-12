namespace GameLauncherGUI;

public partial class Form1
{
    private void InstallOperationStrip()
    {
        if(lblStatus.Parent is not TableLayoutPanel footer||footer.Parent is not TableLayoutPanel content)return;
        footer.RowCount=2;footer.RowStyles.Clear();footer.RowStyles.Add(new(SizeType.Absolute,34));footer.RowStyles.Add(new(SizeType.Percent,100));
        var operations=new OperationStrip(Workspace.For(appDataFolder).Transfers);footer.Controls.Add(operations,0,1);footer.SetColumnSpan(operations,2);
        operations.ActivityChanged+=active=>{if(!IsDisposed){content.RowStyles[4].Height=active?86:34;content.PerformLayout();}};
        operations.OpenRequested+=()=>{using var window=new DownloadsForm(Workspace.For(appDataFolder).Transfers);window.ShowDialog(this);};
    }
}
