// 半透明の物（今いない段の石、置ける場所の印、今の段の板）を描く。
// Resources に置いてあるので、Web（WebGL）版にも必ず含まれる
Shader "Osero3D/Ghost"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 0.3)
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

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float shade : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                o.shade = 0.6 + 0.4 * saturate(dot(n, normalize(float3(0.4, 0.9, -0.3))));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(_BaseColor.rgb * i.shade, _BaseColor.a);
            }
            ENDCG
        }
    }
}
