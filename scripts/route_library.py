"""Local saved-course index. Reads existing exports; saves new immutable recipes only."""
from datetime import datetime, timezone
from pathlib import Path
import re
import uuid

from analyze_routes import ROOT, read_json, write_json
from build_name_probe import sha
from route_map import catalog
from route_maker import PRESET, canonical_recipe, recipe_hash, validate_recipe, OFFLINE_SUCCESS, ONLINE_SUCCESS


def deletion_marker(root, key):
    return root / 'working/route-library/removed' / key


def delete_recipe(raw, root=ROOT):
    key = recipe_hash(canonical_recipe(raw))
    marker = deletion_marker(root, key)
    marker.parent.mkdir(parents=True, exist_ok=True)
    marker.touch()
    return dict(recipe_hash=key)


def save_recipe(raw, root=ROOT):
    recipe, _ = validate_recipe(raw)
    folder = root / 'working/route-library'
    folder.mkdir(parents=True, exist_ok=True)
    slug = re.sub(r'[^a-zA-Z0-9_-]+', '-', recipe['name']).strip('-')[:45] or 'route'
    path = folder / (datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S-%f') + '-' + slug + '-' + uuid.uuid4().hex[:8] + '.json')
    # Exclusive creation: saving an edit never replaces an earlier revision.
    import json
    with path.open('x', encoding='utf-8') as f:
        json.dump(recipe, f, ensure_ascii=False, indent=2)
    deletion_marker(root, recipe_hash(recipe)).unlink(missing_ok=True)
    return dict(path=str(path), recipe_hash=recipe_hash(recipe), name=recipe['name'])


def index(root=ROOT):
    data = catalog()
    entries = {}
    issues = []

    def issue(path, exc):
        issues.append(dict(path=str(path), message=str(exc)))

    def add(raw, source, priority=0):
        recipe, preview = validate_recipe(raw, data)
        key = recipe_hash(recipe)
        if key not in entries:
            entries[key] = dict(id=key, name=recipe['name'], recipe=recipe, preview=preview,
                                aliases=[], sources=[], variants=[], offline_build=None, priority=-1)
        entry = entries[key]
        if recipe['name'] not in entry['aliases']:
            entry['aliases'].append(recipe['name'])
        if priority >= entry['priority']:
            entry.update(name=recipe['name'], recipe=recipe, priority=priority)
        entry['sources'].append(str(source))
        return entry

    preset = add(PRESET, 'Built-in Grand Ocean Sprint preset')
    for path in sorted((root / 'working/route-library').glob('*.json')):
        try:
            add(read_json(path), path, 2)
        except (ValueError, KeyError, TypeError, OSError) as exc:
            issue(path, exc)
    # Legacy Save recipe used the working folder; do not index transient session drafts.
    for path in sorted((root / 'working').glob('*.json')):
        try:
            raw = read_json(path)
            if isinstance(raw, dict) and 'locations' in raw and 'restriction_source' in raw:
                add(raw, path, 1)
        except (ValueError, KeyError, TypeError, OSError) as exc:
            if path.name.endswith('.recipe.json'):
                issue(path, exc)

    # Existing report is testimony, matched to exact recipe and candidate identity.
    reported = set()
    report_path = root / 'notes/user-reports/2026-11-04/report.json'
    if report_path.exists():
        try:
            report = read_json(report_path)
            if report.get('completion_status') == 'user_reported_offline_and_online_completion_without_issues_on_both_pcs':
                named = report.get('online_build_explicitly_named_by_user')
                for b in report['builds']:
                    if b.get('build') == named and b.get('mode') == 'online':
                        reported.add((b['recipe_hash'], b['candidate_sha256']))
        except (ValueError, KeyError, TypeError, OSError) as exc:
            issue(report_path, exc)

    for folder in sorted((root / 'working/route-maker-builds').glob('*')):
        if not folder.is_dir():
            continue
        try:
            entry = add(read_json(folder / 'recipe.json'), folder / 'recipe.json')
        except (ValueError, KeyError, TypeError, OSError) as exc:
            issue(folder, exc)
            continue
        variant = dict(path=str(folder), mode='unknown', status='Unavailable', valid=False,
                       candidate_sha256='', tested=False, source='Export', problem='')
        entry['variants'].append(variant)
        try:
            manifest = read_json(folder / 'manifest.json')
            mode = manifest['mode']
            if mode not in ('offline', 'online') or manifest['recipe_hash'] != entry['id']:
                raise ValueError('Manifest does not match the recipe/mode')
            variant.update(mode=mode, candidate_sha256=manifest['candidate_sha256'])
            candidate = folder / 'candidate/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
            if sha(candidate) != manifest['candidate_sha256']:
                raise ValueError('Candidate hash differs from its manifest')
            variant.update(valid=True, status='Built; no completion recorded')
            proof_path = folder / 'offline-success.json'
            if mode == 'offline' and proof_path.exists():
                proof = read_json(proof_path)
                if (proof.get('recipe_hash') != entry['id'] or
                        proof.get('candidate_sha256') != variant['candidate_sha256'] or
                        proof.get('result') != 'user_reported_completed_without_issues'):
                    raise ValueError('Offline completion record does not match this build')
                variant.update(tested=True, status='User-reported complete')
            if (entry['id'], variant['candidate_sha256']) in reported:
                variant.update(tested=True, status='User-reported complete')
        except (ValueError, KeyError, TypeError, OSError) as exc:
            variant.update(valid=False, status='Unavailable: ' + str(exc), problem=str(exc))
            issue(folder, exc)

    # References are displayed separately from maker exports and never passed as offline-build.
    for mode, name, expected in [('offline', '014-custom-grand-ocean-sprint', OFFLINE_SUCCESS),
                                  ('online', '015-custom-grand-ocean-multiplayer', ONLINE_SUCCESS)]:
        folder = root / 'experiments' / name
        if not folder.exists():
            continue
        try:
            if sha(folder / 'candidate/GAMELOGIC/CHALLENGEINSTANCES.BNDL') != expected:
                raise ValueError('Reference candidate hash differs')
            proof = read_json(folder / 'runtime-result.json')
            tested = (proof.get('result') in ('user_reported_completed_without_issues',
                                              'user_reported_two_pc_race_completed_without_issues')
                      and proof.get('candidate_sha256', expected) == expected)
            preset['variants'].append(dict(path=str(folder), mode=mode, candidate_sha256=expected,
                status='User-reported complete' if tested else 'Reference; inspect report',
                valid=True, tested=tested, source='Reference experiment', problem=''))
        except (ValueError, KeyError, TypeError, OSError) as exc:
            issue(folder, exc)

    for entry in entries.values():
        candidates = [v for v in entry['variants'] if v['valid'] and v['mode'] == 'offline' and v['source'] == 'Export']
        if candidates:
            entry['offline_build'] = max(candidates, key=lambda v: (v['tested'], v['path']))['path']
        summaries = []
        for mode in ('offline', 'online'):
            variants = [v for v in entry['variants'] if v['mode'] == mode]
            label = ('reported complete' if any(v['valid'] and v['tested'] for v in variants) else
                     'built' if any(v['valid'] for v in variants) else
                     'unavailable' if variants else 'not built')
            summaries.append(mode.capitalize() + ': ' + label)
        entry['status'] = ' | '.join(summaries)
        entry['marker_count'] = len(entry['recipe']['locations'])
        entry['estimated_length'] = entry['preview']['sample_length']
        entry.pop('priority')
    return dict(entries=sorted((e for e in entries.values() if not deletion_marker(root,e['id']).exists()), key=lambda e: (e['name'].casefold(), e['id'])),
                issues=issues, length_units='world units (estimate)', game_writes=False)
