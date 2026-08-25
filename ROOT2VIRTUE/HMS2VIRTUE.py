#!/usr/bin/env python3

"""
Hall C HMS ROOT -> VIRTUE Converter

Converts reconstructed "golden track" (H.gtr.*) electrons from an HMS
replay ROOT file into the JSON format used by the VIRTUE event display.
Only track objects are produced (no hits/clusters/jets/blocks).

Each electron is written as six track segments, since EventLoader.cs
applies B_field_T uniformly across the whole tracker volume:
    1-3. Target -> Q1 -> Q2 -> Q3: straight, each ending in an
       idealized thin-lens kick (shared focal length from
       solve_shared_focal_length(), imaging the target onto the dipole
       entrance point-to-point).
    4. Q3 -> dipole entrance: straight.
    5. Inside the dipole: curved (real qOverP and B_field_T).
    6. Dipole exit -> detector hut back plane: straight.
Dipole/quad z-extents come from HallC_HMS.json. Each segment's
vertex/duration_ns continues from where the previous one ended.

Coordinate systems
-------------------
HMS golden-track branches are in the (left-handed) Hall Coordinate
System: X horizontal beam-left, Y vertical up, Z downstream.

VIRTUE uses a right-handed frame with Z along the beamline. Converting
HCS -> VIRTUE just flips Y (hcs_to_virtue(), phi_rot=0.0). HMS's own
central ray -- what Q1/Q2/Q3 and the dipole entrance sit along -- is
HMS_DIR, tilted HMS_ANGLE_DEG off Z toward -X (beam-right). Straight
segments before the dipole are found by plane intersection (point/
normal = HMS_DIR) since these elements aren't on the Z axis.

There is no reconstructed vertex-z (reactz) branch here, so the target
vertex is taken as (H.gtr.x, H.gtr.y, 0) before rotation.
"""

import json
import os
import numpy as np
import uproot as ur

# ============================================================================
# User Configuration
# ============================================================================

INPUT_FILE = "RootFiles/hms_replay_production_5848_-1.root"
TREE_NAME = "T"

# Local output folder; copy into a Unity project's StreamingAssets manually.
_STREAMING_ASSETS = "output"
OUTPUT_FILE = f"{_STREAMING_ASSETS}/Events/HMS_Events.json"
GEOMETRY_FILE = f"{_STREAMING_ASSETS}/Models/HallC_HMS.json"

MAX_EVENTS = 50

ELECTRON_MASS_GEV = 0.000511

# HMS's fixed mechanical bend angle; B_field_T is derived from this and the
# mean reconstructed momentum below (the run's real field setting isn't
# otherwise known).
DESIGN_BEND_ANGLE_DEG = 25.5

# Deliberately loose: each segment's endpoint is computed exactly (see
# BACK_PLANE_POINT/NORMAL), so this boundary should never be what clips a
# track.
TRACKER_RADIUS_M = 50.0
TRACKER_LENGTH_M = 50.0

SEGMENT_NS = 0.2  # ns per rendered curve step

# EventLoader's rate/before/after timing defaults break down at
# length_unit="m" (sub-nanosecond fallback windows), so lengths are written
# in mm here even though geometry is computed in meters.
MM_PER_M = 1000.0

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
        min_energy = min(
            e for e in [min_energy, energy] if e > 0
        )

    if max_energy <= min_energy:
        return 0.5

    return (
        np.log(energy / min_energy)
        / np.log(max_energy / min_energy)
    )


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


def hcs_to_virtue(x, y, z, phi_rot):
    """
    Rotate an HCS vector into the VIRTUE frame (see module docstring).
    """

    x_v = x * np.cos(phi_rot) + z * np.sin(phi_rot)
    y_v = -y
    z_v = -x * np.sin(phi_rot) + z * np.cos(phi_rot)

    return x_v, y_v, z_v


C_LIGHT = 0.299792  # m/ns


def helix_basis(b_hat):
    """
    Right-handed basis {e1, e2, b_hat} perpendicular to the field,
    mirroring EventLoader.cs's CreateTrackObjects exactly.
    """

    up = np.array([0.0, 1.0, 0.0])
    right = np.array([1.0, 0.0, 0.0])

    e1 = np.cross(up, b_hat)
    if np.dot(e1, e1) < 1e-6:
        e1 = np.cross(right, b_hat)
    e1 = e1 / np.linalg.norm(e1)
    e2 = np.cross(b_hat, e1)

    return e1, e2


