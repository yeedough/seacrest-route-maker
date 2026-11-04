"""Read-only, build-specific research catalog. No editing or game deployment.

Offsets are observations of the preserved Steam build, not a general HPR schema.
Imported references are labeled by actual schema names, never guessed as routes.
"""
import hashlib
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
EXTRACTED = ROOT / 'experiments/002-inspection/unpacked'


def u32(data, at):
    return struct.unpack_from('<I', data, at)[0]


def u64(data, at):
    return struct.unpack_from('<Q', data, at)[0]


def filename(resource_id, index=0):
    stem = '_'.join(f'{v:02X}' for v in struct.pack('<I', resource_id))
    return stem + (f'_0_{index}' if index else '') + '.dat'


def language(data):
    count, ids, entries = u32(data, 4), u64(data, 8), u64(data, 16)
    assert ids + count * 4 == entries
    assert entries + count * 16 <= len(data)
    strings = {}
    for i in range(count):
        identity = u32(data, ids + i * 4)
        offset, length = struct.unpack_from('<QQ', data, entries + i * 16)
        assert offset >= entries + count * 16 and offset + length * 2 <= len(data)
        raw = data[offset:offset + length * 2]
        assert raw.endswith(b'\0\0') and identity not in strings
        strings[identity] = {'text': raw.decode('utf-16le').rstrip('\0'),
                             'offset': offset, 'length_utf16_units': length}
    return strings


def main():
    output = ROOT / 'experiments/004-event-catalog'
    output.mkdir(exist_ok=False)
    lang_path = ROOT / 'experiments/003-language/unpacked/0001/Language/FA_3B_D5_D8.dat'
    strings = language(lang_path.read_bytes())
    archive_path = ROOT / 'working/initial/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
    archive = archive_path.read_bytes()
    assert hashlib.sha256(archive).hexdigest() == 'db8e2d3b54dfcfce0cc2b68cc386a5a72ea310e5aac3e957ea5f9592bb247052'
    base = EXTRACTED / 'GAMELOGIC/CHALLENGEINSTANCES'
    instance = (base / 'GenesysInstance/5F_47_06_00.dat').read_bytes()
    # Locate import metadata in the archive entry, rather than guessing from tail markers.
    records = [archive[u32(archive, 20) + i * 80:u32(archive, 20) + (i + 1) * 80]
               for i in range(u32(archive, 16))]
    record, = [r for r in records if u32(r, 0) == 0x6475f]
    import_start = u32(record, 64)
    import_count = struct.unpack_from('<H', record, 72)[0]
    assert import_start + import_count * 16 == len(instance)
    imports = {}
    for i in range(import_count):
        identity, typ, idx, kind, location, reserved = struct.unpack_from('<IHBBII', instance, import_start + 16 * i)
        assert location & 0x80000000
        location &= 0x7fffffff
        assert location not in imports and location < import_start
        imports[location] = {'resource_id': identity, 'type': typ, 'index': idx, 'kind': kind}
    definition_names = {}
    for path in (base / 'GenesysDefinition').glob('*.dat'):
        data = path.read_bytes()
        definition_names[(u32(data, 4), data[20])] = data[21:72].split(b'\0')[0].decode('ascii')
    definitions = []
    xml_dir = output / 'xml'
    xml_dir.mkdir()
    for path in sorted((EXTRACTED / 'GAMELOGIC/CHALLENGEDEFINITIONS/TextFile').glob('*.dat')):
        data = path.read_bytes()
        # Length-prefixed UTF-8 document (BOM and terminating NUL included in length).
        length = u32(data, 0)
        assert 4 + length <= len(data)
        text = data[4:4+length].rstrip(b'\0').decode('utf-8-sig')
        tree = ET.fromstring(text)
        name = tree.attrib['Name']
        xmlpath = xml_dir / (path.stem + '.xml')
        xmlpath.write_text(text, encoding='utf-8')
        identity = int.from_bytes(bytes.fromhex(path.stem.split('_0_')[0].replace('_','')), 'little')
        definitions.append({'resource_id': identity, 'index': 1, 'name': name,
                            'source': str(path.relative_to(ROOT)), 'xml': str(xmlpath.relative_to(ROOT)),
                            'primitive_types': sorted({e.attrib['type'] for e in tree.iter('Primitive')})})
    mode_names = {d['resource_id']: d['name'] for d in definitions}
    array_body = u64(instance, 16)
    array_offset, count, stride = struct.unpack_from('<QII', instance, array_body)
    assert count == 423 and stride == 8
    offsets = [u64(instance, array_offset + i * stride) for i in range(count)]
    assert offsets == sorted(set(offsets)) and offsets[-1] < import_start
    catalog = []
    for slot, offset in enumerate(offsets):
        definition = imports[offset]
        assert definition_names[(definition['resource_id'], definition['index'])] == 'ChallengeInstance'
        body = u64(instance, offset + 16)
        assert body == offset + 48
        # Observed title-ID field: body + 0xf8. All instances resolve to language entries.
        title_id = u32(instance, body + 0xf8)
        title = strings.get(title_id)
        refs = []
        end = offsets[slot + 1] if slot + 1 < count else import_start
        for location, ref in imports.items():
            if offset <= location < end:
                refs.append({'location': location, 'relative_to_object': location-offset, **ref,
                             'schema_name_if_definition': definition_names.get((ref['resource_id'], ref['index']))})
        mode_ref = imports.get(body)
        catalog.append({'slot': slot, 'object_offset': offset, 'body_offset': body,
                        'serialized_object_id': u32(instance, offset + 28),
                        'title_id': title_id, 'title': title['text'] if title else None,
                        'definition_reference': mode_ref,
                        'definition_name': mode_names.get(mode_ref['resource_id']) if mode_ref else None,
                        'imports': refs})
    target = [r for r in catalog if r['title'] == 'SEACREST TOUR']
    assert len(target) == 1
    report = {'language_record_count': len(strings), 'event_object_count': len(catalog),
              'titles_resolved': sum(r['title'] is not None for r in catalog),
              'seacrest_tour': target[0],
              'limits': ['Serialized object IDs have not been verified as network event IDs.',
                         'Definition references indicate stored linkage, not runtime execution.',
                         'Field offsets apply only to this captured build.',
                         'No route/checkpoint schema or menu eligibility established.'],
              'source_payload_sha256': hashlib.sha256(instance).hexdigest(),
              'language_payload_sha256': hashlib.sha256(lang_path.read_bytes()).hexdigest()}
    for name, value in [('language-strings.json',strings), ('definitions.json',definitions),
                        ('events.json',catalog), ('summary.json',report)]:
        (output / name).write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='seacrest_tour'}, indent=2))
    print(json.dumps({k:v for k,v in target[0].items() if k!='imports'}, indent=2))
    print('Definitions:', {d['resource_id']:d['name'] for d in definitions})


if __name__ == '__main__':
    main()
