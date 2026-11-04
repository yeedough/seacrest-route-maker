"""Local route recipes, map previews, and verified offline/online archive exports.

Never installs, restores, launches the game, or writes save data. The v0.1 recipe
uses 2..14 existing Locations and an existing two-record restriction set.
"""
import argparse
import base64
import copy
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import zipfile

from analyze_routes import ROOT, EXTRACTED, read_json, write_json, schema, event_route, u32, u64
from build_name_probe import sha
from build_owned_route_probe import require
from compare_roundtrip import compare, container
from route_map import catalog, estimated_leg, RESOURCE, PAYLOAD_HASH
from build_route_manager import build_manager

ORIGINAL_HASH = 'db8e2d3b54dfcfce0cc2b68cc386a5a72ea310e5aac3e957ea5f9592bb247052'
TOOL_HASH = '68f369ce5630212fa796e424d26e074f83dd359b6067f0e6f57c183de3bd4635'
OFFLINE_SUCCESS = '1ad2d49462c9a1e3d52d32cd410886a5f16a3cb99be3e6a6d6a8f8d2ab8f233f'
ONLINE_SUCCESS = '0d5c4cda823bce57874e615f21c13a1f60741a0006c2a0f49ac70eac1878ef3b'
OUTPUT_ROOT = ROOT/'working/route-maker-builds'
PRESET = dict(version=1,name='Grand Ocean Sprint',locations=['321312:0','744257:2'],restriction_source=321312)


def canonical_recipe(raw):
    require(isinstance(raw,dict) and raw.get('version') == 1,'Unsupported recipe version')
    name = raw.get('name','').strip()
    require(1 <= len(name) <= 80 and all(ord(c)>=32 for c in name),'Choose a name of 1..80 characters')
    locations = raw.get('locations')
    require(isinstance(locations,list) and 2 <= len(locations) <= 14,'Choose a start, finish and at most twelve waypoints')
    require(all(isinstance(x,str) for x in locations),'Marker keys must be strings')
    require(len(locations)==len(set(locations)),'A marker cannot be repeated in v0.1')
    require(type(raw.get('restriction_source')) is int,'Choose a restriction source')
    return dict(version=1,name=name,locations=locations,restriction_source=raw['restriction_source'])