def helix_state(vertex, momentum, p_mag, b_field, q, b_hat, t):
    """
    Position and velocity at time t for a charged particle in a uniform
    field b_field*b_hat, mirroring EventLoader.cs's HelixPosition.
    """

    e1, e2 = helix_basis(b_hat)
    omega = (q * b_field / p_mag) * C_LIGHT
    v_par = C_LIGHT * np.dot(momentum, b_hat) / p_mag
    a0 = C_LIGHT * np.dot(momentum, e1) / p_mag
    b0 = C_LIGHT * np.dot(momentum, e2) / p_mag

    e1_coeff = (a0 * np.sin(omega * t) + b0 * (1 - np.cos(omega * t))) / omega
    e2_coeff = (a0 * (np.cos(omega * t) - 1) + b0 * np.sin(omega * t)) / omega
    position = vertex + e1_coeff * e1 + e2_coeff * e2 + v_par * t * b_hat

    ve1 = a0 * np.cos(omega * t) + b0 * np.sin(omega * t)
    ve2 = -a0 * np.sin(omega * t) + b0 * np.cos(omega * t)
    velocity = ve1 * e1 + ve2 * e2 + v_par * b_hat

    return position, velocity


def find_time_at_plane(plane_point, plane_normal, position_of_t, t_max, coarse_steps=5000):
    """
    First time in [0, t_max] at which position_of_t(t) crosses the plane
    (plane_point, plane_normal): a coarse forward scan brackets the
    crossing, then bisection refines it. None if never crossed.
    """

    def signed_dist(t):
        return np.dot(position_of_t(t) - plane_point, plane_normal)

    dt = t_max / coarse_steps
    t_prev, d_prev = 0.0, signed_dist(0.0)

    for i in range(1, coarse_steps + 1):
        t_cur = i * dt
        d_cur = signed_dist(t_cur)

        if d_prev < 0.0 <= d_cur:
            lo, hi = t_prev, t_cur
            for _ in range(50):
                mid = 0.5 * (lo + hi)
                if signed_dist(mid) < 0.0:
                    lo = mid
                else:
                    hi = mid
            return 0.5 * (lo + hi)

        t_prev, d_prev = t_cur, d_cur

    return None


def time_to_plane(vertex, velocity, plane_point, plane_normal):
    """
    Exact time at which straight-line motion from vertex at velocity
    crosses the plane (plane_point, plane_normal). None if parallel.
    """

    denom = np.dot(velocity, plane_normal)
    if denom == 0.0:
        return None
    return np.dot(plane_point - vertex, plane_normal) / denom


def _drift_matrix(d):
    return np.array([[1.0, d], [0.0, 1.0]])


def _lens_matrix(f):
    return np.array([[1.0, 0.0], [-1.0 / f, 1.0]])


def solve_shared_focal_length(z_object, z_lenses, z_image):
    """
    Focal length f, shared by thin lenses at each z in z_lenses, such
    that a point at z_object images point-to-point onto z_image
    (combined ABCD matrix's (0,1) element is zero). Generalizes the
    single-lens f = d1*d2/(d1+d2) result to N lenses. Multiple roots can
    satisfy this; returns the largest (weakest-lensing, physically
    sensible) one found in the scanned range.
    """

    def m01(f):
        m = np.eye(2)
        z_prev = z_object
        for z_lens in z_lenses:
            m = _lens_matrix(f) @ _drift_matrix(z_lens - z_prev) @ m
            z_prev = z_lens
        m = _drift_matrix(z_image - z_prev) @ m
        return m[0, 1]

    f_scan = np.linspace(0.5, 20.0, 4000)
    m01_scan = np.array([m01(f) for f in f_scan])
    sign_changes = np.nonzero(np.diff(np.sign(m01_scan)))[0]
    if len(sign_changes) == 0:
        raise ValueError("No focal length found imaging the target onto z_image")

    lo, hi = f_scan[sign_changes[-1]], f_scan[sign_changes[-1] + 1]
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if np.sign(m01(mid)) == np.sign(m01(lo)):
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


