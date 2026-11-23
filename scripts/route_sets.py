"""Compose independent stock-event replacements from the pinned original archive.

No live game writes. Installed state is keyed by the observed archive SHA-256,
not a mutable 'current selection' file. Historical manifests remain recovery data.
"""
import copy
import re
from functools import lru_cache
from analyze_routes import ROOT, EXTRACTED, read_json, event_route
from build_name_probe import sha
from build_owned_route_probe import require
from route_map import RESOURCE, PAYLOAD_HASH, catalog


@lru_cache(maxsize=1)
def targets():
    payload=EXTRACTED/'GAMELOGIC/CHALLENGEINSTANCES'/RESOURCE
    require(sha(payload)==PAYLOAD_HASH,'Stock event payload changed')
    data=payload.read_bytes()
    result=[]
    for e in read_json(ROOT/'experiments/004-event-catalog/events.json'):
        mode={'RaceDefinition':'offline','OnlineRaceDefinition':'online','OnlineArmsRaceDefinition':'online-arms-race'}.get(e['definition_name'])
        if not mode or not e['title'] or 'TEST' in e['title'].upper():continue
        r=event_route(data,e)
        if len(r['Location'])>=2 and len(r['RestrictedRoadSections'])==2:
            result.append(dict(event_id=r['event_id'],title=e['title'],mode=mode))
    return sorted(result,key=lambda t:(t['mode'],t['title'],t['event_id']))


def normalize(entries):
    from route_maker import validate_recipe, recipe_hash
    supported={t['event_id']:t for t in targets()}
    result=[]
    for entry in entries:
        tid=entry['target_event_id'];require(type(tid) is int and tid in supported,'Unsupported target race')
        recipe,_=validate_recipe(entry['recipe'])
        if 'recipe_hash' in entry:require(entry['recipe_hash']==recipe_hash(recipe),'Replacement recipe identity changed')
        t=supported[tid]
        require(entry.get('mode',t['mode'])==t['mode'],'Replacement mode changed')
        result.append(dict(target_event_id=tid,stock_title=t['title'],mode=t['mode'],recipe=recipe,recipe_hash=recipe_hash(recipe)))
    require(len({e['target_event_id'] for e in result})==len(result),'Duplicate replacement targets')
    return sorted(result,key=lambda e:e['target_event_id'])


def installed(digest):
    from route_maker import ORIGINAL_HASH, OUTPUT_ROOT, PRESET, OFFLINE_SUCCESS, ONLINE_SUCCESS, emit_delta
    require(isinstance(digest,str) and re.fullmatch('[0-9a-f]{64}',digest),'Invalid installed archive hash')
    if digest==ORIGINAL_HASH:return []
    matches=[]
    for path in sorted(OUTPUT_ROOT.glob('*/manifest.json')):
        try:
            m=read_json(path)
            if m.get('candidate_sha256')!=digest:continue
            folder=path.parent;candidate=folder/'candidate/GAMELOGIC/CHALLENGEINSTANCES.BNDL'
            require(m['original_sha256']==ORIGINAL_HASH and sha(candidate)==digest,'Installed build evidence changed')
            original=(ROOT/'working/initial/GAMELOGIC/CHALLENGEINSTANCES.BNDL').read_bytes()
            require(sha(ROOT/'working/initial/GAMELOGIC/CHALLENGEINSTANCES.BNDL')==ORIGINAL_HASH,'Original changed')
            patch=read_json(folder/'package/patch.json')
            require(patch['original_sha256']==ORIGINAL_HASH and patch['candidate_sha256']==digest and
                    patch['operations']==emit_delta(original,candidate.read_bytes()),'Installed build patch changed')
            entries=m['replacements'] if m.get('version')==2 else [dict(target_event_id=m['target_event_id'],
                mode=m['mode'],recipe=read_json(folder/'recipe.json'),recipe_hash=m['recipe_hash'])]
            matches.append(normalize(entries))
        except (OSError,KeyError,ValueError,TypeError):
            continue
    if not matches:
        legacy=_legacy(digest)
        if legacy is not None:return legacy
    require(matches,'No intact replacement manifest for this archive. Restore stock before adding routes; existing backups are retained.')
    # Same bytes may have different custom names, but must describe identical route assignments.
    identity=lambda rows:[(e['target_event_id'],e['recipe_hash']) for e in rows]
    require(all(identity(x)==identity(matches[0]) for x in matches),'Conflicting replacement manifests')
    return copy.deepcopy(matches[-1])


def prepare(before,recipe=None,target_id=None,remove_target=None):
    from route_maker import build, ORIGINAL_HASH
    entries=installed(before)
    require((recipe is not None and target_id is not None and remove_target is None) or
            (recipe is None and target_id is None and remove_target is not None),'Choose install or individual restore')
    if remove_target is not None:
        require(any(e['target_event_id']==remove_target for e in entries),'Selected replacement is no longer installed')
        entries=[e for e in entries if e['target_event_id']!=remove_target]
    else:
        entries=[e for e in entries if e['target_event_id']!=target_id]+[dict(target_event_id=target_id,recipe=recipe)]
    entries=normalize(entries)
    if not entries:return dict(build_dir=None,candidate_sha256=ORIGINAL_HASH,replacements=[],mode='stock')
    return build(entries[0]['recipe'],entries[0]['mode'],target_id=entries[0]['target_event_id'],
                 replacements=entries,installed_before=before)


def _legacy(digest):
    from route_maker import PRESET, OFFLINE_SUCCESS, ONLINE_SUCCESS
    # Legacy reference archives have immutable identities; reconstruct their recipes.
    if digest in (OFFLINE_SUCCESS,ONLINE_SUCCESS):
        return normalize([dict(target_event_id=321312 if digest==OFFLINE_SUCCESS else 744283,recipe=PRESET)])
    seacrest=['e2dc242d6071360ff75675f4adb9203051625634c9f3f88945fdee2456863b75',
              '42d5c88f1893f048ccf75bba803f1adc5286e301d54978b742a03b8bb44f2c71']
    if digest in seacrest:
        payload=(EXTRACTED/'GAMELOGIC/CHALLENGEINSTANCES'/RESOURCE).read_bytes()
        events=read_json(ROOT/'experiments/004-event-catalog/events.json')
        donor=event_route(payload,next(e for e in events if e['serialized_object_id']==321311))
        markers=catalog()['markers'];keys=[]
        for i,loc in enumerate(donor['Location']):
            role='start' if i==0 else 'finish' if i==len(donor['Location'])-1 else 'waypoint'
            candidates=[m for m in markers if m['role']==role and m['values']==loc['values']]
            require(candidates,'Legacy marker is unavailable');keys.append(candidates[0]['key'])
        return normalize([dict(target_event_id=321312 if digest==seacrest[0] else 744283,
            recipe=dict(version=1,name='Seacrest Tour',locations=keys,restriction_source=321311))])
    return None
