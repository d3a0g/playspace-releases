namespace GameLauncherGUI;

internal sealed class DuplicateGamesForm : StudioDialog
{
    internal DuplicateGamesForm(Func<IReadOnlyList<DuplicateGroup>> find,Action<DuplicateGroup,GameInfo> merge)
    {
        Text="PLAYSPACE / Дубликаты библиотеки";Size=new(960,720);MinimumSize=new(800,600);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,Padding=new Padding(24)};layout.RowStyles.Add(new(SizeType.Absolute,110));layout.RowStyles.Add(new(SizeType.Percent,40));layout.RowStyles.Add(new(SizeType.Percent,60));layout.RowStyles.Add(new(SizeType.Absolute,52));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new SmoothLabel{Dock=DockStyle.Fill,Text="Совпадение Steam AppID или пути EXE. Одинаковые названия не объединяются автоматически. Выберите группу, затем запись, которую сохранить. Пути и аргументы запуска останутся у выбранной записи; закрепления, коллекции и максимальное время будут объединены. Файлы игр не удаляются. Перед объединением сохраняется копия библиотеки.",AccessibleName="Правила объединения дубликатов"},0,0);
        var groups=new ListBox{Dock=DockStyle.Fill,AccessibleName="Группы дубликатов"};var games=new ListBox{Dock=DockStyle.Fill,AccessibleName="Запись, которую сохранить",HorizontalScrollbar=true};layout.Controls.Add(groups,0,1);layout.Controls.Add(games,0,2);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill};var apply=new ModernButton("Объединить выбранную группу",VisualTheme.Accent){Width=330,Height=42};var status=new SmoothLabel{Width=470,Height=42};footer.Controls.Add(apply);footer.Controls.Add(status);layout.Controls.Add(footer,0,3);
        IReadOnlyList<DuplicateGroup> found=Array.Empty<DuplicateGroup>();
        void Refresh(){found=find();groups.Items.Clear();games.Items.Clear();foreach(var group in found)groups.Items.Add(group.Games[0].Name+" · записей: "+group.Games.Length);if(groups.Items.Count>0)groups.SelectedIndex=0;else status.Text="Дубликатов не найдено.";apply.Enabled=found.Count>0;}
        groups.SelectedIndexChanged+=(s,e)=>{games.Items.Clear();if(groups.SelectedIndex<0)return;foreach(var game in found[groups.SelectedIndex].Games)games.Items.Add(game.Name+" | "+game.ExePath+" | Steam: "+game.SteamAppId);games.SelectedIndex=0;};
        apply.Click+=(s,e)=>{if(groups.SelectedIndex<0||games.SelectedIndex<0)return;var group=found[groups.SelectedIndex];var keep=group.Games[games.SelectedIndex];if(MessageBox.Show(this,"Сохранить запись «"+keep.Name+"» с путём:\n"+keep.ExePath+"\n\nДругие записи группы исчезнут из библиотеки, файлы игр останутся.","Объединение",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;try{merge(group,keep);Refresh();status.Text="Группа объединена. Файлы игр сохранены.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){status.Text=ex.Message;}};
        Shown+=(s,e)=>Refresh();
    }
}
