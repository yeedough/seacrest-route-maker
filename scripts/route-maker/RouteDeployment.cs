using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

internal sealed class RouteDeployment : ArchiveSafety {
    internal const string Stock = Original;
    const string RelativeArchive = @"GAMELOGIC\CHALLENGEINSTANCES.BNDL";
    readonly string project;
    readonly string state;

    // Older project packages have fixed, independently recorded identities.
    static readonly Dictionary<string,string> Legacy = new Dictionary<string,string> {
        {"dc7574026f102e85b2056e80b95a5e0dc15c95f0a1c1659eac6ec02bfab9dc61","006 name probe"},
        {"6400b1703b21b06740fe057cf3c0759ade6acfa4871056aba0f7208ef211356f","008 route probe"},
        {"3a28e872dfa83b83f2d48c83017426b084bfd64f2e74aeb870d6b2ee28b8d28f","009 route values"},
        {"e2dc242d6071360ff75675f4adb9203051625634c9f3f88945fdee2456863b75","Seacrest Tour (offline)"},
        {"42d5c88f1893f048ccf75bba803f1adc5286e301d54978b742a03b8bb44f2c71","Seacrest Tour (online)"},
        {"1ad2d49462c9a1e3d52d32cd410886a5f16a3cb99be3e6a6d6a8f8d2ab8f233f","Grand Ocean Sprint (offline)"},
        {"0d5c4cda823bce57874e615f21c13a1f60741a0006c2a0f49ac70eac1878ef3b","Grand Ocean Sprint (online)"}
    };

    internal sealed class Inspection {
        public string Hash, Name;
        public bool Known, GameRunning;
        public override string ToString() {
            return Name + Environment.NewLine + Hash + Environment.NewLine +
                (GameRunning ? "Game running" : "Game closed");
        }
    }

    internal RouteDeployment(string projectRoot) {
        project = Path.GetFullPath(projectRoot);
        state = Path.Combine(project, "working", "route-deployment");
    }

    internal string LogPath(string id) {
        return Path.Combine(state, "logs", Guid.ParseExact(id, "N").ToString("N") + ".txt");
    }

