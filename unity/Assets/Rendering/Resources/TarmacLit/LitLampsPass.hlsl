// URP Lit's forward pass with the other lights shaded through Lamps.hlsl: its fragment's call to UniversalFragmentPBR
// becomes TarmacFragmentPBR, which is URP's own with the night's budget on the lamps.
#ifndef TARMAC_LIT_LAMPS_PASS_INCLUDED
#define TARMAC_LIT_LAMPS_PASS_INCLUDED

#include "Assets/Rendering/Shaders/Lamps.hlsl"
#define UniversalFragmentPBR TarmacFragmentPBR
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
#undef UniversalFragmentPBR

#endif