# ============================================================================
# Open ROOT File
# ============================================================================

print("Opening ROOT file...")

tree = ur.open(f"{INPUT_FILE}:{TREE_NAME}")

# ============================================================================
# Determine HMS Angle + Charge Sign from Run Settings
# ============================================================================

print("Reading run settings...")

settings_tree = ur.open(f"{INPUT_FILE}:E")

hms_angle_readback = settings_tree["ecHMS_Angle"].array(library="np")
hms_angle_readback = hms_angle_readback[np.abs(hms_angle_readback) < 1e6]
hms_angle_deg = float(hms_angle_readback[0]) if len(hms_angle_readback) else 20.0

hms_p_readback = settings_tree["ecP_HMS"].array(library="np")
hms_p_readback = hms_p_readback[np.abs(hms_p_readback) < 1e6]
# Negative central momentum -> HMS polarity for electrons.
charge = -1.0 if (len(hms_p_readback) and hms_p_readback[0] < 0) else 1.0

phi_rot = np.radians(hms_angle_deg)

# HMS's central ray in the (Z=beamline) VIRTUE frame, tilted off Z toward -X.
HMS_DIR = np.array([-np.sin(phi_rot), 0.0, np.cos(phi_rot)])

print(f"HMS angle: {hms_angle_deg} deg, track charge: {charge:+.0f}")

# ============================================================================
# Incoming Beam Particle
# ============================================================================

# angle_rad=[0,0] gives direction (0,0,1) under VIRTUE's Particle convention
# (dx=-cos(b)sin(a), dy=sin(b), dz=cos(b)cos(a)) -- the beam travels +Z.
beam_angle_rad = [0.0, 0.0]

# ============================================================================
# Load Golden Tracks
# ============================================================================

print("Loading tracks...")

branches = tree.arrays(
    ["H.gtr.ok", "H.gtr.px", "H.gtr.py", "H.gtr.pz", "H.gtr.x", "H.gtr.y"],
    library="np",
)

ok = branches["H.gtr.ok"] == 1

finite = (
    np.isfinite(branches["H.gtr.px"])
    & np.isfinite(branches["H.gtr.py"])
    & np.isfinite(branches["H.gtr.pz"])
    & np.isfinite(branches["H.gtr.x"])
    & np.isfinite(branches["H.gtr.y"])
)

# Reject badly-reconstructed tracks that pass H.gtr.ok but carry unphysical
# values. H.gtr.x/y are in cm (TRANSPORT convention).
sane = (
    (np.abs(branches["H.gtr.px"]) < 10)
    & (np.abs(branches["H.gtr.py"]) < 10)
    & (np.abs(branches["H.gtr.pz"]) < 10)
    & (np.abs(branches["H.gtr.x"]) < 50)
    & (np.abs(branches["H.gtr.y"]) < 50)
)

selected = np.nonzero(ok & finite & sane)[0][:MAX_EVENTS]

print(f"Selected {len(selected)} good tracks (of {tree.num_entries} events)")

track_px, track_py, track_pz = [], [], []
track_vertex = []

for i in selected:

    px_v, py_v, pz_v = hcs_to_virtue(
        branches["H.gtr.px"][i],
        branches["H.gtr.py"][i],
        branches["H.gtr.pz"][i],
        0.0,
    )

    vx_v, vy_v, vz_v = hcs_to_virtue(
        branches["H.gtr.x"][i] / 100.0,  # cm -> m
        branches["H.gtr.y"][i] / 100.0,  # cm -> m
        0.0,
        0.0,
    )

    track_px.append(px_v)
    track_py.append(py_v)
    track_pz.append(pz_v)
    track_vertex.append([vx_v, vy_v, vz_v])

# ============================================================================
# Determine Energy Range for Color Scaling
# ============================================================================

track_p = np.sqrt(
    np.array(track_px) ** 2 + np.array(track_py) ** 2 + np.array(track_pz) ** 2
)
track_energy = np.sqrt(track_p**2 + ELECTRON_MASS_GEV**2)

if len(track_energy) > 0:
    min_energy = float(track_energy.min())
    max_energy = float(track_energy.max())
