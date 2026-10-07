using System;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    public static class ModelPolishProbe
    {
        public static void Verify(GameObject prefab,RailExpansionAssetBuilder.Spec spec)
        {
            var c=prefab.GetComponent<RCCCarControllerV2>();
            var wheels=new[]{c.FrontLeftWheelTransform,c.FrontRightWheelTransform,c.RearLeftWheelTransform,c.RearRightWheelTransform};
            var colliders=new[]{c.FrontLeftWheelCollider,c.FrontRightWheelCollider,c.RearLeftWheelCollider,c.RearRightWheelCollider};
            for(int i=0;i<4;i++) {
                var center=prefab.transform.InverseTransformPoint(colliders[i].transform.position);
                if(Mathf.Abs(center.y-colliders[i].radius)>.0001f || Mathf.Abs(Mathf.Abs(center.x)-spec.HalfGauge)>.0001f
                    || Vector3.Distance(center,wheels[i].localPosition)>.0001f)
                    throw new Exception(spec.Key+": changed contact plane, gauge or wheel/collider center");
                if(Mathf.Abs(wheels[i].Find("Wheel tyre").localScale.x*.5f-colliders[i].radius)>.0001f)
                    throw new Exception(spec.Key+": wheel model and physical radius differ");
            }
            if(spec.Pullable) {
                var model=prefab.transform.Find("WoodenPlankBody");
                if(model==null || model.Cast<Transform>().Count(t=>t.name=="Side plank")!=8
                    || model.Cast<Transform>().Count(t=>t.name=="Floor plank")!=5)
                    throw new Exception("Wooden cart lacks separate structural planks");
            }
            if(spec.Powered && spec.Model!="Tram") {
                var model=prefab.transform.Find(spec.Key+"_Visual");
                var supports=model.Cast<Transform>().Where(t=>t.name=="Boiler saddle").ToArray();
                var floor=spec.Length>3?.70f:.50f;
                if(supports.Length!=2 || supports.Any(t=>t.localPosition.y-t.localScale.y/2>.461f || t.localPosition.y+t.localScale.y/2<floor))
                    throw new Exception(spec.Key+": boiler supports do not reach both deck and boiler");
                if(spec.Length>3) {
                    var riser=model.Find("Cab riser");
                    if(riser==null || riser.localPosition.y-riser.localScale.y/2>.461f || riser.localPosition.y+riser.localScale.y/2<floor-.076f)
                        throw new Exception("Raised cab lacks continuous riser");
                }
                var deck=prefab.transform.Find("CabFittings/Cab floor");
                var finish=prefab.transform.Find("VisualFinish");
                var deckTop=deck.localPosition.y+deck.localScale.y/2;
                var sills=finish.Cast<Transform>().Where(t=>t.name=="Cab sill").ToArray();
                if(sills.Length!=2 || sills.Any(t=>deckTop-(t.localPosition.y+t.localScale.y/2)<.01f))
                    throw new Exception(spec.Key+": cab sill is coplanar with the deck surface");
                var board=model.Find("RunningBoard");
                if(board!=null){
                    var boardTop=board.localPosition.y+board.localScale.y/2;
                    var outriggers=finish.Cast<Transform>().Where(t=>t.name=="Cab outrigger").ToArray();
                    if(outriggers.Length!=2 || outriggers.Any(t=>boardTop-(t.localPosition.y+t.localScale.y/2)<.015f))
                        throw new Exception(spec.Key+": cab outrigger competes with the running-board surface");
                }
            }
            if(spec.HumanPowered) {
                var copy=Object.Instantiate(prefab);
                try {
                    var clip=copy.GetComponent<Animator>().runtimeAnimatorController.animationClips.Single();
                    for(int i=0;i<=256;i++) {
                        clip.SampleAnimation(copy,i/256f);
                        var rod=copy.transform.Find("PumpRod"); var upper=copy.transform.Find("PumpPivot/Linkage upper pin"); var lower=copy.transform.Find("PumpSlider");
                        if(Vector3.Distance(rod.TransformPoint(Vector3.up),upper.position)>.0005f
                            || Vector3.Distance(rod.TransformPoint(Vector3.down),lower.position)>.0005f)
                            throw new Exception("Handcar connecting rod disconnects at animation phase "+i+"/256");
                    }
                } finally { Object.DestroyImmediate(copy); }
                Debug.Log("HANDCAR_LINKAGE_OK: 257 phases, both joints remain within 0.5 mm.");
            }
            Debug.Log("MODEL_POLISH_GEOMETRY_OK: "+spec.Key+" contact plane, wheel gauge/radius and structural supports.");
        }
    }
}
