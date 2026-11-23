using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

partial class MakerForm {
    readonly TabPage gamePage = new TabPage("Game installation");
    readonly TextBox gamePath = new TextBox(), savePath = new TextBox();
    readonly ComboBox installChoices = new ComboBox();
    readonly TextBox operationResult = new TextBox();
    readonly Label installedLabel = new Label();
    Button inspectGame, installRoute, restoreGame;
    RouteDeployment.Inspection installed;
    string lastDeploymentLog;
    readonly ComboBox stockTargets=new ComboBox(),eventKind=new ComboBox();
    static string ModeTitle(string mode){return mode=="offline"?"Offline Race":mode=="online-arms-race"?"Online Arms Race":"Online Race";}
    void SyncEventKind(){if(installMode.SelectedIndex!=1)eventKind.SelectedIndex=0;eventKind.Enabled=installMode.SelectedIndex==1;FillTargets();}
    readonly List<StockTarget> targets=new List<StockTarget>();
    protected virtual bool ConfirmRouteChange(string message,string caption) {
        return MessageBox.Show(this,message,caption,MessageBoxButtons.OKCancel)==DialogResult.OK;
    }
    sealed class StockTarget {
        public int Id;public string Name,Mode;
        public override string ToString(){return Name+" ("+ModeTitle(Mode)+") ["+Id+"]";}
    }
    void FillTargets() {
        var previous=stockTargets.SelectedItem as StockTarget;
        string mode=installMode.SelectedIndex==1?(eventKind.SelectedIndex==1?"online-arms-race":"online"):"offline";
        var available=targets.Where(t=>t.Mode==mode).ToArray();
        stockTargets.Items.Clear();stockTargets.Items.AddRange(available);
        stockTargets.SelectedItem=available.FirstOrDefault(t=>previous!=null&&t.Id==previous.Id)??
            available.FirstOrDefault(t=>t.Id==(mode=="offline"?321312:mode=="online-arms-race"?1104032:744283))??available.FirstOrDefault();
    }
    void SetPreparedChoice(Dictionary<string,object> result,bool navigate=true) {
        string folder=(string)result["build_dir"];
        latestBuild=folder;
        var choice=new InstallChoice{Name="Prepared replacements",Mode="offline / online",Folder=folder,Hash=(string)result["candidate_sha256"]};
        installChoices.Items.Clear();installChoices.Items.Add(choice);installChoices.SelectedItem=choice;
        if(navigate)pages.SelectedTab=gamePage;UpdateDeploymentButtons();
    }

    sealed class ReplacementRow {
        public Dictionary<string,object> Data;
        public override string ToString(){return (string)Json.Obj(Data["recipe"])["name"]+" => "+Data["stock_title"]+
            " ("+ModeTitle((string)Data["mode"])+") ["+Data["target_event_id"]+"]";}
    }
    readonly TabPage replacementsPage=new TabPage("Replaced routes");
    readonly ListBox replacementList=new ListBox();
    readonly MapCanvas replacementMap=new MapCanvas();
    readonly Label replacementInfo=new Label(),replacementDetails=new Label();
    Button restoreReplacement;
    string replacementObserved;

