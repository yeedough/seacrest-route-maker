using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal abstract class ArchiveSafety {
    protected const string Original="db8e2d3b54dfcfce0cc2b68cc386a5a72ea310e5aac3e957ea5f9592bb247052";
    protected static readonly string[] Support={"NFS11Remastered.exe","GAMELOGIC\\CHALLENGEDEFINITIONS.BNDL","GAMELOGIC\\GAMEPLAY.BNDL","TRIGGERS.BNDL"};
    protected static readonly string[] SupportHashes={"fab844539ad04f18b8c920615e27812fb22612d42c537417964a502d9862a017","e2c26f6b788637c416fb1c5d89460a98b10252d8da27bc1df691bca79e9b3031","9275b4b3500e5cae851c2ccc2ee5a76e45356f349490aef5bef58650936c6896","c4ebcf9c456753959bc30e19b5395453128b5b5259f0773ca5c84def1ba11ba9"};
    protected static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=4194304};}
    protected static string Stamp(){return DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);}
    protected static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    protected static string HashFile(string path){using(var f=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    protected static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    protected static bool Running(){var ps=Process.GetProcessesByName("NFS11Remastered");bool found=ps.Length>0;foreach(var p in ps)p.Dispose();return found;}
    protected static void Closed(){Require(!Running(),"Close Hot Pursuit Remastered normally before changing its archive.");}
    protected static void NoLink(string path){Require((File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"Linked files/folders are not supported: "+path);}
    protected static string Archive(string folder){return Path.Combine(folder,"GAMELOGIC","CHALLENGEINSTANCES.BNDL");}
    protected static string NormalizeGame(string input){
        Require(!String.IsNullOrWhiteSpace(input),"The game folder is blank.");
        string path=input.Trim().Trim('"','\'');
        if(File.Exists(path)){
            Require(String.Equals(Path.GetFileName(path),"NFS11Remastered.exe",StringComparison.OrdinalIgnoreCase),"Choose the game folder or NFS11Remastered.exe.");
            path=Path.GetDirectoryName(path);
        }
        path=Path.GetFullPath(path);
        Require(Directory.Exists(path)&&File.Exists(Path.Combine(path,"NFS11Remastered.exe"))&&File.Exists(Archive(path)),"Game folder not found or missing NFS11Remastered.exe: "+path);
        return path;
    }
    protected static SortedDictionary<string,string> SaveHashes(string root){
        Require(Directory.Exists(root),"Save folder not found. Supply the active save folder instead of continuing: "+root);
        NoLink(root);var files=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);var pending=new Stack<string>();pending.Push(root);
        while(pending.Count>0){string dir=pending.Pop();foreach(string path in Directory.GetFileSystemEntries(dir)){NoLink(path);if(Directory.Exists(path))pending.Push(path);else files.Add(path.Substring(root.Length+1),HashFile(path));}}
        Require(files.Count>0,"No current save files were found; installation stopped.");return files;
    }
    protected static void CheckSaveHashes(string root,SortedDictionary<string,string> expected){
        var after=SaveHashes(root);Require(after.Count==expected.Count&&expected.All(x=>after.ContainsKey(x.Key)&&after[x.Key]==x.Value),"Live save inventory or hashes changed during preparation.");
    }
    protected static string BackupSaves(string state,string root,out SortedDictionary<string,string> hashes){
        hashes=SaveHashes(root);string backup=Path.Combine(state,"saves-"+Stamp());Directory.CreateDirectory(backup);
        foreach(var item in hashes){string dest=Path.Combine(backup,item.Key);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(Path.Combine(root,item.Key),dest,false);Require(HashFile(dest)==item.Value,"Save backup verification failed.");}
        CheckSaveHashes(root,hashes);Closed();
        var entries=hashes.Select(x=>new Dictionary<string,object>{{"RelativePath",x.Key},{"SHA256",x.Value}}).ToArray();
        File.WriteAllText(Path.Combine(backup,hashes.ContainsKey("backup-manifest.json")?"backup-manifest-"+Stamp()+".json":"backup-manifest.json"),Json().Serialize(new {SourceRoot=root,Files=entries,TimeUTC=DateTime.UtcNow.ToString("o")}),new UTF8Encoding(false));
        Console.WriteLine("Verified save backup: "+backup+" ("+hashes.Count+" files)");return backup;
    }
    protected static void VerifySupport(string game) {
        for (int i = 0; i < Support.Length; i++) {
            string file = Path.Combine(game, Support[i]);
            NoLink(file);
            Require(HashFile(file) == SupportHashes[i], "Unsupported/changed baseline file: " + Support[i]);
        }
    }

    protected static string ReplaceArchive(string state, string target, string action, string before,
            string wanted, byte[] output, string saveRoot, SortedDictionary<string,string> saveHashes, string saveBackup) {
        string stem = target + ".route-maker-" + Stamp();
        string staged = stem + ".tmp", retained = stem + ".bak";
        var intent = new {TimeUTC=DateTime.UtcNow.ToString("o"), Action=action, BeforeHash=before,
            AfterHash=wanted, Target=target, Staging=staged, RetainedBackup=retained, SaveBackup=saveBackup};
        File.AppendAllText(Path.Combine(state,"replacement-intents.jsonl"), Json().Serialize(intent) + Environment.NewLine);
        bool created = false;
        try {
            using (var stream = new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
                created = true;
                stream.Write(output,0,output.Length);
                stream.Flush(true);
            }
            Require(HashFile(staged)==wanted,"Staged archive hash mismatch.");
            if (saveHashes != null) {
                VerifySupport(Path.GetDirectoryName(Path.GetDirectoryName(target)));
                CheckSaveHashes(saveRoot,saveHashes);
            }
            NoLink(Path.GetDirectoryName(target));
            NoLink(target);
            Require(HashFile(target)==before,"Installed archive changed during preparation.");
            Closed();
            File.Replace(staged,target,retained);
            Require(HashFile(target)==wanted && HashFile(retained)==before,
                "Replacement verification failed. Keep the game closed and inspect the retained archive: " + retained);
        } finally {
            if (created && File.Exists(staged)) File.Delete(staged);
        }
        var history = new {TimeUTC=DateTime.UtcNow.ToString("o"), Action=action, SHA256=wanted,
            SaveFilesModified=false, SaveBackup=saveBackup, AtomicBackup=retained};
        File.AppendAllText(Path.Combine(state,"deployment-history.jsonl"), Json().Serialize(history) + Environment.NewLine);
        return retained;
    }
    protected static byte[] ReconstructPatch(byte[] original,Dictionary<string,object> patch,string expected){
        Require((string)patch["original_sha256"]==Original&&(string)patch["candidate_sha256"]==expected,"Embedded patch identity mismatch.");
        int length=Convert.ToInt32(patch["candidate_length"]);Require(length>0&&length<=1048576,"Invalid patch size.");
        using(var result=new MemoryStream()){
            foreach(var item in (IEnumerable)patch["operations"]){
                var op=(Dictionary<string,object>)item;
                if(op.ContainsKey("data")){byte[] bytes=Convert.FromBase64String((string)op["data"]);result.Write(bytes,0,bytes.Length);}
                else {int at=Convert.ToInt32(op["offset"]),count=Convert.ToInt32(op["length"]);Require(at>=0&&count>=0&&(long)at+count<=original.Length,"Invalid patch bounds.");result.Write(original,at,count);}
                Require(result.Length<=length,"Patch exceeded output length.");
            }
            byte[] data=result.ToArray();Require(data.Length==length&&Hash(data)==expected,"Candidate reconstruction mismatch.");return data;
        }
    }
}
