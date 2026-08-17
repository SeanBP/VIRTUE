#!/usr/bin/env python3

"""
Simple ePIC ROOT -> VIRTUE Converter

This script demonstrates the basic workflow for converting an EDM4eic
ROOT file into the JSON format used by the VIRTUE event display.

To download the ROOT file, in eic-shell run:

xrdcp root://epicxrd1.sdcc.bnl.gov:1095//eic/EPIC//RECO/26.04.1/epic_craterlake/EXCLUSIVE/DVMP/JPsi_MuMu_ABCONV/lAger3.6.1-1.0/10x130/q2_1to50/lAger3.6.1-1.0_DVMP_JPsi_10x130_q2_1to50_ab_run001.1367.eicrecon.edm4eic.root ./

Features
--------
- Reconstructed calorimeter hits
- Reconstructed clusters
- Reconstructed tracks
- Reconstructed jets
- Bjorken x displayed in event metadata
"""

import json

import numpy as np
import uproot as ur

# ============================================================================
# User Configuration
# ============================================================================

EVENT_TYPE = "NCDIS"
MAX_EVENTS = 20
INPUT_FILE = "RootFiles/lAger3.6.1-1.0_DVMP_JPsi_10x130_q2_1to50_ab_run001.1367.eicrecon.edm4eic.root"
OUTPUT_FILE = "output/JPsi_VIRTUE.json"

# ============================================================================
# Helper Functions
# ============================================================================


def normalize_energy_log(energy, min_energy, max_energy):
    """
    Convert energy to a value between 0 and 1 using a logarithmic scale.
    """

    if energy <= 0:
        return 0.0

    if min_energy <= 0:
        min_energy = min(e for e in [min_energy, energy] if e > 0)

    if max_energy <= min_energy:
        return 0.5

    return np.log(energy / min_energy) / np.log(max_energy / min_energy)


def flatten_detector_collections(collections):
    return [
        [obj for detector in collections for obj in detector[event]]
        for event in range(len(collections[0]))
    ]


def calculate_angles(px, py, pz):
    """
    Convert momentum vector into spherical coordinates.
    """

    magnitude = np.sqrt(px**2 + py**2 + pz**2)

    if magnitude == 0:
        return 0.0, 0.0

    theta = np.arccos(pz / magnitude)
    phi = np.arctan2(py, px)

    return theta, phi


# ============================================================================
# Open ROOT File + Discover Detector Branches
# ============================================================================

print("Opening ROOT file...")

tree = ur.open(f"{INPUT_FILE}:events")

print("Scanning branches...")

hit_branches = [key for key in tree.keys() if key.endswith("RecHits.position.x")]

cluster_branches = [
    key
    for key in tree.keys()
    if key.endswith("Clusters.position.x") and "TruthClusters" not in key
]

# ============================================================================
# Load Hits
# ============================================================================

print("Loading hits...")

hit_x = []
hit_y = []
hit_z = []
hit_t = []
hit_e = []

for branch in hit_branches:

    hit_x.append(np.array(tree[branch]))

    hit_y.append(np.array(tree[branch.replace(".position.x", ".position.y")]))

    hit_z.append(np.array(tree[branch.replace(".position.x", ".position.z")]))

    time_branch = branch.replace(".position.x", ".time")

    if time_branch in tree:
        hit_t.append(np.array(tree[time_branch]))
    else:
        hit_t.append([np.zeros_like(event) for event in np.array(tree[branch])])

    energy_branch = branch.replace(".position.x", ".energy")

    if energy_branch in tree:
        hit_e.append(np.array(tree[energy_branch]))
    else:
        hit_e.append([np.zeros_like(event) for event in np.array(tree[branch])])

# Merge detector collections into per-event lists

hit_x = flatten_detector_collections(hit_x)
hit_y = flatten_detector_collections(hit_y)
hit_z = flatten_detector_collections(hit_z)
hit_t = flatten_detector_collections(hit_t)
hit_e = flatten_detector_collections(hit_e)

# ============================================================================
# Load Clusters
# ============================================================================

print("Loading clusters...")

cluster_x = []
cluster_y = []
cluster_z = []
cluster_t = []
cluster_e = []

for branch in cluster_branches:

    cluster_x.append(np.array(tree[branch]))

    cluster_y.append(np.array(tree[branch.replace(".position.x", ".position.y")]))

    cluster_z.append(np.array(tree[branch.replace(".position.x", ".position.z")]))

    time_branch = branch.replace(".position.x", ".time")

    if time_branch in tree:
        cluster_t.append(np.array(tree[time_branch]))
    else:
        cluster_t.append([np.zeros_like(event) for event in np.array(tree[branch])])

    cluster_e.append(np.array(tree[branch.replace(".position.x", ".energy")]))

cluster_x = flatten_detector_collections(cluster_x)
cluster_y = flatten_detector_collections(cluster_y)
cluster_z = flatten_detector_collections(cluster_z)
cluster_t = flatten_detector_collections(cluster_t)
cluster_e = flatten_detector_collections(cluster_e)

# ============================================================================
# Load Tracks
# ============================================================================

print("Loading tracks...")

vertex_x = np.array(tree["CentralTrackVertices/CentralTrackVertices.position.x"])

vertex_y = np.array(tree["CentralTrackVertices/CentralTrackVertices.position.y"])

vertex_z = np.array(tree["CentralTrackVertices/CentralTrackVertices.position.z"])

q_over_p = np.array(
    tree["CentralCKFTrackParameters/CentralCKFTrackParameters.qOverP"]
)

track_theta = np.array(
    tree["CentralCKFTrackParameters/CentralCKFTrackParameters.theta"]
)

track_phi = np.array(tree["CentralCKFTrackParameters/CentralCKFTrackParameters.phi"])

