Shader "Custom/SpriteOutline"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (1,1,0,1)
        _OutlineSize ("Outline Size", Float) = 1
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _OutlineColor;
            float _OutlineSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float alpha = col.a;
                float2 offset = _OutlineSize * _MainTex_TexelSize.xy;
                float maxAlpha = 0.0;
                maxAlpha = max(maxAlpha, tex2D(_MainTex, i.uv + float2(offset.x, 0)).a);
                maxAlpha = max(maxAlpha, tex2D(_MainTex, i.uv - float2(offset.x, 0)).a);
                maxAlpha = max(maxAlpha, tex2D(_MainTex, i.uv + float2(0, offset.y)).a);
                maxAlpha = max(maxAlpha, tex2D(_MainTex, i.uv - float2(0, offset.y)).a);
                float outline = step(0.001, maxAlpha) * step(alpha, 0.001);
                fixed4 result = _OutlineColor;
                result.a *= outline;
                return result;
            }
            ENDCG
        }
    }
}
