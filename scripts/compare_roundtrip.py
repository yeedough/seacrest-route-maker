"""Read-only comparison of HPR bnd2 archives and tool-extracted resources.

Container fields follow DGIorio's pinned unpack_bundle_hp implementation.
This checks preservation, not event semantics or runtime compatibility.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def sha(data):
    return hashlib.sha256(data).hexdigest()


def container(path):
    data = path.read_bytes()
    if len(data) < 44 or data[:4] != b'bnd2':
        raise ValueError(f'Not a supported bnd2 archive: {path}')
    values = struct.unpack_from('<10I', data, 4)
    keys = ('version', 'platform', 'debug_offset', 'count', 'table_offset',
            'stream0', 'stream1', 'stream2', 'stream3', 'flags')
    header = dict(zip(keys, values))
    if header['platform'] != 1:
        raise ValueError('Only observed little-endian PC format supported')
    end = header['table_offset'] + header['count'] * 80
    if end > len(data):
        raise ValueError('Resource table exceeds file')
    records = []
    for i in range(header['count']):
        record = data[header['table_offset'] + i * 80:header['table_offset'] + (i + 1) * 80]
        records.append({
            'identity': record[:8].hex(),
            'import_identity': record[8:16].hex(),
            'uncompressed_sizes_alignment': list(struct.unpack_from('<4I', record, 16)),
            'stored_sizes_alignment': list(struct.unpack_from('<4I', record, 32)),
            'disk_offsets': list(struct.unpack_from('<4I', record, 48)),
            'import_offset': struct.unpack_from('<I', record, 64)[0],
            'type': struct.unpack_from('<I', record, 68)[0],
            'import_count': struct.unpack_from('<H', record, 72)[0],
            'flags': record[74], 'stream_index': record[75],
            'tail': record[76:80].hex(),
        })
    return data, header, records


def extracted(path):
    return {str(p.relative_to(path)).replace('\\', '/'): sha(p.read_bytes())
            for p in sorted(path.rglob('*.dat'))}


def compare(original, repacked, before, after):
    a, ah, ar = container(original)
    b, bh, br = container(repacked)
    af, bf = extracted(before), extracted(after)
    metadata_changes = []
    layout_changes = []
    layout_fields = {'disk_offsets', 'stored_sizes_alignment'}
    for i, (x, y) in enumerate(zip(ar, br)):
        for key in x:
            if x[key] != y[key]:
                item = {'entry': i, 'identity': x['identity'], 'field': key,
                        'before': x[key], 'after': y[key]}
                (layout_changes if key in layout_fields else metadata_changes).append(item)
    first_json = json.loads(next(before.glob('IDs_*.json')).read_text())
    second_json = json.loads(next(after.glob('IDs_*.json')).read_text())
    # Root names reflect the archive filename; compare their contents.
    json_equal = list(first_json.values()) == list(second_json.values())
    return {
        'original': str(original.resolve()), 'repacked': str(repacked.resolve()),
        'original_sha256': sha(a), 'repacked_sha256': sha(b),
        'original_bytes': len(a), 'repacked_bytes': len(b), 'byte_identical': a == b,
        'header_before': ah, 'header_after': bh,
        'entry_count_equal': len(ar) == len(br),
        'resource_metadata_changes': metadata_changes,
        'resource_layout_changes': layout_changes,
        'payload_count_before': len(af), 'payload_count_after': len(bf),
        'missing_payloads': sorted(af.keys() - bf.keys()),
        'added_payloads': sorted(bf.keys() - af.keys()),
        'changed_payloads': [k for k in af.keys() & bf.keys() if af[k] != bf[k]],
        'extracted_payloads_identical': bool(af) and af == bf,
        'exported_resource_json_equal': json_equal,
        'different_byte_positions_in_common_prefix': sum(x != y for x, y in zip(a, b)),
        'original_is_exact_prefix': b.startswith(a),
        'appended_bytes': len(b) - len(a) if b.startswith(a) else None,
        'appended_bytes_all_zero': all(v == 0 for v in b[len(a):]) if b.startswith(a) else None,
        'runtime_tested': False,
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('original', 'repacked', 'before', 'after'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    print(json.dumps(compare(args.original, args.repacked, args.before, args.after), indent=2))
