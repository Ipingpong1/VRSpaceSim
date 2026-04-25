Shader "Skybox/ProceduralSpace"
{
    Properties
    {
        [Header(Deep Space)]
        _SpaceColor ("Deep Space Color", Color) = (0.01, 0.01, 0.02, 1)

        [Header(Nebula)]
        _NebulaColor      ("Nebula Color",      Color)               = (0.08, 0.04, 0.25, 1)
        _NebulaBrightness ("Nebula Brightness", Range(0, 2))         = 0.35
        _NebulaScale      ("Nebula Scale",      Range(0.1, 6))       = 1.4
        _NebulaContrast   ("Nebula Contrast",   Range(0.5, 4))       = 2.0

        [Header(Stars)]
        _StarDensity    ("Star Density (higher = fewer stars)", Range(0.8, 0.9999)) = 0.975
        _StarBrightness ("Star Brightness",                     Range(0, 6))        = 2.0

        [Header(Milky Way)]
        _MilkyWayBrightness ("Milky Way Brightness", Range(0, 3))   = 1.0
        _MilkyWayWidth      ("Milky Way Width",      Range(0.05, 1)) = 0.22
        _MilkyWayTilt       ("Milky Way Tilt (deg)", Range(-90, 90)) = 20
        _MilkyWayColor      ("Milky Way Color",      Color)          = (0.55, 0.65, 1.0, 1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #include "UnityCG.cginc"

            // ---------------------------------------------------------------
            // Properties
            // ---------------------------------------------------------------
            fixed4 _SpaceColor;
            fixed4 _NebulaColor;
            half   _NebulaBrightness;
            half   _NebulaScale;
            half   _NebulaContrast;

            half _StarDensity;
            half _StarBrightness;

            half   _MilkyWayBrightness;
            half   _MilkyWayWidth;
            half   _MilkyWayTilt;
            fixed4 _MilkyWayColor;

            // ---------------------------------------------------------------
            // Structs
            // ---------------------------------------------------------------
            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---------------------------------------------------------------
            // Hash / noise helpers
            // ---------------------------------------------------------------

            // Fast 3-D value hash — no trig, good distribution
            float hash3(float3 p)
            {
                p  = frac(p * float3(443.8975, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            // Smooth 3-D value noise (trilinear interpolation of hashed lattice)
            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep

                return lerp(
                    lerp(lerp(hash3(i),                   hash3(i + float3(1,0,0)), f.x),
                         lerp(hash3(i + float3(0,1,0)),   hash3(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash3(i + float3(0,0,1)),   hash3(i + float3(1,0,1)), f.x),
                         lerp(hash3(i + float3(0,1,1)),   hash3(i + float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            // Fractal Brownian Motion — 4 octaves
            float fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                p += float3(31.41, 27.18, 14.28); // offset to avoid origin artefacts
                UNITY_UNROLL
                for (int i = 0; i < 4; i++)
                {
                    v += a * valueNoise(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return v;
            }

            // ---------------------------------------------------------------
            // Star field — noise threshold approach
            //
            // Samples valueNoise at a high frequency and keeps only the
            // brightest peaks. Because valueNoise is smoothly interpolated
            // (no hard lattice edges visible), star brightness changes
            // continuously as the view direction rotates — no cell-boundary
            // flickering in VR.
            // ---------------------------------------------------------------
            float starLayer(float3 dir, float scale, float threshold, float sharpness)
            {
                // Primary noise gives star existence / position
                float n = valueNoise(dir * scale);

                // Remap so only values above threshold survive, then sharpen to a pinpoint
                float star = pow(smoothstep(threshold, 1.0, n), sharpness);

                // Secondary noise offsets vary per-star brightness smoothly
                float brightness = valueNoise(dir * scale * 0.71 + 5.1) * 0.6 + 0.4;

                return star * brightness;
            }

            // ---------------------------------------------------------------
            // Vertex shader
            // ---------------------------------------------------------------
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz; // raw object-space position = view direction for skybox
                return o;
            }

            // ---------------------------------------------------------------
            // Fragment shader
            // ---------------------------------------------------------------
            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                // === 1. DEEP SPACE BASE ===
                float3 col = _SpaceColor.rgb;

                // === 2. NEBULA ===
                // Two offset FBM samples multiplied → patchy, non-uniform shapes.
                // Raised to _NebulaContrast to keep most of the sky dark.
                float3 np  = dir * _NebulaScale;
                float  n1  = fbm(np);
                float  n2  = fbm(np * 1.7 + float3(4.3, 1.7, 2.9));
                float  neb = pow(n1 * n2 * 4.0, _NebulaContrast);
                col += _NebulaColor.rgb * neb * _NebulaBrightness;

                // === 3. MILKY WAY BAND ===
                // Rotate around the X axis by _MilkyWayTilt degrees to tilt the plane.
                float tilt    = _MilkyWayTilt * UNITY_PI / 180.0;
                float milkyY  = dir.y * cos(tilt) - dir.z * sin(tilt);
                float band    = exp(-milkyY * milkyY / (_MilkyWayWidth * _MilkyWayWidth));

                // Soft nebula-like glow along the band
                float milkyGlow = fbm(dir * 2.8) * fbm(dir * 4.5 + 2.1);
                col += _MilkyWayColor.rgb * band * milkyGlow * _MilkyWayBrightness;

                // === 4. STAR FIELD ===
                // Three layers at different scales give near/mid/far depth.
                // sharpness controls pinpoint tightness — higher = smaller star disc.
                float stars = 0.0;
                stars += starLayer(dir,               200.0, _StarDensity,        8.0);
                stars += starLayer(dir * 1.3 + 0.71,  260.0, _StarDensity * 0.97, 10.0) * 0.75;
                stars += starLayer(dir * 1.7 + 1.31,  340.0, _StarDensity * 0.94, 12.0) * 0.55;

                // Sparse bright-star layer — larger, lower sharpness = soft halo
                float brightStars = starLayer(dir * 0.6 + 2.3, 90.0, max(0.0, _StarDensity - 0.01), 4.0) * 2.5;

                // Milky Way boosts star density along the band
                stars *= 1.0 + band * 3.5;

                col += stars       * _StarBrightness;
                col += brightStars * _StarBrightness;

                return fixed4(col, 1.0);
            }

            ENDCG
        }
    }
}
