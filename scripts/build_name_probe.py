"""Build a reversible, offline-only event-name probe in a new experiment folder.

Changes one existing ChallengeInstance Name reference. No gameplay fields, menu
lists, installed files, or saves are modified. Does not launch the game.
"""
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys

from analyze_routes import ROOT, EXTRACTED, read_json, schema, write_json
from compare_roundtrip import compare


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    out=ROOT/'experiments/006-offline-name-probe'
    if out.exists():
        raise ValueError('Refusing to overwrite a previous experiment')
    tool=ROOT/'tools/bundle_packer_unpacker/bundle_packer_unpacker.py'
    if sha(tool)!='68f369ce5630212fa796e424d26e074f83dd359b6067f0e6f57c183de3bd4635':
        raise ValueError('Tool hash differs from reviewed version')
    source=ROOT/'working/initial/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
    if sha(source)!='db8e2d3b54dfcfce0cc2b68cc386a5a72ea310e5aac3e957ea5f9592bb247052':
        raise ValueError('Unexpected base archive')
    extracted=EXTRACTED/'GAMELOGIC/CHALLENGEINSTANCES'
    resource=Path('GenesysInstance/5F_47_06_00.dat')
    if sha(extracted/resource)!='828b3fa32d772cc7f204d5d566946e626f4f949d75d3af831e9bc935e13dfe9f':
        raise ValueError('Unexpected base payload')
    event,=[e for e in read_json(ROOT/'experiments/004-event-catalog/events.json')
            if e['serialized_object_id']==321312 and e['title']=='ROADSTERS REBORN' and e['definition_name']=='RaceDefinition']
    field,=[f for f in schema(extracted/'GenesysDefinition/52_6B_03_00.dat')['fields'] if f['key']=='4e616d65']
    if (field['type'],field['count'],field['size'])!=(7,1,4):
        raise ValueError('Unexpected Name field schema')
    strings=read_json(ROOT/'experiments/004-event-catalog/language-strings.json')
    if strings['572998']['text']!='DEBUG_TEST_RACE':
        raise ValueError('Expected built-in display string missing')
    offset=event['body_offset']+field['offset']
    original=(extracted/resource).read_bytes()
    if struct.unpack_from('<I',original,offset)[0]!=256688:
        raise ValueError('Original title reference mismatch')
    edited=bytearray(original)
    struct.pack_into('<I',edited,offset,572998)
    expected_changed=[i for i,(a,b) in enumerate(zip(original,edited)) if a!=b]
    if not expected_changed or any(i<offset or i>=offset+4 for i in expected_changed):
        raise ValueError('Change extends beyond the intended field')
    work=out/'resources'
    shutil.copytree(extracted,work)
    (work/resource).write_bytes(edited)
    commands=[]
    def run(args, log):
        command=[sys.executable,str(tool),*map(str,args)]
        result=subprocess.run(command,capture_output=True,text=True,timeout=120)
        output=result.stdout+result.stderr
        (out/log).write_text(output,encoding='utf-8')
        commands.append({'argv':command,'returncode':result.returncode})
        if result.returncode or any(s in output.lower() for s in ['error','warning','traceback']):
            raise RuntimeError(f'Tool failure: {log}')
    packed=out/'candidate/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
    run(['-p','HPR',work/'IDs_CHALLENGEINSTANCES.BNDL',packed.parent,packed.name],'pack.log')
    reextracted=out/'reextracted'
    run(['-u','HPR',packed,reextracted],'unpack.log')
    check=compare(source,packed,extracted,reextracted)
    expected_resource=resource.as_posix()
    if (check['changed_payloads']!=[expected_resource] or check['missing_payloads'] or check['added_payloads']
            or check['resource_metadata_changes'] or check['resource_layout_changes']
            or not check['exported_resource_json_equal'] or not check['entry_count_equal']):
        raise ValueError('Unexpected resource or container changes')
    if (reextracted/resource).read_bytes()!=edited:
        raise ValueError('Repacked payload differs from intended edit')
    a,b=source.read_bytes(),packed.read_bytes()
    diff=[i for i,(x,y) in enumerate(zip(a,b)) if x!=y]
    if len(diff)!=len(expected_changed) or b[len(a):]!=bytes(54):
        raise ValueError('Unexpected file-level changes')
    # Independently locate the uncompressed payload in the original archive.
    entry,=[a[struct.unpack_from('<I',a,20)[0]+i*80:struct.unpack_from('<I',a,20)[0]+(i+1)*80]
            for i in range(struct.unpack_from('<I',a,16)[0])
            if struct.unpack_from('<I',a,struct.unpack_from('<I',a,20)[0]+i*80)[0]==0x6475f]
    file_payload=struct.unpack_from('<I',a,24)[0]+struct.unpack_from('<I',entry,48)[0]
    if diff!=[file_payload+i for i in expected_changed]:
        raise ValueError('File differences are outside the known payload field')
    manifest={'purpose':'Offline visual loading test only', 'status':'built_not_deployed_not_runtime_tested',
              'game_relative_path':'GAMELOGIC/CHALLENGEINSTANCES.BNDL',
              'original_sha256':sha(source),'candidate_sha256':sha(packed),
              'original_file':str((ROOT/'baseline/2026-10-26-initial/GAMELOGIC/CHALLENGEINSTANCES.BNDL').resolve()),
              'candidate_file':str(packed.resolve()),'event_id':321312,'original_title':'ROADSTERS REBORN',
              'expected_test_title':'DEBUG_TEST_RACE','original_string_id':256688,'test_string_id':572998,
              'payload_field_offset':offset,'file_changed_byte_offsets':diff,
              'appended_zero_bytes':54,'payloads_changed':1,'total_payloads_checked':check['payload_count_before'],
              'save_backup':str((ROOT/'baseline/2026-10-26-userdata/manifest.json').resolve()),
              'commands':commands}
    write_json(out/'comparison.json',check)
    write_json(out/'manifest.json',manifest)
    print(json.dumps({k:manifest[k] for k in ['status','event_id','original_title','expected_test_title','payloads_changed','total_payloads_checked','file_changed_byte_offsets','appended_zero_bytes']},indent=2))


if __name__=='__main__':main()