else:
    min_energy = 0.0
    max_energy = 1.0

# ============================================================================
# Geometry: Read Fixed Distances, Then Reposition Everything Along HMS_DIR
# ============================================================================

# Every pre-dipole position below is HMS_DIR times a fixed distance from the
# target (these elements aren't on the Z axis). {HMS_U, HMS_V} is the local
# transverse basis for the thin-lens optics: HMS_V is global Y, HMS_U is
# in-plane perpendicular to HMS_DIR.
HMS_U = np.array([np.cos(phi_rot), 0.0, np.sin(phi_rot)])
HMS_V = np.array([0.0, 1.0, 0.0])
HMS_YAW_DEG = float(np.degrees(np.arctan2(HMS_DIR[0], HMS_DIR[2])))

with open(GEOMETRY_FILE) as f:
    geometry = json.load(f)

# Distances are read via norm of each component's current position (not its
# Z-coordinate), so this is idempotent whether the file is fresh or already
# repositioned by a previous run.
target = next(c for c in geometry["components"] if c["name"] == "Target")
beampipe = next(c for c in geometry["components"] if c["name"] == "Beampipe")
q1 = next(c for c in geometry["components"] if c["name"] == "HMS Q1")
q2 = next(c for c in geometry["components"] if c["name"] == "HMS Q2")
q3 = next(c for c in geometry["components"] if c["name"] == "HMS Q3")
dipole = next(c for c in geometry["components"] if c["name"] == "HMS Dipole")
hut = next(c for c in geometry["components"] if c["name"] == "HMS Detector Hut")
shms_magnets = next(c for c in geometry["components"] if c["name"] == "SHMS Magnets")
shms_hut = next(c for c in geometry["components"] if c["name"] == "SHMS Detector Hut")

Q1_DIST = float(np.linalg.norm(q1["position"]))
Q2_DIST = float(np.linalg.norm(q2["position"]))
Q3_DIST = float(np.linalg.norm(q3["position"]))
DIPOLE_LENGTH_M = dipole["size"][2]
DIPOLE_CENTER_DIST = float(np.linalg.norm(dipole["position"]))
DIPOLE_ENTRANCE_DIST = DIPOLE_CENTER_DIST - DIPOLE_LENGTH_M / 2.0
DIPOLE_EXIT_DIST = DIPOLE_CENTER_DIST + DIPOLE_LENGTH_M / 2.0

Q1_POS = HMS_DIR * Q1_DIST
Q2_POS = HMS_DIR * Q2_DIST
Q3_POS = HMS_DIR * Q3_DIST
DIPOLE_ENTRANCE_POS = HMS_DIR * DIPOLE_ENTRANCE_DIST
DIPOLE_EXIT_POS = HMS_DIR * DIPOLE_EXIT_DIST

BEAMPIPE_GAP_M = 0.1
BEAMPIPE_LENGTH_M = beampipe["length"][0]
target["euler_angles_deg"] = [0.0, 0.0, 0.0]
beampipe["position"] = [0.0, 0.0, float(-(BEAMPIPE_GAP_M + BEAMPIPE_LENGTH_M / 2.0))]
beampipe["euler_angles_deg"] = [0.0, 0.0, 0.0]

q1["position"] = [float(v) for v in Q1_POS]
q1["euler_angles_deg"] = [0.0, HMS_YAW_DEG, 0.0]
q2["position"] = [float(v) for v in Q2_POS]
q2["euler_angles_deg"] = [0.0, HMS_YAW_DEG, 0.0]
q3["position"] = [float(v) for v in Q3_POS]
q3["euler_angles_deg"] = [0.0, HMS_YAW_DEG, 0.0]
dipole["position"] = [float(v) for v in HMS_DIR * DIPOLE_CENTER_DIST]
dipole["size"][0] = 1.0
# dipole["size"][1] and dipole["euler_angles_deg"] are set later, once the
# average exit direction is known (see "Average Entry Angle" below).