    static void CheckPath(string path) {
        for (string p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p)) {
            if (File.Exists(p) || Directory.Exists(p)) NoLink(p);
        }
    }

    static bool IsUnder(string child, string parent) {
        string prefix = Path.GetFullPath(parent).TrimEnd('\\') + "\\";
        return Path.GetFullPath(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    static byte[] ReadArchive(string path, string expected) {
        CheckPath(path);
        Require(File.Exists(path) && new FileInfo(path).Length <= 1048576, "Archive missing or unsupported: " + path);
        byte[] bytes = File.ReadAllBytes(path);
        Require(Hash(bytes) == expected, "Archive hash mismatch: " + path);
        return bytes;
    }

    static Dictionary<string,object> ReadJson(string path, long maxSize) {
        CheckPath(path);
        Require(new FileInfo(path).Length <= maxSize, "Unexpected file size: " + path);
        var record = Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
        Require(record != null, "Empty metadata file: " + path);
        return record;
    }

    string Builds { get { return Path.Combine(project, "working", "route-maker-builds"); } }
    string[] ExportFolders() {
        CheckPath(Builds);
        return Directory.Exists(Builds) ? Directory.GetDirectories(Builds) : new string[0];
    }

    byte[] OriginalBytes(string game) {
        string cached = Path.Combine(state, "original.bndl");
        if (File.Exists(cached)) return ReadArchive(cached, Original);
        var locations = new List<string> {
            Path.Combine(project, "working", "initial", RelativeArchive),
            Path.Combine(project, "baseline", "2026-10-26-initial", RelativeArchive),
            Archive(game)
        };
        locations.AddRange(ExportFolders().Select(d=>Path.Combine(d,"package","state","original-CHALLENGEINSTANCES.BNDL")));
        foreach (string path in locations.Where(File.Exists)) {
            CheckPath(path);
            if (new FileInfo(path).Length <= 1048576 && HashFile(path) == Original)
                return ReadArchive(path, Original);
        }
        throw new InvalidOperationException("No hash-verified original archive found. Keep the game closed and recover the project's original backup.");
    }

    // Publish each cache file only after it is complete. An existing unexpected copy is retained.
    static void SaveVerified(string path, byte[] bytes, string expected) {
        CheckPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (!File.Exists(path)) {
            string temp = path + "." + Stamp() + ".tmp";
            File.WriteAllBytes(temp, bytes);
            try {
                ReadArchive(temp, expected);
                try { File.Move(temp, path); }
                catch (IOException) { if (!File.Exists(path)) throw; }
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        ReadArchive(path, expected);
    }

    static string RecipeIdentity(Dictionary<string,object> recipe) {
        var keys = ((System.Collections.IEnumerable)recipe["locations"]).Cast<object>().Select(Convert.ToString).ToArray();
        Require(Convert.ToInt32(recipe["version"]) == 1 && keys.Length >= 2 && keys.Length <= 6 &&
            keys.All(k=>System.Text.RegularExpressions.Regex.IsMatch(k, @"^\d+:\d+$")), "Invalid route recipe.");
        // Same canonical, name-independent identity used by route_maker.recipe_hash().
        string canonical = "{\"locations\":" + Json().Serialize(keys) + ",\"restriction_source\":" +
            Convert.ToInt32(recipe["restriction_source"]).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"version\":1}";
        return Hash(Encoding.UTF8.GetBytes(canonical));
    }

    byte[] ValidateBuild(string folder, string wanted, byte[] original, out string label) {
        folder = Path.GetFullPath(folder).TrimEnd('\\');
        CheckPath(folder);
        string refs = Path.Combine(project, "experiments");
        string refId = Path.GetFileName(folder);
        if (String.Equals(Path.GetDirectoryName(folder), refs, StringComparison.OrdinalIgnoreCase)) {
            string pinned = refId == "010-offline-owned-route" ? "e2dc242d6071360ff75675f4adb9203051625634c9f3f88945fdee2456863b75" :
                refId == "012-multiplayer-owned-route" ? "42d5c88f1893f048ccf75bba803f1adc5286e301d54978b742a03b8bb44f2c71" :
                refId == "014-custom-grand-ocean-sprint" ? "1ad2d49462c9a1e3d52d32cd410886a5f16a3cb99be3e6a6d6a8f8d2ab8f233f" :
                refId == "015-custom-grand-ocean-multiplayer" ? "0d5c4cda823bce57874e615f21c13a1f60741a0006c2a0f49ac70eac1878ef3b" : null;
            Require(pinned != null && wanted == pinned, "Unknown reference build.");
            label = Legacy[pinned];
            return ReadArchive(Path.Combine(folder,"candidate",RelativeArchive), pinned);
        }
        Require(String.Equals(Path.GetDirectoryName(folder), Builds, StringComparison.OrdinalIgnoreCase), "Choose a build from Saved races.");
        var manifest = ReadJson(Path.Combine(folder, "manifest.json"), 65536);
        var recipe = ReadJson(Path.Combine(folder, "recipe.json"), 65536);
        string mode = (string)manifest["mode"];
        Require((mode == "offline" || mode == "online") && (string)manifest["original_sha256"] == Original &&
            (string)manifest["candidate_sha256"] == wanted && (string)manifest["recipe_hash"] == RecipeIdentity(recipe) &&
            Convert.ToInt32(manifest["target_event_id"]) == (mode == "offline" ? 321312 : 744283), "Build identity differs from the selected recipe or mode.");
        var bytes = ReadArchive(Path.Combine(folder, "candidate", RelativeArchive), wanted);
        var patch = ReadJson(Path.Combine(folder, "package", "patch.json"), 4194304);
        Require(bytes.SequenceEqual(ReconstructPatch(original, patch, wanted)), "Package and candidate differ.");
        label = (string)recipe["name"] + " (" + mode + ")";
        return bytes;
    }

    string Recognize(string hash,string game) {
        if (hash == Original) return "Original game";
        string name;
        if (Legacy.TryGetValue(hash, out name)) return name;
        string cached = Path.Combine(state, "routes", hash + ".bndl");
        string receipt = Path.ChangeExtension(cached, ".json");
        if (File.Exists(receipt)) {
            var record = ReadJson(receipt, 65536);
            Require((string)record["candidate"] == hash && (string)record["original"] == Original, "Invalid recognized-route receipt.");
            ReadArchive(cached, hash);
            return (string)record["name"];
        }
        foreach (string folder in ExportFolders()) {
            string manifest = Path.Combine(folder,"manifest.json");
            try {
                if (!File.Exists(manifest) || Convert.ToString(ReadJson(manifest,65536)["candidate_sha256"]) != hash) continue;
                ValidateBuild(folder,hash,OriginalBytes(game),out name);
                return name;
            } catch (Exception ex) {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException ||
                    ex is InvalidCastException || ex is FormatException || ex is OverflowException)) throw;
                // A damaged unrelated export cannot authorize replacement.
                continue;
            }
        }
        return null;
    }

    void Remember(string digest, byte[] data, string label) {
        string cached = Path.Combine(state, "routes", digest + ".bndl");
        SaveVerified(cached,data,digest);
        string receipt = Path.ChangeExtension(cached,".json");
        CheckPath(receipt);
        if (!File.Exists(receipt)) {
            string temp=receipt+"."+Stamp()+".tmp";
            File.WriteAllText(temp,Json().Serialize(new {candidate=digest,original=Original,name=label}));
            try { try {File.Move(temp,receipt);} catch(IOException){if(!File.Exists(receipt))throw;} }
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
        var record=ReadJson(receipt,65536);
        Require((string)record["original"]==Original && (string)record["candidate"]==digest,"Existing route receipt is unexpected.");
    }

    internal Inspection Inspect(string input) {
        string game = NormalizeGame(input);
        string target = Archive(game);
        CheckPath(target);
        string hash = HashFile(target);
        string name = Recognize(hash, game);
        return new Inspection {Hash=hash,Name=name ?? "Unrecognized game archive",Known=name!=null,GameRunning=Running()};
    }

    internal string Execute(string action,string input,string saves,string folder,string wanted,string expectedBefore,string id) {
        string game = NormalizeGame(input);
        string target = Archive(game);
        CheckPath(target);
        CheckPath(state);
        Require(!IsUnder(state,game) && !IsUnder(game,state), "Game and deployment state must be in separate folders.");
        Require(action == "install" || action == "restore", "Unknown deployment action.");
        string log = LogPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(log));
        CheckPath(log);
        // Serialize app operations for this installation, including a normal/elevated pair.
        string lockName = @"Local\SeacrestRouteMaker-" + Hash(Encoding.UTF8.GetBytes(target.ToUpperInvariant()));
        using (var mutex = new Mutex(false,lockName)) {
            bool held;
            try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held=true; }
            Require(held,"Another install or restore is in progress.");
            try {
                Closed();
                var before = Inspect(game);
                Require(before.Known, "Unrecognized archive. No replacement performed.");
                Require(before.Hash == expectedBefore, "The installation changed since inspection. Inspect it again.");
                bool install = action == "install";
                if (!install) Require(wanted == Original, "Restore target must be original.");
                if (install) VerifySupport(game);
                string label = "Original game";
                byte[] original = null, output = null;
                if (install || before.Hash != wanted) {
                    original = OriginalBytes(game);
                    output = install ? ValidateBuild(folder,wanted,original,out label) : original;
                }
                if (before.Hash == wanted) return Record(log, "Already installed: " + label + Environment.NewLine + wanted);
                SaveVerified(Path.Combine(state,"original.bndl"),original,Original);
                SortedDictionary<string,string> hashes = null;
                string backup = null;
                if (install) {
                    saves = Path.GetFullPath(saves.Trim().Trim('"')).TrimEnd('\\');
                    CheckPath(saves);
                    Require(!IsUnder(state,saves) && !IsUnder(saves,state) && !IsUnder(saves,game) &&
                        !IsUnder(game,saves) && !String.Equals(saves,game,StringComparison.OrdinalIgnoreCase) && !String.Equals(saves,state,StringComparison.OrdinalIgnoreCase), "Select the separate, active game save folder.");
                    backup = BackupSaves(state,saves,out hashes);
                    Remember(wanted,output,label);
                }
                // Adopt a previously separate helper's recognized route before switching away.
                if (before.Hash != Original && !Legacy.ContainsKey(before.Hash)) {
                    Remember(before.Hash,ReadArchive(target,before.Hash),before.Name);
                }
                string retained = ReplaceArchive(state,target,install?"Install":"Restore",before.Hash,wanted,output,saves,hashes,backup);
                string result=(install?"Installed and verified: ":"Restored and verified: ")+label+
                    Environment.NewLine+wanted+Environment.NewLine+"Previous archive: "+retained;
                if (backup!=null)result+=Environment.NewLine+"Save backup: "+backup;
                return Record(log,result);
            } catch(Exception ex) {
                Record(log,"Stopped: "+ex.Message);
                throw;
            } finally { mutex.ReleaseMutex(); }
        }
    }

    static string Record(string log,string message) {
        File.AppendAllText(log,DateTime.UtcNow.ToString("o")+Environment.NewLine+message+Environment.NewLine);
        return message;
    }
}
