// The volumetric clouds' look, in one place. Volumetrics.shader includes it; tools/cloud-preview reads these same lines
// to draw its previews, and the atmosphere check fails if CloudLook.cs disagrees with any value both have. Change a value
// here and nowhere else. Every line is "#define TG_CLOUD_<name> <number>": the preview reads nothing else.
#ifndef TG_CLOUD_LOOK_INCLUDED
#define TG_CLOUD_LOOK_INCLUDED

// The layer: its base sinks and it thickens as the sky closes into a deck. m.
#define TG_CLOUD_THICKNESS 900.0
#define TG_CLOUD_DECK_THICKENS 600.0
#define TG_CLOUD_DECK_LOWERS 300.0

// How much of the sky the clouds may take: soft, so the 3D noise draws each cloud's edge.
#define TG_CLOUD_COVER_FLOOR 0.31
#define TG_CLOUD_COVER_SLOPE 2.5

// The 3D noise's periods, m: each cloud's shape (and, far off, the larger, flatter shapes they merge into), the billows
// that eat into it and the detail that frays its edges. Every one divides TG_CLOUD_REPEAT, where the wind's drift wraps.
#define TG_CLOUD_SHAPE_PERIOD 7200.0
#define TG_CLOUD_FAR_SHAPE_PERIOD 21600.0
#define TG_CLOUD_BILLOW_PERIOD 900.0
#define TG_CLOUD_DETAIL_PERIOD 300.0
#define TG_CLOUD_REPEAT 21600.0

// Where the clouds start to merge into the far field, and where they have, m from the camera.
#define TG_CLOUD_FAR_NEAR 4000.0
#define TG_CLOUD_FAR 16000.0
// How much of their cover the far clouds lose: perspective packs them together, so the low sky stays mostly clear.
#define TG_CLOUD_FAR_THINS 0.75

// The shape: where in the layer its rounded top starts to narrow, how much the billows and detail take, how thick it is.
#define TG_CLOUD_TOP_ROUNDS 0.2
#define TG_CLOUD_EROSION 0.35
#define TG_CLOUD_FRAY 0.1
#define TG_CLOUD_DENSITY 3.0
#define TG_CLOUD_CUT 0.06
// How thick a cloud's shape must get before it shows at all: the tips of the noise that only just clear the cover would
// be scraps a few pixels across.
#define TG_CLOUD_SHAPE_CUT 0.18
#define TG_CLOUD_EXTINCTION 0.04

// The overcast deck's periods, m: how its underside rolls and where it is thick or thin.
#define TG_CLOUD_DECK_ROLL_PERIOD 2700.0
#define TG_CLOUD_DECK_LUMP_PERIOD 5400.0
#define TG_CLOUD_DECK_PATCH_PERIOD 2160.0
#define TG_CLOUD_DECK_THIN 0.25
#define TG_CLOUD_DECK_DENSITY 0.45

// The light: how bright the sun lights the clouds, the sun's light scattered on twice more inside the cloud, the sky's light, how thick cloud above shuts the
// sky's light out, and the haze laying over far clouds, as a multiple of the haze's own reach.
#define TG_CLOUD_SUN 0.85
#define TG_CLOUD_SCATTER_ONCE 0.6
#define TG_CLOUD_SCATTER_TWICE 0.3
#define TG_CLOUD_AMBIENT 0.5
#define TG_CLOUD_AMBIENT_DECK 0.4
#define TG_CLOUD_SHUT_OUT 0.3
#define TG_CLOUD_HAZE_REACH 8.0
#define TG_CLOUD_DECK_HAZE_REACH 24.0

// The march: short steps a share of the distance, within a least and a most, m, and strides through empty air this many
// short steps long. Steps a ray takes at most, at High and at Ultra.
#define TG_CLOUD_STEP_SHARE 0.01
#define TG_CLOUD_STEP_MIN 40.0
#define TG_CLOUD_STEP_MAX 200.0
#define TG_CLOUD_STRIDE 3.0
#define TG_CLOUD_HIGH_STEPS 48.0
#define TG_CLOUD_ULTRA_STEPS 64.0

#endif