# SHMS placeholder: mirrored across the beamline (HMS beam-right, SHMS
# beam-left).
SHMS_PLACEHOLDER_ANGLE_DEG = hms_angle_deg
shms_phi = np.radians(SHMS_PLACEHOLDER_ANGLE_DEG)
SHMS_DIR = np.array([np.sin(shms_phi), 0.0, np.cos(shms_phi)])
SHMS_YAW_DEG = float(np.degrees(np.arctan2(SHMS_DIR[0], SHMS_DIR[2])))
SHMS_MAGNETS_DIST = float(np.linalg.norm(shms_magnets["position"]))
SHMS_HUT_DIST = float(np.linalg.norm(shms_hut["position"]))
shms_magnets["position"] = [float(v) for v in SHMS_DIR * SHMS_MAGNETS_DIST]
shms_magnets["euler_angles_deg"] = [0.0, SHMS_YAW_DEG, 0.0]
shms_hut["position"] = [float(v) for v in SHMS_DIR * SHMS_HUT_DIST]
shms_hut["euler_angles_deg"] = [0.0, SHMS_YAW_DEG, 0.0]

design_radius_m = DIPOLE_LENGTH_M / np.sin(np.radians(DESIGN_BEND_ANGLE_DEG))
B_FIELD_T = float(track_p.mean() / (0.3 * design_radius_m))
print(
    f"Design bend radius = {design_radius_m:.2f} m, mean p = "
    f"{track_p.mean():.2f} GeV -> B_field_T = {B_FIELD_T:.3f} T"
)

# HMS bends vertically: the field is horizontal, perpendicular to both
# HMS_DIR and Y. Sign (-HMS_U) chosen so that, with `charge`, an electron
# curves upward -- verified numerically via helix_state(). This is the true
# physical field, used below for every position/direction this script
# computes for itself.
B_FIELD_DIRECTION = -HMS_U

# EventGeometry.cs (VIRTUE Scripts/com.quantanaut.virtue.core) reconstructs
# each track from vertex/angle_rad with an X-only mirror for the right-
# handed-physics-to-Unity-left-handed-display flip, but (as of commit
# c4735ea) uses B_field_direction from the header unreflected. Under an
# X-only mirror, B is a pseudovector and needs its Y,Z components negated to
# keep the Lorentz force self-consistent (verified: this is what makes the
# rendered curve land exactly on the following segment). So the header gets
# this Y,Z-negated field, while this script's own geometry keeps using the
# true, unflipped B_FIELD_DIRECTION.
HEADER_B_FIELD_DIRECTION = np.array(
    [B_FIELD_DIRECTION[0], -B_FIELD_DIRECTION[1], -B_FIELD_DIRECTION[2]]
)

# EventLoader.cs derives its own momentum scale from qOverP alone
# (P_internal = eScale/(cm*|qOverP|) = (1e9/2.998e8)*P_real) rather than
# using a track's real GeV momentum, to keep curvature radii sane at
# VIRTUE's rescaled display speed. omega=(q*B/P)*c isn't scale-invariant, so
# the dipole curvature must use this same internal P or the radius comes
# out ~3.34x too small.
INTERNAL_P_SCALE = 1e9 / 2.998e8

# ============================================================================
# Reposition Bent Geometry (Detector Hut placement)
# ============================================================================

# Design (mean-momentum) trajectory, used only as the hut's *position*
# anchor -- orientation instead uses the real per-track average below,
# since the dipole's bend angle is momentum-dependent.
POST_DIPOLE_DRIFT_M = 3.85
_design_momentum = HMS_DIR * track_p.mean() * INTERNAL_P_SCALE
_design_p_mag = track_p.mean() * INTERNAL_P_SCALE


def _design_position_of_t(t):
    pos, _ = helix_state(
        DIPOLE_ENTRANCE_POS, _design_momentum, _design_p_mag, B_FIELD_T,
        charge, B_FIELD_DIRECTION, t
    )
    return pos


_design_t_exit = find_time_at_plane(
    DIPOLE_EXIT_POS, HMS_DIR, _design_position_of_t, 3.0 * DIPOLE_LENGTH_M / C_LIGHT
)
design_exit_vertex, design_exit_velocity = helix_state(
    DIPOLE_ENTRANCE_POS, _design_momentum, _design_p_mag, B_FIELD_T, charge,
    B_FIELD_DIRECTION, _design_t_exit
)
design_exit_direction = design_exit_velocity / np.linalg.norm(design_exit_velocity)

