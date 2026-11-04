"""Prepare machine-local route data from a supported stock PC installation."""
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / 'scripts'))
from route_maker import ORIGINAL_HASH, TOOL_HASH
from route_map import catalog


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def setup(game):
    if sys.version_info < (3, 10):
        raise ValueError('Install Python 3.10 or newer.')
    tool = ROOT / 'tools/bundle_packer_unpacker/bundle_packer_unpacker.py'
    if digest(tool) != TOOL_HASH:
        raise ValueError('Packer identity mismatch. Extract the release again.')
    rows = json.loads((ROOT/'handoff/multiplayer-test/expected-hashes.json').read_text())
    for row in rows:
        if digest(game/row['RelativePath']) != row['SHA256'].lower():
            raise ValueError('Stock game file required: '+row['RelativePath'])
    with tempfile.TemporaryDirectory(dir=ROOT) as temp:
        stage = Path(temp)
        base = stage/'experiments/002-inspection/unpacked'
        for name in ['GAMELOGIC/CHALLENGEINSTANCES','GAMELOGIC/CHALLENGEDEFINITIONS','GAMELOGIC/ROADDATA','TRIGGERS','UI/LANGUAGE/0001']:
            source = game/(name+'.BNDL')
            dest = base/name if not name.startswith('UI/') else stage/'experiments/003-language/unpacked/0001'
            subprocess.run([sys.executable,str(tool),'-u','HPR',str(source),str(dest)],check=True,stdout=subprocess.DEVNULL)
        stock = stage/'working/initial/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
        stock.parent.mkdir(parents=True)
        shutil.copyfile(game/'GAMELOGIC/CHALLENGEINSTANCES.BNDL',stock)
        if digest(stock) != ORIGINAL_HASH:
            raise ValueError('Game archive changed during setup.')
        script = stage/'scripts/catalog_events.py'
        script.parent.mkdir()
        shutil.copyfile(ROOT/'scripts/catalog_events.py',script)
        subprocess.run([sys.executable,str(script)],check=True,stdout=subprocess.DEVNULL)
        for folder in ['experiments','working']:
            for src in (stage/folder).rglob('*'):
                if src.is_file():
                    dest=ROOT/src.relative_to(stage)
                    if dest.exists() and digest(dest)!=digest(src):
                        raise ValueError('Existing local data differs. Extract the beta into a fresh folder.')
            shutil.copytree(stage/folder,ROOT/folder,dirs_exist_ok=True)
    app = ROOT/'route-maker'
    (app/'map.json').write_text(json.dumps(catalog()),encoding='utf-8')
    (app/'config.json').write_text(json.dumps(dict(project=str(ROOT),python=sys.executable)),encoding='utf-8')
    exe = app/'SeacrestRouteMaker.exe'
    if not exe.is_file():
        subprocess.run([sys.executable,str(ROOT/'scripts/build_route_maker_app.py')],check=True)
    if not exe.is_file():
        raise ValueError('Editor executable missing after setup.')
    print('Ready. Open '+str(exe))


if __name__ == '__main__':
    try:
        folder = sys.argv[1] if len(sys.argv)>1 else input('Paste the stock game folder: ')
        setup(Path(folder.strip().strip('"')).resolve())
    except (ValueError,OSError,subprocess.SubprocessError) as exc:
        sys.exit(str(exc))
