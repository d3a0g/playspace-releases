using GameLauncherGUI;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class HardwareCatalogChecks
{
    internal static void Run(bool ui)
    {
        int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        var rows=BenchmarkCatalog.All;
        Check(rows.Length==337,"Unexpected evidence count");
        Check(rows.Select(r=>r.Gpu).Distinct().Count()==9&&HardwareEvidence.Rigs.Length==39,"Rig coverage changed");
        Check(rows.Select(r=>r.GameId).Distinct().Count()==11,"Game coverage changed");
        Check(rows.Select(r=>(r.Publisher,r.Observation)).Distinct().Count()==rows.Length,"Duplicate observation");
        Check(rows.All(r=>r.Fps>0&&double.IsFinite(r.Fps)&&r.Cpu.Length>0&&new Uri(r.Source).Scheme=="https"),"Invalid observation");
        Check(rows.Any(r=>r.Gpu=="RTX 3060"&&r.GameId=="1091500"&&r.Fps==74.8),"Source reference lost");
        var steam=HardwareEvidence.Steam;
        Check(steam.Month=="October 2026"&&steam.Gpus.Length==32&&steam.HtmlSHA256.Length==64,"Steam snapshot provenance");
        Check(HardwareEvidence.SteamShare("RTX 3060")==3.62,"Wrong survey column imported");
        Check(steam.Gpus.All(r=>r.Share is >=0 and <=100)&&steam.Ram.All(r=>r.Share is >=0 and <=100),"Invalid percentages");
        Check(BenchmarkCatalog.GpuKey("NVIDIA GeForce GTX 1650")=="GTX 1650"&&BenchmarkCatalog.GpuKey("RTX 3060 Laptop GPU")=="laptop","GPU variants mixed");
        if(ui)
        {
            Application.EnableVisualStyles();
            foreach(var view in Enum.GetValues<EvidenceView>())
            {
                using var form=new Form{Size=new(1100,760)};using var browser=new BenchmarkBrowser(view);form.Controls.Add(browser);form.Show();Application.DoEvents();
                var grid=Children(browser).OfType<DataGridView>().Single();Check(grid.Rows.Count>0&&grid.Rows.Cast<DataGridViewRow>().All(r=>r.Tag!=null),"Empty evidence page / lost source");
                if(view==EvidenceView.Fps){var search=Children(browser).OfType<TextBox>().Single();search.Text="unlikely-game-xyz";Check(grid.Rows.Count==0,"Search does not filter");search.Clear();Check(grid.Rows.Count==337,"Search reset");form.ClientSize=new(620,250);Application.DoEvents();Check(grid.Height>=80,"Compact benchmark table collapsed");}
                form.Close();
            }
            using var host=new Form();using var text=new TextBox();host.Controls.Add(text);UiTypography.Apply(host);host.Show();Application.DoEvents();
            IntPtr font=SendMessage(text.Handle,0x31,IntPtr.Zero,IntPtr.Zero);var spec=new byte[92];Check(font!=IntPtr.Zero&&GetObject(font,spec.Length,spec)>0&&spec[26]==5,"Native edit font is not ClearType");
            using var label=new SmoothLabel();Check(label.UseCompatibleTextRendering,"Label renderer changed");host.Close();
        }
        Console.WriteLine($"PASS: {checks} hardware evidence / filters / native text quality checks.");
    }
    private static IEnumerable<Control> Children(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Children(c))yield return child;}}
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("gdi32.dll",EntryPoint="GetObjectW")]private static extern int GetObject(IntPtr obj,int length,byte[] data);
}
