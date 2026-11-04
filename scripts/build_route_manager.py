"""Compile a standalone route-specific menu from the local export delta."""
from pathlib import Path
import subprocess
from analyze_routes import ROOT, read_json
from build_owned_route_probe import require


def build_manager(package,manifest):
    package=Path(package)
    source=(ROOT/'scripts/route-maker/RouteTestManager.cs').read_text(encoding='utf-8')
    candidate=manifest['candidate_sha256'];mode=manifest['mode']
    require(len(candidate)==64 and all(c in '0123456789abcdef' for c in candidate) and mode in ('offline','online'),'Invalid manager profile')
    patch=read_json(package/'patch.json');require(patch['candidate_sha256']==candidate,'Patch mismatch')
    source=source.replace('__CANDIDATE_HASH__',candidate).replace('__ROUTE_MODE__',mode)
    folder=package.parent/'manager-build';folder.mkdir(exist_ok=True)
    cs=folder/'RouteTestManager.cs';cs.write_text(source,encoding='utf-8-sig')
    out=package/'Manage-Route.exe'
    compiler=Path(r'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe')
    proc=subprocess.run([str(compiler),'/nologo','/optimize+','/target:exe','/platform:anycpu',
                         '/r:System.Web.Extensions.dll','/out:'+str(out),
                         '/resource:'+str(package/'patch.json')+',route.patch.json',str(cs),str(ROOT/'scripts/route-maker/ArchiveSafety.cs')],capture_output=True,text=True,timeout=120)
    (folder/'compile.log').write_text(proc.stdout+proc.stderr,encoding='utf-8')
    require(proc.returncode==0,'Native manager compilation failed: '+proc.stdout+proc.stderr)
    return out
