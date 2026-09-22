// 물 애니메이션을 칸마다 기록하지 않고 셰이더에서 처리한다.
// river_animated.png 는 한 줄에 20칸, 애니메이션 한 묶음이 가로로 5칸 이어져 있어서
// 프레임을 바꾸는 일이 U 좌표를 일정 간격만큼 미는 것과 같다.
Shader "AINPC/Water Frame Scroll"
{
    Properties
    {
        [PerRendererData] _MainTex ("스프라이트 텍스처", 2D) = "white" {}
        _Color ("색조", Color) = (1, 1, 1, 1)
        _FrameCount ("프레임 수", Float) = 5
        _FrameStepU ("프레임 간격 (U)", Float) = 0.05
        _FPS ("초당 프레임", Float) = 6
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "False"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Lighting Off

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
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _FrameCount;
            float _FrameStepU;
            float _FPS;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 지금 프레임 번호 (0 ~ _FrameCount-1)
                float frames = max(1.0, _FrameCount);
                float frame = fmod(floor(_Time.y * _FPS), frames);

                float2 uv = i.uv + float2(frame * _FrameStepU, 0.0);

                // 픽셀 아트라 텍셀 한가운데에서 뽑아야 한다.
                // 프레임 경계에 딱 걸치면 옆 프레임 끝줄이 1픽셀 새어 들어와 이음새가 보인다.
                uv = (floor(uv * _MainTex_TexelSize.zw) + 0.5) * _MainTex_TexelSize.xy;

                return tex2D(_MainTex, uv) * i.color * _Color;
            }
            ENDCG
        }
    }
}
