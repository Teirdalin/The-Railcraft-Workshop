"""Measure exported object payloads and shared mesh/texture dependencies."""
from pathlib import Path
from collections import defaultdict
import json,argparse
import UnityPy

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--output',default='validation/presentation-0.2.66');parser.add_argument('--version',default='0.2.66');args=parser.parse_args()
path=root/'.unity-verify/minecart-client-01/Build/EcoMinecarts.unity3d'
env=UnityPy.load(str(path))
assets={}; totals=defaultdict(int)
def key(obj):return (obj.assets_file.name,obj.path_id)
def resolve(obj,ptr):
    if not ptr or not ptr.get('m_PathID') or ptr.get('m_FileID',0):return None
    return obj.assets_file.objects.get(ptr['m_PathID'])
for obj in env.objects:
    size=len(obj.get_raw_data()); kind=obj.type.name
    if kind in ('Texture2D','Mesh','AudioClip'):
        data=obj.read(); stream=getattr(data,'m_StreamData',None)
        size+=getattr(stream,'size',0)
        extra={}
        if kind=='Texture2D':extra=dict(width=data.m_Width,height=data.m_Height,format=str(data.m_TextureFormat))
        assets[key(obj)]=dict(name=data.m_Name,type=kind,bytes=size,pathId=obj.path_id,**extra)
    totals[kind]+=size
def pointers(value):
    if isinstance(value,dict):
        if 'm_PathID' in value:yield value
        else:
            for item in value.values():yield from pointers(item)
    elif isinstance(value,(list,tuple)):
        for item in value:yield from pointers(item)
def deps(go):
    found=set()
    def visit(obj):
        data=obj.read_typetree()
        for entry in data['m_Component']:
            component=resolve(obj,entry['component'])
            if component is None:continue
            tree=component.read_typetree(); kind=component.type.name
            if kind in ('MeshFilter','SkinnedMeshRenderer'):
                mesh=resolve(component,tree.get('m_Mesh'))
                if mesh:found.add(key(mesh))
            if kind in ('MeshRenderer','SkinnedMeshRenderer'):
                for ptr in tree['m_Materials']:
                    material=resolve(component,ptr)
                    if not material:continue
                    for texture_ptr in pointers(material.read_typetree()['m_SavedProperties']):
                        texture=resolve(material,texture_ptr)
                        if texture and texture.type.name=='Texture2D':found.add(key(texture))
            if kind=='Transform':
                for ptr in tree['m_Children']:
                    child=resolve(component,ptr)
                    if child:visit(resolve(child,child.read_typetree()['m_GameObject']))
    visit(go)
    return found
names=['Minecart','WoodenMinecart','MineTrain','HeritageTram','RailroadHandcar','PassengerLocomotive','FreightLocomotive','PassengerCar','CoalTender','LargeTrainEngine','LargeCargoCar','LargePassengerCar','LargeCoalTender','RollerCoasterCart']
models={}
for obj in env.objects:
    if obj.type.name!='GameObject':continue
    name=obj.read().m_Name
    if name.endswith(('Object','Block')):models[name]=deps(obj)
rows=[]
owners=defaultdict(list)
for name,ids in models.items():
    for k in ids:owners[k].append(name)
    selected=[assets[k] for k in ids if k in assets]
    rows.append(dict(name=name,meshBytes=sum(a['bytes'] for a in selected if a['type']=='Mesh'),textureBytes=sum(a['bytes'] for a in selected if a['type']=='Texture2D'),assets=sorted(selected,key=lambda a:a['bytes'],reverse=True)))
groups=defaultdict(set)
for name,ids in models.items():
    category='Vehicles' if name in [n+'Object' for n in names] else 'Stations' if 'Station' in name else 'Workbench' if 'Workbench' in name else 'Coaster track and previews' if name.startswith('Coaster') else 'Other infrastructure'
    groups[category].update(ids)
exclusive=defaultdict(lambda:defaultdict(int))
for k,asset in assets.items():
    membership=[n for n,ids in groups.items() if k in ids]
    category=membership[0] if len(membership)==1 else 'Shared assets' if membership else 'Other bundled assets'
    exclusive[category][asset['type']]+=asset['bytes']
meshUsers=defaultdict(set)
def game_path(go):
    parts=[]
    for _ in range(24):
        data=go.read_typetree();parts.append(data['m_Name'])
        transform=next((resolve(go,c['component']) for c in data['m_Component'] if resolve(go,c['component']).type.name=='Transform'),None)
        if not transform:break
        father=resolve(transform,transform.read_typetree()['m_Father'])
        if not father:break
        go=resolve(father,father.read_typetree()['m_GameObject'])
    return '/'.join(reversed(parts))
for obj in env.objects:
    if obj.type.name not in ('MeshFilter','SkinnedMeshRenderer'):continue
    data=obj.read_typetree();mesh=resolve(obj,data['m_Mesh']);go=resolve(obj,data['m_GameObject'])
    if mesh and go and not owners[key(mesh)]:meshUsers[key(mesh)].add(game_path(go))
otherMeshes=sorted([dict(assets[k],users=sorted(v)) for k,v in meshUsers.items()],key=lambda a:a['bytes'],reverse=True)
zip_path=root/('dist/Railworks-Workshop-Server-'+args.version.replace('.','-')+'.zip')
report=dict(bundleBytes=path.stat().st_size,zipBytes=zip_path.stat().st_size if zip_path.exists() else None,
            payloadTotals=dict(sorted(totals.items(),key=lambda p:p[1],reverse=True)),
            largestAssets=sorted([dict(a,owners=owners[k]) for k,a in assets.items()],key=lambda a:a['bytes'],reverse=True)[:40],
            vehicles=sorted([r for r in rows if r['name'] in [n+'Object' for n in names]],key=lambda a:a['meshBytes']+a['textureBytes'],reverse=True),
            prefabs=sorted(rows,key=lambda a:a['meshBytes']+a['textureBytes'],reverse=True),exclusiveCategories={k:dict(v) for k,v in exclusive.items()},
            otherMeshUsers=otherMeshes,
            note='Serialized payload bytes before bundle/archive compression. Per-model totals include both designs and shared assets, so they overlap.')
out=root/args.output/'bundle-sizes.json';out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(report,indent=2))
print(json.dumps({k:report[k] for k in ['bundleBytes','zipBytes','payloadTotals']}))
for row in report['vehicles']:print(row['name'],round(row['meshBytes']/1048576,2),round(row['textureBytes']/1048576,2))
print('Categories',report['exclusiveCategories'])
for row in report['prefabs'][:8]:print('Largest prefab',row['name'],round((row['meshBytes']+row['textureBytes'])/1048576,2))
for row in otherMeshes[:8]:print('Other mesh',round(row['bytes']/1048576,2),row['users'][:2])
