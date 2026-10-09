"""Inspect the exported animation bundle and preserve the native physics contract."""
from pathlib import Path
import argparse,hashlib,json,UnityPy
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--output',default='validation/models-0.2.49');args=parser.parse_args()
names=['Minecart','WoodenMinecart','MineTrain','HeritageTram','RailroadHandcar','PassengerLocomotive','FreightLocomotive','PassengerCar','CoalTender','LargeTrainEngine','LargeCargoCar','LargePassengerCar','LargeCoalTender','RollerCoasterCart']
def inspect(path):
    env=UnityPy.load(str(path));assets={};vehicles={}
    for obj in env.objects:
        if obj.type.name in ('Mesh','Material','Texture2D','AudioClip'):
            payload=obj.get_raw_data()
            if obj.type.name=='Material':
                # Unity's embedded shader path IDs shift when adding assets.
                # Compare actual material properties and named dependencies.
                def semantic(value):
                    if isinstance(value,dict):
                        if set(value)=={'m_FileID','m_PathID'} and value['m_FileID']==0:
                            if value['m_PathID']==0:return {'null':True}
                            target=obj.assets_file.objects[value['m_PathID']]
                            return {'type':target.type.name,'name':target.read().m_Name}
                        return {k:semantic(v) for k,v in value.items()}
                    if isinstance(value,(list,tuple)):return [semantic(v) for v in value]
                    return value
                payload=json.dumps(semantic(obj.read_typetree()),sort_keys=True).encode()
            assets[(obj.type.name,obj.read().m_Name)]=hashlib.sha256(payload).hexdigest()
        if obj.type.name!='GameObject':continue
        game=obj.read_typetree()
        if game['m_Name'] not in [n+'Object' for n in names]:continue
        file=obj.assets_file
        def local(ptr):return file.objects[ptr['m_PathID']]
        physics=[];wheels=[];world=None;rcc=None;mechanisms=[];dump=False
        def visit(node):
            nonlocal world,rcc,dump
            if node['m_Name']=='MechanicalLinkage':mechanisms.append(node['m_Name'])
            if node['m_Name']=='DumpHinge':dump=True
            for ptr in node['m_Component']:
                component=local(ptr['component']);data=component.read_typetree()
                if component.type.name in ('Rigidbody','WheelCollider'):
                    physics.append((component.type.name,{k:v for k,v in data.items() if k!='m_GameObject'}))
                if component.type.name=='Animator' and node['m_Name'].startswith(('WheelRoll_','UpstopRoll_')):
                    assert not data['m_Enabled'],'Parked wheel animator enabled'
                    assert data['m_Controller']['m_PathID'],'Missing controller'
                    wheels.append(node['m_Name'])
                if component.type.name=='MonoBehaviour' and node['m_Name']==game['m_Name']:
                    if 'FloatStates' in data:world=data
                    if 'engineTorque' in data:rcc=data
                if component.type.name=='Transform':
                    for child in data['m_Children']:visit(local(local(child).read_typetree()['m_GameObject']).read_typetree())
        visit(game)
        vehicles[game['m_Name']]=dict(physics=physics,wheels=wheels,world=world,rcc=rcc,mechanisms=mechanisms,dump=dump)
    return assets,vehicles
old=root/'dist/Railworks-Workshop-0.2.48/Railworks.unity3d'
new=root/'.unity-verify/minecart-client-01/Build/EcoMinecarts.unity3d'
before,previous=inspect(old);after,current=inspect(new)
assert previous.keys()==current.keys() and len(current)==14,'Vehicle identities changed'
for name,v in current.items():
 assert v['physics']==previous[name]['physics'],('Rigid body or native wheel physics changed',name)
 for key in ('engineTorque','brake','maxspeed','footPoweredCart','m_Enabled','syncWheelPositions'):
  if name=='MineTrainObject' and key=='m_Enabled':
   assert not v['rcc'][key], 'Minetrain competing client physics still enabled'
   continue
  assert v['rcc'][key]==previous[name]['rcc'][key],(name,key)
 assert len(v['wheels'])==(8 if name=='RollerCoasterCartObject' else 4),name
 assert 'RailWheelSpeed' in v['world']['FloatStates'],name
for key,value in before.items():
 if key[0]=='AudioClip':assert after.get(key)==value,('Sound unexpectedly changed',key)
assert sum(len(v['mechanisms']) for v in current.values())==8
assert sum(v['dump'] for v in current.values())==5
assert 'RailRestraintPose' in current['RollerCoasterCartObject']['world']['StringStates']
for name in ('MineTrainObject','PassengerLocomotiveObject','FreightLocomotiveObject','LargeTrainEngineObject'):
 assert all(k in current[name]['world']['StringStates'] for k in ('RailThrottlePose','RailBrakePose','RailReverserPose'))
report=dict(bundleHash=hashlib.sha256(new.read_bytes()).hexdigest(),vehicles=14,animatedWheels=60,steamEngines=4,sliderCranks=8,dumpBuckets=5,nativePhysicsUnchanged=True,disabledCompetingMinetrainController=True,liveClientVerified=False)
out=root/args.output;out.mkdir(parents=True,exist_ok=True)
(out/'bundle-audit.json').write_text(json.dumps(report,indent=2))
print('INTEGRATED_MODEL_BUNDLE_OK:',json.dumps(report))
