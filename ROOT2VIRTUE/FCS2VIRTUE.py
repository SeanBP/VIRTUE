#!/usr/bin/env python3

"""
STAR FCS ROOT -> VIRTUE Converter

Converts calorimeter hits and mcparticle truth tracks from a
SimpleTree ROOT file into the JSON format used by the VIRTUE event
display.

Features
--------
- ECAL/HCAL towers rendered as blocks, shifted from front-face to
  center and tilted by DETECTOR_ANGLE (left/right arms)
- EPD hits rendered as simple points
- mcparticle truth tracks (SimpleTree's mcpart_* branches)
- Per-event energy range used for a log-scale red/blue color gradient
"""

import json

import numpy as np
import uproot as ur

# ============================================================================
# User Configuration
# ============================================================================

INPUT_FILE = "RootFiles/SimpleTree_pythia8_0.root"
TREE_NAME = "data"
OUTPUT_FILE = "output/STAR_FCS_VIRTUE.json"

MAX_EVENTS = 100

# ROOT units are cm -> output mm
UNIT_SCALE = 10.0

# Tower dimensions (mm)
ECAL_SIZE = [55.2, 55.2, 330.0]
HCAL_SIZE = [100.0, 100.0, 840.0]

EPD_HIT_SIZE = 50.0

# Speed of light (mm/ns)
C_LIGHT = 299.792458

# ECAL/HCAL arm tilt off the beamline (degrees)
DETECTOR_ANGLE = 1.73003

# Fixed render-stop time for every track (ns); tracks also stop
# rendering earlier if they reach the tracker_boundary.
TRACK_END_NS = 30.0

# ============================================================================
# Helper Functions
# ============================================================================


def energy_color(energy, min_energy, max_energy):
    """
    Convert energy to an RGBA color/opacity using a logarithmic scale
    (red = high energy/opaque, blue = low energy/transparent).
    """

    energy = max(float(energy), 1e-12)
    min_energy = max(float(min_energy), 1e-12)
    max_energy = max(float(max_energy), min_energy * 1.0001)

    fraction = (
        np.log(energy) - np.log(min_energy)
    ) / (
        np.log(max_energy) - np.log(min_energy)
    )

    fraction = np.clip(fraction, 0.0, 1.0)

    return [float(fraction), 0.0, float(1.0 - fraction), float(fraction)]


def shift_block_position(x, y, z, depth, side):
    """
    Shift a tower's position from its ROOT front-face convention to
    its center, along the arm's tilt direction.
    """

    theta = np.deg2rad(DETECTOR_ANGLE)

    if side == "left":
        theta *= -1

    shift = depth / 2.0

    dx = np.sin(theta) * shift
    dz = np.cos(theta) * shift

    return [float(x + dx), float(y), float(z + dz)]


def propagation_time(position):
    """
    Light-travel time from the origin to a position (ns).
    """

    distance = np.sqrt(position[0] ** 2 + position[1] ** 2 + position[2] ** 2)

    return float(distance / C_LIGHT)


# ============================================================================
# Open ROOT File
# ============================================================================

print("Opening ROOT file...")

tree = ur.open(f"{INPUT_FILE}:{TREE_NAME}")

# ============================================================================
# Load Hits
# ============================================================================

print("Loading hits...")

Cal_hit_posx = tree["Cal_hit_posx"].array(library="np")
Cal_hit_posy = tree["Cal_hit_posy"].array(library="np")
Cal_hit_posz = tree["Cal_hit_posz"].array(library="np")
Cal_detid = tree["Cal_detid"].array(library="np")

if "Cal_hit_energy" in tree.keys():
    Cal_hit_energy = tree["Cal_hit_energy"].array(library="np")
else:
    Cal_hit_energy = None

# ============================================================================
# Load Tracks
# ============================================================================

print("Loading tracks...")

mcpart_px = tree["mcpart_px"].array(library="np")
mcpart_py = tree["mcpart_py"].array(library="np")
mcpart_pz = tree["mcpart_pz"].array(library="np")
mcpart_E = tree["mcpart_E"].array(library="np")
mcpart_charge = tree["mcpart_charge"].array(library="np")
mcpart_vtx_x = tree["mcpart_Vtx_x"].array(library="np")
mcpart_vtx_y = tree["mcpart_Vtx_y"].array(library="np")
mcpart_vtx_z = tree["mcpart_Vtx_z"].array(library="np")

# ============================================================================
# Create VIRTUE Header
# ============================================================================

output = {
    "header": {
        "version": "3.2.0",
        "experiment": "STAR FCS",
        "energy_unit": "GeV",
        "color_bar": "Log",
        "scale": 1.0,
        "length_unit": "mm",
        "particles": [
            {
                "size": 150.0,
                "color_rgba": [1.0, 0.0, 0.0, 1.0],
                "ip": [0.0, 0.0, 0.0],
                "angle_rad": [0.0, 0.0],
            },
            {
                "size": 150.0,
                "color_rgba": [1.0, 0.0, 0.0, 1.0],
                "ip": [0.0, 0.0, 0.0],
                "angle_rad": [float(np.pi), 0.0],
            },
        ],
        "tracker_settings": {
            # STAR's standard solenoid field strength -- the tracks are
            # real (charge, momentum) mcparticles, so they should curve
            # through it like the reference C++ version's B_FIELD_TESLA.
            "B_field_T": 0.5,
            "tracker_boundary": [5000.0, -5000.0, 5000.0],
        },
    },
    "events": [],
}

