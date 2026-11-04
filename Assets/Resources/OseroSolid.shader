// 石・盤の線・外枠など、ふつうの（不透明な）物を描く。光の向きを決め打ちした簡単な明暗とつやだけを付ける。
// Resources に置いてあるので、Web（WebGL）版にも必ず含まれる（Unity 既定のマテリアルは Web 版でピンクになることがある）
Shader "Osero3D/Solid"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BaseColor;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 l = normalize(float3(0.4, 0.9, -0.3));
                float3 h = normalize(l + normalize(i.v));
                float diff = 0.45 + 0.55 * saturate(dot(n, l));
                float spec = pow(saturate(dot(n, h)), 40) * 0.35;
                return fixed4(_BaseColor.rgb * diff + spec, 1);
            }
            ENDCG
        }
    }
}
