"""Decode the captured road polylines and junction membership for a local picker.

The graph is undirected. It is a connectivity/visualization model, not the game's
direction-, restriction-, or spline-aware race pathfinder.
"""
import hashlib
import heapq
import itertools
import math
import struct
from collections import Counter, defaultdict

from analyze_routes import ROOT, EXTRACTED, read_json, event_route, trigger_records
from build_owned_route_probe import require

ROAD_HASH = 'ecd307d592a909dcddd08668a6d87933ea9165ea9a505774120006f2ded9d779'
PAYLOAD_HASH = '828b3fa32d772cc7f204d5d566946e626f4f949d75d3af831e9bc935e13dfe9f'
RESOURCE = 'GenesysInstance/5F_47_06_00.dat'


def distance(a, b):
    return math.dist(a, b)


def load_map():
    path = EXTRACTED/'GAMELOGIC/ROADDATA/RoadData/85_54_04_D5.dat'
    data = path.read_bytes()
    require(hashlib.sha256(data).hexdigest() == ROAD_HASH, 'Captured RoadData changed')
    version, zero, road_at, junction_at, road_count, junction_count = struct.unpack_from('<IIQQII', data)
    require((version,zero,road_at,road_count,junction_count) == (9,0,128,115,225), 'Road header changed')
    require(road_at+road_count*64 <= junction_at, 'Road table bounds')
    require(data[road_at+road_count*64:junction_at] == bytes(junction_at-road_at-road_count*64), 'Road padding changed')
    roads, sections = [], []
    for i in range(road_count):
        at = road_at+i*64
        bbox = list(struct.unpack_from('<4f',data,at))
        identity, reverse_identity, end_a, end_b, pointer, count, flags = struct.unpack_from('<4IQHH',data,at+32)
        require(pointer+count*40 <= len(data), 'Section table bounds')
        road = dict(id=identity,reverse_id=reverse_identity,bbox=bbox,flags=flags,sections=[])
        for j in range(count):
            pos = pointer+j*40
            bounds = list(struct.unpack_from('<4f',data,pos))
            sid, reserved, points_at, point_count, unknown = struct.unpack_from('<IIQIf',data,pos+16)
            require(reserved == 0 and point_count >= 2 and points_at+point_count*32 <= len(data), 'Section header/bounds')
            points = [list(struct.unpack_from('<3f',data,points_at+k*32)) for k in range(point_count)]
            require(all(all(math.isfinite(v) for v in p) for p in points), 'Non-finite road point')
            require(all(bounds[0]-.1<=p[0]<=bounds[2]+.1 and bounds[1]-.1<=p[2]<=bounds[3]+.1 for p in points), 'Section bounds do not enclose points')
            require(all(bbox[0]-.1<=p[0]<=bbox[2]+.1 and bbox[1]-.1<=p[2]<=bbox[3]+.1 for p in points), 'Road bounds do not enclose points')
            sections.append(dict(id=sid,road_id=identity,points=points,bbox=bounds,
                                 sample_length=sum(distance(a,b) for a,b in zip(points,points[1:]))))
            road['sections'].append(sid)
        roads.append(road)
    require(len({s['id'] for s in sections}) == len(sections), 'Duplicate sections')
    by_id = {s['id']:s for s in sections}
    junctions, membership = [], defaultdict(list)
    for i in range(junction_count):
        row = struct.unpack_from('<6I',data,junction_at+i*24)
        identity, count = row[0], row[5]
        require(2 <= count <= 4 and all(v == 0 for v in row[1+count:5]), 'Junction record shape')
        ids = list(row[1:1+count])
        require(len(ids) == len(set(ids)) and all(s in by_id for s in ids), 'Unresolved/duplicate junction section')
        # The membership is explicit; endpoint orientation is inferred by compactness.
        possibilities = []
        for ends in itertools.product((0,-1), repeat=count):
            pts = [by_id[s]['points'][end] for s,end in zip(ids,ends)]
            center = [sum(p[k] for p in pts)/count for k in range(3)]
            cost = sum(distance(p,center)**2 for p in pts)
            possibilities.append((cost,ends,center))
        _, ends, center = min(possibilities)
        for sid,end in zip(ids,ends):
            membership[sid].append((identity,end))
        junctions.append(dict(id=identity,sections=ids,xyz=center,endpoint_indices=list(ends)))
    require(set(membership) == set(by_id) and all(len(v)==2 for v in membership.values()), 'Each section must join two junctions')
    require(all({end for _,end in pairs} == {0,-1} for pairs in membership.values()), 'Inconsistent endpoint orientation inference')
    for sid,pairs in membership.items():
        by_id[sid]['junctions'] = [next(j for j,e in pairs if e==end) for end in (0,-1)]
    return dict(roads=roads,sections=sections,junctions=junctions,
                evidence=dict(source_sha256=ROAD_HASH,road_groups=len(roads),sections=len(sections),
                              sampled_points=sum(len(s['points']) for s in sections),junctions=len(junctions),
                              all_junction_references_resolve=True,each_section_has_two_junctions=True,
                              endpoint_orientation_inference_consistent=True))


