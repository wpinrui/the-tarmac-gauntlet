// The race's sky: a zenith to horizon gradient that pales over the lower half of the sky, a band of haze in the fog's
// colour at the horizon so the far trees fade into the same air, the sunset glow towards the sun, the sun and moon,
// stars at night and two layers of drifting cloud. Every colour comes from TimeOfDay each frame.
// Under weather (_Overcast) the clouds grow, soften and close into a grey deck with broad darker patches. On a clear day
// (_Deep) the haze at the horizon is thinner, and a low sun warms the sky towards it (_GoldenGlow).
//
// With _SKY_DETAIL (the Sky detail graphics option) the sky is drawn with more care, still in one pass with no
// volumetrics: the air round
// the sun scatters its light in a bright halo and a broad glow, the sun is a sharp disc darker at its rim, the
// moon has a faint halo, and the clouds get a third, finer layer that breaks up their edges, light and shade
// from two steps towards the sun, darker bases where they are thick and bright rims where the sun is behind them.
//
// While the camera drawing has the volumetric clouds (_TgVolumetricClouds, set by VolumetricsFeature), the painted clouds
// are left out and the volumetric ones are laid over the sky in their place.
Shader "Hidden/TarmacGauntlet/Sky"
{
    Properties
    {
        _CloudTex ("Cloud noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _SKY_DETAIL
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);
            float4 _Zenith, _Horizon, _Glow, _Ground, _SunColor, _CloudLit, _CloudShade, _Haze;
            float4 _SunDir, _MoonDir;
            float4 _CloudOffset;   // xy the big layer, zw the small one
            float _Cover, _Night, _CloudScale, _Overcast;
            // The clear day's palette (SkyPalette): how far it is on, which thins the horizon's haze, and the warm glow
            // towards a low sun laid over that haze.
            float _Deep;
            float4 _GoldenGlow;
            float _TgVolumetricClouds;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(Attributes v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float StarHash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // o is how overcast the sky is: the small layer's detail fades towards the deck.
            float Density(float2 uv, float o)
            {
                float small = lerp(SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv * 2.3 + _CloudOffset.zw).g, 0.5, 0.7 * o);
                return SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv + _CloudOffset.xy).r * 0.7 + small * 0.3;
            }

            // Henyey-Greenstein scattering towards the eye, scaled so it is 1 looking straight at the light.
            float Scatter(float cs, float g)
            {
                float k = 1.0 + g * g - 2.0 * g * cs;
                return (1.0 - g) * (1.0 - g) * (1.0 - g) / (k * sqrt(k));
            }

            // A disc of this angular radius, radians, about a direction whose cosine with the view is cs:
            // 1 inside, 0 outside, the edge smoothed over one pixel. rim is 0 at the middle and 1 at the edge.
            float Disc(float cs, float radius, out float rim)
            {
                float a = sqrt(max(2.0 * (1.0 - cs), 0.0));
                float px = max(fwidth(a), 1e-6);
                rim = saturate(a / radius);
                return saturate((radius - a) / px + 0.5);
            }

            // The sky was made while the game rendered in Gamma colour space, mixing sRGB colours. In Linear the colours
            // arrive in linear light, so they are taken back to sRGB, mixed just as before and the sky turned into linear
            // light at the end: it looks as it did, glows and sun disc included.
            float3 Srgb(float4 c)
            {
            #if defined(UNITY_COLORSPACE_GAMMA)
                return c.rgb;
            #else
                return LinearToSRGB(max(c.rgb, 0.0));
            #endif
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                float h = 1.0 - saturate(up);
                float3 sun = _SunDir.xyz;
                float2 dh = normalize(d.xz + 1e-5), sh = normalize(sun.xz + 1e-5);
                float towards = dot(dh, sh) * 0.5 + 0.5;
                float cs = dot(d, sun);
                float above = saturate(sun.y * 20.0 + 1.0);
                float o = _Overcast;

                // How much air the view looks through, 1 at the horizon falling away smoothly overhead: the deep blue
                // is only high up, and the sky pales through the lower half towards the horizon.
                float air = exp(-max(up, 0.0) * 3.2);
                float3 sky = lerp(Srgb(_Zenith), Srgb(_Horizon), air);
                // A low band of haze in the fog's colour, so the far trees and the sky meet in one colour: thinner and
                // fainter on a clear day, so the sky's blue comes down to it.
                float haze = exp(-abs(up) * (10.0 + 14.0 * _Deep));
                float hazeShows = 0.85 * (1.0 - 0.75 * _Deep);
                float golden = towards * towards * towards * towards * exp(-max(up, 0.0) * 4.0);
            #if defined(_SKY_DETAIL)
                float band = exp(-max(up, 0.0) * 6.5);
                sky += Srgb(_Glow) * (towards * towards * towards * band);
                sky = lerp(sky, Srgb(_Haze), haze * hazeShows);
                sky = lerp(sky, Srgb(_Ground), saturate(-up * 6.0) * (1.0 - haze * 0.6));
                sky += Srgb(_GoldenGlow) * golden;

                // The sun's halo and a broad glow round it, stronger through the thicker air low down.
                float scatter = Scatter(cs, 0.8) * 0.55 + Scatter(cs, 0.55) * 0.05;
                sky += Srgb(_SunColor) * scatter * (0.55 + 0.45 * air) * above;
                float rim;
                float disc = Disc(cs, 0.0145, rim) * above;
                // Limb darkening: the edge of the sun is a little darker and redder than its middle.
                float3 discCol = Srgb(_SunColor) * (4.0 * (1.0 - 0.45 * rim * rim)) * lerp(float3(1, 1, 1), float3(1.0, 0.8, 0.65), rim * rim);
                // Low down the sun sinks into the haze.
                discCol *= saturate(up * 25.0 + 0.4);
            #else
                float band = h * h * h * h * h * h;
                sky += Srgb(_Glow) * (towards * towards * towards * band);
                sky = lerp(sky, Srgb(_Haze), haze * hazeShows);
                sky = lerp(sky, Srgb(_Ground), saturate(-up * 6.0) * (1.0 - haze * 0.6));
                sky += Srgb(_GoldenGlow) * golden;
                sky += Srgb(_SunColor) * (pow(saturate(cs), 12.0) * 0.18 + pow(saturate(cs), 400.0) * 0.6) * above;
                float disc = smoothstep(0.99985, 0.99992, cs) * above;
                float3 discCol = Srgb(_SunColor) * 3.0;
            #endif

                float3 p = d * 500.0;
                float star = step(0.9975, StarHash(floor(p))) * saturate(1.0 - length(frac(p) - 0.5) * 2.2);
                star *= _Night * saturate(up * 4.0);
                float cm = dot(d, _MoonDir.xyz);
                float moonUp = saturate(_MoonDir.y * 20.0);
            #if defined(_SKY_DETAIL)
                float moonRim;
                float moon = Disc(cm, 0.011, moonRim) * moonUp;
                sky += float3(0.55, 0.6, 0.72) * (Scatter(cm, 0.85) * 0.08 * _Night * moonUp);
            #else
                float moon = smoothstep(0.99990, 0.99994, cm) * moonUp;
            #endif

                // Under cloud the forms grow to twice the size and their edges soften, so a cloudy sky
                // closes into a deck; its thicker parts are darker underneath.
                float2 uv = d.xz / (max(up, 0.0) * 0.85 + 0.15) * _CloudScale * lerp(1.0, 0.5, o);
                float n = Density(uv, o);
            #if defined(_SKY_DETAIL)
                // Finer detail eats into the edges, less of it in an overcast deck.
                float fine = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv * 6.1 + _CloudOffset.zw * 1.7).g;
                n += (fine - 0.5) * 0.1 * (1.0 - 0.6 * o);
                float c = smoothstep(_Cover - 0.2 * o, _Cover + 0.18 + 0.1 * o, n) * smoothstep(0.0, 0.12, up);
                // Light from two steps towards the sun: a cloud is lit where it thins towards the sun.
                float lit = saturate(0.55 + (n - Density(uv + sh * 0.03, o)) * 4.0 + (n - Density(uv + sh * 0.08, o)) * 2.5);
                lit = lerp(lit, 1.0 - smoothstep(0.3, 0.75, n), o);
                float thick = saturate((n - _Cover) / 0.35);
                float3 cloud = lerp(Srgb(_CloudShade), Srgb(_CloudLit), lit * (1.0 - 0.3 * thick)) * (1.0 - 0.35 * o * smoothstep(0.3, 0.8, n));
                // Thin edges with the sun behind them shine.
                float edge = saturate(c * (1.0 - c) * 4.0);
                cloud += Srgb(_SunColor) * (pow(saturate(cs), 8.0) * edge * 1.4 + pow(saturate(cs), 6.0) * (1.0 - c) * 0.6) * above * (1.0 - o);
                cloud = lerp(cloud, Srgb(_Haze), haze * 0.8);
            #else
                float c = smoothstep(_Cover - 0.2 * o, _Cover + 0.18 + 0.1 * o, n) * smoothstep(0.0, 0.12, up);
                float lit = saturate(0.55 + (n - Density(uv + sh * 0.035, o)) * 6.0);
                lit = lerp(lit, 1.0 - smoothstep(0.3, 0.75, n), o);
                float3 cloud = lerp(Srgb(_CloudShade), Srgb(_CloudLit), lit) * (1.0 - 0.35 * o * smoothstep(0.3, 0.8, n));
                cloud += Srgb(_SunColor) * pow(saturate(cs), 6.0) * (1.0 - c) * 0.6 * above;
                cloud = lerp(cloud, Srgb(_Haze), haze * 0.8);
            #endif

                c *= 1.0 - _TgVolumetricClouds;
                float3 col = sky + (discCol * disc + 0.9 * star + float3(0.85, 0.88, 0.95) * moon * _Night) * (1.0 - c);
                col = lerp(col, cloud, c);
            #if !defined(UNITY_COLORSPACE_GAMMA)
                col = SRGBToLinear(col);
            #endif
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
