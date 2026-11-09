"""Matching points picked by eye in the reference renders.

Each landmark is one 3D point. L is the car's left instance, R its mirror
(same x and z, y negated), C a point on the centre line. 'F' marks a point
with free y that has no mirror (the exhaust). Pixel coordinates are (u, v).
"""

# name: (kind, weight, {view: {side: (u, v)}})
LANDMARKS = {
    'jet': ('LR', 1.0, {
        'front': {'L': (516, 235), 'R': (307, 234)},
        'top': {'L': (1115, 285), 'R': (1111, 472)},
        'three_quarter_front': {'L': (702, 354), 'R': (492, 332)},
    }),
    'badge_front': ('C', 1.0, {
        'front': {'C': (412, 368)},
        'top': {'C': (1273, 382)},
        'three_quarter_front': {'C': (360, 528)},
    }),
    'headlamp_tip': ('LR', 1.0, {
        'front': {'L': (571, 404), 'R': (252, 400)},
        'three_quarter_front': {'L': (502, 608)},
    }),
    'fog': ('LR', 1.0, {
        'front': {'L': (622, 512), 'R': (200, 505)},
        'three_quarter_front': {'L': (542, 800), 'R': (162, 664)},
    }),
    'mirror_tip': ('LR', 0.7, {
        'front': {'L': (734, 210), 'R': (89, 210)},
        'top': {'L': (809, 24), 'R': (791, 720)},
    }),
    'wheel_front': ('LR', 1.0, {
        'side': {'L': (270, 432)},
        'three_quarter_front': {'L': (1035, 740)},
        'three_quarter_rear': {'L': (74, 365)},
    }),
    'wheel_rear': ('LR', 1.0, {
        'side': {'L': (1246, 433)},
        'three_quarter_front': {'L': (1466, 490)},
        'three_quarter_rear': {'L': (432, 510)},
    }),
    'door_handle': ('LR', 1.0, {
        'side': {'L': (857, 270)},
        'three_quarter_front': {'L': (1362, 372)},
        'three_quarter_rear': {'L': (245, 268)},
    }),
    'strip_front': ('LR', 1.0, {
        'side': {'L': (437, 381)},
        'three_quarter_front': {'L': (1152, 585)},
        'three_quarter_rear': {'L': (112, 342)},
    }),
    'strip_rear': ('LR', 1.0, {
        'side': {'L': (1100, 357)},
        'three_quarter_front': {'L': (1428, 432)},
        'three_quarter_rear': {'L': (345, 388)},
    }),
    'taillamp_tip': ('LR', 1.0, {
        'rear': {'L': (209, 399), 'R': (770, 399)},
        'three_quarter_rear': {'L': (722, 388)},
    }),
    'badge_rear': ('C', 1.0, {
        'rear': {'C': (487, 264)},
        'three_quarter_rear': {'C': (940, 245)},
    }),
    'brake_lamp': ('C', 1.0, {
        'rear': {'C': (492, 67)},
        'three_quarter_rear': {'C': (787, 47)},
    }),
    'exhaust': ('F', 0.7, {
        'rear': {'C': (697, 632)},
        'three_quarter_rear': {'C': (1095, 563)},
    }),
    'headlamp_rear_tip': ('LR', 0.5, {
        'side': {'L': (250, 250)},
        'front': {'L': (705, 267), 'R': (127, 268)},
    }),
}
