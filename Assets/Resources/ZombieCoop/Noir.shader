// Нуар-фильтр для каждой части экрана: всё в оттенках серого,
// красным остаётся только кровь. Плюс контраст, зерно плёнки и виньетка.
// Расчёт идёт в гамма-пространстве, как в Godot-версии.
Shader "ZombieCoop/Noir"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Contrast ("Contrast", Float) = 1.3
        _Grain ("Grain", Float) = 0.07
        _Vignette ("Vignette", Float) = 0.5
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Contrast;
            float _Grain;
            float _Vignette;
            float _Seed;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float rand (float2 co)
            {
                return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                c = LinearToGammaSpace(c);
            #endif
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                lum = saturate((lum - 0.5) * _Contrast + 0.5);

                // Насколько пиксель «красный»: только кровь в игре имеет такой цвет.
                float redness = saturate((c.r - max(c.g, c.b)) * 3.0);
                float3 blood = float3(max(c.r, 0.6), 0.0, 0.02);
                float3 col = lerp(float3(lum, lum, lum), blood, redness);

                float t = _Time.y;
                float n = rand(i.uv * 731.0 + float2(t * 17.0 + _Seed, t * 9.0)) - 0.5;
                col += n * _Grain;

                float2 d = i.uv - 0.5;
                col *= 1.0 - dot(d, d) * _Vignette * 2.0;
                col = max(col, 0.0);
            #if !defined(UNITY_COLORSPACE_GAMMA)
                col = GammaToLinearSpace(col);
            #endif
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
