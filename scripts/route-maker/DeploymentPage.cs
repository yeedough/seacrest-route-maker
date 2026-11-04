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
        restoreGame=Button("Restore original",async delegate{await ChangeInstallation(false);});
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
        gamePath.TextChanged+=delegate{installed=null;installedLabel.Text="Inspect this game folder before making changes.";UpdateDeploymentButtons();};
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
            Busy(true);status.Text=install?"Checking build and backing up current saves…":"Verifying the original archive…";
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