    void BuildReplacementsPage() {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(8)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));
        var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(0,0,10,0)};
        left.RowStyles.Add(new RowStyle(SizeType.Absolute,76));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        replacementInfo.Dock=DockStyle.Fill;replacementInfo.Text="Choose the game folder in Game installation. This list shows its installed replacements.";
        replacementList.Name="ReplacementList";replacementList.Dock=DockStyle.Fill;replacementList.HorizontalScrollbar=true;replacementList.IntegralHeight=false;
        replacementList.SelectedIndexChanged+=delegate{ShowReplacementPreview();UpdateDeploymentButtons();};
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};
        actions.Controls.Add(Button("Refresh",async delegate{await RefreshReplacements();}));
        restoreReplacement=Button("Restore selected to stock",async delegate{await RestoreReplacement();});restoreReplacement.Name="RestoreReplacement";actions.Controls.Add(restoreReplacement);
        left.Controls.Add(replacementInfo,0,0);left.Controls.Add(replacementList,0,1);left.Controls.Add(actions,0,2);
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,116));
        replacementMap.Dock=DockStyle.Fill;replacementDetails.Dock=DockStyle.Fill;replacementDetails.Padding=new Padding(0,8,0,0);
        right.Controls.Add(replacementMap,0,0);right.Controls.Add(replacementDetails,0,1);
        layout.Controls.Add(left,0,0);layout.Controls.Add(right,1,0);replacementsPage.Controls.Add(layout);pages.TabPages.Add(replacementsPage);
    }
    void ShowReplacementPreview() {
        replacementMap.Route.Clear();replacementMap.Guide.Clear();
        var row=replacementList.SelectedItem as ReplacementRow;
        if(row==null){replacementDetails.Text="Select an installed replacement to preview its custom course.";replacementMap.Invalidate();return;}
        var recipe=Json.Obj(row.Data["recipe"]);var preview=Json.Obj(row.Data["preview"]);
        replacementMap.Route=Json.Arr(recipe["locations"]).Select(k=>markers.Single(m=>m.Key==Json.Str(k))).ToList();
        replacementMap.Guide=Json.Arr(preview["estimated_lines"]).Select(Lines).ToList();replacementMap.FitMap();
        replacementDetails.Text=row+"\n"+replacementMap.Route.Count+" markers   |   Estimated length: "+Json.Num(preview["sample_length"]).ToString("N0")+" world units\n"+
            "Amber preview estimates the custom course; it ignores direction, restrictions and junction gaps.\nRestoring this route keeps your other replacements and current saves.";
    }
    async Task RefreshReplacements() {
        if(busy)return;
        var selected=replacementList.SelectedItem as ReplacementRow;
        int selectedId=selected==null?0:Convert.ToInt32(selected.Data["target_event_id"]);
        try {
            Busy(true);status.Text="Reading installed replacements…";
            string game=gamePath.Text;var service=new RouteDeployment(project);
            var current=await Task.Run(()=>service.Inspect(game));
            installed=current;installedLabel.Text=current.ToString();
            var result=await Run("installed-replacements","--installed-hash "+current.Hash);
            replacementList.Items.Clear();foreach(var v in Json.Arr(result["replacements"]))replacementList.Items.Add(new ReplacementRow{Data=Json.Obj(v)});
            replacementObserved=current.Hash;
            replacementList.SelectedItem=replacementList.Items.Cast<ReplacementRow>().FirstOrDefault(r=>Convert.ToInt32(r.Data["target_event_id"])==selectedId)??replacementList.Items.Cast<ReplacementRow>().FirstOrDefault();
            replacementInfo.Text=(replacementList.Items.Count==0?"All supported routes are stock.":replacementList.Items.Count+" installed replacement(s). Select a custom course to see its route.")+"\nGame folder: "+game;
            ShowReplacementPreview();status.Text="Installed replacements refreshed.";
        } catch(Exception ex){replacementObserved=null;replacementList.Items.Clear();ShowReplacementPreview();replacementInfo.Text=ex.Message;status.Text="Could not read replacements.";}
        finally{Busy(false);}
    }
    async Task RestoreReplacement() {
        var row=replacementList.SelectedItem as ReplacementRow;if(busy||row==null)return;
        if(!ConfirmRouteChange("Restore only "+row+" to stock?","Restore route"))return;
        try {
            Busy(true);
            var current=new RouteDeployment(project).Inspect(gamePath.Text);
            if(current.Hash!=replacementObserved || current.GameRunning)throw new InvalidOperationException("Installation changed or game started. Close the game and refresh.");
            installed=current;
            var result=await Run("prepare-replacements","--installed-hash "+replacementObserved+" --remove-target "+row.Data["target_event_id"]);
            bool stock=(string)result["candidate_sha256"]==RouteDeployment.Stock;
            if(!stock)SetPreparedChoice(result,false);
            Busy(false);await ChangeInstallation(!stock);
            if(installed==null || installed.Hash!=(string)result["candidate_sha256"])throw new InvalidOperationException(operationResult.Text);
        } catch(Exception ex){MessageBox.Show(this,ex.Message,"Restore stopped");}
        finally{Busy(false);}
        await RefreshReplacements();
    }

    sealed class InstallChoice {
        public string Name, Mode, Folder, Hash;
        public override string ToString() { return Name+"  /  "+Mode+"  /  "+Hash.Substring(0,8); }
    }

    void BuildGamePage() {
        var panel = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=3, RowCount=9, Padding=new Padding(14) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,138));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,98));
        foreach (int height in new[]{36,36,36,66,38,48,42})
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        var heading=Label("Choose a build to install, or restore the original game from any supported route.");
        panel.Controls.Add(heading,0,0);panel.SetColumnSpan(heading,3);
        gamePath.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steamapps","common","Need For Speed Hot Pursuit Remastered");
        savePath.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Criterion Games","Need for Speed(TM) Hot Pursuit Remastered");
        string prefs=Path.Combine(project,"working","route-deployment","folders.json");
        if(File.Exists(prefs))try {
            var data=Json.Read(prefs);
            gamePath.Text=(string)data["game"]; savePath.Text=(string)data["saves"];
        } catch(Exception ex) {operationResult.Text="Could not read saved folder choices. "+ex.Message;}
        AddFolderRow(panel,1,"Game folder",gamePath);
        AddFolderRow(panel,2,"Current save folder",savePath);
        panel.Controls.Add(Label("Installed archive"),0,3);
        installedLabel.Dock=DockStyle.Fill;installedLabel.Text="Inspect to identify the installed route.";
        panel.Controls.Add(installedLabel,1,3);panel.SetColumnSpan(installedLabel,2);
        installChoices.DropDownStyle=ComboBoxStyle.DropDownList;installChoices.Dock=DockStyle.Fill;
        panel.Controls.Add(Label("Build to install"),0,4);panel.Controls.Add(installChoices,1,4);panel.SetColumnSpan(installChoices,2);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};
        inspectGame=Button("Inspect",async delegate{await InspectInstallation();});
        installRoute=Button("Install selected build",async delegate{await ChangeInstallation(true);});
        restoreGame=Button("Restore all to stock",async delegate{await ChangeInstallation(false);});
        actions.Controls.Add(inspectGame);actions.Controls.Add(installRoute);actions.Controls.Add(restoreGame);
        panel.Controls.Add(actions,1,5);panel.SetColumnSpan(actions,2);
        var help=Label("Install backs up current saves and retains the previous archive. Restore keeps your current progress.");
        panel.Controls.Add(help,0,6);panel.SetColumnSpan(help,3);
        operationResult.Multiline=true;operationResult.ReadOnly=true;operationResult.ScrollBars=ScrollBars.Vertical;operationResult.Dock=DockStyle.Fill;
        panel.Controls.Add(operationResult,0,7);panel.SetColumnSpan(operationResult,3);
        var logButton=Button("View last operation log",delegate{
            if(lastDeploymentLog!=null && File.Exists(lastDeploymentLog)) operationResult.Text=File.ReadAllText(lastDeploymentLog);
        });
        panel.Controls.Add(logButton,1,8);
        gamePage.Controls.Add(panel);pages.TabPages.Add(gamePage);
        gamePath.TextChanged+=delegate{replacementObserved=null;replacementList.Items.Clear();ShowReplacementPreview();installed=null;installedLabel.Text="Inspect this game folder before making changes.";UpdateDeploymentButtons();};
        installChoices.SelectedIndexChanged+=delegate{UpdateDeploymentButtons();};
        UpdateDeploymentButtons();
    }

    void AddFolderRow(TableLayoutPanel panel,int row,string title,TextBox field) {
        field.Dock=DockStyle.Fill;panel.Controls.Add(Label(title),0,row);panel.Controls.Add(field,1,row);
        panel.Controls.Add(Button("Browse…",delegate{
            using(var dialog=new FolderBrowserDialog{Description=title,ShowNewFolderButton=false}) {
                if(Directory.Exists(field.Text))dialog.SelectedPath=field.Text;
                if(dialog.ShowDialog(this)==DialogResult.OK)field.Text=dialog.SelectedPath;
            }
        }),2,row);
    }

    void PopulateInstallChoices(string preferred=null) {
        var current=installChoices.SelectedItem as InstallChoice;
        var choices=new List<InstallChoice>();
        foreach(var race in savedRaces)foreach(var value in Json.Arr(race.Data["variants"])) {
            var v=Json.Obj(value);
            if(!Convert.ToBoolean(v["valid"]))continue;
            choices.Add(new InstallChoice{Name=(string)race.Data["name"],Mode=(string)v["mode"],Folder=(string)v["path"],Hash=(string)v["candidate_sha256"]});
        }
        foreach(var item in new[]{
            new InstallChoice{Name="Seacrest Tour",Mode="offline",Folder=Path.Combine(project,"experiments","010-offline-owned-route"),Hash="e2dc242d6071360ff75675f4adb9203051625634c9f3f88945fdee2456863b75"},
            new InstallChoice{Name="Seacrest Tour",Mode="online",Folder=Path.Combine(project,"experiments","012-multiplayer-owned-route"),Hash="42d5c88f1893f048ccf75bba803f1adc5286e301d54978b742a03b8bb44f2c71"}})
            if(File.Exists(Path.Combine(item.Folder,"candidate","GAMELOGIC","CHALLENGEINSTANCES.BNDL")))choices.Add(item);
        string selected=preferred??(current==null?null:current.Folder);
        // Two exports with identical bytes are the same installation choice.
        choices=choices.GroupBy(c=>c.Hash).Select(g=>g.FirstOrDefault(c=>c.Folder==selected)??g.First()).ToList();
        installChoices.Items.Clear();installChoices.Items.AddRange(choices.Cast<object>().ToArray());
        installChoices.SelectedItem=choices.FirstOrDefault(c=>c.Folder==selected)??(preferred==null?choices.FirstOrDefault():null);
        UpdateDeploymentButtons();
    }

    void OpenGamePage(string build) {
        PopulateInstallChoices(build);
        if(build==null)installChoices.SelectedIndex=-1;
        pages.SelectedTab=gamePage;
    }

    void UpdateDeploymentButtons() {
        bool ready=!busy && installed!=null && installed.Known && !installed.GameRunning;
        var selected=installChoices.SelectedItem as InstallChoice;
        installRoute.Enabled=ready && selected!=null && installed.Hash!=selected.Hash;
        restoreGame.Enabled=ready && installed.Hash!=RouteDeployment.Stock;
        if(restoreReplacement!=null)restoreReplacement.Enabled=ready && replacementList.SelectedItem!=null && replacementObserved==installed.Hash;
    }

    async Task InspectInstallation() {
        if(busy)return;
        string folder=gamePath.Text;
        try {
            Busy(true);status.Text="Inspecting the game archive…";
            var service=new RouteDeployment(project);
            installed=await Task.Run(()=>service.Inspect(folder));
            installedLabel.Text=installed.ToString();
            status.Text="Installation inspected.";
        } catch(Exception ex) {
            installed=null;installedLabel.Text=ex.Message;status.Text="Inspection stopped.";
        } finally {Busy(false);}
    }

    async Task ChangeInstallation(bool install) {
        if(busy || installed==null)return;
        var choice=installChoices.SelectedItem as InstallChoice;
        if(install && choice==null)return;
        var service=new RouteDeployment(project);
        string action=install?"install":"restore", id=Guid.NewGuid().ToString("N");
        string folder=gamePath.Text,saves=savePath.Text,source=install?choice.Folder:"-";
        string wanted=install?choice.Hash:RouteDeployment.Stock, before=installed.Hash;
        lastDeploymentLog=service.LogPath(id);
        bool completed=false;
        try {
            Busy(true);
            if(install) {
                var manifest=Json.Read(Path.Combine(source,"manifest.json"));
                if(!manifest.ContainsKey("version") || Convert.ToInt32(manifest["version"])!=2) {
                    var old=await Run("installed-replacements","--installed-hash "+wanted);
                    var rows=Json.Arr(old["replacements"]).ToArray();if(rows.Length!=1)throw new InvalidOperationException("Choose a single saved course to install.");
                    var row=Json.Obj(rows[0]);string recipeFile=Path.Combine(session,"install-"+(++serial)+".json");Json.Write(recipeFile,row["recipe"]);
                    var combined=await Run("prepare-replacements","--installed-hash "+before+" --target-id "+row["target_event_id"]+" --recipe "+Quote(recipeFile));
                    source=(string)combined["build_dir"];wanted=(string)combined["candidate_sha256"];
                }
            }
            status.Text=install?"Checking build and backing up current saves…":"Verifying the original archive…";
            operationResult.Text=status.Text;
            bool elevate=false;
            try { operationResult.Text=await Task.Run(()=>service.Execute(action,folder,saves,source,wanted,before,id)); }
            catch(UnauthorizedAccessException) {
                using(var user=WindowsIdentity.GetCurrent())
                    if(new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator))throw;
                elevate=true;
            }
            if(elevate) {
                status.Text="Windows permission is required to finish this operation.";
                var args=new[]{"--route-action",action,folder,saves,source,wanted,before,id};
                var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,String.Join(" ",args.Select(Quote))){
                    UseShellExecute=true,Verb="runas"
                };
                int code=await Task.Run(delegate{
                    using(var child=Process.Start(info)){child.WaitForExit();return child.ExitCode;}
                });
                string log=File.Exists(lastDeploymentLog)?File.ReadAllText(lastDeploymentLog):"No operation log available.";
                if(code!=0)throw new InvalidOperationException(log);
                operationResult.Text=log;
            }
            completed=true;
            status.Text=install?"Selected build installed and verified.":"Original game restored and verified.";
            try {Json.Write(Path.Combine(project,"working","route-deployment","folders.json"),new {game=folder,saves=saves});}
            catch(IOException ex){operationResult.AppendText(Environment.NewLine+"Folder preferences: "+ex.Message);}
        } catch(Exception ex) {
            operationResult.Text=ex.Message;status.Text="Operation stopped. See the result below.";
        }
        try {
                installed=await Task.Run(()=>service.Inspect(folder));
                installedLabel.Text=installed.ToString();
                if(completed && installed.Hash!=wanted)status.Text="The installed archive has changed since verification. Inspect it again.";
        } catch(Exception ex){installed=null;installedLabel.Text=ex.Message;}
        finally {Busy(false);}
    }

    static int RunDeploymentWorker(string[] args) {
        if(args.Length!=8 || args[0]!="--route-action")return 2;
        RouteDeployment service=null;
        try {
            var config=Json.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"config.json"));
            service=new RouteDeployment((string)config["project"]);
            service.Execute(args[1],args[2],args[3],args[4],args[5],args[6],args[7]);
            return 0;
        } catch(Exception ex) {
            // Some failures happen before the operation lock/log is opened.
            if(service!=null)try {
                string log=service.LogPath(args[7]);
                Directory.CreateDirectory(Path.GetDirectoryName(log));
                File.AppendAllText(log,Environment.NewLine+ex.Message+Environment.NewLine);
            } catch(IOException){} catch(UnauthorizedAccessException){} catch(FormatException){}
            return 1;
        }
    }
}