print(
    f"Dipole spans {DIPOLE_ENTRANCE_DIST:.2f} to {DIPOLE_EXIT_DIST:.2f} m "
    f"along HMS_DIR"
)

TRIPLET_FOCAL_LENGTH_M = solve_shared_focal_length(
    0.0, [Q1_DIST, Q2_DIST, Q3_DIST], DIPOLE_ENTRANCE_DIST
)
print(
    f"Idealized Q1/Q2/Q3 lenses at {Q1_DIST:.2f}, {Q2_DIST:.2f}, {Q3_DIST:.2f} m "
    f"along HMS_DIR, shared focal length = {TRIPLET_FOCAL_LENGTH_M:.3f} m"
)


def propagate_through_dipole(vertex0, momentum0, p_mag):
    """
    Segments 1-5 of one electron's path (straight legs through the
    Q1/Q2/Q3 lenses, straight to the dipole entrance, curved through the
    dipole). Split from the final segment because that one needs the hut's
    back plane, which depends on every track's exit direction (see
    "Average Entry Angle" below). Returns (segments, start_time, vertex2,
    velocity2).
    """

    segments = []
    vertex = vertex0
    direction = momentum0 / p_mag
    start_time = 0.0

    # Segments 1-3: straight legs through the Q1/Q2/Q3 thin lenses, kick
    # applied in the {HMS_U, HMS_V} transverse basis.
    for lens_pos in (Q1_POS, Q2_POS, Q3_POS):
        theta, phi = calculate_angles(*direction)
        velocity = C_LIGHT * direction
        dt = max(time_to_plane(vertex, velocity, lens_pos, HMS_DIR) or 0.0, 0.0)
        vertex_at_lens = vertex + velocity * dt

        segments.append({
            "qOverP": 0.0,
            "angle_rad": [float(theta), float(phi)],
            "vertex": vertex,
            "duration_ns": [float(start_time), float(dt)],
        })
        start_time += dt

        s_comp = np.dot(direction, HMS_DIR)
        up = np.dot(direction, HMS_U) / s_comp
        vp = np.dot(direction, HMS_V) / s_comp
        u_at_lens = np.dot(vertex_at_lens, HMS_U)
        v_at_lens = np.dot(vertex_at_lens, HMS_V)
        up -= u_at_lens / TRIPLET_FOCAL_LENGTH_M
        vp -= v_at_lens / TRIPLET_FOCAL_LENGTH_M

        direction = up * HMS_U + vp * HMS_V + HMS_DIR
        direction = direction / np.linalg.norm(direction)
        vertex = vertex_at_lens

    # Segment 4: straight, Q3 -> dipole entrance.
    theta_f, phi_f = calculate_angles(*direction)
    velocity_focused = C_LIGHT * direction
    t_to_dipole = max(
        time_to_plane(vertex, velocity_focused, DIPOLE_ENTRANCE_POS, HMS_DIR) or 0.0, 0.0
    )
    vertex1 = vertex + velocity_focused * t_to_dipole

    segments.append({
        "qOverP": 0.0,
        "angle_rad": [float(theta_f), float(phi_f)],
        "vertex": vertex,
        "duration_ns": [float(start_time), float(t_to_dipole)],
    })
    start_time += t_to_dipole

    momentum_focused = direction * p_mag

    # Segment 5: curved, inside the dipole.
    momentum_focused_internal = momentum_focused * INTERNAL_P_SCALE
    p_mag_internal = p_mag * INTERNAL_P_SCALE

    def position_of_t(t):
        position, _ = helix_state(
            vertex1, momentum_focused_internal, p_mag_internal, B_FIELD_T,
            charge, B_FIELD_DIRECTION, t
        )
        return position

    t_dipole_window = 3.0 * DIPOLE_LENGTH_M / max(np.dot(velocity_focused, HMS_DIR), 1e-6)
    t_dipole = find_time_at_plane(
        DIPOLE_EXIT_POS, HMS_DIR, position_of_t, t_dipole_window
    )
    if t_dipole is None:
        t_dipole = find_time_at_plane(
            DIPOLE_EXIT_POS, HMS_DIR, position_of_t, t_dipole_window * 5.0
        )
    if t_dipole is None:
        t_dipole = t_dipole_window

    vertex2, velocity2 = helix_state(
        vertex1, momentum_focused_internal, p_mag_internal, B_FIELD_T,
        charge, B_FIELD_DIRECTION, t_dipole
    )

    segments.append({
        "qOverP": float(charge / p_mag),
        "angle_rad": [float(theta_f), float(phi_f)],
        "vertex": vertex1,
        "duration_ns": [float(start_time), float(t_dipole)],
    })
    start_time += t_dipole

    return segments, start_time, vertex2, velocity2


