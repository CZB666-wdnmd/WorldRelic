Shader "Anique/InteractionOverlay"
{
    Properties { _Color ("Tint", Color) = (0,0.4,1,0.4) _MainTex ("Alpha", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Back Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color; sampler2D _MainTex;
            struct input { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct varying { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            varying vert(input v) { varying o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(varying,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex = UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(varying i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); fixed4 c=_Color; c.a *= tex2D(_MainTex,i.uv).a; return c; }
            ENDCG
        }
    }
}
