using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Json {
    public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }; }
    public static Dictionary<string, object> Obj(object x) { return (Dictionary<string, object>)x; }
    public static IEnumerable<object> Arr(object x) { return ((IEnumerable)x).Cast<object>(); }
    public static double Num(object x) { return Convert.ToDouble(x); }
    public static string Str(object x) { return Convert.ToString(x); }
    public static Dictionary<string, object> Read(string file) { return Obj(Serializer().DeserializeObject(File.ReadAllText(file))); }
    public static void Write(string file, object value) { File.WriteAllText(file, Serializer().Serialize(value), new UTF8Encoding(false)); }
}
class Marker {
    public string Key, Title, Role; public int Road, Id; public double X, Z;
    public override string ToString() { return Title + "  /  " + Id; }
}
class Section { public int Id, Road; public PointF[] Points; }
class Restriction {
    public int Id; public string Title;
    public override string ToString() { return Title + "  [" + Id + "]"; }
}
class MapCanvas : Control {
    public List<Section> Sections = new List<Section>();
    public List<Marker> Markers = new List<Marker>();
    public List<PointF[]> Guide = new List<PointF[]>();
    public List<Marker> Route = new List<Marker>();
    public int SelectedRoad; public string Role = "start";
    public Action<int, Marker> Pick;
    float scale, ox, oy; bool fit = true, dragging; Point dragAt;
    public MapCanvas() {
        DoubleBuffered = true; BackColor = Color.FromArgb(12, 22, 31); Dock = DockStyle.Fill;
        MouseWheel += delegate(object s, MouseEventArgs e) {
            float factor = e.Delta > 0 ? 1.2f : 1f / 1.2f;
            float next = Math.Max(.015f, Math.Min(2f, scale * factor)); factor = next / scale;
            ox = e.X - (e.X - ox) * factor; oy = e.Y - (e.Y - oy) * factor; scale = next; fit = false; Invalidate();
        };
        MouseDown += delegate(object s, MouseEventArgs e) {
            Focus(); if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) { dragging = true; dragAt = e.Location; Cursor = Cursors.SizeAll; }
        };
        MouseMove += delegate(object s, MouseEventArgs e) {
            if (dragging) { ox += e.X - dragAt.X; oy += e.Y - dragAt.Y; dragAt = e.Location; fit = false; Invalidate(); }
        };
        MouseUp += delegate { dragging = false; Cursor = Cursors.Default; };
        MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Choose(e.Location); };
        Resize += delegate { if (fit) FitMap(); };
    }
    public void FitMap() {
        if (Sections.Count == 0 || Width < 20 || Height < 20) return;
        var pts = Sections.SelectMany(s => s.Points).ToList();
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minZ = pts.Min(p => p.Y), maxZ = pts.Max(p => p.Y);
        scale = Math.Min((Width - 90) / (maxX - minX), (Height - 90) / (maxZ - minZ));
        ox = (Width - (maxX - minX) * scale) / 2 - minX * scale;
        oy = (Height - (maxZ - minZ) * scale) / 2 - minZ * scale; fit = true; Invalidate();
    }
    // Reflect the old map about its horizontal axis; world/recipe coordinates stay unchanged.
    PointF Screen(PointF p) { return new PointF(p.X * scale + ox, p.Y * scale + oy); }
    PointF Screen(Marker m) { return Screen(new PointF((float)m.X, (float)m.Z)); }
    static double Dist(PointF a, PointF b) { return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)); }
    static double SegmentDist(PointF p, PointF a, PointF b) {
        double dx = b.X-a.X, dy = b.Y-a.Y, n = dx*dx+dy*dy;
        double t = n == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X-a.X)*dx+(p.Y-a.Y)*dy)/n));
        return Dist(p, new PointF((float)(a.X+t*dx), (float)(a.Y+t*dy)));
    }
    void Choose(Point point) {
        var marker = Markers.Where(m => m.Role == Role).OrderBy(m => Dist(Screen(m),point)).FirstOrDefault();
        if (marker != null && Dist(Screen(marker),point) < 13) { if (Pick != null) Pick(marker.Road,marker); return; }
        double best = 12; int road = 0;
        foreach (var section in Sections) for (int i=0;i<section.Points.Length-1;i++) {
            double d = SegmentDist(point, Screen(section.Points[i]), Screen(section.Points[i+1]));
            if (d < best) { best = d; road = section.Road; }
        }
        if (road != 0 && Pick != null) Pick(road,null);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var pen=new Pen(Color.FromArgb(27,44,57),1)) {
            for(int x=0;x<Width;x+=64)g.DrawLine(pen,x,0,x,Height);
            for(int y=0;y<Height;y+=64)g.DrawLine(pen,0,y,Width,y);
        }
        using(var normal=new Pen(Color.FromArgb(95,125,142),2))
        using(var selected=new Pen(Color.FromArgb(72,205,221),4))
        foreach(var section in Sections) if(section.Points.Length>1) g.DrawLines(section.Road==SelectedRoad?selected:normal,section.Points.Select(Screen).ToArray());
        using(var pen=new Pen(Color.FromArgb(249,196,91),3)) {
            pen.DashStyle=DashStyle.Dash;
            foreach(var line in Guide) if(line.Length>1)g.DrawLines(pen,line.Select(Screen).ToArray());
        }
        foreach(var m in Markers.Where(m=>m.Role==Role)) {
            var p=Screen(m); bool onRoad=m.Road==SelectedRoad; float r=onRoad?5:3;
            using(var b=new SolidBrush(onRoad?Color.FromArgb(98,228,236):Color.FromArgb(160,180,192)))g.FillEllipse(b,p.X-r,p.Y-r,r*2,r*2);
        }
        for(int i=0;i<Route.Count;i++) {
            var m=Route[i]; var p=Screen(m); Color c=m.Role=="start"?Color.FromArgb(81,222,157):m.Role=="finish"?Color.FromArgb(250,139,119):Color.FromArgb(248,205,91);
            using(var brush=new SolidBrush(c))g.FillEllipse(brush,p.X-10,p.Y-10,20,20);
            string label=m.Role=="start"?"S":m.Role=="finish"?"F":i.ToString();
            using(var b=new SolidBrush(Color.FromArgb(12,22,31)))g.DrawString(label,Font,b,p.X-5,p.Y-7);
        }
        using(var b=new SolidBrush(Color.FromArgb(188,206,215))) {
            g.DrawString("SEACREST COUNTY  /  sampled road network",Font,b,18,16);
            g.DrawString("Wheel: zoom    Right-drag: pan"+(Pick==null?"    Route preview":"    Click a road or marker"),Font,b,18,Height-30);
        }
    }
}
partial class MakerForm : Form {
    string project, python, backend, session, offlineBuild; int serial; bool busy, updating;
    List<Marker> markers = new List<Marker>(); List<Restriction> restrictions = new List<Restriction>();
    Marker start, finish; List<Marker> via=new List<Marker>();
    MapCanvas map=new MapCanvas(); TextBox routeName=new TextBox(); ComboBox role=new ComboBox(), roadRules=new ComboBox();
    ListBox choices=new ListBox(), itinerary=new ListBox(); Label roadLabel=new Label(), status=new Label(), previewLabel=new Label();
    Button buildOffline, buildOnline, preview, saveLibrary; FlowLayoutPanel tools;
    TabControl pages=new TabControl(); TabPage editorPage=new TabPage("Route editor"),libraryPage=new TabPage("Saved races");
    ListBox libraryList=new ListBox(); MapCanvas libraryMap=new MapCanvas();
    Label libraryDetails=new Label(),libraryIssues=new Label(); ComboBox libraryVariants=new ComboBox();
    Button libraryEdit,libraryOpen; List<LibraryRace> savedRaces=new List<LibraryRace>();
    class LibraryRace {
        public Dictionary<string,object> Data;
        public override string ToString(){return Json.Str(Data["name"])+"  ["+Json.Str(Data["id"]).Substring(0,8)+"]";}
    }
    class LibraryVariant {
        public Dictionary<string,object> Data;
        public override string ToString(){return Json.Str(Data["mode"])+" — "+Json.Str(Data["status"])+" — "+Path.GetFileName(Json.Str(Data["path"]));}
    }
    string latestBuild;
    public MakerForm() {
        var config=Json.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"config.json"));
        project=Json.Str(config["project"]);python=Json.Str(config["python"]);backend=Path.Combine(project,"scripts","route_maker.py");
        session=Path.Combine(project,"working","route-maker-sessions",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));Directory.CreateDirectory(session);
        Text="Seacrest Route Maker — local prototype"; Size=new Size(1320,900); MinimumSize=new Size(1100,790); StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",9.5f); AutoScaleMode=AutoScaleMode.Dpi; BackColor=Color.FromArgb(238,243,245);
        var outer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(16)};
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,62));outer.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.Absolute,42));Controls.Add(outer);
        var title=new Label {Text="SEACREST  /  ROUTE MAKER",Dock=DockStyle.Top,Height=30,Font=new Font("Segoe UI",19,FontStyle.Bold),ForeColor=Color.FromArgb(20,45,60)};
        var head=new Panel {Dock=DockStyle.Fill};head.Controls.Add(new Label {Text="Create test courses on existing roads. Library, offline and online builds, and game installation.",Dock=DockStyle.Bottom,Height=24});head.Controls.Add(title);outer.Controls.Add(head,0,0);
        tools=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
        tools.Controls.Add(Button("Tested 6 km preset",delegate{pages.SelectedTab=editorPage;Preset();}));tools.Controls.Add(Button("New route",delegate{pages.SelectedTab=editorPage;Clear();}));
        tools.Controls.Add(Button("Import recipe",OpenRecipe));saveLibrary=Button("Save to library",SaveRecipe);tools.Controls.Add(saveLibrary);tools.Controls.Add(Button("Fit map",delegate{if(pages.SelectedTab==libraryPage)libraryMap.FitMap();else map.FitMap();}));
        tools.Controls.Add(Button("Open latest export",delegate{if(latestBuild!=null)Process.Start(new ProcessStartInfo(latestBuild){UseShellExecute=true});}));outer.Controls.Add(tools,0,1);
        var body=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,370));
        body.Controls.Add(map,0,0);
        var side=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=16,Padding=new Padding(14,0,0,0)};
        int[] heights={22,29,23,30,24,110,32,22,100,32,22,30,50,36,50,36};
        foreach(int h in heights)side.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        side.Controls.Add(Label("Route name"),0,0);routeName.Dock=DockStyle.Fill;routeName.Text="Grand Ocean Sprint";side.Controls.Add(routeName,0,1);
        side.Controls.Add(Label("1  PICK ROAD + EXISTING MARKER"),0,2);
        role.DropDownStyle=ComboBoxStyle.DropDownList;role.Items.AddRange(new object[]{"start","waypoint","finish"});role.SelectedIndex=0;role.Dock=DockStyle.Fill;side.Controls.Add(role,0,3);
        roadLabel.Dock=DockStyle.Fill;roadLabel.Text="Click a road to filter markers";side.Controls.Add(roadLabel,0,4);
        choices.Dock=DockStyle.Fill;side.Controls.Add(choices,0,5);side.Controls.Add(Button("Use selected marker",UseSelected),0,6);
        side.Controls.Add(Label("2  COURSE ORDER  (maximum 6 markers)"),0,7);itinerary.Dock=DockStyle.Fill;side.Controls.Add(itinerary,0,8);
        var controls=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};controls.Controls.Add(Button("Remove",Remove));controls.Controls.Add(Button("Via up",delegate{MoveVia(-1);}));controls.Controls.Add(Button("Via down",delegate{MoveVia(1);}));side.Controls.Add(controls,0,9);
        side.Controls.Add(Label("Stock restriction set"),0,10);roadRules.DropDownStyle=ComboBoxStyle.DropDownList;roadRules.Dock=DockStyle.Fill;side.Controls.Add(roadRules,0,11);
        previewLabel.Dock=DockStyle.Fill;previewLabel.Text="Dashed amber path = undirected estimate. Direction and race restrictions are not simulated.";side.Controls.Add(previewLabel,0,12);
        var exportRow=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};preview=Button("Preview",async delegate{await Preview();});buildOffline=Button("Export offline",async delegate{await Export(false);});exportRow.Controls.Add(preview);exportRow.Controls.Add(buildOffline);side.Controls.Add(exportRow,0,13);
        var caveat=Label("V0.1 uses existing start grids, waypoints and finish markers. No free placement. Test every new recipe offline first.");caveat.Dock=DockStyle.Fill;side.Controls.Add(caveat,0,14);
        buildOnline=Button("Export online after offline success",async delegate{await Export(true);});buildOnline.Dock=DockStyle.Fill;side.Controls.Add(buildOnline,0,15);
        var scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true};side.AutoSize=true;side.Dock=DockStyle.Top;scroll.Controls.Add(side);body.Controls.Add(scroll,1,0);
        pages.Dock=DockStyle.Fill;editorPage.Controls.Add(body);pages.TabPages.Add(editorPage);pages.TabPages.Add(libraryPage);BuildLibraryPage();BuildGamePage();outer.Controls.Add(pages,0,2);
        status.Dock=DockStyle.Fill;status.ForeColor=Color.FromArgb(20,75,92);status.Text="Loading local map…";outer.Controls.Add(status,0,3);
        role.SelectedIndexChanged+=delegate {map.Role=role.Text;RefreshChoices();map.Invalidate();};
        choices.DoubleClick+=delegate{UseSelected();};roadRules.SelectedIndexChanged+=delegate{if(!updating)Changed();};
        map.Pick=delegate(int road,Marker m){map.SelectedRoad=road;roadLabel.Text="Road "+road+"  /  "+role.Text+" markers";RefreshChoices();if(m!=null){choices.SelectedItem=m;SelectMarker(m);}map.Invalidate();};
        LoadData();libraryMap.Sections=map.Sections;libraryMap.Markers=new List<Marker>();
        pages.SelectedIndexChanged+=delegate{saveLibrary.Enabled=!busy&&pages.SelectedTab==editorPage;if(pages.SelectedTab==gamePage){if(!busy)InspectOnTab();}else if(pages.SelectedTab==libraryPage)libraryMap.FitMap();else map.FitMap();};
        Shown+=async delegate{map.FitMap();await Preview();await RefreshLibrary();};
    }
    async void InspectOnTab(){await InspectInstallation();}
    void BuildLibraryPage() {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(8)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,350));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(0,0,12,0)};
        left.RowStyles.Add(new RowStyle(SizeType.Absolute,30));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,42));left.RowStyles.Add(new RowStyle(SizeType.Absolute,105));
        left.Controls.Add(Label("SAVED COURSES  /  choose one to preview"),0,0);
        libraryList.Dock=DockStyle.Fill;libraryList.DrawMode=DrawMode.OwnerDrawFixed;libraryList.ItemHeight=66;libraryList.IntegralHeight=false;
        libraryList.DrawItem+=delegate(object s,DrawItemEventArgs e){if(e.Index<0)return;e.DrawBackground();var item=(LibraryRace)libraryList.Items[e.Index];var d=item.Data;Color c=(e.State&DrawItemState.Selected)!=0?SystemColors.HighlightText:Color.FromArgb(25,50,65);
            using(var b=new SolidBrush(c)) {var box=new RectangleF(e.Bounds.X+8,e.Bounds.Y+5,e.Bounds.Width-16,22);gText(e.Graphics,Json.Str(d["name"]),b,box);
                box.Y+=23;box.Height=20;gText(e.Graphics,Json.Num(d["estimated_length"]).ToString("N0")+" world units · "+d["marker_count"]+" markers · "+Json.Str(d["id"]).Substring(0,8),b,box);}
            e.DrawFocusRectangle();};
        libraryList.SelectedIndexChanged+=delegate{ShowLibraryRace();};left.Controls.Add(libraryList,0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};buttons.Controls.Add(Button("Refresh",async delegate{await RefreshLibrary();}));buttons.Controls.Add(Button("Import recipe",OpenRecipe));left.Controls.Add(buttons,0,2);
        libraryIssues.Dock=DockStyle.Fill;libraryIssues.AutoEllipsis=true;left.Controls.Add(libraryIssues,0,3);
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,104));right.RowStyles.Add(new RowStyle(SizeType.Absolute,34));right.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        right.Controls.Add(libraryMap,0,0);libraryDetails.Dock=DockStyle.Fill;libraryDetails.Padding=new Padding(0,7,0,0);right.Controls.Add(libraryDetails,0,1);
        libraryVariants.Dock=DockStyle.Fill;libraryVariants.DropDownStyle=ComboBoxStyle.DropDownList;libraryVariants.DropDownWidth=850;right.Controls.Add(libraryVariants,0,2);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};libraryEdit=Button("Edit a copy",EditLibraryRace);libraryOpen=Button("Open selected build",OpenLibraryBuild);actions.Controls.Add(libraryEdit);actions.Controls.Add(libraryOpen);actions.Controls.Add(Button("Game installation…",delegate{var v=libraryVariants.SelectedItem as LibraryVariant;OpenGamePage(v==null?null:(string)v.Data["path"]);}));right.Controls.Add(actions,0,3);
        layout.Controls.Add(left,0,0);layout.Controls.Add(right,1,0);libraryPage.Controls.Add(layout);ShowLibraryRace();
    }
    void gText(Graphics g,string text,Brush brush,RectangleF box) {using(var fmt=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,Font,brush,box,fmt);}
    async Task ReloadLibrary(string preferred=null) {
        var result=await Run("library","");var current=libraryList.SelectedItem as LibraryRace;
        string key=preferred??(current==null?null:Json.Str(current.Data["id"]));
        savedRaces=Json.Arr(result["entries"]).Select(x=>new LibraryRace{Data=Json.Obj(x)}).ToList();
        libraryList.BeginUpdate();libraryList.Items.Clear();libraryList.Items.AddRange(savedRaces.Cast<object>().ToArray());libraryList.EndUpdate();
        libraryList.SelectedItem=savedRaces.FirstOrDefault(x=>Json.Str(x.Data["id"])==key)??savedRaces.FirstOrDefault();
        var errors=Json.Arr(result["issues"]).Select(x=>Json.Obj(x)).ToList();
        libraryIssues.Text=errors.Count==0?"Estimates use world units. Reported game distances appear separately.\nBrowsing keeps your editor draft intact.":errors.Count+" unavailable item(s).\n"+String.Join("\n",errors.Take(2).Select(x=>Path.GetFileName(Json.Str(x["path"]))+": "+Json.Str(x["message"])));
        libraryPage.Text="Saved races ("+savedRaces.Count+")";ShowLibraryRace();PopulateInstallChoices();
    }
    async Task RefreshLibrary() {
        if(busy)return;
        try{Busy(true);status.Text="Reading saved courses and checking export files…";await ReloadLibrary();status.Text="Saved-race library refreshed. Choose a course to preview it.";}
        catch(Exception ex){status.Text="Cannot refresh library: "+ex.Message;}
        finally{Busy(false);}
    }
    void ShowLibraryRace() {
        var item=libraryList.SelectedItem as LibraryRace;libraryMap.Route.Clear();libraryMap.Guide.Clear();libraryVariants.Items.Clear();libraryEdit.Enabled=item!=null;libraryOpen.Enabled=false;
        if(item==null){libraryDetails.Text="Saved courses will appear here. Save your editor route or import a recipe.";libraryMap.Invalidate();return;}
        var d=item.Data;var p=Json.Obj(d["preview"]);var recipe=Json.Obj(d["recipe"]);
        libraryMap.Route=Json.Arr(recipe["locations"]).Select(x=>markers.Single(m=>m.Key==Json.Str(x))).ToList();
        libraryMap.Guide=Json.Arr(p["estimated_lines"]).Select(Lines).ToList();libraryMap.FitMap();
        libraryDetails.Text=Json.Str(d["name"])+"   /   revision "+Json.Str(d["id"]).Substring(0,8)+"   /   "+d["marker_count"]+" markers\n"+
            "Estimated length: "+Json.Num(d["estimated_length"]).ToString("N0")+" world units"+(p["reported_game_distance_km"]==null?"":"   |   Reported in game: "+p["reported_game_distance_km"]+" km")+"\n"+
            Json.Str(d["status"])+"\nAmber preview ignores direction, restrictions and junction gaps.";
        libraryVariants.Items.AddRange(Json.Arr(d["variants"]).Select(x=>(object)new LibraryVariant{Data=Json.Obj(x)}).ToArray());
        if(libraryVariants.Items.Count>0)libraryVariants.SelectedIndex=0;libraryOpen.Enabled=libraryVariants.Items.Count>0;
    }
    void PreserveEditorDraft() {
        var draft=new Dictionary<string,object>{{"name",routeName.Text},{"locations",map.Route.Select(m=>m.Key).ToArray()},{"restriction_source",roadRules.SelectedItem==null?0:((Restriction)roadRules.SelectedItem).Id},{"offline_build",offlineBuild}};
        Json.Write(Path.Combine(session,"editor-draft-"+(++serial)+".json"),draft);
    }
    void ApplyLibraryRace(LibraryRace item) {
        PreserveEditorDraft();var r=Json.Obj(item.Data["recipe"]);var selected=Json.Arr(r["locations"]).Select(x=>markers.Single(m=>m.Key==Json.Str(x))).ToList();
        start=selected.First();finish=selected.Last();via=selected.Skip(1).Take(selected.Count-2).ToList();routeName.Text=Json.Str(r["name"]);
        updating=true;roadRules.SelectedItem=restrictions.Single(x=>x.Id==Convert.ToInt32(r["restriction_source"]));updating=false;Changed();
        offlineBuild=Json.Str(item.Data["offline_build"]);if(String.IsNullOrWhiteSpace(offlineBuild))offlineBuild=null;
        map.Guide=Json.Arr(Json.Obj(item.Data["preview"])["estimated_lines"]).Select(Lines).ToList();map.SelectedRoad=0;RefreshChoices();map.FitMap();pages.SelectedTab=editorPage;
        previewLabel.Text="Estimated length: "+Json.Num(item.Data["estimated_length"]).ToString("N0")+" world units. "+Json.Str(item.Data["status"]);
        status.Text="Editing a copy. Save to library creates a revision; previous editor draft retained in the session folder.";
    }
    void EditLibraryRace(){var item=libraryList.SelectedItem as LibraryRace;if(item==null)return;try{ApplyLibraryRace(item);}catch(Exception ex){status.Text="Cannot edit course: "+ex.Message;}}
    void OpenLibraryBuild(){var v=libraryVariants.SelectedItem as LibraryVariant;if(v==null)return;string path=Json.Str(v.Data["path"]);if(Directory.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});else status.Text="Build folder is missing. Refresh the library.";}
    Label Label(string text) {return new Label {Text=text,Dock=DockStyle.Fill,AutoEllipsis=false};}
    Button Button(string text,Action action) {var b=new Button {Text=text,AutoSize=true,Height=30,FlatStyle=FlatStyle.System,Margin=new Padding(0,0,7,0)};b.Click+=delegate{if(!busy)action();};return b;}
    void LoadData() {
        var data=Json.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"map.json"));
        foreach(var item in Json.Arr(data["sections"])) {var o=Json.Obj(item);map.Sections.Add(new Section {Id=Convert.ToInt32(o["id"]),Road=Convert.ToInt32(o["road_id"]),Points=Lines(o["points"])});}
        foreach(var item in Json.Arr(data["markers"])) {var o=Json.Obj(item);var xyz=Json.Arr(o["xyz"]).ToArray();markers.Add(new Marker{Key=Json.Str(o["key"]),Title=Json.Str(o["title"]),Role=Json.Str(o["role"]),Road=Convert.ToInt32(o["road_id"]),Id=Convert.ToInt32(o["marker_id"]),X=Json.Num(xyz[0]),Z=Json.Num(xyz[2])});}
        map.Markers=markers;
        foreach(var item in Json.Arr(data["restriction_sources"])) {var o=Json.Obj(item);restrictions.Add(new Restriction{Id=Convert.ToInt32(o["event_id"]),Title=Json.Str(o["title"])});}
        updating=true;roadRules.Items.AddRange(restrictions.Cast<object>().ToArray());updating=false;Preset();
    }
    static PointF[] Lines(object raw) {return Json.Arr(raw).Select(x=>{var a=Json.Arr(x).ToArray();return new PointF((float)Json.Num(a[0]),(float)Json.Num(a[2]));}).ToArray();}
    void RefreshChoices() {
        choices.Items.Clear();choices.Items.AddRange(markers.Where(m=>m.Role==role.Text&&(map.SelectedRoad==0||m.Road==map.SelectedRoad)).Cast<object>().ToArray());
        roadLabel.Text=map.SelectedRoad==0?"All roads / "+choices.Items.Count+" markers":"Road "+map.SelectedRoad+" / "+choices.Items.Count+" "+role.Text+" markers";
    }
    void Preset() {
        start=markers.Single(m=>m.Key=="321312:0");finish=markers.Single(m=>m.Key=="744257:2");via.Clear();routeName.Text="Grand Ocean Sprint";
        updating=true;roadRules.SelectedItem=restrictions.Single(r=>r.Id==321312);updating=false;map.SelectedRoad=301028;role.SelectedIndex=0;RefreshChoices();Changed();
    }
    void Clear() {start=null;finish=null;via.Clear();routeName.Text="My Seacrest route";map.SelectedRoad=0;RefreshChoices();Changed();}
    void UseSelected() {var m=choices.SelectedItem as Marker;if(m!=null)SelectMarker(m);}
    void SelectMarker(Marker m) {
        if(m.Role=="start")start=m;else if(m.Role=="finish")finish=m;else if(!via.Contains(m)&&via.Count<4)via.Add(m);
        Changed();
    }
    void Remove() {var m=itinerary.SelectedItem as Marker;if(m==null)return;if(m==start)start=null;else if(m==finish)finish=null;else via.Remove(m);Changed();}
    void MoveVia(int delta) {var m=itinerary.SelectedItem as Marker;int i=via.IndexOf(m);if(i<0||i+delta<0||i+delta>=via.Count)return;via.RemoveAt(i);via.Insert(i+delta,m);Changed();itinerary.SelectedItem=m;}
    void Changed() {
        offlineBuild=null;map.Guide.Clear();map.Route.Clear();if(start!=null)map.Route.Add(start);map.Route.AddRange(via);if(finish!=null)map.Route.Add(finish);
        itinerary.Items.Clear();itinerary.Items.AddRange(map.Route.Cast<object>().ToArray());map.Invalidate();
        status.Text="Pick a start and finish; optional waypoint markers shape the course. Preview before export.";
        previewLabel.Text="Dashed amber path = undirected estimate. Direction, restrictions and junction gaps are not simulated.";
    }
    Dictionary<string,object> Recipe() {
        if(start==null||finish==null)throw new InvalidOperationException("Choose both a start and a finish.");
        var keys=new List<string>{start.Key};keys.AddRange(via.Select(m=>m.Key));keys.Add(finish.Key);
        var rules=roadRules.SelectedItem as Restriction;if(rules==null)throw new InvalidOperationException("Choose a stock restriction set.");
        return new Dictionary<string,object>{{"version",1},{"name",routeName.Text.Trim()},{"locations",keys},{"restriction_source",rules.Id}};
    }
    string WriteRecipe() {string path=Path.Combine(session,"recipe-"+(++serial)+".json");Json.Write(path,Recipe());return path;}
    static string Quote(string s) {
        var b=new StringBuilder("\"");int slashes=0;
        foreach(char c in s) {if(c=='\\'){slashes++;continue;}if(c=='\"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0;}
        b.Append('\\',slashes*2);b.Append('"');return b.ToString();
    }
    async Task<Dictionary<string,object>> Run(string action,string extra) {
        string output=Path.Combine(session,"result-"+(++serial)+".json");
        var info=new ProcessStartInfo(python,Quote(backend)+" "+action+" "+extra+" --output "+Quote(output)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=project};
        return await Task.Run(delegate {
            using(var p=Process.Start(info)) {
                var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
                if(!p.WaitForExit(180000))throw new InvalidOperationException("Build is taking longer than expected. Check the local build logs before trying again.");
                Task.WaitAll(stdout,stderr);if(p.ExitCode!=0)throw new InvalidOperationException(stderr.Result.Trim());
            }
            return Json.Read(output);
        });
    }
    void Busy(bool value) {busy=value;pages.Enabled=!value;saveLibrary.Enabled=!value&&pages.SelectedTab==editorPage;tools.Enabled=!value;map.Enabled=!value;choices.Enabled=!value;role.Enabled=!value;roadRules.Enabled=!value;routeName.Enabled=!value;itinerary.Enabled=!value;preview.Enabled=!value;buildOffline.Enabled=!value;buildOnline.Enabled=!value;UseWaitCursor=value;UpdateDeploymentButtons();}
    async Task Preview() {
        if(busy)return;
        try {string recipe=WriteRecipe();Busy(true);status.Text="Checking marker references and road connectivity…";var result=await Run("preview","--recipe "+Quote(recipe));
            map.Guide=Json.Arr(result["estimated_lines"]).Select(Lines).ToList();map.Invalidate();
            bool tested=Convert.ToBoolean(result["tested_preset"]);
            previewLabel.Text=(tested?"Tested preset: game displayed 6 km. ":"Experimental course. ")+"Graph samples: "+Json.Num(result["sample_length"]).ToString("N0")+" world units; not game distance.";
            status.Text=Json.Str(result["validation"])+". Amber guide ignores direction, restrictions and junction gaps.";
        }catch(Exception ex){status.Text=ex.Message;MessageBox.Show(this,ex.Message,"Preview",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{Busy(false);}
    }
    async Task Export(bool online) {
        if(busy)return;
        try {
            if(online&&offlineBuild==null)throw new InvalidOperationException("Export offline first, or use Saved races > Edit a copy on a course with a valid offline export.");
            if(online&&!File.Exists(Path.Combine(offlineBuild,"offline-success.json"))) {
                string observation=AskObservation();if(observation==null)return;
                string file=Path.Combine(session,"observation-"+(++serial)+".txt");File.WriteAllText(file,observation,new UTF8Encoding(false));Busy(true);
                await Run("record-offline","--offline-build "+Quote(offlineBuild)+" --observation-file "+Quote(file));Busy(false);
            }
            string recipe=WriteRecipe();Busy(true);status.Text="Building and re-extracting archive; checking all 423 events and 232 resources…";
            var result=await Run(online?"build-online":"build-offline","--recipe "+Quote(recipe)+(online?" --offline-build "+Quote(offlineBuild):""));
            latestBuild=Json.Str(result["build_dir"]);if(!online)offlineBuild=latestBuild;
            await ReloadLibrary();PopulateInstallChoices(latestBuild);
            status.Text=(online?"Online":"Offline")+" test export ready. Game files were not changed.";
            MessageBox.Show(this,"Verified export created:\n"+latestBuild+"\n\n"+Json.Str(result["state"])+"\n\nChoose Game installation to install this build when ready.","Export ready",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }catch(Exception ex){status.Text=ex.Message;MessageBox.Show(this,ex.Message,"Export",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{Busy(false);}
    }
    string AskObservation() {
        using(var dialog=new Form {Text="Record your offline test",Size=new Size(570,320),StartPosition=FormStartPosition.CenterParent,Font=Font}) {
            var label=new Label {Text="Only continue after you completed this exported route offline.\nDescribe the start, guidance, finish/results and any abnormalities.",Dock=DockStyle.Top,Height=60};
            var input=new TextBox {Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft};
            var ok=new Button {Text="Record success",AutoSize=true};var cancel=new Button {Text="Cancel",DialogResult=DialogResult.Cancel};
            ok.Click+=delegate{if(input.Text.Trim().Length>=10)dialog.DialogResult=DialogResult.OK;else MessageBox.Show(dialog,"Enter your actual completed-test observations.");};
            buttons.Controls.Add(ok);buttons.Controls.Add(cancel);dialog.Controls.Add(input);dialog.Controls.Add(label);dialog.Controls.Add(buttons);
            return dialog.ShowDialog(this)==DialogResult.OK?input.Text:null;
        }
    }
    async void SaveRecipe() {
        if(busy)return;
        try {string file=WriteRecipe();Busy(true);var saved=await Run("save-library","--recipe "+Quote(file));await ReloadLibrary(Json.Str(saved["recipe_hash"]));
            status.Text="Saved to library: "+Json.Str(saved["name"])+". Previous revisions kept.";pages.SelectedTab=libraryPage;
        }catch(Exception ex){status.Text="Cannot save course: "+ex.Message;MessageBox.Show(this,ex.Message,"Save to library");}finally{Busy(false);}
    }
    async void OpenRecipe() {
        if(busy)return;
        using(var d=new OpenFileDialog {Filter="Route recipe (*.json)|*.json",InitialDirectory=Path.Combine(project,"working")})if(d.ShowDialog(this)==DialogResult.OK) {
            try {Busy(true);var saved=await Run("save-library","--recipe "+Quote(d.FileName));await ReloadLibrary(Json.Str(saved["recipe_hash"]));
                pages.SelectedTab=libraryPage;status.Text="Recipe imported into the library. Choose Edit a copy to load it into the editor.";
            }catch(Exception ex){status.Text="Cannot import recipe: "+ex.Message;MessageBox.Show(this,ex.Message,"Import recipe");}finally{Busy(false);}
        }
    }
    [STAThread] public static int Main(string[] args) {
        if(args.Length>0)return RunDeploymentWorker(args);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try{Application.Run(new MakerForm());return 0;}catch(Exception ex){MessageBox.Show(ex.Message,"Route Maker could not start");return 1;}
    }
}