def finish_track_segments(segments, start_time, vertex2, velocity2):
    """
    Appends segment 6 (straight, dipole exit -> detector hut back plane)
    using the final BACK_PLANE_POINT/NORMAL (see "Average Entry Angle").
    """

    theta2, phi2 = calculate_angles(*velocity2)
    denom = np.dot(velocity2, BACK_PLANE_NORMAL)
    if denom > 0:
        t_back = np.dot(BACK_PLANE_POINT - vertex2, BACK_PLANE_NORMAL) / denom
    else:
        t_back = 50.0
    t_back = max(t_back, 1.0)

    segments.append({
        "qOverP": 0.0,
        "angle_rad": [float(theta2), float(phi2)],
        "vertex": vertex2,
        "duration_ns": [float(start_time), float(t_back)],
    })

    return segments


# ============================================================================
# Average Entry Angle at Detector Hut
# ============================================================================

# The hut is tilted to the actual sampled tracks' average dipole-exit
# direction, not the single idealized design trajectory (the dipole's bend
# angle is momentum-dependent). Segments 1-5 are computed once here and
# cached for reuse when segment 6 is appended below.
dipole_exit_cache = []
_exit_direction_sum = np.zeros(3)

for n in range(len(selected)):
    momentum0 = np.array([track_px[n], track_py[n], track_pz[n]])
    vertex0 = np.array(track_vertex[n])
    segments, start_time, vertex2, velocity2 = propagate_through_dipole(
        vertex0, momentum0, track_p[n]
    )
    dipole_exit_cache.append((segments, start_time, vertex2, velocity2))
    _exit_direction_sum += velocity2 / np.linalg.norm(velocity2)

avg_exit_direction = _exit_direction_sum / np.linalg.norm(_exit_direction_sum)


def direction_to_pitch_yaw(direction):
    """
    euler_angles_deg [pitch, yaw] orienting a component's local +Z axis
    along `direction` (a unit vector in this script's own unmirrored
    frame). Derived from how ComponentMaker.cs applies euler_angles_deg
    (`eulerAngles = (angles[0], -angles[1], angles[2])`, Unity's left-
    handed Rx then Ry(-yaw), Z/X/Y order): solving
    Ry(-yaw)*Rx(pitch)*(0,0,1) = (-dx, dy, dz) gives yaw = atan2(dx, dz)
    (unchanged by pitch) and pitch = -arcsin(dy). Matches the existing
    yaw-only formula when dy=0; not independently verified in Unity for
    nonzero pitch.
    """

    yaw_deg = float(np.degrees(np.arctan2(direction[0], direction[2])))
    pitch_deg = float(np.degrees(-np.arcsin(direction[1])))
    return pitch_deg, yaw_deg


# Dipole: tilted along the bisector of entrance (HMS_DIR) and average exit
# direction, leaning partway into the bend like a real sector-magnet
# diagram rather than sitting flat.
dipole_bisector = HMS_DIR + avg_exit_direction
dipole_bisector = dipole_bisector / np.linalg.norm(dipole_bisector)
dipole_pitch_deg, dipole_yaw_deg = direction_to_pitch_yaw(dipole_bisector)
dipole["euler_angles_deg"] = [dipole_pitch_deg, dipole_yaw_deg, 0.0]

# Sized off the *design* trajectory's climb rather than the sample max --
# real per-track exit height varies a lot with momentum (one low-p outlier
# in a typical sample can be 2-3x the rest), so a max-driven box is
# oversized for the typical track.
DIPOLE_HEIGHT_MARGIN = 2.0
dipole_climb_m = abs(float(design_exit_vertex[1]))
dipole["size"][1] = float(DIPOLE_HEIGHT_MARGIN * dipole_climb_m)