# ============================================================================
# Load Jets
# ============================================================================

print("Loading jets...")

jet_px = np.array(tree["ReconstructedJets/ReconstructedJets.momentum.x"])

jet_py = np.array(tree["ReconstructedJets/ReconstructedJets.momentum.y"])

jet_pz = np.array(tree["ReconstructedJets/ReconstructedJets.momentum.z"])

jet_energy = np.array(tree["ReconstructedJets/ReconstructedJets.energy"])

# ============================================================================
# Load Kinematics
# ============================================================================

print("Loading kinematics...")

bjorken_x = np.array(tree["InclusiveKinematicsTruth.x"])

# ============================================================================
# Create VIRTUE Header
# ============================================================================

output = {
    "header": {
        "version": "3.2.0",
        "experiment": f"ePIC {EVENT_TYPE}",
        "energy_unit": "GeV",
        "color_bar": "Log",
        "length_unit": "mm",
        "particles": [
            {
                "angle_rad": [0.0, 0.0],
                "size": float(150),
                "color_rgba": [float(1), float(0), float(0), float(1)],
            },
            {
                "angle_rad": [3.14159, 0.0],
                "size": float(100),
                "color_rgba": [float(0), float(0), float(1), float(1)],
            },
        ],
        "tracker_settings": {
            "B_field_T": float(1.7),
            "tracker_boundary": [
                float(0.7817 * 1000),
                float(-1.235 * 1000),
                float(1.88 * 1000),
            ],
        },
    },
    "events": [],
}

# ============================================================================
# Build Events
# ============================================================================

print("Building events...")

num_events = min(MAX_EVENTS, len(hit_x))

for event_index in range(num_events):

    info_text = f"Event #{event_index}"

    if bjorken_x is not None:
        info_text += f", x = {float(bjorken_x[event_index][0]):.4f}"

    # ------------------------------------------------------------------------
    # Determine energy range for color scaling
    # ------------------------------------------------------------------------

    all_energies = [energy for energy in list(hit_e[event_index]) if energy > 0]

    if len(all_energies) > 0:
        min_energy = min(all_energies)
        max_energy = max(all_energies)
    else:
        min_energy = 0
        max_energy = 1

    event = {
        "event_data": {
            "info_text": info_text,
            "energy_scale": [min_energy, max_energy],
        },
        "hits": [],
        "tracks": [],
        "clusters": [],
        "jets": [],
        "blocks": [],
    }

    # ------------------------------------------------------------------------
    # Hits
    # ------------------------------------------------------------------------

    for i in range(len(hit_x[event_index])):

        energy = hit_e[event_index][i]

        fraction = normalize_energy_log(energy, min_energy, max_energy)

        fraction = max(0.0, min(1.0, fraction))

        event["hits"].append({
            "position": [
                float(hit_x[event_index][i]),
                float(hit_y[event_index][i]),
                float(hit_z[event_index][i]),
            ],
            "time_ns": float(hit_t[event_index][i]),
            "size": 100.0,
            "color_rgba": (
                [0.0, 0.0, 0.0, 1.0]
                if fraction == 0
                else [float(fraction), 0.0, float(1.0 - fraction), 1.0]
            ),
        })

    # ------------------------------------------------------------------------
    # Tracks
    # ------------------------------------------------------------------------

    if len(vertex_x[event_index]) > 0:
        vertex = [
            float(vertex_x[event_index][0]),
            float(vertex_y[event_index][0]),
            float(vertex_z[event_index][0]),
        ]
    else:
        vertex = [0.0, 0.0, 0.0]

    for i in range(len(q_over_p[event_index])):

        event["tracks"].append({
            "qOverP": float(q_over_p[event_index][i]),
            "angle_rad": [
                float(track_theta[event_index][i]),
                float(track_phi[event_index][i]),
            ],
            "vertex": vertex,
            "duration_ns": [0.0, 1000.0],
            "color_rgba": [0.0, 1.0, 1.0, 1.0],
        })

    # ------------------------------------------------------------------------
    # Clusters
    # ------------------------------------------------------------------------

    for i in range(len(cluster_x[event_index])):

        event["clusters"].append({
            "position": [
                float(cluster_x[event_index][i]),
                float(cluster_y[event_index][i]),
                float(cluster_z[event_index][i]),
            ],
            "time_ns": float(cluster_t[event_index][i]),
            "granularity": 100.0,
            "length": float(100 * cluster_e[event_index][i]),
            "color_rgba": [0.0, 1.0, 0.0, 0.5],
        })

    # ------------------------------------------------------------------------
    # Jets
    # ------------------------------------------------------------------------

    for i in range(len(jet_px[event_index])):

        theta, phi = calculate_angles(
            jet_px[event_index][i],
            jet_py[event_index][i],
            jet_pz[event_index][i],
        )

        event["jets"].append({
            "length": float(100 * jet_energy[event_index][i]),
            "R_rad": 0.5,
            "angle_rad": [float(theta), float(phi)],
            "color_rgba": [0.0, 0.3, 1.0, 0.5],
            "time_ns": 0.0,
        })

    output["events"].append(event)

# ============================================================================
# Write JSON
# ============================================================================

print(f"Writing {OUTPUT_FILE}")


def deep_json_convert(obj):
    if isinstance(obj, dict):
        return {k: deep_json_convert(v) for k, v in obj.items()}
    if isinstance(obj, list):
        return [deep_json_convert(v) for v in obj]
    if isinstance(obj, np.ndarray):
        return obj.tolist()
    if isinstance(obj, (np.floating, np.integer)):
        return obj.item()
    return obj


output = deep_json_convert(output)

with open(OUTPUT_FILE, "w") as outfile:
    json.dump(output, outfile, indent=4)

print("Done.")
