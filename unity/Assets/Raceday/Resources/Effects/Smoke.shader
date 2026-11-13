// Tyre smoke (CarEffects): soft, lumpy round puffs, lit as little balls of haze. The sun lights the side facing it, wraps
// round a little and shines through when the camera looks towards it; the sky fills the shade; the sun's shadow and the
// race's lights reach it too. Soft: where the depth is drawn, a puff fades where it meets the track or a car. No texture
// is read. The particle system sends each puff's centre (CarEffects sets the streams), so the lighting turns with nothing.
// At night the floodlights and the headlamps light it all round, under the night's budget (Lamps.hlsl's LampAround). And
// at night smoke mostly scatters light rather
// than blocking it: a puff hides what is behind it only as far as it is lit itself, so it shows as a pale haze where a
// lamp catches it and never as a dark blot over a lit road.
Shader "Hidden/TarmacGauntlet/Smoke"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.82, 0.82, 0.84, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        // Premultiplied: the colour carries its own alpha, and the alpha says how much of what is behind is hidden.
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Assets/Rendering/Shaders/LitKeywords.hlsl"
            // Smoke is see-through, and the sun's shadow is read per vertex. URP's screen-space shadows are a texture of
            // the opaque scene, which a vertex can't read and which URP switches off before it draws see-through things.
            // Marked transparent, URP reads the shadow map itself in every variant.
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Rendering/Shaders/Lamps.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
            CBUFFER_END
            // Set by CarEffects: 1 when the scene's depth is drawn, so the puffs can fade into it; how far the lamps are on,
            // 0 by day to 1 at night.
            float _EffectsSoft;
            float _EffectsDark;

            // A puff at night is lit by no more than this, linear luminance, eased towards: a puff right in front of a
            // lamp glows but doesn't blow out.
            #define SMOKE_LAMP_CAP 0.15
            // At night a puff hides what is behind it as far as its own light reaches this, linear luminance: about the
            // brightest a lit road gets, so over any lit road it adds at least as much light as it hides.
            #define SMOKE_DARK_SHOWS 0.6

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;     // xy the quad's corner, zw the puff's centre x and y
                float uv1 : TEXCOORD1;     // the centre's z
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 offset : TEXCOORD1;   // from the puff's centre, over its radius
                float3 lit : TEXCOORD2;      // the sun's light reaching the puff, after its shadow
                float3 sky : TEXCOORD3;      // the sky's and the lights' light all round
                float3 toSun : TEXCOORD4;
                float3 toEye : TEXCOORD5;
                float fog : TEXCOORD6;
                float eye : TEXCOORD7;       // the puff's depth from the camera
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                float3 centre = float3(v.uv.zw, v.uv1);
                float3 off = world - centre;
                // A billboard's corner sits a radius times the square root of two from its centre.
                float radius = max(length(off) * 0.7071, 1e-3);
                o.offset = off / radius;
                o.positionCS = TransformWorldToHClip(world);
                o.color = v.color * _Tint;
                o.uv = v.uv.xy;
                o.toEye = normalize(_WorldSpaceCameraPos - centre);
                o.eye = -TransformWorldToView(world).z;

                Light sun = GetMainLight(TransformWorldToShadowCoord(centre));
                o.lit = sun.color * sun.shadowAttenuation;
                o.toSun = sun.direction;
                float3 sky = SampleSH(float3(0, 1, 0)) * 0.6 + SampleSH(o.toEye) * 0.4;
                // The lamps all round the puff's centre, found through Forward+'s clusters from where it is on screen.
                InputData at = (InputData)0;
                at.positionWS = centre;
                float4 clip = TransformWorldToHClip(centre);
                at.normalizedScreenSpaceUV = ComputeScreenPos(clip).xy / max(clip.w, 1e-4);
                float3 lamps = LampAround(at) * 0.6;
                float lum = dot(lamps * _Tint.rgb, float3(0.2126, 0.7152, 0.0722));
                o.sky = sky + lamps / (1.0 + lum / SMOKE_LAMP_CAP);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r2 = dot(p, p);
                // Lumpy edges, turning with the particle's own spin.
                float a = atan2(p.y, p.x);
                float lump = 1.0 + 0.09 * sin(a * 5.0 + 1.3) + 0.06 * sin(a * 9.0 + 2.0);
                float r = sqrt(r2) * lump;
                float body = saturate(1.0 - r);
                body = body * body * (3.0 - 2.0 * body);
                if (body <= 0.0) discard;

                // The puff's surface as a ball's, bulging towards the camera.
                float3 n = normalize(i.offset * 0.8 + i.toEye * sqrt(saturate(1.0 - r2)));
                float facing = dot(n, i.toSun);
                float wrap = saturate((facing + 0.5) / 1.5);
                // Sun through the smoke when the camera looks towards it.
                float through = pow(saturate(dot(-i.toEye, i.toSun)), 6.0) * 0.9;
                float3 light = i.lit * (wrap * 0.85 + through) + i.sky * (0.75 + 0.25 * n.y);
                half4 col = half4(_Tint.rgb * light, i.color.a * _Tint.a * body);

                if (_EffectsSoft > 0.5)
                {
                    float2 screen = GetNormalizedScreenSpaceUV(i.positionCS);
                    float scene = LinearEyeDepth(SampleSceneDepth(screen), _ZBufferParams);
                    col.a *= saturate((scene - i.eye) / 0.6);
                }
                // Puffs right at the lens thin out instead of filling the screen.
                col.a *= saturate((i.eye - 0.4) / 1.6);
                col.rgb = MixFog(col.rgb, i.fog);
                // By day a puff hides what is behind it; at night only as far as it is lit.
                float shows = saturate(dot(col.rgb, float3(0.2126, 0.7152, 0.0722)) / SMOKE_DARK_SHOWS);
                return half4(col.rgb * col.a, col.a * lerp(1.0, shows, _EffectsDark));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
