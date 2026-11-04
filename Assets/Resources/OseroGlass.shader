// 「置ける場所」を表す半透明の青い立方体。辺を少し濃く描いて、マスの境目を見やすくする。
// Resources に置いてあるので、Web（WebGL）版にも必ず含まれる
Shader "Osero3D/Glass"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.35, 0.55, 1, 0.28)
        _EdgeWidth ("Edge Width", Float) = 0.05
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            float _EdgeWidth;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float shade : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                o.shade = 0.8 + 0.2 * dot(n, normalize(float3(0.4, 0.9, -0.3)));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 d = min(i.uv, 1 - i.uv);
                float edge = step(min(d.x, d.y), _EdgeWidth); // 面の端（立方体の辺）
                fixed4 c = _BaseColor;
                c.rgb *= i.shade;
                c.rgb = lerp(c.rgb, c.rgb * 0.6, edge);
                c.a = lerp(c.a, saturate(c.a * 2.8), edge);
                return c;
            }
            ENDCG
        }
    }
}