# ============================================================================
# Build Events
# ============================================================================

print("Building events...")

n_events = min(MAX_EVENTS, len(Cal_hit_posx))

for event in range(n_events):

    hits = []
    blocks = []

    x = np.asarray(Cal_hit_posx[event]) * UNIT_SCALE
    y = np.asarray(Cal_hit_posy[event]) * UNIT_SCALE
    z = np.asarray(Cal_hit_posz[event]) * UNIT_SCALE

    det = np.asarray(Cal_detid[event])

    if Cal_hit_energy is not None:
        energy = np.asarray(Cal_hit_energy[event])
    else:
        energy = np.ones(len(x))

    # ------------------------------------------------------------------------
    # Determine energy range for color scaling
    # ------------------------------------------------------------------------

    positive_energy = energy[energy > 0]

    if len(positive_energy) > 0:
        min_energy = np.min(positive_energy)
        max_energy = np.max(positive_energy)
    else:
        min_energy = 1.0
        max_energy = 1.0

    # ------------------------------------------------------------------------
    # Hits and towers
    # ------------------------------------------------------------------------

    for i in range(len(x)):

        detector = int(det[i])

        if detector in (0, 1):
            # ECAL
            side = "left" if detector == 0 else "right"

            position = shift_block_position(x[i], y[i], z[i], ECAL_SIZE[2], side)

            blocks.append({
                "position": position,
                "time_ns": propagation_time(position),
                "size": ECAL_SIZE,
                "euler_angles_deg": [
                    0.0,
                    -DETECTOR_ANGLE if side == "left" else DETECTOR_ANGLE,
                    0.0,
                ],
                "color_rgba": energy_color(energy[i], min_energy, max_energy),
            })

        elif detector in (2, 3):
            # HCAL
            side = "left" if detector == 2 else "right"

            position = shift_block_position(x[i], y[i], z[i], HCAL_SIZE[2], side)

            blocks.append({
                "position": position,
                "time_ns": propagation_time(position),
                "size": HCAL_SIZE,
                "euler_angles_deg": [
                    0.0,
                    -DETECTOR_ANGLE if side == "left" else DETECTOR_ANGLE,
                    0.0,
                ],
                "color_rgba": energy_color(energy[i], min_energy, max_energy),
            })

        elif detector in (4, 5):
            # EPD
            position = [float(x[i]), float(y[i]), float(z[i])]

            hits.append({
                "position": position,
                "time_ns": propagation_time(position),
                "size": EPD_HIT_SIZE,
                "color_rgba": [0.0, 0.8, 1.0, 0.8],
            })

    # ------------------------------------------------------------------------
    # Tracks (mcparticle truth tracks)
    # ------------------------------------------------------------------------

    tracks = []

    track_px = np.asarray(mcpart_px[event])
    track_py = np.asarray(mcpart_py[event])
    track_pz = np.asarray(mcpart_pz[event])
    track_e = np.asarray(mcpart_E[event])
    track_charge = np.asarray(mcpart_charge[event])
    track_vtx_x = np.asarray(mcpart_vtx_x[event]) * UNIT_SCALE
    track_vtx_y = np.asarray(mcpart_vtx_y[event]) * UNIT_SCALE
    track_vtx_z = np.asarray(mcpart_vtx_z[event]) * UNIT_SCALE

    track_p = np.sqrt(track_px**2 + track_py**2 + track_pz**2)

    # Energy range across every mcparticle in this event (not just the
    # rendered/qualifying subset below) -- same real-min/max-anchored
    # log scale as the calo hits above.
    track_positive_energy = track_e[track_e > 0]

    if len(track_positive_energy) > 0:
        track_min_energy = np.min(track_positive_energy)
        track_max_energy = np.max(track_positive_energy)
    else:
        track_min_energy = 1.0
        track_max_energy = 1.0

    for i in range(len(track_px)):

        if track_p[i] < 1e-9:
            continue  # at-rest particle, no meaningful direction

        theta = np.arccos(track_pz[i] / track_p[i])
        phi = np.arctan2(track_py[i], track_px[i])
        q_over_p = float(track_charge[i] / track_p[i]) if track_charge[i] != 0 else 0.0

        vertex = [float(track_vtx_x[i]), float(track_vtx_y[i]), float(track_vtx_z[i])]

        tracks.append({
            "qOverP": q_over_p,
            "angle_rad": [float(theta), float(phi)],
            "vertex": vertex,
            "duration_ns": [propagation_time(vertex), TRACK_END_NS],
            "color_rgba": energy_color(track_e[i], track_min_energy, track_max_energy),
        })

    output["events"].append({
        "event_data": {
            "info_text": f"STAR FCS Event {event}",
            "energy_scale": [float(min_energy), float(max_energy)],
        },
        "hits": hits,
        "blocks": blocks,
        "tracks": tracks,
    })

# ============================================================================
# Write JSON
# ============================================================================

print(f"Writing {OUTPUT_FILE}")

with open(OUTPUT_FILE, "w") as f:
    json.dump(output, f, indent=2)

print("Done.")
