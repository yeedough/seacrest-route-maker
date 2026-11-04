"""Static route/menu analysis for the captured HPR build; never writes game data.

Schema-driven decoding of observed fields, with unknown property names kept as
hex IDs. Location sequences are candidate route anchors, not runtime-validated
checkpoint definitions. Container import metadata supplies reference targets.
"""
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
EXTRACTED = ROOT / 'experiments/002-inspection/unpacked'


def u32(data, at):
    return struct.unpack_from('<I', data, at)[0]


def u64(data, at):
    return struct.unpack_from('<Q', data, at)[0]


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')


def schema(path):
    data = path.read_bytes()
    start, count = u64(data, 8), u32(data, 16)
    if start + count * 20 > len(data):
        raise ValueError(f'Schema table exceeds bounds: {path}')
    fields = []
    for i in range(count):
        key, typ, n, offset, size = struct.unpack_from('<5I', data, start + i * 20)
        fields.append(dict(key=f'{key:08x}', type=typ, count=n, offset=offset, size=size))
    return dict(type_hash=u32(data, 0), resource_id=u32(data, 4), index=data[20],
                name=data[21:72].split(b'\0')[0].decode('ascii'), fields=fields)


def vector(data, offset, expected_stride, limit=None):
    pointer, count, stride = struct.unpack_from('<QII', data, offset)
    if stride != expected_stride or pointer + count * stride > (limit or len(data)):
        raise ValueError(f'Invalid vector at {offset:#x}: {(pointer, count, stride)}')
    return pointer, count


def event_route(data, event):
    refs = {r['location']: r for r in event['imports']}
    arrays = {}
    for relative, expected_name, words in [(0xd8, 'Location', 11), (0x40, 'RestrictedRoadSections', 3)]:
        pointer, count = vector(data, event['body_offset'] + relative, 8)
        values = []
        for i in range(count):
            obj = u64(data, pointer + i * 8)
            if refs[obj]['schema_name_if_definition'] != expected_name:
                raise ValueError('Unexpected nested type')
            body = u64(data, obj + 16)
            # RestrictedRoadSections has a one-byte boolean, followed by padding.
            value = list(struct.unpack_from('<' + 'I' * words, data, body))
            if expected_name == 'RestrictedRoadSections':
                if data[body+8] not in (0, 1):
                    raise ValueError('Unexpected boolean')
                value[-1] = bool(data[body+8])
            values.append({'object_offset': obj, 'body_offset': body, 'values': value})
        arrays[expected_name] = values
    return {'event_id': event['serialized_object_id'], 'title': event['title'],
            'definition': event['definition_name'], 'body_offset': event['body_offset'], **arrays}


def trigger_records(path):
    data = path.read_bytes()
    # Observed header contains count/offset pairs for two fixed-record tables.
    gate_start, gate_count = u64(data, 8), u32(data, 0x10)
    point_start = u64(data, 0x28)
    point_count = u32(data, 0x30)
    following = u64(data, 0x38)
    if gate_start + gate_count * 48 != point_start or point_start + point_count * 32 != following:
        raise ValueError('Trigger record table boundaries do not match observed format')
    result = {}
    for kind, start, count, stride in [('shape', gate_start, gate_count, 48), ('point', point_start, point_count, 32)]:
        for i in range(count):
            offset = start + i * stride
            x, y, z, identity = struct.unpack_from('<fffI', data, offset)
            if not all(math.isfinite(v) for v in (x,y,z)):
                raise ValueError('Invalid trigger coordinates')
            # Some stock IDs have multiple records. Retain all candidates.
            result.setdefault(identity, []).append({'kind': kind, 'offset': offset, 'xyz': [x,y,z],
                                'record_hex': data[offset:offset+stride].hex()})
    return result


def resource_filename(record):
    identity, typ, index, kind = struct.unpack_from('<IHBB', record)
    name = '_'.join(f'{v:02X}' for v in struct.pack('<I', identity))
    if typ or index:
        name += f'_{typ}'
        if index:
            name += f'_{index}'
    return name + '.dat'


def gameplay_objects():
    base = EXTRACTED / 'GAMELOGIC/GAMEPLAY'
    schemas = [schema(p) for p in (base/'GenesysDefinition').glob('*.dat')]
    by_type = {s['type_hash']: s for s in schemas}
    archive = (ROOT/'working/initial/GAMELOGIC/GAMEPLAY.BNDL').read_bytes()
    result = []
    for i in range(u32(archive, 16)):
        entry = archive[u32(archive,20)+i*80:u32(archive,20)+(i+1)*80]
        if u32(entry,68) != 0x13:
            continue
        path = base/'GenesysInstance'/resource_filename(entry)
        data = path.read_bytes()
        import_start, import_count = u32(entry,64), struct.unpack_from('<H',entry,72)[0]
        if import_start + import_count * 16 > len(data):
            raise ValueError('Import table exceeds payload bounds')
        imports = {}
        for j in range(import_count):
            identity, typ, index, kind, location, reserved = struct.unpack_from('<IHBBII',data,import_start+j*16)
            imports[location & 0x7fffffff] = {'resource_id':identity, 'type':typ, 'index':index, 'kind':kind}
        locations = [0]
        if by_type[u32(data,24)]['name'] == 'InstanceList':
            ptr,n = vector(data,u64(data,16),8,import_start)
            locations.extend(u64(data,ptr+j*8) for j in range(n))
        for obj in locations:
            sc = by_type[u32(data,obj+24)]
            body = u64(data,obj+16)
            fields = {}
            for f in sc['fields']:
                at=body+f['offset'];typ=f['type'];n=f['count'];size=f['size']
                if at+n*size>len(data):
                    raise ValueError('Field exceeds payload')
                value={'offset':at, 'relative_offset':f['offset'], 'type':typ}
                if typ in (0x1007,0x1006):
                    stride=4 if typ==0x1007 else 16
                    ptr,c=vector(data,at,stride,import_start)
                    value['values']=[u32(data,ptr+k*4) for k in range(c)] if stride==4 else [imports.get(ptr+k*16) for k in range(c)]
                elif typ==6:
                    value['references']=[imports.get(at+k*16) for k in range(n)]
                elif typ in (0,7,9):
                    value['values']=list(struct.unpack_from('<'+'I'*n,data,at))
                elif typ==1:
                    value['values']=list(struct.unpack_from('<'+'f'*n,data,at))
                else:
                    value['raw_hex']=data[at:at+n*size].hex()
                fields[f['key']]=value
            result.append({'id':u32(data,obj+28),'class':sc['name'],'object_offset':obj,
                           'file':str(path.relative_to(ROOT)),'fields':fields})
    return result


