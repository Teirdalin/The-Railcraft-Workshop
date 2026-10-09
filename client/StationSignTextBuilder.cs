using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    public static class StationSignTextBuilder
    {
        internal static void Configure(GameObject root)
        {
            var old=root.transform.Find("Station lettering");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var host=new GameObject("Station lettering").transform;host.SetParent(root.transform,false);
            var font=RailVehicleTextBuilder.PrepareFont();
            const string path="Assets/EcoMinecarts/Materials/MAT_StationCaption.mat";
            var ink=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(ink==null){ink=new Material(font.material);AssetDatabase.CreateAsset(ink,path);}
            RailWorldMaterialBuilder.TextMaterial(ink,font.atlasTexture);
            ink.SetColor("_Color",new Color(.06f,.065f,.055f));EditorUtility.SetDirty(ink);
            var world=root.GetComponent<WorldObject>();int slot=RailWheelAnimationBuilder.StringIndex(world,"StationSignText");
            world.OnStringStateChanged[slot]=new ChangedStringStateEvent();
            foreach(var side in new[]{-1,1}){
                var label=new GameObject(side<0?"Front station text":"Rear station text",typeof(RectTransform));label.transform.SetParent(host,false);
                // Keep the lettering a few millimetres above the inset sign
                // faces, rather than floating in front of the wooden frame.
                label.transform.localPosition=new Vector3(.178f,.996f,side<0?-.101f:.0565f);
                label.transform.localRotation=Quaternion.Euler(0,side<0?0:180,0);
                var text=label.AddComponent<TextMeshPro>();text.font=font;text.fontSharedMaterial=ink;text.richText=false;text.text="";
                text.color=Color.white;text.alignment=TextAlignmentOptions.Center;text.textWrappingMode=TextWrappingModes.NoWrap;
                text.enableAutoSizing=true;text.fontSizeMin=.4f;text.fontSizeMax=1.6f;text.fontSize=1.6f;
                text.overflowMode=TextOverflowModes.Ellipsis;text.rectTransform.sizeDelta=new Vector2(.32f,.22f);
                var setter=(UnityEngine.Events.UnityAction<string>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<string>),text,typeof(TMP_Text).GetProperty("text").GetSetMethod());
                UnityEventTools.AddPersistentListener(world.OnStringStateChanged[slot],setter);
            }
        }
    }
}