def load_markers():
    data = (EXTRACTED/'GAMELOGIC/CHALLENGEINSTANCES'/RESOURCE).read_bytes()
    require(hashlib.sha256(data).hexdigest() == PAYLOAD_HASH, 'Captured event payload changed')
    events = read_json(ROOT/'experiments/004-event-catalog/events.json')
    triggers = trigger_records(next((EXTRACTED/'TRIGGERS/TriggerData').glob('*.dat')))
    markers, seen, restriction_sources = [], set(), []
    for event in events:
        if event['definition_name'] not in ('RaceDefinition','OnlineRaceDefinition'):
            continue
        route = event_route(data,event)
        if len(route['RestrictedRoadSections']) == 2:
            restriction_sources.append(dict(event_id=route['event_id'],title=route['title'],mode=route['definition'],
                                            values=[v['values'] for v in route['RestrictedRoadSections']]))
        if 'TEST' in (route['title'] or '').upper():
            continue
        for index,location in enumerate(route['Location']):
            values = location['values']
            if not values[0] or len(triggers.get(values[0],[]))!=1 or triggers[values[0]][0]['kind']!='shape':
                continue
            if any(v and len(triggers.get(v,[]))!=1 for v in values[:10]):
                continue
            role = 'start' if index==0 else 'finish' if index==len(route['Location'])-1 else 'waypoint'
            if role=='start' and not all(v and triggers[v][0]['kind']=='point' for v in values[1:9]):
                continue
            key = (role,tuple(values))
            if key in seen:
                continue
            seen.add(key)
            markers.append(dict(key=f"{route['event_id']}:{index}",event_id=route['event_id'],index=index,
                                title=route['title'],role=role,marker_id=values[0],road_id=values[-1],
                                xyz=triggers[values[0]][0]['xyz'],values=values))
            if role == 'start':
                slots = [triggers[v][0] for v in values[1:9]]
                dx, _, dz = struct.unpack_from('<3f', bytes.fromhex(slots[0]['record_hex']), 20)
                # Accept the recorded vector only when it agrees with back-to-front grid order.
                ax, az = (sum(p['xyz'][i] for p in slots[:2]) - sum(p['xyz'][i] for p in slots[-2:]) for i in (0, 2))
                length = math.hypot(dx, dz)
                if .85 < length < 1.05 and ax*dx + az*dz > .8 * math.hypot(ax, az) * length:
                    markers[-1]['grid_heading'] = [dx/length, dz/length]
    return markers,restriction_sources


def project(point, points):
    best = None
    along = 0.0
    for index,(a,b) in enumerate(zip(points,points[1:])):
        vector = [b[k]-a[k] for k in range(3)]
        length = distance(a,b)
        t = max(0,min(1,sum((point[k]-a[k])*vector[k] for k in range(3))/(length*length))) if length else 0
        p = [a[k]+t*vector[k] for k in range(3)]
        candidate = (distance(point,p),along+t*length,index,p)
        if best is None or candidate[0] < best[0]:
            best=candidate
        along += length
    return dict(offset=best[1],distance=best[0],index=best[2],xyz=best[3])


def marker_projection(marker, road_map):
    choices = [(project(marker['xyz'],s['points']),s) for s in road_map['sections'] if s['road_id']==marker['road_id']]
    require(choices, f"No decoded road for marker {marker['marker_id']}")
    p,s = min(choices,key=lambda x:x[0]['distance'])
    return s,p


def estimated_leg(start,finish,road_map):
    by_id = {s['id']:s for s in road_map['sections']}
    a,pa = marker_projection(start,road_map)
    b,pb = marker_projection(finish,road_map)
    # Temporary nodes split chosen sections at the projected marker locations.
    graph=defaultdict(list)
    for s in road_map['sections']:
        cuts=[(0,s['junctions'][0]),(s['sample_length'],s['junctions'][1])]
        if s['id']==a['id']:cuts.append((pa['offset'],-1))
        if s['id']==b['id']:cuts.append((pb['offset'],-2))
        cuts.sort()
        for (pos1,n1),(pos2,n2) in zip(cuts,cuts[1:]):
            graph[n1].append((n2,pos2-pos1,s['id'],pos1,pos2))
            graph[n2].append((n1,pos2-pos1,s['id'],pos2,pos1))
    queue=[(0,-1)]; costs={-1:0}; previous={}
    while queue:
        cost,node=heapq.heappop(queue)
        if cost!=costs[node]:continue
        if node==-2:break
        for nxt,weight,sid,p1,p2 in graph[node]:
            value=cost+weight
            if value < costs.get(nxt,float('inf')):
                costs[nxt]=value; previous[nxt]=(node,sid,p1,p2); heapq.heappush(queue,(value,nxt))
    require(-2 in costs,'Markers are disconnected in the decoded undirected graph')
    edges=[]; node=-2
    while node!=-1:
        prev,sid,p1,p2=previous[node]; edges.append((sid,p1,p2));node=prev
    edges.reverse()
    lines=[]
    for sid,p1,p2 in edges:
        s=by_id[sid]; points=s['points']; positions=[0.0]
        for x,y in zip(points,points[1:]):positions.append(positions[-1]+distance(x,y))
        def at(v):
            for k in range(len(positions)-1):
                if positions[k]<=v<=positions[k+1]:
                    t=(v-positions[k])/(positions[k+1]-positions[k]) if positions[k+1]!=positions[k] else 0
                    return [points[k][d]+t*(points[k+1][d]-points[k][d]) for d in range(3)]
            return points[-1]
        low,high=sorted((p1,p2)); line=[at(low)]+[p for p,t in zip(points,positions) if low<t<high]+[at(high)]
        if p2<p1:line.reverse()
        lines.append(line)
    return dict(sample_length=costs[-2],lines=lines,sections=[e[0] for e in edges],
                endpoint_projection_errors=[pa['distance'],pb['distance']])


def catalog():
    road_map=load_map(); markers,restrictions=load_markers()
    known={r['id'] for r in road_map['roads']}
    require(all(m['road_id'] in known for m in markers),'Marker references unknown decoded road')
    road_map.update(markers=markers,restriction_sources=restrictions,
                    caveat='Sampled road map. Preview ignores driving direction, race restrictions and junction traversal distance; it is not an exact in-game route.')
    return road_map