def recipe_hash(recipe):
    # Name is metadata, not route behavior.
    fields={k:v for k,v in recipe.items() if k!='name'}
    return hashlib.sha256(json.dumps(fields,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def validate_recipe(raw, data=None):
    recipe=canonical_recipe(raw)
    data=data or catalog()
    markers={m['key']:m for m in data['markers']}
    require(all(k in markers for k in recipe['locations']),'Unknown or unsupported race marker')
    selected=[markers[k] for k in recipe['locations']]
    require(selected[0]['role']=='start','First marker must have an existing race starting grid')
    require(selected[-1]['role']=='finish','Last marker must be an existing race finish')
    require(all(m['role']=='waypoint' for m in selected[1:-1]),'Intermediate markers must be existing route waypoints')
    require(len({m['marker_id'] for m in selected})==len(selected),'Repeated trigger IDs are not supported in v0.1')
    restrictions={r['event_id']:r for r in data['restriction_sources']}
    require(recipe['restriction_source'] in restrictions,'Restriction source must contain exactly two stock records')
    legs=[estimated_leg(a,b,data) for a,b in zip(selected,selected[1:])]
    known = recipe_hash(recipe)==recipe_hash(PRESET)
    return recipe,dict(recipe_hash=recipe_hash(recipe),markers=selected,
                       restriction_source=restrictions[recipe['restriction_source']],
                       estimated_lines=[line for leg in legs for line in leg['lines']],
                       sample_length=sum(leg['sample_length'] for leg in legs),
                       endpoint_projection_errors=[leg['endpoint_projection_errors'] for leg in legs],
                       tested_preset=known,reported_game_distance_km=6 if known else None,
                       validation='Two-PC-tested preset' if known else 'Experimental recipe; gameplay not tested',
                       caveat=data['caveat'])


def safe_build_dir(path, must_exist=True):
    path=Path(path).resolve()
    require(path.is_relative_to(OUTPUT_ROOT.resolve()) and path!=OUTPUT_ROOT.resolve(),'Build path must stay under working/route-maker-builds')
    if must_exist:require(path.is_dir(),'Build directory missing')
    return path


def emit_delta(source,candidate):
    a,b=source,candidate
    ops=[]
    # Use fixed-alignment copy/literals; simple, bounded and reliable for this build.
    limit=min(len(a),len(b));i=0
    while i<limit:
        same=a[i]==b[i];j=i+1
        while j<limit and (a[j]==b[j])==same:j+=1
        ops.append(dict(offset=i,length=j-i) if same else dict(data=base64.b64encode(b[i:j]).decode()))
        i=j
    if len(b)>limit:ops.append(dict(data=base64.b64encode(b[limit:]).decode()))
    result=b''.join(base64.b64decode(o['data']) if 'data' in o else a[o['offset']:o['offset']+o['length']] for o in ops)
    require(result==b,'Export reconstruction mismatch')
    return ops


def build(raw,mode='offline',offline_build=None):
    recipe,preview=validate_recipe(raw)
    require(mode in ('offline','online'),'Unknown mode')
    if mode=='online':
        require(offline_build is not None,'Build and test the offline export first')
        offline_dir=safe_build_dir(offline_build)
        previous=read_json(offline_dir/'manifest.json')
        require(previous['mode']=='offline' and previous['recipe_hash']==recipe_hash(recipe),'Offline recipe does not match')
        require(sha(offline_dir/'candidate/GAMELOGIC/CHALLENGEINSTANCES.BNDL')==previous['candidate_sha256'],'Offline candidate changed')
        proof=read_json(offline_dir/'offline-success.json')
        require(proof['candidate_sha256']==previous['candidate_sha256'] and proof['recipe_hash']==recipe_hash(recipe)
                and proof['result']=='user_reported_completed_without_issues','Matching offline completion record required')
    base=EXTRACTED/'GAMELOGIC/CHALLENGEINSTANCES'
    source=ROOT/'working/initial/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
    tool=ROOT/'tools/bundle_packer_unpacker/bundle_packer_unpacker.py'
    require(sha(source)==ORIGINAL_HASH and sha(base/RESOURCE)==PAYLOAD_HASH and sha(tool)==TOOL_HASH,'Input/tool hash mismatch')
    original=(base/RESOURCE).read_bytes()
    events=read_json(ROOT/'experiments/004-event-catalog/events.json')
    before=[event_route(original,e) for e in events]
    by_event={e['serialized_object_id']:e for e in events}
    by_route={r['event_id']:r for r in before}
    target_id=321312 if mode=='offline' else 744283
    target=by_route[target_id]
    donors=[by_route[m['event_id']]['Location'][m['index']] for m in preview['markers']]
    restrictions=by_route[recipe['restriction_source']]['RestrictedRoadSections']
    require(len(target['Location'])==len(target['RestrictedRoadSections'])==len(restrictions)==2,'Target layout changed')
    _,_,records=container(source)
    rec_index,=[i for i,r in enumerate(records) if r['identity'].startswith('5f470600')]
    old_start,old_count=records[rec_index]['import_offset'],records[rec_index]['import_count']
    require(old_start+old_count*16==len(original),'Import bounds changed')
    import_bytes=[original[old_start+i*16:old_start+(i+1)*16] for i in range(old_count)]
    imports={u32(b,8)&0x7fffffff:b for b in import_bytes}
    require(len(imports)==old_count,'Duplicate import locations')
    definitions=[schema(p) for p in (base/'GenesysDefinition').glob('*.dat')]
    loc,=[s for s in definitions if s['name']=='Location']
    res,=[s for s in definitions if s['name']=='RestrictedRoadSections']
    require([(f['type'],f['offset'],f['size']) for f in loc['fields']]==[(7,i*4,4) for i in range(11)],'Location schema changed')
    require([(f['type'],f['offset'],f['size']) for f in res['fields']]==[(7,0,4),(9,4,4),(2,8,1)],'Restriction schema changed')
    edited=bytearray(original[:old_start]);allowed=set();added=[]
    addresses=[x['object_offset'] for x in target['Location']]
    if len(donors)>2:
        array_at=len(edited);edited.extend(bytes(((len(donors)*8+15)//16)*16))
        for donor in donors[2:]:
            pos=donor['object_offset'];at=len(edited)
            require(original[pos:pos+16]==bytes(16) and original[pos+28:pos+48]==bytes(20)
                    and original[pos+92:pos+96]==bytes(4) and u64(original,pos+16)==pos+48
                    and u32(original,pos+24)==loc['type_hash'],'Unexpected Location object')
            clone=bytearray(original[pos:pos+96]);struct.pack_into('<Q',clone,16,at+48);edited.extend(clone)
            imp=bytearray(imports[pos]);require(u32(imp,0)==loc['resource_id'],'Wrong Location import')
            struct.pack_into('<I',imp,8,at|0x80000000);added.append(bytes(imp));addresses.append(at)
        for i,at in enumerate(addresses):struct.pack_into('<Q',edited,array_at+i*8,at)
        field=target['body_offset']+0xd8
        struct.pack_into('<QII',edited,field,array_at,len(donors),8);allowed.update(range(field,field+16))
    for kind,srcs,sc in [('Location',donors[:2],loc),('RestrictedRoadSections',restrictions,res)]:
        for src,dst in zip(srcs,target[kind]):
            for field in sc['fields']:
                a,b,size=src['body_offset']+field['offset'],dst['body_offset']+field['offset'],field['size']
                edited[b:b+size]=original[a:a+size];allowed.update(range(b,b+size))
    new_start=len(edited);edited.extend(b''.join(import_bytes+added))
    changed=[i for i in range(old_start) if original[i]!=edited[i]]
    require(changed and set(changed)<=allowed,'No route change, or unexpected non-route edits')
    after_events=copy.deepcopy(events)
    after_target=next(e for e in after_events if e['serialized_object_id']==target_id)
    for imp in added:
        identity,typ,index,kind,at,reserved=struct.unpack('<IHBBII',imp)
        require(reserved==0,'Unexpected import data')
        after_target['imports'].append(dict(location=at&0x7fffffff,resource_id=identity,type=typ,index=index,kind=kind,schema_name_if_definition='Location'))
    after=[event_route(edited,e) for e in after_events]
    require([a['event_id'] for a,b in zip(before,after) if a!=b]==[target_id],'Other event changed')
    result=next(r for r in after if r['event_id']==target_id)
    for kind,expected in [('Location',donors),('RestrictedRoadSections',restrictions)]:
        require([x['values'] for x in result[kind]]==[x['values'] for x in expected],'Decoded route mismatch')
        all_addresses=[x['object_offset'] for r in after for x in r[kind]]
        require(len(all_addresses)==len(set(all_addresses)),'Aliased nested object')
    slug=re.sub(r'[^a-zA-Z0-9_-]+','-',recipe['name']).strip('-')[:45] or 'route'
    out=safe_build_dir(OUTPUT_ROOT/(datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S-%f')+'-'+slug+'-'+mode),False)
    require(not out.exists(),'Build already exists')
    out.mkdir(parents=True)
    write_json(out/'recipe.json',recipe);write_json(out/'preview.json',preview)
    work=out/'resources';shutil.copytree(base,work);(work/RESOURCE).write_bytes(edited)
    def run(args,log):
        proc=subprocess.run([sys.executable,str(tool),*map(str,args)],capture_output=True,text=True,timeout=120)
        text=proc.stdout+proc.stderr;(out/log).write_text(text,encoding='utf-8')
        require(proc.returncode==0 and not any(v in text.lower() for v in ('error','warning','traceback')),'Packer failed: '+log)
    packed=out/'candidate/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
    run(['-p','HPR',work/'IDs_CHALLENGEINSTANCES.BNDL',packed.parent,packed.name],'pack.log')
    run(['-u','HPR',packed,out/'reextracted'],'unpack.log')
    check=compare(source,packed,base,out/'reextracted')
    require(check['changed_payloads']==[RESOURCE] and not check['missing_payloads'] and not check['added_payloads']
            and check['entry_count_equal'] and (out/'reextracted'/RESOURCE).read_bytes()==edited,'Re-extraction mismatch')
    _,_,new_records=container(packed)
    growth=len(edited)-len(original)
    for i,(old,new) in enumerate(zip(records,new_records)):
        expected=copy.deepcopy(old);expected['disk_offsets']=new['disk_offsets']
        if i==rec_index:
            expected['import_offset']=new_start;expected['import_count']=old_count+len(added)
            expected['uncompressed_sizes_alignment'][0]+=growth;expected['stored_sizes_alignment'][0]+=growth
        require(expected==new,'Unexpected resource metadata change')
    candidate_hash=sha(packed)
    manifest=dict(version=1,mode=mode,name=recipe['name'],recipe_hash=recipe_hash(recipe),
                  original_sha256=ORIGINAL_HASH,candidate_sha256=candidate_hash,target_event_id=target_id,
                  displayed_title='ROADSTERS REBORN',locations=len(donors),added_objects=len(added),
                  payloads_checked=232,events_checked=423,changed_event_ids=[target_id],
                  state='built_not_installed_not_gameplay_tested',candidate_file=str(packed),
                  restrictions_source=recipe['restriction_source'],map_preview_is_estimate=True)
    expected_success=OFFLINE_SUCCESS if mode=='offline' else ONLINE_SUCCESS
    if candidate_hash==expected_success:
        manifest['state']='byte_identical_to_user_completed_reference'
        manifest['reference_experiment']=14 if mode=='offline' else 15
        if mode=='offline':
            proof=read_json(ROOT/'experiments/014-custom-grand-ocean-sprint/runtime-result.json')
            require(proof['result']=='user_reported_completed_without_issues','Reference evidence missing')
            write_json(out/'offline-success.json',dict(result=proof['result'],recipe_hash=recipe_hash(recipe),
                       candidate_sha256=candidate_hash,observation=proof['quote'],source='Existing experiment 014 user report; archive reproduced byte-for-byte'))
    write_json(out/'manifest.json',manifest);write_json(out/'comparison.json',check);write_json(out/'decoded-target.json',result)
    package=out/'package';package.mkdir()
    write_json(package/'patch.json',dict(original_sha256=ORIGINAL_HASH,candidate_sha256=candidate_hash,
               candidate_length=packed.stat().st_size,operations=emit_delta(source.read_bytes(),packed.read_bytes())))
    helper=(ROOT/'scripts/custom-route/Manage-Test.ps1').read_text(encoding='utf-8-sig')
    require(helper.count(ONLINE_SUCCESS)==1,'Helper template changed')
    helper=helper.replace(ONLINE_SUCCESS,candidate_hash).replace('Grand Ocean Sprint candidate 015','Route maker export').replace('grand-ocean-015-','route-maker-')
    (package/'Manage-Test.ps1').write_text(helper,encoding='utf-8-sig')
    build_manager(package,manifest)
    shutil.copy2(ROOT/'handoff/multiplayer-test/expected-hashes.json',package/'expected-hashes.json')
    instructions=(f"{recipe['name']} — {mode.upper()} route-maker export\n"
        f"Displayed event: ROADSTERS REBORN ({'career' if mode=='offline' else 'online Race'}).\n"
        f"Candidate SHA-256: {candidate_hash}\nStatus: {manifest['state']}\n\n"
        "This ZIP contains a patch and helper, not full game archives or saves.\n"
        "Extract to a new writable folder outside the game. Keep its state/backups/logs local.\n"
        "EASY START: open Manage-Route.exe. Choose 1 Inspect, 2 Install, or 3 Restore.\n"
        "Paste the folder containing NFS11Remastered.exe, or the executable path.\n"
        "The native menu keeps results/errors visible and writes a log under package/logs.\n"
        "It requests normal Windows elevation if game-folder writes need permission.\n"
        "It does not need PowerShell and does not change script execution policy.\n"
        "Running Manage-Test.ps1 by itself only inspects and may close its temporary\n"
        "window when finished. Use the EXE menu or the explicit commands below.\n\n"
        "Close the game. Inspect original files first. The helper accepts only original\n"
        "or this exact candidate, checks five baseline files and backs up current saves.\n"
        "Advanced: open PowerShell in this package folder, then run:\n"
        "Install with: & '.\\Manage-Test.ps1' -Action Install -GameFolder 'GAME FOLDER'\n"
        "Use -SaveFolder 'SAVE FOLDER' if automatic detection is incorrect.\n"
        "Restore with: & '.\\Manage-Test.ps1' -Action Restore -GameFolder 'GAME FOLDER'\n"
        "Use normal filesystem approval. Do not change execution policy if blocked.\n"
        "Retain backups; never restore old saves automatically. Do not use the Seacrest\n"
        "switcher on this candidate. Unknown versions/modifications are refused.\n\n"
        +("Test career OFFLINE first: preview, start, guidance, full finish/results.\n"
          "Record actual completion in the maker before creating an online export.\n"
          "Completing this event may update its saved career result.\n" if mode=='offline' else
          "Both PCs need this exact candidate, fresh backups and successful startup.\n"
          "Test only a private lobby. Do not use public matchmaking.\n")
        +"Every new recipe needs gameplay testing; the map path is an estimate.\n"
        "Quit and restore before ordinary play. No geometry or new event IDs are added.\n")
    (package/'README.txt').write_text(instructions,encoding='utf-8')
    with zipfile.ZipFile(out/'Route-Test.zip','x',zipfile.ZIP_DEFLATED) as z:
        for path in sorted(package.iterdir()):z.write(path,path.name)
    return dict(build_dir=str(out),candidate_sha256=candidate_hash,mode=mode,state=manifest['state'],
                package=str(out/'Route-Test.zip'),recipe_hash=recipe_hash(recipe))


def record_success(build_dir,observation):
    out=safe_build_dir(build_dir);manifest=read_json(out/'manifest.json');recipe=read_json(out/'recipe.json')
    require(manifest['mode']=='offline','Only offline reports unlock online export')
    require(len(observation.strip())>=10,'Describe the completed offline test')
    require(sha(out/'candidate/GAMELOGIC/CHALLENGEINSTANCES.BNDL')==manifest['candidate_sha256'],'Candidate changed')
    require(recipe_hash(canonical_recipe(recipe))==manifest['recipe_hash'],'Recipe changed')
    require(not (out/'offline-success.json').exists(),'An offline success record already exists')
    proof=dict(result='user_reported_completed_without_issues',recipe_hash=manifest['recipe_hash'],
               candidate_sha256=manifest['candidate_sha256'],observation=observation.strip(),
               source='User-entered offline completion report in route maker',time_utc=datetime.now(timezone.utc).isoformat())
    write_json(out/'offline-success.json',proof)
    return proof


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['catalog','preview','build-offline','build-online','record-offline','library','save-library','delete-library'])
    parser.add_argument('--recipe',type=Path)
    parser.add_argument('--offline-build',type=Path)
    parser.add_argument('--observation-file',type=Path)
    parser.add_argument('--output',type=Path)
    args=parser.parse_args()
    if args.action in ('library','save-library','delete-library'):
        import route_library
        if args.action=='library':result=route_library.index()
        else:
            require(args.recipe is not None,'Recipe required')
            result=(route_library.delete_recipe if args.action=='delete-library' else route_library.save_recipe)(read_json(args.recipe))
    elif args.action=='catalog':result=catalog()
    elif args.action=='record-offline':
        require(args.offline_build and args.observation_file,'Build and observation file required')
        result=record_success(args.offline_build,args.observation_file.read_text(encoding='utf-8-sig'))
    else:
        require(args.recipe is not None,'Recipe required')
        recipe=read_json(args.recipe)
        if args.action=='preview':_,result=validate_recipe(recipe)
        else:result=build(recipe,'online' if args.action=='build-online' else 'offline',args.offline_build)
    if args.output:
        destination=args.output.resolve()
        require(destination.is_relative_to((ROOT/'working').resolve()),'Response output must be under working/')
        require(not destination.exists(),'Response output already exists')
        destination.parent.mkdir(parents=True,exist_ok=True);write_json(destination,result)
    else:print(json.dumps(result,ensure_ascii=False,indent=2))


if __name__=='__main__':
    try:main()
    except (ValueError,KeyError,OSError) as exc:
        print(str(exc),file=sys.stderr);sys.exit(1)
