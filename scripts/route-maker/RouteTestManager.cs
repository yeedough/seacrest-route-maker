using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class RouteTestManager : ArchiveSafety {
    const string Candidate="__CANDIDATE_HASH__";
    const string Mode="__ROUTE_MODE__";
    static readonly string Base=AppDomain.CurrentDomain.BaseDirectory;
    static readonly string State=Path.Combine(Base,"state");
    static string Inspect(string folder){
        string hash=HashFile(Archive(folder));
        Require(hash==Original||hash==Candidate,"Unknown archive: a different route/mod/version is installed. Nothing was replaced. Restore that route with its own tool first.");
        Console.WriteLine("State: "+(hash==Original?"original":"this route installed"));
        Console.WriteLine("SHA256: "+hash);Console.WriteLine("Game running: "+Running());return hash;
    }
    static byte[] Reconstruct(byte[] original){
        Require(Hash(original)==Original,"Original archive hash mismatch.");
        Dictionary<string,object> patch;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("route.patch.json"))using(var reader=new StreamReader(stream))patch=Json().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
        return ReconstructPatch(original,patch,Candidate);
    }
    static void Apply(string folder,bool install,string saveRoot){
        Closed();string target=Archive(folder);NoLink(folder);NoLink(Path.GetDirectoryName(target));NoLink(target);
        Require(new FileInfo(target).Length<=1048576,"Unsupported archive size.");
        string before=Inspect(folder),wanted=install?Candidate:Original;
        if(install)VerifySupport(folder);
        if(before==wanted){Console.WriteLine(install?"This route is already installed. No changes.":"Already original. No changes.");return;}
        Directory.CreateDirectory(State);NoLink(State);
        string originalBackup=Path.Combine(State,"original-CHALLENGEINSTANCES.BNDL");
        byte[] output;string saveBackup=null;SortedDictionary<string,string> saveHashes=null;
        if(install){
            output=Reconstruct(File.ReadAllBytes(target));
            if(File.Exists(originalBackup)){NoLink(originalBackup);Require(HashFile(originalBackup)==Original,"Existing original backup is unexpected. Keep it safe; installation stopped.");}
            else File.Copy(target,originalBackup,false);
            Require(HashFile(originalBackup)==Original,"Original backup verification failed.");
            string built=Path.Combine(State,"candidate-CHALLENGEINSTANCES.BNDL");
            if(File.Exists(built)){NoLink(built);Require(HashFile(built)==Candidate,"Existing candidate copy is unexpected; installation stopped.");}
            else File.WriteAllBytes(built,output);
            saveRoot=Path.GetFullPath(saveRoot.Trim().Trim('"','\'')).TrimEnd(Path.DirectorySeparatorChar);
            saveBackup=BackupSaves(State,saveRoot,out saveHashes);
        }else{
            Require(File.Exists(originalBackup),"Original backup is missing. Keep this tool's state folder with the package.");
            NoLink(originalBackup);Require(HashFile(originalBackup)==Original,"Original backup hash mismatch; restoration stopped.");output=File.ReadAllBytes(originalBackup);
        }
        string atomic=ReplaceArchive(State,target,install?"Install":"Restore",before,wanted,output,saveRoot,saveHashes,saveBackup);
        Console.WriteLine(install?"Installed and verified. Select ROADSTERS REBORN in "+(Mode=="offline"?"career, with internet disconnected.":"a private online Race lobby."):"Original archive restored and verified. Current saves retained.");
        Console.WriteLine("SHA256: "+wanted);Console.WriteLine("Archive backup: "+atomic);
    }
    static string Quote(string s){var b=new StringBuilder("\"");int n=0;foreach(char c in s){if(c=='\\'){n++;continue;}b.Append('\\',c=='"'?n*2+1:n);b.Append(c);n=0;}b.Append('\\',n*2);return b.Append('"').ToString();}
    static bool Admin(){using(var user=WindowsIdentity.GetCurrent())return new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator);}
    static string DefaultSave(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Criterion Games","Need for Speed(TM) Hot Pursuit Remastered");}
    static string SelectGame(){
        string guess=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steamapps","common","Need For Speed Hot Pursuit Remastered");
        Console.WriteLine("Paste the game folder or NFS11Remastered.exe path.");
        bool found=File.Exists(Archive(guess));if(found)Console.WriteLine("Enter uses: "+guess);
        string answer=Console.ReadLine();if(answer==null)throw new OperationCanceledException();
        return NormalizeGame(String.IsNullOrWhiteSpace(answer)&&found?guess:answer);
    }
    static void Pause(){Console.WriteLine("\nPress Enter to close.");Console.ReadLine();}
    sealed class Tee : TextWriter {
        readonly TextWriter console,log;public Tee(TextWriter console,TextWriter log){this.console=console;this.log=log;}
        public override Encoding Encoding{get{return Encoding.UTF8;}}
        public override void Write(char value){console.Write(value);log.Write(value);log.Flush();}
        public override void Write(string value){console.Write(value);log.Write(value);log.Flush();}
        public override void WriteLine(string value){console.WriteLine(value);log.WriteLine(value);log.Flush();}
    }
    public static int Main(string[] args){
        Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
        bool noPause=false;string action=null,folder=null,saves=null;string logPath=null;StreamWriter log=null;TextWriter originalOut=Console.Out;
        int exit=0;
        try{
            foreach(string arg in args)if(arg=="--no-pause")noPause=true;
            Directory.CreateDirectory(Path.Combine(Base,"logs"));logPath=Path.Combine(Base,"logs","route-"+Stamp()+".log");log=new StreamWriter(logPath,false,new UTF8Encoding(false));Console.SetOut(new Tee(originalOut,log));
            Console.Title="Seacrest Route Test";Console.WriteLine("SEACREST ROUTE TEST — "+Mode.ToUpperInvariant());Console.WriteLine("Candidate: "+Candidate);Console.WriteLine("Log: "+logPath);
            for(int i=0;i<args.Length;i++){
                if(args[i]=="--no-pause")continue;
                Require(i+1<args.Length,"Missing option value.");string value=args[++i];
                switch(args[i-1]){case "--action":action=value;break;case "--game":folder=value;break;case "--saves":saves=value;break;default:throw new ArgumentException("Unknown option.");}
            }
            if(action==null){
                Require(!noPause,"Supply an explicit action for unattended use.");
                Console.WriteLine("\n1) Inspect installed state\n2) Install this route\n3) Restore original game archive\nQ) Exit\n");
                string choice=Console.ReadLine();if(choice==null||choice.Equals("Q",StringComparison.OrdinalIgnoreCase))return 0;
                action=choice=="1"?"inspect":choice=="2"?"install":choice=="3"?"restore":null;Require(action!=null,"Choose 1, 2 or 3.");
            }
            Require(action=="inspect"||action=="install"||action=="restore","Action must be inspect, install or restore.");
            folder=folder==null?SelectGame():NormalizeGame(folder);
            saves=String.IsNullOrWhiteSpace(saves)?DefaultSave():saves;
            Console.WriteLine("\nGame folder: "+folder);
            if(action=="install"&&!noPause&&args.Length==0){Console.WriteLine("Save folder: "+saves);Console.WriteLine("Press Enter to use it, or paste the actual save folder:");string answer=Console.ReadLine();if(!String.IsNullOrWhiteSpace(answer))saves=answer.Trim().Trim('"','\'');}
            try{if(action=="inspect")Inspect(folder);else Apply(folder,action=="install",saves);}
            catch(UnauthorizedAccessException){
                if(Admin()||noPause)throw;
                Console.WriteLine("Windows permission is needed. The normal administrator prompt will keep your selected folder and action.");
                string arguments="--action "+action+" --game "+Quote(folder)+" --saves "+Quote(saves);
                var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,arguments){UseShellExecute=true,Verb="runas"};
                using(var child=Process.Start(info)){child.WaitForExit();exit=child.ExitCode;}
            }
        }catch(OperationCanceledException){exit=1;Console.WriteLine("Cancelled. No automatic retry.");}
        catch(Exception ex){exit=1;Console.WriteLine("\nERROR: "+ex.Message);Console.WriteLine("The error remains in this window and log. No security or execution-policy settings were changed.");}
        finally{
            if(logPath!=null)Console.WriteLine("\nSaved log: "+logPath);
            if(!noPause)Pause();Console.SetOut(originalOut);if(log!=null)log.Dispose();
        }
        return exit;
    }
}
