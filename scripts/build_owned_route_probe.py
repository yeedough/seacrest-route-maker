"""Build six-location career test with distinct nested objects and checked imports.

Reuse the target's first two Location objects; allocate four more and a new
six-entry pointer array before the import table. Existing object offsets stay
fixed. Only target scalar fields and its Location vector are edited in place.
"""
import copy
import argparse
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys

from analyze_routes import ROOT, EXTRACTED, read_json, write_json, schema, event_route, u32, u64
from build_name_probe import sha
from compare_roundtrip import container, compare


def require(condition, message):
    if not condition:
        raise ValueError(message)


def main(multiplayer=False):
    target_id = 744283 if multiplayer else 321312
    target_mode = 'OnlineRaceDefinition' if multiplayer else 'RaceDefinition'
    out = ROOT/('experiments/012-multiplayer-owned-route' if multiplayer else 'experiments/010-offline-owned-route')
    require(not out.exists(), 'Refusing to overwrite an existing experiment')
    base = EXTRACTED/'GAMELOGIC/CHALLENGEINSTANCES'
    resource = Path('GenesysInstance/5F_47_06_00.dat')
    source = ROOT/'working/initial/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
    tool = ROOT/'tools/bundle_packer_unpacker/bundle_packer_unpacker.py'
    for path, expected in [
        (source, 'db8e2d3b54dfcfce0cc2b68cc386a5a72ea310e5aac3e957ea5f9592bb247052'),
        (base/resource, '828b3fa32d772cc7f204d5d566946e626f4f949d75d3af831e9bc935e13dfe9f'),
        (tool, '68f369ce5630212fa796e424d26e074f83dd359b6067f0e6f57c183de3bd4635'),
    ]:
        require(sha(path) == expected, f'Unexpected input: {path}')
    original = (base/resource).read_bytes()
    _, _, records = container(source)
    record_index, = [i for i, r in enumerate(records) if r['identity'].startswith('5f470600')]
    record = records[record_index]
    old_start, old_count = record['import_offset'], record['import_count']
    require(old_start + old_count*16 == len(original), 'Import table boundary mismatch')
    import_bytes = [original[old_start+i*16:old_start+(i+1)*16] for i in range(old_count)]
    imports = {u32(b, 8) & 0x7fffffff: b for b in import_bytes}
    require(len(imports) == old_count and u32(import_bytes[0], 8) == 0x80000000,
            'Unexpected root/duplicate imports')
    events = read_json(ROOT/'experiments/004-event-catalog/events.json')
    donor, = [e for e in events if e['serialized_object_id'] == 321311]
    target, = [e for e in events if e['serialized_object_id'] == target_id]
    require(donor['title'] == 'SEACREST TOUR' and target['title'] == 'ROADSTERS REBORN', 'Wrong events')
    require(donor['definition_name'] == 'RaceDefinition' and target['definition_name'] == target_mode, 'Wrong event modes')
    donor_route, target_route = event_route(original, donor), event_route(original, target)
    require(len(donor_route['Location']) == 6 and len(target_route['Location']) == 2, 'Wrong location counts')
    require(len(donor_route['RestrictedRoadSections']) == len(target_route['RestrictedRoadSections']) == 2,
            'Wrong restriction counts')
    schemas = [schema(p) for p in (base/'GenesysDefinition').glob('*.dat')]
    loc_schema, = [s for s in schemas if s['name'] == 'Location']
    res_schema, = [s for s in schemas if s['name'] == 'RestrictedRoadSections']
    require([(f['type'], f['count'], f['offset'], f['size']) for f in loc_schema['fields']] ==
            [(7, 1, i*4, 4) for i in range(11)], 'Location schema mismatch')
    require([(f['type'], f['count'], f['offset'], f['size']) for f in res_schema['fields']] ==
            [(7, 1, 0, 4), (9, 1, 4, 4), (2, 1, 8, 1)], 'Restriction schema mismatch')
    edited = bytearray(original[:old_start])
    allowed = set()
    # Keep all old objects fixed. Appended storage is aligned like stock objects.
    require(old_start % 16 == 0, 'Unaligned old data end')
    array_at = len(edited)
    edited.extend(bytes(48))
    location_objects = [v['object_offset'] for v in target_route['Location']]
    added_imports = []
    for item in donor_route['Location'][2:]:
        src, body = item['object_offset'], item['body_offset']
        require(body == src+48 and original[src+92:src+96] == bytes(4), 'Unexpected Location layout')
        require(original[src:src+16] == bytes(16) and original[src+28:src+48] == bytes(20),
                'Unexpected nonzero nested header fields')
        require(u32(original, src+24) == loc_schema['type_hash'], 'Wrong nested type')
        at = len(edited)
        clone = bytearray(original[src:src+96])
        struct.pack_into('<Q', clone, 16, at+48)
        edited.extend(clone)
        location_objects.append(at)
        imp = bytearray(imports[src])
        require(u32(imp, 0) == loc_schema['resource_id'], 'Wrong schema import')
        struct.pack_into('<I', imp, 8, at | 0x80000000)
        added_imports.append(bytes(imp))
    for i, obj in enumerate(location_objects):
        struct.pack_into('<Q', edited, array_at+i*8, obj)
    for kind, sc, n in [('Location', loc_schema, 2), ('RestrictedRoadSections', res_schema, 2)]:
        for src, dst in zip(donor_route[kind][:n], target_route[kind]):
            for f in sc['fields']:
                a, b, size = src['body_offset']+f['offset'], dst['body_offset']+f['offset'], f['size']
                edited[b:b+size] = original[a:a+size]
                allowed.update(range(b, b+size))
    field_at = target['body_offset']+0xd8
    require(struct.unpack_from('<QII', original, field_at)[1:] == (2, 8), 'Wrong target vector')
    struct.pack_into('<QII', edited, field_at, array_at, 6, 8)
    allowed.update(range(field_at, field_at+16))
    new_start = len(edited)
    edited.extend(b''.join(import_bytes+added_imports))
    require(new_start == old_start+432 and len(edited) == len(original)+496, 'Unexpected growth')
    changed_old = [i for i in range(old_start) if original[i] != edited[i]]
    require(set(changed_old) <= allowed, 'Unexpected original-data edits')
    require(edited[new_start:new_start+old_count*16] == original[old_start:], 'Original imports changed')

    # Global import lookup is required: the added objects are beyond the old
    # event block, so assigning imports by adjacent event offsets is invalid.
    after_events = copy.deepcopy(events)
    new_refs = []
    for imp in added_imports:
        identity, typ, index, kind, loc, reserved = struct.unpack('<IHBBII', imp)
        require(loc & 0x80000000 and reserved == 0, 'Unexpected added import')
        new_refs.append(dict(location=loc & 0x7fffffff, resource_id=identity,
                             type=typ, index=index, kind=kind, schema_name_if_definition='Location'))
    next(e for e in after_events if e['serialized_object_id'] == target_id)['imports'].extend(new_refs)
    before_routes = [event_route(original, e) for e in events]
    after_routes = [event_route(edited, e) for e in after_events]
    require([a['event_id'] for a, b in zip(before_routes, after_routes) if a != b] == [target_id],
            'Unexpected changed event')
    new_target, = [r for r in after_routes if r['event_id'] == target_id]
    for kind in ('Location', 'RestrictedRoadSections'):
        require([x['values'] for x in new_target[kind]] == [x['values'] for x in donor_route[kind]],
                'Target does not match donor route values')
        addresses = [v['object_offset'] for r in after_routes for v in r[kind]]
        require(len(addresses) == len(set(addresses)), 'Shared nested objects introduced')
    for obj in location_objects:
        require(obj % 16 == 0 and u64(edited, obj+16) == obj+48 and obj+96 <= new_start,
                'Invalid nested object bounds/alignment/body pointer')
    require(len({u32(imp, 8) for imp in import_bytes+added_imports}) == old_count+4, 'Duplicate import location')

    work = out/'resources'
    shutil.copytree(base, work)
    (work/resource).write_bytes(edited)
    def run(args, log):
        result = subprocess.run([sys.executable, str(tool), *map(str, args)],
                                capture_output=True, text=True, timeout=120)
        output = result.stdout+result.stderr
        (out/log).write_text(output, encoding='utf-8')
        require(not result.returncode and not any(w in output.lower() for w in ('error','warning','traceback')),
                f'Packer failure: {log}')
    packed = out/'candidate/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
    run(['-p', 'HPR', work/'IDs_CHALLENGEINSTANCES.BNDL', packed.parent, packed.name], 'pack.log')
    again = out/'reextracted'
    run(['-u', 'HPR', packed, again], 'unpack.log')
    check = compare(source, packed, base, again)
    require(check['changed_payloads'] == [resource.as_posix()] and not check['missing_payloads']
            and not check['added_payloads'] and check['entry_count_equal'], 'Unexpected resource changes')
    require((again/resource).read_bytes() == edited, 'Repacked payload mismatch')
    _, _, new_records = container(packed)
    require(len(records) == len(new_records), 'Resource count mismatch')
    for i, (before, after) in enumerate(zip(records, new_records)):
        expected = copy.deepcopy(before)
        expected['disk_offsets'] = after['disk_offsets']  # packing shifts later resources
        if i == record_index:
            expected['import_offset'] = new_start
            expected['import_count'] = old_count+4
            expected['uncompressed_sizes_alignment'][0] += 496
            expected['stored_sizes_alignment'][0] += 496
        require(after == expected, f'Unexpected resource metadata: {i}')
    write_json(out/'comparison.json', check)
    write_json(out/'decoded-target.json', new_target)
    manifest = dict(status='built_not_deployed_not_runtime_tested',
                    purpose=('Private multiplayer candidate; NOT installed or runtime validated' if multiplayer else
                             'Offline Seacrest Tour route with separately owned Location objects'),
                    original_sha256=sha(source), candidate_sha256=sha(packed),
                    target_event_id=target_id, target_definition=target_mode, donor_event_id=321311, expected_title='ROADSTERS REBORN',
                    location_count=6, location_object_offsets=location_objects,
                    new_array_offset=array_at, additional_objects=4, additional_imports=4,
                    original_import_offset=old_start, new_import_offset=new_start,
                    original_import_count=old_count, new_import_count=old_count+4,
                    payload_growth=496, changed_original_payload_byte_offsets=changed_old,
                    changed_event_ids=[target_id], payloads_changed=1, total_payloads_checked=232,
                    candidate_file=str(packed.resolve()),
                    limitations=['Appended nested-object loading is not runtime verified.',
                                 'Route completion and multiplayer behavior remain untested.'])
    write_json(out/'manifest.json', manifest)
    print(json.dumps({k: manifest[k] for k in ('status','location_count','additional_objects',
                                             'additional_imports','payload_growth','payloads_changed')}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--multiplayer', action='store_true', help='Build separate multiplayer candidate; never install it')
    main(parser.parse_args().multiplayer)
