using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // Native terrain meshes: one selectable shape occupies one grid cell.
    public static class TrackBlockAssetBuilder
    {
        private const string Root = "Assets/EcoMinecarts/TrackBlocks";
        private static Material Iron => AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronBare.mat");
        private static Material Wood => AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_WoodRail.mat");
        public static Material TramBed
        {
            get
            {
                var path=Root+"/MAT_TramStreetBed.mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material!=null)return material;
                if(!AssetDatabase.IsValidFolder(Root))AssetDatabase.CreateFolder("Assets/EcoMinecarts","TrackBlocks");
                material=new Material(Iron){name="MAT_TramStreetBed",color=new Color(.48f,.51f,.54f)};
                AssetDatabase.CreateAsset(material,path);
                return material;
            }
        }

        public static BlockSet Build(bool chain = false, bool wooden = false, bool tram = false)
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/EcoMinecarts", "TrackBlocks");
            var prefix = wooden ? "WoodenTrack" : chain ? "MinecartChain" : tram ? "TramTrack" : "MinecartTrack";
            var set = AssetDatabase.LoadAssetAtPath<BlockSet>(Root + "/" + prefix + ".asset");
            if (set == null) { set = ScriptableObject.CreateInstance<BlockSet>(); AssetDatabase.CreateAsset(set, Root + "/" + prefix + ".asset"); }
            set.Blocks.Clear();
            string[] shapes = { "Straight", "Bend", "Stopper", "Slope1", "Slope2", "Slope3", "Slope4", "RampTop", "RampTop1", "RampTop2", "RampTop3", "RampTop4" };
            if (chain) shapes = shapes.Concat(new[] { "BendLeft" }).ToArray();
            if (tram) shapes = shapes.Concat(new[] { "Crossing" }).ToArray();
            foreach (var shape in shapes)
            for (var turn = 0; turn < 4; turn++)
            {
                var name = prefix + shape + (turn == 0 ? "" : (turn * 90).ToString()) + "Block";
                var root = new GameObject(name);
                var geometry = new GameObject("Geometry");
                geometry.transform.SetParent(root.transform, false);
                var geometryShape = shape == "BendLeft" ? "Bend" : shape;
                BuildShape(geometry.transform, geometryShape);
                if (chain) BuildChain(geometry.transform, geometryShape);
                // Rotating the symmetric right arc gives the mirrored left arc,
                // without negative-scale winding/normal problems.
                geometry.transform.localRotation = Quaternion.Euler(0, turn * 90 + (shape == "BendLeft" ? 90 : 0), 0);
                Register(set, root, name, shape.StartsWith("Slope") ? 1.15f : shape == "Stopper" ? .65f : .15f);
            }
            var origin = new GameObject(prefix + "Block");
            BuildShape(origin.transform, "Straight");
            if (chain) BuildChain(origin.transform, "Straight");
            Register(set, origin, origin.name, .15f);
            for (var count = 1; count <= 4; count++)
            {
                var stack = new GameObject(prefix + "Stacked" + count + "Block");
                for (var i = 0; i < count; i++)
                {
                    var layer = new GameObject("Layer" + i);
                    layer.transform.SetParent(stack.transform, false);
                    layer.transform.localPosition = Vector3.up * (i * .18f);
                    BuildShape(layer.transform, "Straight");
                    if (chain) BuildChain(layer.transform, "Straight");
                }
                Register(set, stack, stack.name, count * .18f);
            }
            EditorUtility.SetDirty(set);
            Debug.Log("ECO_TRACK_BLOCKS_OK: " + prefix + " " + set.Blocks.Count + " voxel definitions, four rotations per form.");
            return set;
        }

        private static void Register(BlockSet set, GameObject root, string name, float height)
        {
            var primary = name.StartsWith("WoodenTrack") ? Wood : Iron;
            var secondary = name.StartsWith("TramTrack") ? TramBed : Wood;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                if (renderer.sharedMaterial == null) throw new InvalidOperationException(name + " has a missing material.");
            var mesh = BakeMesh(root, name);
            VerifySupportClearance(name.Substring(0, name.Length - 5), mesh);
            height = Mathf.Max(.15f, mesh.bounds.max.y + .5f);
            var fallback = new Mesh { name = name + "VisualFallbackMesh" };
            fallback.CombineMeshes(Enumerable.Range(0, mesh.subMeshCount).Select(i => new CombineInstance
                { mesh = mesh, subMeshIndex = i, transform = Matrix4x4.identity }).ToArray(), true);
            fallback = SaveAsset(fallback, Root + "/" + name + "VisualFallbackMesh.asset");
            var collider = SaveAsset(BuildCollisionDeck(root, name, fallback), Root + "/" + name + "CollisionMesh.asset");
            var lods = AssetDatabase.LoadAssetAtPath<BlockMeshLodGroup>(Root + "/" + name + "LODs.asset");
            if (lods == null) { lods = ScriptableObject.CreateInstance<BlockMeshLodGroup>(); AssetDatabase.CreateAsset(lods, Root + "/" + name + "LODs.asset"); }
            lods.LOD0 = new[] { new MeshAndFlags { mesh = mesh, concaveFaces = PerFaceFlag.All } };
            lods.Collider = collider;
            lods.LOD1 = new MeshAndFlags { mesh = fallback, concaveFaces = PerFaceFlag.All };
            lods.LOD2 = lods.LOD1;
            EditorUtility.SetDirty(lods);
            // Keep the authoring prefab solely for icons and visual comparisons.
            // Eco's carried/preview/chunk mesh paths consume CustomBuilder data,
            // not the standalone prefab that the earlier tests instantiated.
            if (name.StartsWith("WoodenTrack"))
                foreach (var renderer in root.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = Wood;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            // Installed IronPipe supplies BOTH mesh and blockMeshLodGroup. Keep
            // the compatibility/preview path populated as well as chunk LOD data.
            var source = new GameObject(name + "TerrainSource", typeof(MeshFilter), typeof(MeshRenderer));
            source.GetComponent<MeshFilter>().sharedMesh = mesh;
            source.GetComponent<MeshRenderer>().sharedMaterials = new[] { primary, secondary };
            var sourcePrefab = PrefabUtility.SaveAsPrefabAsset(source, Root + "/" + name + "TerrainSource.prefab");
            UnityEngine.Object.DestroyImmediate(source);
            var path = Root + "/" + name + "TerrainBuilder.asset";
            var builder = AssetDatabase.LoadAssetAtPath<CustomBuilder>(path);
            if (builder == null) { builder = ScriptableObject.CreateInstance<CustomBuilder>(); AssetDatabase.CreateAsset(builder, path); }
            builder.usageCases = new List<MeshUsageCase> { new MeshUsageCase {
                enabled = true, mesh = sourcePrefab, blockMeshLodGroup = lods, applyConditionsToAllRotations = false,
                dontRotateBaseMesh = true, isMeshFacesConcave = PerFaceFlag.All } };
            builder.previewMaterial = primary;
            EditorUtility.SetDirty(builder);
            // Eco's client block registry uses the server type name WITHOUT "Block".
            // Keep asset filenames stable (icons reference them), but register the
            // native key. Full type names silently resolve to Eco's fallback cube.
            if (!name.EndsWith("Block", StringComparison.Ordinal)) throw new InvalidOperationException(name);
            // Eco maps submesh 0 to Block.Material. Block.Materials begins at
            // submesh 1; it does not repeat the main material. Repeating Iron here
            // assigned Iron to the sleeper submesh and left Wood unused in-game.
            set.Blocks.Add(new Block { Name = name.Substring(0, name.Length - 5), Builder = builder, Material = primary,
                Materials = new[] { secondary }, Solid = true, WaterLoggable = true, BuildCollider = true, GenerateMeshCollider = true,
                OverrideSubMaterialsTransparency = new OverrideMaterialTransparency[1],
                Rendered = true, PrefabHeightOffset = -.5f, ActualHeight = height,
                Category = name.StartsWith("TramTrack") ? "Tram Rail" : "Minecart Track", AudioCategory = "Metal", Tier = 1 });
        }

        private static Mesh BuildCollisionDeck(GameObject root, string name, Mesh fallback)
        {
            if (name.Contains("Stacked")) return UnityEngine.Object.Instantiate(fallback);
            var prefixLength = PrefixLength(name);
            var shape = name.Substring(prefixLength, name.Length - prefixLength - 5);
            var turn = 0;
            foreach (var degrees in new[] { 270, 180, 90 })
                if (shape.EndsWith(degrees.ToString(), StringComparison.Ordinal))
                { turn = degrees; shape = shape.Substring(0, shape.Length - degrees.ToString().Length); break; }
            if (shape == "BendLeft") { shape = "Bend"; turn += 90; }
            var overlay = shape.StartsWith("RampTop");
            var full = shape == "RampTop";
            var slope = overlay || shape.StartsWith("Slope");
            var phase = !slope || full ? 1 : int.Parse(shape.Substring(overlay ? 7 : 5));
            var low = (phase - 1) * .25f - (overlay ? 1 : 0);
            var rise = full ? 1f : slope ? .25f : 0;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var rotation = Quaternion.Euler(0, turn, 0);
            // A closed, continuous wheel-support deck, independent of visual sleeper gaps.
            // Width covers both wheels but does not fill the curve's whole bounding box.
            void Panel(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var start = vertices.Count;
                foreach (var point in new[] { a, b, c, d, a - Vector3.up * .025f,
                    b - Vector3.up * .025f, c - Vector3.up * .025f, d - Vector3.up * .025f })
                    vertices.Add(rotation * (point - Vector3.up * .5f));
                foreach (var index in new[] { 0,1,2, 0,2,3, 4,6,5, 4,7,6,
                    0,4,5, 0,5,1, 1,5,6, 1,6,2, 2,6,7, 2,7,3, 3,7,4, 3,4,0 })
                    triangles.Add(start + index);
            }
            if (shape == "Bend")
            {
                Vector3 Arc(float radius, float angle) => new Vector3(.5f + radius * Mathf.Cos(angle), .15f, -.5f + radius * Mathf.Sin(angle));
                // One watertight strip with shared seams, not 24 touching boxes
                // with internal faces that confuse cooked edge contacts.
                for (var i = 0; i <= 24; i++)
                {
                    var angle = Mathf.PI - i * Mathf.PI / 48;
                    var inner = Arc(.04f, angle); var outer = Arc(.95f, angle);
                    foreach (var point in new[] { inner, outer, inner - Vector3.up * .025f, outer - Vector3.up * .025f })
                        vertices.Add(rotation * (point - Vector3.up * .5f));
                    if (i == 24) continue;
                    var start = i * 4;
                    foreach (var index in new[] { 0,1,5, 0,5,4, 2,7,3, 2,6,7,
                        0,4,6, 0,6,2, 1,3,7, 1,7,5 }) triangles.Add(start + index);
                }
                triangles.AddRange(new[] { 0,2,3, 0,3,1, 96,97,99, 96,99,98 });
            }
            else if (shape == "Crossing")
            {
                Panel(new Vector3(-.45f, .15f, -.5f), new Vector3(-.45f, .15f, .5f),
                    new Vector3(.45f, .15f, .5f), new Vector3(.45f, .15f, -.5f));
                rotation = Quaternion.Euler(0, turn + 90, 0);
                Panel(new Vector3(-.45f, .15f, -.5f), new Vector3(-.45f, .15f, .5f),
                    new Vector3(.45f, .15f, .5f), new Vector3(.45f, .15f, -.5f));
                rotation = Quaternion.Euler(0, turn, 0);
            }
            else Panel(new Vector3(-.45f, low + .15f, -.5f), new Vector3(-.45f, low + rise + .15f, .5f),
                new Vector3(.45f, low + rise + .15f, .5f), new Vector3(.45f, low + .15f, -.5f));
            var deck = new Mesh();
            deck.SetVertices(vertices); deck.SetTriangles(triangles, 0);
            // Eco copies collision meshes through the same ThreadSafeMeshCopy path
            // as visual meshes. That path unconditionally reads TexCoord0.
            deck.SetUVs(0, vertices.Select(v => new Vector2(v.x, v.z)).ToList());
            deck.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), vertices.Count).ToArray();
            deck.RecalculateNormals(); deck.RecalculateTangents(); deck.RecalculateBounds();
            var parts = new List<CombineInstance> { new CombineInstance { mesh = deck, transform = Matrix4x4.identity } };
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>().Where(f => f.name == "Buffer" || f.name == "Brace"))
                parts.Add(new CombineInstance { mesh = filter.sharedMesh,
                    transform = Matrix4x4.Translate(Vector3.down * .5f) * root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix });
            var collision = new Mesh { name = name + "CollisionMesh" };
            collision.CombineMeshes(parts.ToArray(), true);
            collision.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), collision.vertexCount).ToArray();
            collision.RecalculateTangents();
            UnityEngine.Object.DestroyImmediate(deck);
            // Test every longitudinal interval for missing wheel support using the actual collider.
            var probe = new GameObject("CollisionDeckProbe", typeof(MeshCollider));
            var physics = probe.GetComponent<MeshCollider>(); physics.sharedMesh = collision;
            for (var i = 1; i < 40; i++)
            foreach (var side in new[] { -1, 1 })
            {
                var t = i / 40f;
                var angle = Mathf.PI - t * Mathf.PI / 2;
                var point = shape == "Bend" ? new Vector3(.5f + (.5f + side * .30f) * Mathf.Cos(angle), .15f,
                    -.5f + (.5f + side * .30f) * Mathf.Sin(angle)) : new Vector3(side * .30f, low + rise * t + .15f, t - .5f);
                point = rotation * (point - Vector3.up * .5f);
                if (!HasSupport(physics, point))
                    throw new InvalidOperationException(name + " has a wheel-support collision gap at " + point);
            }
            UnityEngine.Object.DestroyImmediate(probe);
            return collision;
        }

        public static bool HasSupport(MeshCollider collider, Vector3 point)
        {
            bool Hit(Vector3 offset) => collider.Raycast(new Ray(point + offset + Vector3.up * .01f, Vector3.down), out var hit, .02f);
            if (Hit(Vector3.zero)) return true;
            // PhysX ray/triangle tests can miss a point exactly on a shared seam.
            // Require support on BOTH sides within 10 microns, not just one nearby hit.
            return (Hit(Vector3.right * .00001f) && Hit(Vector3.left * .00001f))
                || (Hit(Vector3.forward * .00001f) && Hit(Vector3.back * .00001f));
        }

        private static Mesh BakeMesh(GameObject root, string name)
        {
            var parts = new List<Mesh>();
            foreach (var material in new[] { Iron, name.StartsWith("TramTrack") ? TramBed : Wood })
            {
                var instances = root.GetComponentsInChildren<MeshFilter>().Where(f => f.GetComponent<MeshRenderer>().sharedMaterial == material)
                    .Select(f => new CombineInstance { mesh = f.sharedMesh,
                        transform = Matrix4x4.Translate(Vector3.down * .5f) * root.transform.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray();
                var part = new Mesh();
                part.CombineMeshes(instances, true);
                parts.Add(part);
            }
            var mesh = new Mesh { name = name + "TerrainMesh" };
            mesh.CombineMeshes(parts.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), false);
            mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), mesh.vertexCount).ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            foreach (var part in parts) UnityEngine.Object.DestroyImmediate(part);
            // Leave the CPU copy readable: Eco builds chunk geometry from it.
            if (!mesh.isReadable || mesh.subMeshCount != 2 || mesh.vertexCount < 24) throw new InvalidOperationException("Invalid terrain mesh " + name);
            return SaveAsset(mesh, Root + "/" + name + "TerrainMesh.asset");
        }

        // Terrain meshes use cell-center coordinates; runtime support heights
        // use the bottom of that cell. Ramp overlays intentionally occupy the
        // empty space above a ramp in the cell below, not a full dirt block.
        public static void VerifySupportClearance(string name, Mesh mesh)
        {
            var shape = name.Substring(PrefixLength(name));
            var turn = 0;
            foreach (var degrees in new[] { 270, 180, 90 })
                if (shape.EndsWith(degrees.ToString(), StringComparison.Ordinal))
                { turn = degrees; shape = shape.Substring(0, shape.Length - degrees.ToString().Length); break; }
            var overlay = shape.StartsWith("RampTop");
            var full = shape == "RampTop";
            var slope = overlay || shape.StartsWith("Slope");
            var phase = !slope || full ? 1 : int.Parse(shape.Substring(overlay ? 7 : 5));
            var low = (phase - 1) * .25f - (overlay ? 1 : 0);
            var rise = full ? 1f : slope ? .25f : 0;
            foreach (var vertex in mesh.vertices)
            {
                var local = Quaternion.Euler(0, -turn, 0) * vertex;
                var support = low + rise * (local.z + .5f);
                if (local.y + .5f - support < .005f)
                    throw new InvalidOperationException(name + " penetrates its support surface at " + vertex);
            }
        }

        private static int PrefixLength(string name)=>name.StartsWith("WoodenTrack")?11:name.StartsWith("TramTrack")?9:13;

        private static T SaveAsset<T>(T generated, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void BuildShape(Transform root, string shape)
        {
            var tram = root.root.name.StartsWith("TramTrack", StringComparison.Ordinal);
            if (shape == "Bend")
            {
                // The compact curve joins adjacent face centers, preserving .60 gauge.
                for (var side = -1; side <= 1; side += 2)
                {
                    var radius = .5f + side * .30f;
                    var angles=Enumerable.Range(0,49).Select(i=>Mathf.PI-i*Mathf.PI/96).ToArray();
                    RailSectionMesh.Create(root,root.root.name+root.name+"Arc"+side,
                        angles.Select(a=>new Vector3(.5f+radius*Mathf.Cos(a),.15f,-.5f+radius*Mathf.Sin(a))).ToArray(),
                        angles.Select(a=>new Vector3(-Mathf.Cos(a),0,-Mathf.Sin(a))).ToArray(),.055f,.075f,Iron,root.root.name.StartsWith("WoodenTrack"));
                }
                for (var i = 0; i < 5; i++)
                {
                    var angle = Mathf.PI - (i + .5f) * Mathf.PI / 10;
                    var center = new Vector3(.5f + .5f * Mathf.Cos(angle), .045f, -.5f + .5f * Mathf.Sin(angle));
                    var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Beam(root, tram ? "Street tie" : "Sleeper", center - radial * .40f, center + radial * .40f,
                        tram ? .065f : .095f, tram ? .045f : .07f, tram ? TramBed : Wood);
                }
                return;
            }
            if (shape == "Crossing")
            {
                foreach (var angle in new[] { 0f, 90f })
                {
                    var leg = new GameObject("CrossingLeg");
                    leg.transform.SetParent(root, false);
                    leg.transform.localRotation = Quaternion.Euler(0, angle, 0);
                    BuildShape(leg.transform, "Straight");
                }
                return;
            }
            var rampTop = shape.StartsWith("RampTop");
            var fullSlope = shape == "RampTop";
            var slope = shape.StartsWith("Slope") || rampTop;
            var phase = fullSlope ? 1 : slope ? int.Parse(shape.Substring(rampTop ? 7 : 5)) : 1;
            // The voxel is placed above its supporting ramp; geometry descends into
            // the ramp's empty upper volume. The underlying terrain is preserved.
            var low = (phase - 1) * .25f - (rampTop ? 1f : 0f);
            var rise = fullSlope ? 1f : slope ? .25f : 0;
            for (var side = -1; side <= 1; side += 2)
                RailSectionMesh.Create(root,root.root.name+root.name+"Straight"+side,
                    new[]{new Vector3(side*.30f,low+.15f,-.5f),new Vector3(side*.30f,low+rise+.15f,.5f)},
                    new[]{Vector3.right,Vector3.right},.055f,.075f,Iron,root.root.name.StartsWith("WoodenTrack"));
            for (var i = 0; i < 4; i++)
            {
                var t = (i + .5f) / 4;
                // Sleeper width extends +/- .05 along the slope. Clear the high
                // edge of the supporting surface as well as its center.
                var sleeperY = low + rise * t + .045f + rise * .05f;
                Beam(root, tram ? "Street tie" : "Sleeper", new Vector3(tram ? -.40f : -.45f, sleeperY, t - .5f),
                    new Vector3(tram ? .40f : .45f, sleeperY, t - .5f), tram ? .065f : .10f,
                    tram ? .045f : .07f, tram ? TramBed : Wood);
            }
            if (shape == "Stopper")
            {
                Beam(root, "Buffer", new Vector3(-.44f, .43f, .30f), new Vector3(.44f, .43f, .30f), .15f, .22f, Wood);
                foreach (var x in new[] { -.32f, .32f })
                    Beam(root, "Brace", new Vector3(x, .10f, -.2f), new Vector3(x, .43f, .3f), .10f, .10f, Iron);
            }
        }

        private static void BuildChain(Transform root, string shape)
        {
            if (shape == "Bend")
            {
                for (var i = 0; i < 16; i++)
                {
                    var angle = Mathf.PI - (i + .5f) * Mathf.PI / 32;
                    var center = new Vector3(.5f + .5f * Mathf.Cos(angle), .10f, -.5f + .5f * Mathf.Sin(angle));
                    var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    var tangent = new Vector3(Mathf.Sin(angle), 0, -Mathf.Cos(angle));
                    foreach (var side in new[] { -1, 1 })
                        Beam(root, "ChainSide", center + radial * (.055f * side) - tangent * .028f,
                            center + radial * (.055f * side) + tangent * .028f, .018f, .025f, Iron);
                    Beam(root, "ChainPin", center - radial * .055f, center + radial * .055f, .025f, .025f, Iron);
                }
                return;
            }
            var overlay = shape.StartsWith("RampTop");
            var full = shape == "RampTop";
            var slope = overlay || shape.StartsWith("Slope");
            var phase = !slope || full ? 1 : int.Parse(shape.Substring(overlay ? 7 : 5));
            var low = (phase - 1) * .25f - (overlay ? 1f : 0f);
            var rise = full ? 1f : slope ? .25f : 0;
            for (var i = 0; i < 10; i++)
            {
                var t = (i + .5f) / 10;
                var a = new Vector3(-.055f, low + rise * t + .10f, t - .53f);
                var b = new Vector3(.055f, low + rise * t + .10f, t - .47f);
                Beam(root, "ChainSide", a, a + Vector3.forward * .06f, .018f, .025f, Iron);
                Beam(root, "ChainSide", b - Vector3.forward * .06f, b, .018f, .025f, Iron);
                Beam(root, "ChainPin", a, b - Vector3.forward * .06f, .025f, .025f, Iron);
            }
        }

        private static void Beam(Transform parent, string name, Vector3 a, Vector3 b, float width, float height, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (a + b) * .5f;
            go.transform.localRotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
            go.transform.localScale = new Vector3(width, height, (b - a).magnitude);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