hut_pitch_deg, hut_yaw_deg = direction_to_pitch_yaw(avg_exit_direction)

hut_start = design_exit_vertex + avg_exit_direction * POST_DIPOLE_DRIFT_M
hut_center = hut_start + avg_exit_direction * (hut["size"][2] / 2.0)
BACK_PLANE_POINT = hut_start + avg_exit_direction * hut["size"][2]
BACK_PLANE_NORMAL = avg_exit_direction

hut["position"] = [float(v) for v in hut_center]
hut["euler_angles_deg"] = [hut_pitch_deg, hut_yaw_deg, 0.0]

with open(GEOMETRY_FILE, "w") as f:
    json.dump(geometry, f, indent=4)

print(
    f"HMS_DIR yaw = {HMS_YAW_DEG:.2f} deg, design bend = "
    f"{np.degrees(np.arccos(np.dot(design_exit_direction, HMS_DIR))):.2f} deg further "
    f"(mean p = {track_p.mean():.2f} GeV); average of {len(selected)} sampled tracks' "
    f"actual exit angles = {np.degrees(np.arccos(np.dot(avg_exit_direction, HMS_DIR))):.2f} deg"
)
print(
    f"Dipole tilted to pitch = {dipole_pitch_deg:.2f} deg, yaw = {dipole_yaw_deg:.2f} deg "
    f"(bisector of entrance/exit); height = {dipole['size'][1]:.2f} m "
    f"({DIPOLE_HEIGHT_MARGIN:.1f}x design climb of {dipole_climb_m:.2f} m)"
)
print(
    f"Hut repositioned to {hut['position']}, pitch = {hut_pitch_deg:.2f} deg, "
    f"yaw = {hut_yaw_deg:.2f} deg"
)


# ============================================================================
# Create VIRTUE Header
# ============================================================================

output = {
    "header": {
        "version": "3.2.1",
        "experiment": "Hall C HMS",
        "energy_unit": "GeV",
        "color_bar": "Log",
        "length_unit": "mm",
        "model_file": os.path.basename(GEOMETRY_FILE),
        # time_after covers the longest track's full 6-segment duration
        # (~87-91 ns) with margin; time_before roughly matches the
        # beampipe's own length.
        "time_before": 20.0,
        "time_after": 100.0,
        "particles": [
            {
                "angle_rad": beam_angle_rad,
                "size": 150.0,
                "color_rgba": [1.0, 0.0, 0.0, 1.0],
            }
        ],
        "tracker_settings": {
            "B_field_T": float(B_FIELD_T),
            # HEADER_B_FIELD_DIRECTION, not B_FIELD_DIRECTION -- see its
            # definition above.
            "B_field_direction": [float(v) for v in HEADER_B_FIELD_DIRECTION],
            "tracker_boundary": [
                float(TRACKER_RADIUS_M * MM_PER_M),
                0.0,
                float(TRACKER_LENGTH_M * MM_PER_M),
            ],
            "segment_ns": float(SEGMENT_NS),
        },
    },
    "events": [],
}

# ============================================================================
# Build Events
# ============================================================================

print("Building events...")

for n, i in enumerate(selected):

    fraction = normalize_energy_log(track_energy[n], min_energy, max_energy)
    fraction = max(0.0, min(1.0, fraction))
    color = (
        [0.0, 0.0, 0.0, 1.0]
        if fraction == 0
        else [float(fraction), 0.0, float(1.0 - fraction), 1.0]
    )

    segments, start_time, vertex2, velocity2 = dipole_exit_cache[n]
    segments = finish_track_segments(segments, start_time, vertex2, velocity2)

    event = {
        "event_data": {
            "info_text": f"HMS Event #{int(i)}, p = {track_p[n]:.3f} GeV",
            "energy_scale": [min_energy, max_energy],
        },
        "hits": [],
        "tracks": [
            {
                "qOverP": seg["qOverP"],
                "angle_rad": seg["angle_rad"],
                "vertex": [float(v * MM_PER_M) for v in seg["vertex"]],
                "duration_ns": seg["duration_ns"],
                "color_rgba": color,
            }
            for seg in segments
        ],
        "clusters": [],
        "jets": [],
        "blocks": [],
    }

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
