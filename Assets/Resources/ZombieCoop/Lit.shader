// Матовый материал мира и фигур: цвет, свечение, металл и гладкость.
// Мир чёрно-белый, поэтому почти всё — оттенки серого.
Shader "ZombieCoop/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
        _Glossiness ("Smoothness", Range(0,1)) = 0
        _Metallic ("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.0

        struct Input { float3 worldPos; };

        fixed4 _Color;
        half4 _EmissionColor;
        half _Glossiness;
        half _Metallic;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            o.Emission = _EmissionColor.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
