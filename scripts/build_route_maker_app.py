"""Compile local Windows map picker with installed Framework compiler; no installs."""
from pathlib import Path
import json
import subprocess
import sys
from analyze_routes import ROOT, write_json
from route_map import catalog
from route_maker import PRESET


APP_SOURCES = ["SeacrestRouteMaker.cs", "DeploymentPage.cs", "RouteDeployment.cs", "ArchiveSafety.cs"]


def main():
    out=ROOT/'route-maker';out.mkdir(exist_ok=True)
    data=catalog();write_json(out/'map.json',data)
    write_json(out/'config.json',dict(project=str(ROOT),python=sys.executable))
    write_json(out/'Grand-Ocean-Sprint.recipe.json',PRESET)
    compiler=Path(r'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe')
    proc=subprocess.run([str(compiler),'/nologo','/optimize+','/target:winexe','/platform:anycpu',
                         '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',
                         '/out:'+str(out/'SeacrestRouteMaker.exe'),*[str(ROOT/'scripts/route-maker'/src) for src in APP_SOURCES]],
                        capture_output=True,text=True,timeout=120)
    (out/'compile.log').write_text(proc.stdout+proc.stderr,encoding='utf-8')
    if proc.returncode:raise RuntimeError(proc.stdout+proc.stderr)
    shutil_source=ROOT/'scripts/route-maker/README.md'
    if shutil_source.exists():(out/'README.md').write_bytes(shutil_source.read_bytes())
    write_json(out/'build.json',dict(python=sys.executable,compiler=str(compiler),map_evidence=data['evidence'],
                                    marker_count=len(data['markers']),local_only=True,version="0.3",explicit_install_and_restore=True,build_writes_to_game=False))
    print(json.dumps(dict(executable=str(out/'SeacrestRouteMaker.exe'),roads=len(data['roads']),
                          sections=len(data['sections']),markers=len(data['markers'])),indent=2))


if __name__=='__main__':main()