def main():
    out=ROOT/'experiments/005-route-analysis'
    if out.exists() and any(out.iterdir()):
        raise ValueError('Output directory already contains results')
    out.mkdir(exist_ok=True)
    data=(EXTRACTED/'GAMELOGIC/CHALLENGEINSTANCES/GenesysInstance/5F_47_06_00.dat').read_bytes()
    if hashlib.sha256(data).hexdigest()!='828b3fa32d772cc7f204d5d566946e626f4f949d75d3af831e9bc935e13dfe9f':
        raise ValueError('Captured event payload changed')
    events=read_json(ROOT/'experiments/004-event-catalog/events.json')
    lang=read_json(ROOT/'experiments/004-event-catalog/language-strings.json')
    routes=[event_route(data,e) for e in events]
    triggers=trigger_records(next((EXTRACTED/'TRIGGERS/TriggerData').glob('*.dat')))
    unresolved=[]
    for r in routes:
        for location in r['Location']:
            ids=location['values']
            # Last value is the schema's literal four-character Road field.
            location['resolved_triggers']=[{'id':v,'candidates':triggers[v]} if v in triggers else None for v in ids[:10]]
            unresolved.extend({'event_id':r['event_id'],'id':v} for v in ids[:10] if v and v not in triggers)
    pairs=[]
    for a in routes:
        if a['definition']!='RaceDefinition':continue
        for b in routes:
            if b['definition']!='OnlineRaceDefinition' or b['title']!=a['title']:continue
            pairs.append({'title':a['title'],'career_id':a['event_id'],'online_id':b['event_id'],
                          'location_records_equal':[x['values'] for x in a['Location']]==[x['values'] for x in b['Location']],
                          'restricted_road_records_equal':[x['values'] for x in a['RestrictedRoadSections']]==[x['values'] for x in b['RestrictedRoadSections']]})
    objects=gameplay_objects()
    byid={e['serialized_object_id']:e for e in events}
    playlists=[];eventlists=[];locations=[]
    for obj in objects:
        fields=obj['fields']
        if obj['class']=='Playlist':
            ids=fields['b66f339d']['values'];nameid=fields['4e616d65']['values'][0]
            playlists.append({'id':obj['id'],'name':lang.get(str(nameid),{}).get('text'),
                              'event_ids':ids,'mode_counts':dict(Counter(byid.get(v,{}).get('definition_name','unresolved') for v in ids)),
                              'file':obj['file'],'object_offset':obj['object_offset']})
        if obj['class']=='EventList':
            ids=fields['a1d523c0']['values']
            eventlists.append({'id':obj['id'],'event_ids':ids,'contains_tour':321311 in ids,
                               'mode_counts':dict(Counter(byid.get(v,{}).get('definition_name','list_or_unresolved') for v in ids)),'file':obj['file']})
        if obj['class']=='EventLocation':
            nameid=fields['4e616d65']['values'][0]
            locations.append({'id':obj['id'],'name':lang.get(str(nameid),{}).get('text'),'fields':fields,'file':obj['file']})
    tour=next(r for r in routes if r['event_id']==321311)
    summary={'route_records':len(routes),'trigger_unique_ids':len(triggers),
             'trigger_records':sum(len(v) for v in triggers.values()),
             'ambiguous_trigger_ids':[k for k,v in triggers.items() if len(v)>1],
             'unresolved_nonzero_trigger_refs':len(unresolved),
             'career_online_pairs':len(pairs),'pairs_with_equal_locations':sum(p['location_records_equal'] for p in pairs),
             'pairs_with_equal_restrictions':sum(p['restricted_road_records_equal'] for p in pairs),
             'seacrest_location_count':len(tour['Location']),
             'online_race_location_counts':dict(Counter(len(r['Location']) for r in routes if r['definition']=='OnlineRaceDefinition')),
             'playlists':[{'id':p['id'],'name':p['name'],'count':len(p['event_ids']),'contains_tour':321311 in p['event_ids']} for p in playlists],
             'runtime_tested':False}
    for name,value in [('routes.json',routes),('triggers.json',triggers),('unresolved-triggers.json',unresolved),
                       ('career-online-pairs.json',pairs),('gameplay-objects.json',objects),('playlists.json',playlists),
                       ('event-lists.json',eventlists),('event-locations.json',locations),('seacrest-tour.json',tour),('summary.json',summary)]:
        write_json(out/name,value)
    print(json.dumps(summary,indent=2))


if __name__=='__main__':main()
