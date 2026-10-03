// Eco's native WorldObject painting writes these channel colours and
// _PaintedAmount. No client loader, paint RPC or separate colour state is used.
Shader "EcoMinecarts/Curved Paintable Vehicle"
{
    Properties
    {
        _Color("Original surface tint", Color) = (1,1,1,1)
        _MainTex("Original albedo", 2D) = "white" {}
        _BumpMap("Surface normal", 2D) = "bump" {}
        _BumpScale("Normal strength", Float) = 1
        _Metallic("Metallic", Range(0,1)) = 0
        _Glossiness("Smoothness", Range(0,1)) = 0.25
        _ChannelRedColor("Body paint", Color) = (0,0,0,0)
        _ChannelGreenColor("Frame paint", Color) = (0,0,0,0)
        _ChannelBlueColor("Roof paint", Color) = (0,0,0,0)
        _PaintedAmount("Native paint enabled", Range(0,1)) = 0
        _PaintCombinedTexture("RGB paint regions", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface Surface Standard fullforwardshadows vertex:RailVertex addshadow
        #pragma target 3.0
        // Eco switches distant world objects to indirect instancing. Without
        // its procedural transform, individual vehicle meshes drift apart.
        #pragma multi_compile_instancing
        #pragma instancing_options assumeuniformscaling lodfade procedural:IndirectRenderingSetup
        #pragma multi_compile __ NO_CURVE MINIMAP_NO_CURVE
        #include "../../EcoModKit/Shaders/CurvedHelper.cginc"
        #include "UnityStandardUtils.cginc"
        sampler2D _MainTex, _BumpMap, _PaintCombinedTexture;
        float4 _Color, _ChannelRedColor, _ChannelGreenColor, _ChannelBlueColor;
        float _Metallic, _Glossiness, _PaintedAmount, _BumpScale;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float2 uv_PaintCombinedTexture; };
        void RailVertex(inout appdata_full v)
        {
            // Eco's distant indirect renderer supplies each child mesh's transform
            // through procedural instancing. Set the ID before CurvedHelper reads
            // unity_ObjectToWorld, just as the native Curved/Standard pass does.
            UNITY_SETUP_INSTANCE_ID(v);
            CURVED_VERTEX(v.vertex);
        }
        void Surface(Input i, inout SurfaceOutputStandard o)
        {
            float3 original = tex2D(_MainTex, i.uv_MainTex).rgb;
            float3 mask = tex2D(_PaintCombinedTexture, i.uv_PaintCombinedTexture).rgb;
            // A clear/removed channel has alpha zero. Coat alpha comes directly
            // from Eco; partial coats blend back to the original textured finish.
            float3 weights = mask * float3(_ChannelRedColor.a, _ChannelGreenColor.a, _ChannelBlueColor.a);
            float coverage = saturate(dot(weights, float3(1,1,1))) * saturate(_PaintedAmount);
            float3 paint = (_ChannelRedColor.rgb * weights.r + _ChannelGreenColor.rgb * weights.g
                          + _ChannelBlueColor.rgb * weights.b) / max(dot(weights,float3(1,1,1)),0.0001);
            // Keep the grain/wear modulation without multiplying bright paint by
            // the very dark original iron colour. Unpainted pixels are untouched.
            float grain = saturate(dot(original,float3(.2126,.7152,.0722)) * 2);
            o.Albedo = lerp(original * _Color.rgb, paint * lerp(.72,1.0,grain), coverage);
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap,i.uv_BumpMap),_BumpScale);
            o.Metallic = lerp(_Metallic, .02, coverage);
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Curved/Standard"
}
