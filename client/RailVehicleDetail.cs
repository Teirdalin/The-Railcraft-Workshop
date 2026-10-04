using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Authoring-only visual treatment. Existing seats, controls, couplers,
    // collider geometry, wheel centres and animation bindings are retained.
    public static class RailVehicleDetail
    {
        static Transform body,gear,roof;
        static Material steel,dark,brass,cream,paint,wood,padding;
        static string key;
        static Transform Part(Transform p,string name,PrimitiveType type,Vector3 at,Vector3 size,Material mat,Quaternion rotation)
        {
            var n=GameObject.CreatePrimitive(type);n.name=name;n.transform.SetParent(p,false);
            n.transform.localPosition=at;n.transform.localScale=size;n.transform.localRotation=rotation;
            n.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(n.GetComponent<Collider>());return n.transform;
        }
        static Transform Box(Transform p,string n,Vector3 at,Vector3 size,Material mat)=>Part(p,n,PrimitiveType.Cube,at,size,mat,Quaternion.identity);
        static Transform Pin(Transform p,string n,Vector3 at,float radius,float length,Material mat,Quaternion q)=>Part(p,n,PrimitiveType.Cylinder,at,new Vector3(radius*2,length/2,radius*2),mat,q);
        static void Pipe(Transform p,string n,Vector3 a,Vector3 b,float radius,Material mat)
        {Pin(p,n,(a+b)/2,radius,Vector3.Distance(a,b),mat,Quaternion.FromToRotation(Vector3.up,b-a));}
        static void Ring(Transform p,string n,Vector3 at,float radius,float tube,Material mat,Quaternion q,int segments=16)
        {
            for(var i=0;i<segments;i++){
                var a=i*2*Mathf.PI/segments;var b=(i+1)*2*Mathf.PI/segments;
                Pipe(p,n,at+q*new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius,
                    at+q*new Vector3(Mathf.Cos(b),Mathf.Sin(b),0)*radius,tube,mat);
            }
        }
        sealed class Shape
        {
            readonly List<Vector3> v=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> t=new List<int>();
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {var i=v.Count;v.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});t.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});}
            public void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {var i=v.Count;v.AddRange(new[]{a,b,c});uv.AddRange(new[]{new Vector2(.5f,.5f),Vector2.zero,Vector2.one});t.AddRange(new[]{i,i+1,i+2});}
            public Mesh Mesh(){var m=new Mesh();m.SetVertices(v);m.SetUVs(0,uv);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();return m;}
        }
        static void MeshPart(Transform parent,string name,Vector3 at,Mesh mesh,Material mat)
        {var n=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));n.transform.SetParent(parent,false);n.transform.localPosition=at;n.GetComponent<MeshFilter>().sharedMesh=mesh;n.GetComponent<Renderer>().sharedMaterial=mat;}
        static void ArchedRoof(Transform p,string name,Vector3 at,float width,float length,float rise,Material mat)
        {
            var shape=new Shape();const int spans=12;const float thick=.045f;
            for(var i=0;i<spans;i++){
                var x0=-width/2+width*i/spans;var x1=-width/2+width*(i+1)/spans;
                var y0=rise*(1-Mathf.Pow(x0/(width/2),2));var y1=rise*(1-Mathf.Pow(x1/(width/2),2));
                var a=new Vector3(x0,y0,-length/2);var b=new Vector3(x0,y0,length/2);
                var c=new Vector3(x1,y1,length/2);var d=new Vector3(x1,y1,-length/2);var down=Vector3.down*thick;
                shape.Quad(a,b,c,d);shape.Quad(d+down,c+down,b+down,a+down);
                shape.Quad(b+down,c+down,c,b);shape.Quad(d+down,a+down,a,d);
                if(i==0)shape.Quad(a+down,b+down,b,a);
                if(i==spans-1)shape.Quad(c+down,d+down,d,c);
            }
            MeshPart(p,name,at,shape.Mesh(),mat);
        }
        // Eight-sided outline and four height rings give padded seats and body
        // panels bevelled highlights without excessively dense geometry.
        static void SoftBox(Transform p,string name,Vector3 at,Vector3 size,float bevel,Material mat)
        {
            var s=new Shape();var b=Mathf.Min(bevel,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.35f);
            Vector3[] Loop(float y,float inset){
                var x=size.x/2-inset;var z=size.z/2-inset;var k=Mathf.Min(b,Mathf.Min(x,z)*.5f);
                return new[]{new Vector3(-x+k,y,-z),new Vector3(x-k,y,-z),new Vector3(x,y,-z+k),new Vector3(x,y,z-k),
                    new Vector3(x-k,y,z),new Vector3(-x+k,y,z),new Vector3(-x,y,z-k),new Vector3(-x,y,-z+k)};
            }
            var rings=new[]{Loop(-size.y/2,b),Loop(-size.y/2+b,0),Loop(size.y/2-b,0),Loop(size.y/2,b)};
            for(var r=0;r<3;r++)for(var i=0;i<8;i++){var j=(i+1)%8;s.Quad(rings[r][j],rings[r][i],rings[r+1][i],rings[r+1][j]);}
            for(var i=0;i<8;i++){var j=(i+1)%8;s.Triangle(Vector3.up*size.y/2,rings[3][j],rings[3][i]);
                s.Triangle(Vector3.down*size.y/2,rings[0][i],rings[0][j]);}
            MeshPart(p,name,at,s.Mesh(),mat);
        }
        static void Hide(IEnumerable<Transform> nodes,params string[] names)
        {foreach(var n in nodes.Where(n=>names.Contains(n.name)))if(n.GetComponent<Renderer>() is Renderer r)r.enabled=false;}
        static void Repaint(IEnumerable<Transform> nodes,Material mat,params string[] names)
        {foreach(var n in nodes.Where(n=>names.Contains(n.name)))if(n.GetComponent<Renderer>() is Renderer r)r.sharedMaterial=mat;}
        static void Lamp(Vector3 at,float radius,float facing)
        {
            var q=Quaternion.Euler(90,0,0);
            Pin(body,"Lamp housing",at,radius,.075f,dark,q);
            Pin(body,"Lamp brass bezel",at+Vector3.forward*facing*.043f,radius*.94f,.022f,brass,q);
            Pin(body,"Lamp glass",at+Vector3.forward*facing*.057f,radius*.78f,.008f,cream,q);
            Pipe(body,"Lamp support",at+Vector3.down*.13f,at,.018f,steel);
        }
        static void Engine(GameObject root,Transform[] nodes)
        {
            var boiler=nodes.First(n=>n.name=="Boiler"||n.name=="Steam boiler");var c=boiler.localPosition;
            var r=boiler.localScale.x/2;var length=boiler.localScale.y*2;
            var controller=root.GetComponent<RCCCarControllerV2>();var wb=Mathf.Abs(controller.FrontLeftWheelTransform.localPosition.z-controller.RearLeftWheelTransform.localPosition.z);
            var gauge=Mathf.Abs(controller.FrontLeftWheelTransform.localPosition.x);
            Repaint(nodes,paint,"Boiler","Steam boiler","Cab rear panel","Cab console","Cab riser","Streamlined running skirt","Firebox");
            Repaint(nodes,dark,"ExhaustPipe","Exhaust chimney","SmokeboxDoor","Smokebox door","StackMouth");
            Repaint(nodes,brass,"BoilerBand","Boiler band","StackRim","Chimney lip","SteamDome","Steam dome");
            var front=c.z+length/2;
            Lamp(new Vector3(0,c.y+r+.055f,front-.015f),r*.27f,1);
            // Hinges and locking dogs sit on the door face, not in front of it.
            foreach(var y in new[]{-.30f,.30f}){
                Box(body,"Smokebox hinge leaf",new Vector3(-r*.69f,c.y+y*r,front+.032f),new Vector3(r*.30f,.038f,.020f),dark);
                Pin(body,"Smokebox hinge pin",new Vector3(-r*.80f,c.y+y*r,front+.037f),.022f,.066f,brass,Quaternion.identity);
            }
            foreach(var angle in new[]{-55f,0f,55f}){
                var a=angle*Mathf.Deg2Rad;
                Pin(body,"Smokebox locking dog",new Vector3(Mathf.Cos(a)*r*.73f,c.y+Mathf.Sin(a)*r*.73f,front+.033f),.024f,.020f,steel,Quaternion.Euler(90,0,0));
            }
            foreach(var side in new[]{-1,1}){
                var x=side*(r+.09f);
                SoftBox(body,"Water tank",new Vector3(x,c.y-.08f,c.z-length*.23f),new Vector3(.13f,r*1.15f,length*.34f),.025f,paint);
                Box(body,"Tank lining",new Vector3(x+side*.071f,c.y-.025f,c.z-length*.23f),new Vector3(.009f,.018f,length*.30f),brass);
                Pin(body,"Water filler",new Vector3(x,c.y+r*.52f-.07f,c.z-length*.23f),.044f,.026f,brass,Quaternion.identity);
                foreach(var z in new[]{c.z-length*.36f,c.z-length*.10f}){
                    Box(body,"Tank mounting strap",new Vector3(x+side*.069f,c.y-.08f,z),new Vector3(.012f,r*1.15f,.029f),dark);
                    Pin(body,"Tank strap fastener",new Vector3(x+side*.080f,c.y-.08f-r*.40f,z),.014f,.012f,brass,Quaternion.Euler(0,0,90));
                }
                Pipe(body,"Steam supply",new Vector3(side*(r+.025f),c.y+.10f,c.z-length*.25f),new Vector3(side*(r+.025f),c.y+.10f,front-.10f),.019f,brass);
                Pipe(body,"Steam downpipe",new Vector3(side*(r+.025f),c.y+.10f,front-.10f),new Vector3(side*(gauge+.08f),.32f,wb*.36f),.019f,brass);
                var cylinder=new Vector3(side*(gauge+.09f),root.name=="MineTrainObject"?.26f:.29f,wb*.36f);
                var cylinderHalfLength=root.name=="MineTrainObject"?.15f:.16f;
                var capZ=cylinderHalfLength+.0075f; // Five mm overlap with cylinder shell.
                Pin(gear,"Cylinder end cap",cylinder+Vector3.forward*capZ,.105f,.025f,brass,Quaternion.Euler(90,0,0));
                foreach(var angle in new[]{45f,135f,225f,315f}){
                    var a=angle*Mathf.Deg2Rad;
                    Pin(gear,"Cylinder cover bolt",cylinder+new Vector3(Mathf.Cos(a)*.077f,Mathf.Sin(a)*.077f,capZ+.014f),.012f,.012f,steel,Quaternion.Euler(90,0,0));
                }
                if(root.name=="MineTrainObject")Pin(gear,"Steam cylinder",cylinder,.095f,.30f,dark,Quaternion.Euler(90,0,0));
                Pipe(gear,"Piston guide",cylinder+Vector3.back*.12f,cylinder+Vector3.back*.40f,.027f,steel);
                SoftBox(gear,"Crosshead slide",cylinder+Vector3.back*.28f,new Vector3(.07f,.08f,.12f),.013f,steel);
                for(var z=-wb/2;z<=wb/2+.01f;z+=wb){
                    Pin(gear,"Rod crank boss",new Vector3(side*(gauge+.066f),.20f,z),.044f,.035f,brass,Quaternion.Euler(0,0,90));
                    Box(gear,"Brake hanger",new Vector3(side*(gauge+.025f),.29f,z-.16f),new Vector3(.028f,.21f,.04f),dark);
                }
                Pipe(gear,"Valve linkage",new Vector3(side*(gauge+.075f),.32f,-wb*.30f),new Vector3(side*(gauge+.075f),.32f,wb*.32f),.014f,steel);
            }
            var bell=new Vector3(-r*.50f,c.y+r+.10f,c.z-length*.22f);
            Pin(body,"Bell base",bell,.061f,.10f,brass,Quaternion.identity);
            Pin(body,"Bell lip",bell-Vector3.up*.048f,.08f,.022f,brass,Quaternion.identity);
            foreach(var side in new[]{-1,1}){
                var foot=new Vector3(bell.x+side*.09f,c.y+Mathf.Sqrt(Mathf.Max(0,r*r-(bell.x+side*.09f)*(bell.x+side*.09f)))-.008f,bell.z);
                Pipe(body,"Bell yoke",foot,bell+new Vector3(side*.09f,.08f,0),.014f,dark);
                Box(body,"Bell mounting foot",foot,new Vector3(.060f,.025f,.065f),dark);
            }
            Pipe(body,"Bell crossbar",bell+new Vector3(-.09f,.08f,0),bell+new Vector3(.09f,.08f,0),.014f,dark);
            Pipe(body,"Bell spindle",bell,bell+Vector3.up*.08f,.015f,brass);
            var cab=root.transform.Find("CabFittings");var deck=cab.Find("Cab floor");var floor=deck.localPosition.y+deck.localScale.y/2;
            var w=deck.localScale.x;var zc=deck.localPosition.z;var d=deck.localScale.z;
            Hide(nodes,"Cab roof");ArchedRoof(roof,"Arched locomotive roof",new Vector3(0,floor+2.10f+RailRiderFit.RiderLift,zc),w+.18f,d+.15f,.13f,dark);
            foreach(var side in new[]{-1,1}){
                Box(body,"Cab rear corner casing",new Vector3(side*(w/2-.045f),floor+1.38f+RailRiderFit.RiderLift/2,zc-d/2),new Vector3(.095f,1.37f+RailRiderFit.RiderLift,.065f),paint);
                Box(body,"Rear window sill",new Vector3(side*w*.25f,floor+.86f,zc-d/2),new Vector3(w*.46f,.065f,.075f),brass);
                Box(body,"Rear window header",new Vector3(side*w*.25f,floor+1.98f+RailRiderFit.RiderLift,zc-d/2),new Vector3(w*.46f,.08f,.065f),paint);
                Box(body,"Rear panel lining",new Vector3(side*w*.27f,floor+.43f,zc-d/2-.026f),new Vector3(w*.38f,.019f,.012f),brass);
                Pipe(body,"Cab grab rail",new Vector3(side*(w/2+.018f),floor+.71f,zc+d/2),new Vector3(side*(w/2+.018f),floor+1.40f,zc+d/2),.014f,brass);
                foreach(var y in new[]{floor+.71f,floor+1.40f})
                    Pipe(body,"Grab rail bracket",new Vector3(side*(w/2-.025f),y,zc+d/2),new Vector3(side*(w/2+.018f),y,zc+d/2),.018f,steel);
                Box(roof,"Rain gutter",new Vector3(side*(w/2+.09f),floor+2.105f+RailRiderFit.RiderLift,zc),new Vector3(.035f,.037f,d+.18f),brass);
            }
            Box(body,"Rear centre mullion",new Vector3(0,floor+1.43f+RailRiderFit.RiderLift/2,zc-d/2),new Vector3(.047f,1.12f+RailRiderFit.RiderLift,.055f),paint);
            var cabFront=zc+d/2+.017f;
            SoftBox(body,"Cab front apron",new Vector3(0,floor+.565f,cabFront),new Vector3(w-.045f,1.05f,.055f),.018f,paint);
            Box(body,"Front window sill",new Vector3(0,floor+1.285f,cabFront),new Vector3(w,.045f,.065f),brass);
            foreach(var x in new[]{-w/2+.022f,0f,w/2-.022f})
                Box(body,"Front window mullion",new Vector3(x,floor+1.685f+RailRiderFit.RiderLift/2,cabFront),new Vector3(.045f,.78f+RailRiderFit.RiderLift,.055f),paint);
            Box(body,"Front window header",new Vector3(0,floor+2.045f+RailRiderFit.RiderLift,cabFront),new Vector3(w,.06f,.065f),paint);
            var firebox=new Vector3(0,floor+.25f,zc+d/2-.075f);
            SoftBox(body,"Firebox door frame",firebox,new Vector3(.29f,.25f,.042f),.03f,dark);
            Pin(body,"Firebox latch",firebox+new Vector3(.08f,0,-.027f),.025f,.023f,brass,Quaternion.Euler(90,0,0));
            foreach(var side in new[]{-1,1})for(var i=0;i<4;i++)Box(gear,"Boarding tread",new Vector3(side*(w/2+.10f),floor-.166f,zc+(i-1.5f)*.075f),new Vector3(.20f,.012f,.018f),dark);
        }
        static void Tram(GameObject root,Transform[] nodes)
        {
            var model=root.transform.Find("HeritageTram_Visual");var canopy=model.Find("Canopy roof");var sz=canopy.localScale;
            var w=sz.x;var l=sz.z;
            Hide(nodes,"Canopy roof","Clerestory","Standing handhold");
            Repaint(nodes,paint,"Tram sill","End bulkhead","Automatic cab housing");Repaint(nodes,brass,"Handrail","Handhold hanger");
            ArchedRoof(roof,"Barrel tram canopy",new Vector3(0,2.335f,0),w,l,.12f,dark);
            SoftBox(roof,"Clerestory housing",new Vector3(0,2.47f,0),new Vector3(.78f,.21f,l*.59f),.035f,cream);
            ArchedRoof(roof,"Clerestory cap",new Vector3(0,2.59f,0),.85f,l*.62f,.065f,paint);
            foreach(var side in new[]{-1,1}){
                Box(roof,"Canopy fascia",new Vector3(side*(w/2-.02f),2.323f,0),new Vector3(.046f,.095f,l),cream);
                for(var i=0;i<7;i++)SoftBox(roof,"Clerestory vent",new Vector3(side*.398f,2.50f,(i-3)*l*.069f),new Vector3(.012f,.070f,.115f),.004f,dark);
                var sill=model.Cast<Transform>().First(n=>n.name=="Tram sill"&&Mathf.Sign(n.localPosition.x)==side);var x=sill.localPosition.x+side*.042f;
                foreach(var y in new[]{.575f,.813f})Box(body,"Tram waist lining",new Vector3(x,y,0),new Vector3(.014f,.024f,l-.20f),cream);
                for(var i=0;i<6;i++){
                    var z=(i-2.5f)*(l-.34f)/6;
                    SoftBox(body,"Recessed tram panel",new Vector3(x,.693f,z),new Vector3(.014f,.17f,(l-.34f)/6-.037f),.008f,dark);
                    Box(body,"Panel face",new Vector3(x+side*.009f,.693f,z),new Vector3(.008f,.135f,(l-.34f)/6-.074f),paint);
                }
                foreach(var post in model.Cast<Transform>().Where(n=>n.name=="Canopy upright"&&Mathf.Sign(n.localPosition.x)==side)){
                    var c=post.localPosition;Box(body,"Upright foot ferrule",new Vector3(c.x,.85f,c.z),new Vector3(.076f,.12f,.076f),brass);
                    Pipe(roof,"Canopy corner brace",new Vector3(c.x,2.04f,c.z),new Vector3(c.x-side*.19f,2.32f,c.z),.019f,wood);
                    SoftBox(roof,"Carved post capital",new Vector3(c.x,2.255f,c.z),new Vector3(.10f,.10f,.10f),.02f,cream);
                }
                for(var i=0;i<12;i++)Box(gear,"Running board tread",new Vector3(side*(w/2-.015f),.51f,(i-5.5f)*.115f),new Vector3(.18f,.014f,.018f),dark);
            }
            foreach(var end in new[]{-1,1}){
                var z=end*(l/2-.046f);
                foreach(var x in new[]{-.46f,.46f})Box(body,"Front panel stile",new Vector3(x,1.16f,z),new Vector3(.032f,1.06f,.022f),cream);
                foreach(var y in new[]{.66f,1.66f})Box(body,"Front panel rail",new Vector3(0,y,z),new Vector3(.94f,.032f,.025f),cream);
                Lamp(new Vector3(0,1.38f,z+end*.052f),.095f,end);
                foreach(var x in new[]{-.52f,.52f})Pipe(body,"Platform handrail",new Vector3(x,.62f,z+end*.055f),new Vector3(x,1.04f,z+end*.055f),.016f,brass);
                foreach(var y in new[]{2.08f,2.36f})Box(roof,"Destination frame",new Vector3(0,y,end*(l/2+.015f)),new Vector3(1.17f,.025f,.025f),brass);
                foreach(var x in new[]{-.575f,.575f})Box(roof,"Destination stile",new Vector3(x,2.22f,end*(l/2+.015f)),new Vector3(.025f,.28f,.025f),brass);
            }
            foreach(var grip in nodes.Where(n=>n.name=="Standing handhold"))Ring(body,"Suspended grip",grip.localPosition+Vector3.down*.022f,.056f,.009f,brass,Quaternion.identity,12);
            BenchSlats(nodes);
        }
        static void BenchSlats(Transform[] nodes)
        {
            foreach(var bench in nodes.Where(n=>n.name=="Passenger bench")){
                var c=bench.localPosition;var s=bench.localScale;
                for(var i=0;i<4;i++)Box(gear,"Seat slat",new Vector3(c.x,c.y+s.y/2+.008f,c.z+(i-1.5f)*s.z/4),new Vector3(s.x,.014f,s.z/4-.008f),wood);
            }
            foreach(var back in nodes.Where(n=>n.name=="Bench back")){
                var c=back.localPosition;var s=back.localScale;
                var facing=key=="HeritageTramObject" ? Mathf.Sign(c.z) : 1;
                for(var i=0;i<4;i++)Box(gear,"Backrest slat",new Vector3(c.x,c.y+(i-1.5f)*s.y/4,c.z+facing*(s.z/2+.009f)),new Vector3(s.x,.075f,.015f),wood);
            }
        }
        static void Coaster(GameObject root,Transform[] nodes)
        {
            Hide(nodes,"Coach side","Passenger bench","Bench back","Body top cap","Body seam rivet","Bench pedestal",
                "Seat restraint grip","Restraint pivot upright","Deck","End buffer","Captive wheel carrier");
            SoftBox(body,"Coaster floor pan",new Vector3(0,.48f,0),new Vector3(1.12f,.10f,1.46f),.035f,dark);
            // Side panels taper down beside the footwell; the interior is open.
            foreach(var side in new[]{-1,1}){
                SoftBox(body,"Rear shoulder shell",new Vector3(side*.56f,.83f,-.41f),new Vector3(.095f,.62f,.56f),.033f,paint);
                SoftBox(body,"Footwell side shell",new Vector3(side*.56f,.66f,.21f),new Vector3(.095f,.28f,.70f),.025f,paint);
                Box(body,"Coaster side stripe",new Vector3(side*.612f,.64f,0),new Vector3(.012f,.045f,1.20f),cream);
                Pipe(body,"Upper shell rim",new Vector3(side*.56f,1.12f,-.65f),new Vector3(side*.56f,1.12f,-.18f),.025f,dark);
                Pipe(body,"Raked shell rim",new Vector3(side*.56f,1.12f,-.18f),new Vector3(side*.56f,.81f,.10f),.025f,dark);
                Pipe(body,"Footwell rim",new Vector3(side*.56f,.81f,.10f),new Vector3(side*.56f,.81f,.52f),.025f,dark);
                var x=side*.288f;
                SoftBox(gear,"Bucket seat cushion",new Vector3(x,RailRiderFit.CoasterCushionCentre,0),new Vector3(.46f,RailRiderFit.CoasterCushionThickness,.40f),.045f,padding);
                SoftBox(gear,"Bucket back shell",new Vector3(x,1.19f,-.205f),new Vector3(.48f,.67f,.12f),.038f,paint);
                SoftBox(gear,"Seat back padding",new Vector3(x,1.21f,-.127f),new Vector3(.385f,.53f,.075f),.025f,padding);
                SoftBox(gear,"Headrest",new Vector3(x,1.46f,-.115f),new Vector3(.32f,.14f,.09f),.025f,padding);
                SoftBox(gear,"Seat plinth",new Vector3(x,.67f,-.01f),new Vector3(.26f,.30f,.25f),.025f,dark);
                foreach(var dx in new[]{-.218f,.218f})SoftBox(gear,"Bucket bolster",new Vector3(x+dx,1.025f,-.07f),new Vector3(.052f,.24f,.30f),.017f,padding);
                Pipe(gear,"Restraint swing arm",new Vector3(x+side*.225f,.80f,-.13f),new Vector3(x+side*.225f,1.12f,.225f),.020f,steel);
                Pin(gear,"Restraint hinge",new Vector3(x+side*.238f,.80f,-.13f),.054f,.026f,brass,Quaternion.Euler(0,0,90));
                Pipe(gear,"Lap restraint crossbar",new Vector3(x-.205f,1.12f,.225f),new Vector3(x+.205f,1.12f,.225f),.023f,steel);
                SoftBox(gear,"Padded lap restraint",new Vector3(x,1.115f,.205f),new Vector3(.34f,.11f,.13f),.035f,padding);
                foreach(var end in new[]{-1,1}){
                    var z=end*.40f;
                    SoftBox(gear,"Captive wheel yoke",new Vector3(side*.39f,.10f,z),new Vector3(.09f,.37f,.21f),.025f,dark);
                    Pipe(gear,"Upstop stub axle",new Vector3(side*.30f,-.10f,z),new Vector3(side*.41f,-.10f,z),.027f,steel);
                    Pin(gear,"Lateral guide roller",new Vector3(side*.40f,-.01f,z),.055f,.060f,padding,Quaternion.identity);
                    for(var n=0;n<2;n++)Pin(gear,"Carrier bolt",new Vector3(side*.44f,.11f+n*.09f,z),.021f,.02f,steel,Quaternion.Euler(0,0,90));
                }
            }
            SoftBox(body,"Coaster nose",new Vector3(0,.625f,.675f),new Vector3(1.09f,.28f,.27f),.065f,paint);
            SoftBox(body,"Nose inset",new Vector3(0,.625f,.818f),new Vector3(.71f,.09f,.016f),.004f,dark);
            for(var i=0;i<7;i++)Box(body,"Nose grille fin",new Vector3((i-3)*.095f,.625f,.831f),new Vector3(.013f,.071f,.013f),steel);
            SoftBox(body,"Rear shell bridge",new Vector3(0,.68f,-.685f),new Vector3(1.09f,.34f,.095f),.025f,paint);
            foreach(var x in new[]{-.28f,.28f})for(var i=0;i<4;i++)Box(gear,"Footwell anti slip",new Vector3(x,.538f,.31f+i*.062f),new Vector3(.35f,.012f,.018f),steel);
            foreach(var wheel in nodes.Where(n=>n.name=="Wheel tyre"||n.name=="Recessed wheel web"))wheel.GetComponent<Renderer>().sharedMaterial=padding;
            Hide(nodes,"Cast spoke");
        }
        static void Coach(Transform[] nodes)
        {
            Repaint(nodes,paint,"Coach side","Cargo side","End wall");
            foreach(var r in nodes.Where(n=>n.name=="Coach roof")){
                r.GetComponent<Renderer>().enabled=false;
                ArchedRoof(roof,"Coach barrel roof",r.localPosition,r.localScale.x,r.localScale.z,.14f,dark);
            }
            foreach(var side in nodes.Where(n=>n.name=="Coach side"||n.name=="Cargo side")){
                var c=side.localPosition;var s=side.localScale;var x=c.x+Mathf.Sign(c.x)*(s.x/2+.012f);
                foreach(var y in new[]{c.y-s.y*.32f,c.y+s.y*.30f})Box(body,"Coach waist lining",new Vector3(x,y,c.z),new Vector3(.016f,.021f,s.z-.08f),cream);
                for(var i=0;i<Mathf.CeilToInt(s.z/.45f);i++)Box(body,"Coach panel stile",new Vector3(x,c.y,c.z-s.z/2+.08f+i*.43f),new Vector3(.015f,s.y*.72f,.019f),brass);
            }
            BenchSlats(nodes);
        }
        static Transform Group(Transform parent,string name){var n=new GameObject(name).transform;n.SetParent(parent,false);return n;}
        static void Batch(Transform group)
        {
            const string folder="Assets/EcoMinecarts/VehicleDetailMeshes";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/EcoMinecarts","VehicleDetailMeshes");
            var filters=group.GetComponentsInChildren<MeshFilter>();var index=0;
            foreach(var set in filters.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial)){
                var mesh=new Mesh{name=key+group.name+index};
                mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(set.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=group.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
                var path=folder+"/"+mesh.name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
                var n=new GameObject(group.name+" Detail "+index++,typeof(MeshFilter),typeof(MeshRenderer));n.transform.SetParent(group,false);
                n.GetComponent<MeshFilter>().sharedMesh=saved;n.GetComponent<Renderer>().sharedMaterial=set.Key;
            }
            // Each static finish group is a handful of material meshes, keeping
            // the added detail from creating hundreds of small draw calls.
            foreach(var f in filters)Object.DestroyImmediate(f.gameObject);
        }
        internal static bool Handles(GameObject root)=>root.GetComponent<RCCCarControllerV2>()!=null
            &&(root.transform.Find("CabFittings")!=null || new[]{"HeritageTramObject","RollerCoasterCartObject","PassengerCarObject","LargePassengerCarObject","CoalTenderObject","LargeCoalTenderObject","LargeCargoCarObject"}.Contains(root.name));
        public static void Apply(GameObject root,IReadOnlyDictionary<string,Material> materials)
        {
            if(!Handles(root))return;
            key=root.name;var isEngine=root.transform.Find("CabFittings")!=null;
            var old=root.transform.Find("VehicleDetail");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var nodes=root.GetComponentsInChildren<Transform>(true);var detail=Group(root.transform,"VehicleDetail");
            body=Group(detail,"Body");gear=Group(detail,"Frame");roof=Group(detail,"Roof");
            steel=materials["MAT_IronBare"];wood=materials["MAT_WoodRail"];
            dark=RailVisualFinish.Accent("MAT_VehicleCharcoal",steel,new Color(.065f,.075f,.08f));
            brass=RailVisualFinish.Accent("MAT_VehicleBrass",steel,new Color(.52f,.35f,.13f));
            cream=RailVisualFinish.Accent("MAT_VehicleIvory",steel,new Color(.80f,.73f,.55f));
            padding=RailVisualFinish.Accent("MAT_CoasterPadding",steel,new Color(.095f,.12f,.14f));
            var color=key=="HeritageTramObject"?new Color(.34f,.065f,.048f):key=="RollerCoasterCartObject"?new Color(.075f,.28f,.38f):new Color(.105f,.22f,.17f);
            paint=RailVisualFinish.Accent("MAT_"+key+"Livery",steel,color);
            if(isEngine)Engine(root,nodes);else if(key=="HeritageTramObject")Tram(root,nodes);else if(key=="RollerCoasterCartObject")Coaster(root,nodes);else Coach(nodes);
            foreach(Transform group in detail)Batch(group);
        }
    }
}
