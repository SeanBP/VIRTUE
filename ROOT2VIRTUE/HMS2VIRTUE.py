#!/usr/bin/env python3

"""
Hall C HMS ROOT -> VIRTUE Converter

Converts reconstructed "golden track" (H.gtr.*) electrons from an HMS
replay ROOT file into the JSON format used by the VIRTUE event display.

Only track objects are produced (no hits/clusters/jets/blocks).

Each electron is written as six consecutive track segments rather than
one, since EventLoader.cs applies B_field_T uniformly across the whole
tracker volume -- a single 2 T track over the full 26 m would curve
long before/after the real (5.3 m) dipole region:
    1-3. Target -> Q1 -> Q2 -> Q3: straight (qOverP = 0) legs, each
       ending in an idealized thin-lens kick at that quad's own
       position. There's no real quad transport-matrix data available,
       so the three lenses share one focal length (TRIPLET_FOCAL_LENGTH_M,
       solve_shared_focal_length()) chosen so the *combined* triplet
       still images the target onto the dipole entrance point-to-point
       (independent of the target angle) -- generalizing the same
       single-lens idea used before to the triplet's actual three
       positions. Without some refocusing, the target's angular
       acceptance (tens of mrad) diverges enough over ~9 m to clip the
       tracker radius well before the dipole even starts, especially
       vertically.
    4. Q3 -> dipole entrance: straight (qOverP = 0), using the angle
       coming out of the Q3 kick.
    5. Inside the dipole: curved, using that same direction, the real
       qOverP, and B_field_T.
    6. Dipole exit -> detector hut back plane: straight (qOverP = 0),
       using the direction the field left it pointing.
The dipole's and quads' z-extents are read directly from HallC_HMS.json
rather than hardcoded. Each segment's vertex/duration_ns picks up
exactly where the previous one left off (EventLoader resets its
internal clock to t=0 at each track's own vertex, using duration_ns[0]
only as a display-timing offset and duration_ns[1] as that segment's
own physics duration).

Coordinate systems
-------------------
The HMS golden-track branches are expressed in the Hall Coordinate
System (HCS), which is left-handed:
    X_hcs = horizontal, beam-left
    Y_hcs = vertical, up
    Z_hcs = downstream, along the beam

(Confirmed against the data: px/p averages -sin(20 deg) across all
tracks with only ~1 deg spread, i.e. X carries the full 20 deg HMS
placement offset, while py/p averages ~0 with a wider ~3 deg spread --
so X is the horizontal/dispersive axis and Y is vertical.)

VIRTUE expects a right-handed frame with Z along the beamline itself
(not HMS's own central ray, which sits at HMS_ANGLE_DEG off Z, on the
beam-right side -- the fixed rail HMS occupies in Hall C, matching the
negative px/p above). Since HCS's own Z is already the beamline, the
conversion is just the handedness fix:
    1. Flip Y (up -> down).
No X/Z rotation is needed or applied (hcs_to_virtue() is called with
phi_rot=0.0 below). HMS's own central ray -- what Q1/Q2/Q3 and the
dipole entrance sit along -- is now an off-axis direction, HMS_DIR,
tilted HMS_ANGLE_DEG off Z toward -X. Every straight track segment
before the dipole is found by intersecting with a plane (point on
HMS_DIR, normal HMS_DIR) rather than reaching a Z-coordinate, since
these elements are no longer on the Z axis themselves.

There is no reconstructed vertex-z (reactz) branch in this file, so the
target vertex is taken as (H.gtr.x, H.gtr.y, 0) before rotation.
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

# Written directly into the Unity project's StreamingAssets so the app
# picks them up with no manual copy step.
_STREAMING_ASSETS = (
    "/Users/seanbp/Documents/Unity/VIRTUE Lite Unity Project/Assets/StreamingAssets"
)
OUTPUT_FILE = f"{_STREAMING_ASSETS}/Events/HMS_Events.json"
GEOMETRY_FILE = f"{_STREAMING_ASSETS}/Models/HallC_HMS.json"

MAX_EVENTS = 200

ELECTRON_MASS_GEV = 0.000511

# HMS's dipole bends by a fixed mechanical design angle regardless of the
# momentum it's tuned to select, commonly cited as ~25.5 deg. B_field_T
# is computed further down (after the dipole length is known and tracks
# are loaded) from r = L/sin(DESIGN_BEND_ANGLE_DEG) and r = p/(0.3*B),
# using the mean reconstructed momentum as the run's central value --
# the run's real central-momentum/field setting isn't otherwise known,
# so this is a best estimate consistent with HMS's actual optics rather
# than an assumed round number.
DESIGN_BEND_ANGLE_DEG = 25.5

# Each track segment's own duration_ns is computed exactly (segment 4
# specifically terminates at the detector hut's back plane -- see
# BACK_PLANE_POINT/NORMAL below), so the tracker boundary itself isn't
# what decides where a track ends. It's sized deliberately loose here,
# just large enough that it never clips a track before its own computed
# endpoint does -- the dipole bends tracks by ~25.5 deg, well beyond
# what a boundary tight around the pre-dipole axis could contain anyway.
TRACKER_RADIUS_M = 50.0
TRACKER_LENGTH_M = 50.0

# Segment length used to subdivide each track's curved path (ns). HMS
# tracks span ~26 m at close to the speed of light (~90 ns of flight
# time), so a coarser step than the display default keeps the segment
# count per track manageable when many events are loaded at once.
SEGMENT_NS = 0.2

# EventLoader falls back to timing defaults of the form
# "0.001 * X / totScale" (totScale = header.scale * length_unit factor)
# whenever the app's rate/before/after UI fields are empty, which they
# are by default. That formula is calibrated for millimeter-scale files
# (totScale ~ 0.001); at length_unit "m" (totScale = 1.0) it collapses
# to sub-nanosecond fallback windows, so no track segment ever becomes
# visible before the event auto-advances. All example VIRTUE event
# files use "mm" for this reason -- so length values are written out in
# mm here even though the geometry is computed in meters above.
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
    Right-handed basis {e1, e2, b_hat} spanning the plane perpendicular to
    the field, mirroring EventLoader.cs's CreateTrackObjects exactly (same
    Vector3.up/Vector3.right fallback for a degenerate cross product).
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
    field b_field*b_hat, mirroring EventLoader.cs's HelixPosition (plus its
    time-derivative for velocity).
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
    (plane_point, plane_normal), via a coarse forward scan of the signed
    distance to the plane (brackets the crossing) then bisection. Returns
    None if the plane is never crossed within t_max. Generalizes a
    Z-coordinate crossing, which is just the case plane_point=(0,0,z),
    plane_normal=(0,0,1).
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
    crosses the plane (plane_point, plane_normal). None if moving
    parallel to the plane (never crosses).
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
    that a point at z_object images to a single point at z_image
    independent of the emission angle (point-to-point imaging: the
    combined ABCD matrix's (0,1) element -- the coefficient of the
    initial angle in the final position -- is zero). Generalizes the
    single-lens f = d1*d2/(d1+d2) result to N lenses.

    With multiple lenses this equation has several roots (the ray can
    cross the axis different numbers of times between lenses); this
    returns the largest-f root found in the scanned range, i.e. the
    weakest/least extreme lensing that still satisfies the imaging
    condition, which is the physically sensible one for a real optical
    system that doesn't cross the axis repeatedly between elements.
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
# HMS central-momentum sign indicates spectrometer polarity: negative ->
# configured to bend negatively-charged particles (electrons) to the focal
# plane.
charge = -1.0 if (len(hms_p_readback) and hms_p_readback[0] < 0) else 1.0

phi_rot = np.radians(hms_angle_deg)

# HMS's own central ray direction in the new (Z=beamline) frame: tilted
# HMS_ANGLE_DEG off Z toward -X (beam-right -- verified via the same HCS
# derivation used to sign-check the SHMS placeholder direction below:
# rotating (x_hcs, z_hcs)=(-sin(phi_rot), cos(phi_rot)) -- the vector
# that maps to pure +Z_v under the *old* rotated frame -- through the
# now-trivial (phi_rot=0) hcs_to_virtue transform gives this directly).
HMS_DIR = np.array([-np.sin(phi_rot), 0.0, np.cos(phi_rot)])

print(f"HMS angle: {hms_angle_deg} deg, track charge: {charge:+.0f}")

# ============================================================================
# Incoming Beam Particle
# ============================================================================

# The beam now travels straight along +Z (Z_hcs = Z_virtue, no rotation).
# VIRTUE's Particle direction is built from angle_rad = [a, b] as:
#   dx = -cos(b) sin(a), dy = sin(b), dz = cos(b) cos(a)
# which is satisfied by a = b = 0 for direction (0, 0, 1).
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

# Reject the small number of badly-reconstructed tracks that pass H.gtr.ok
# but carry unphysical (huge) momentum/position values. H.gtr.x/y are in
# the TRANSPORT-convention unit of centimeters (not meters) -- the bulk of
# the distribution sits at a few mm (raster/target size), so a 50 cm bound
# is generous while still rejecting genuine garbage.
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

# HMS_ANGLE_DEG tilts the *entire* pre-dipole spectrometer (target through
# the dipole entrance) off the beamline -- these elements no longer sit on
# the Z axis themselves, so every position below is HMS_DIR times a fixed
# distance from the target, not a Z-coordinate. The local transverse basis
# for the pre-dipole thin-lens optics is {HMS_U, HMS_V}: HMS_V is just
# global Y (the tilt is purely horizontal, so vertical is untouched), and
# HMS_U is the in-plane direction perpendicular to HMS_DIR.
HMS_U = np.array([np.cos(phi_rot), 0.0, np.sin(phi_rot)])
HMS_V = np.array([0.0, 1.0, 0.0])
HMS_YAW_DEG = float(np.degrees(np.arctan2(HMS_DIR[0], HMS_DIR[2])))

with open(GEOMETRY_FILE) as f:
    geometry = json.load(f)

# Distances are read via the norm of each component's *current* position
# rather than its Z-coordinate, so this stays correct (and idempotent)
# whether the file currently holds the original straight-Z layout or an
# already-tilted one from a previous run of this script.
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

# Target and Beampipe sit on the beamline (Z) itself now, so both align
# with it directly -- no tilt.
BEAMPIPE_GAP_M = 0.1  # matches the original gap before the target
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
dipole["euler_angles_deg"] = [0.0, HMS_YAW_DEG, 0.0]
# Widen the dipole block so it visually contains the real (not just
# design-central) per-track spread through the bend.
dipole["size"][0] = 5.0

# SHMS placeholder: mirrored across the beamline from HMS (Hall C's fixed
# hall geometry -- HMS beam-right, SHMS beam-left), at the same
# representative angle magnitude (see the SHMS-placement discussion
# earlier in this project). Now that Z is the beamline itself, this is
# just HMS_DIR reflected in X -- no need to add HMS_ANGLE_DEG in.
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

# B_field_T from HMS's fixed design bend angle and the run's mean
# reconstructed momentum (see DESIGN_BEND_ANGLE_DEG above).
design_radius_m = DIPOLE_LENGTH_M / np.sin(np.radians(DESIGN_BEND_ANGLE_DEG))
B_FIELD_T = float(track_p.mean() / (0.3 * design_radius_m))
print(
    f"Design bend radius = {design_radius_m:.2f} m, mean p = "
    f"{track_p.mean():.2f} GeV -> B_field_T = {B_FIELD_T:.3f} T"
)

B_FIELD_DIRECTION = np.array([0.0, 1.0, 0.0])

# VIRTUE's animated propagation speed is rescaled to a few m/s (not the
# real c) so tracks are watchable in real time. EventLoader.cs keeps
# curvature radii consistent within that rescaled space by never using a
# track's real GeV momentum for the bend math -- it derives an internal
# magnitude from qOverP alone:
#   P_internal = eScale / (cm * |qOverP|) = (1e9 / 2.998e8) * P_real
# (eScale = 1e9 for "GeV", cm = 2.998e8 matches its own constant name).
# Direction-ratio quantities (velocity, a0, b0) are invariant to this
# rescaling, but omega = (q*B/P)*c is not, so the dipole-region curvature
# must be computed with this same internal P or the radius comes out
# ~3.34x too small.
INTERNAL_P_SCALE = 1e9 / 2.998e8

# ============================================================================
# Reposition Bent Geometry (Detector Hut placement)
# ============================================================================

# The dipole bends the design (mean-momentum, on-axis) trajectory by
# DESIGN_BEND_ANGLE_DEG off HMS_DIR, so anything downstream of it --
# concretely, the Detector Hut -- needs to sit along that bent direction.
# Computed with the actual (verified) curvature formula rather than just
# the nominal angle, so it's consistent with the real track physics below
# (INTERNAL_P_SCALE, B_FIELD_DIRECTION, etc.).
#
# The drift-space gap between the dipole exit and the hut is a fixed
# design distance (3.85 m, unrelated to HMS_ANGLE_DEG), same as before.
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
design_yaw_deg = float(
    np.degrees(np.arctan2(design_exit_direction[0], design_exit_direction[2]))
)

hut_start = design_exit_vertex + design_exit_direction * POST_DIPOLE_DRIFT_M
hut_center = hut_start + design_exit_direction * (hut["size"][2] / 2.0)
BACK_PLANE_POINT = hut_start + design_exit_direction * hut["size"][2]
BACK_PLANE_NORMAL = design_exit_direction

hut["position"] = [float(v) for v in hut_center]
hut["euler_angles_deg"] = [0.0, design_yaw_deg, 0.0]

with open(GEOMETRY_FILE, "w") as f:
    json.dump(geometry, f, indent=4)

print(
    f"HMS_DIR yaw = {HMS_YAW_DEG:.2f} deg, design bend = "
    f"{np.degrees(np.arccos(np.dot(design_exit_direction, HMS_DIR))):.2f} deg further, "
    f"hut repositioned to {hut['position']}, yaw = {design_yaw_deg:.2f} deg"
)

print(
    f"Dipole spans {DIPOLE_ENTRANCE_DIST:.2f} to {DIPOLE_EXIT_DIST:.2f} m "
    f"along HMS_DIR"
)

# Shared focal length for the three idealized thin lenses at Q1/Q2/Q3
# (see solve_shared_focal_length() above and the module docstring). This
# is an abstract 1-D optics calculation along whatever axis the lenses
# sit on (HMS_DIR here, not Z), so it just needs the same distances as
# before. Computed once using the nominal target position (0) rather
# than per-track: the target's few-cm spread changes this by a
# completely negligible amount relative to the ~9 m lens-to-image
# distances.
TRIPLET_FOCAL_LENGTH_M = solve_shared_focal_length(
    0.0, [Q1_DIST, Q2_DIST, Q3_DIST], DIPOLE_ENTRANCE_DIST
)
print(
    f"Idealized Q1/Q2/Q3 lenses at {Q1_DIST:.2f}, {Q2_DIST:.2f}, {Q3_DIST:.2f} m "
    f"along HMS_DIR, shared focal length = {TRIPLET_FOCAL_LENGTH_M:.3f} m"
)


def build_track_segments(vertex0, momentum0, p_mag):
    """
    Split one electron's path into six track segments: three straight
    legs through idealized thin lenses at Q1/Q2/Q3, straight from Q3 to
    the dipole entrance (now converging), curved inside the dipole,
    straight after. Returns a list of dicts with
    qOverP/angle_rad/vertex(m)/duration_ns for each segment, in order.
    """

    segments = []
    vertex = vertex0
    direction = momentum0 / p_mag
    start_time = 0.0

    # --- Segments 1-3: straight legs through the Q1/Q2/Q3 thin lenses ---
    # Same idealized-lens point-to-point-imaging idea as before, just
    # split across the triplet's actual three positions instead of one
    # lumped lens (TRIPLET_FOCAL_LENGTH_M, shared by all three, computed
    # once above). Q1/Q2/Q3 are off the Z axis now (tilted along HMS_DIR),
    # so each leg is found via plane intersection rather than reaching a
    # Z-coordinate, and the lens kick itself is applied in the local
    # {HMS_U, HMS_V} transverse basis (perpendicular to HMS_DIR) rather
    # than global (X, Y) -- otherwise the kick wouldn't correctly decouple
    # from the tilted propagation axis.
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

    # --- Segment 4: straight, Q3 -> dipole entrance ---
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

    # --- Segment 5: curved, inside the dipole ---
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
        # Track curves too sharply to cross the dipole exit plane going
        # forward within the search window -- extend once, generously.
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

    # --- Segment 6: straight, dipole exit -> detector hut back plane ---
    # Exact plane intersection (point=BACK_PLANE_POINT, normal=
    # BACK_PLANE_NORMAL) rather than relying on the tracker boundary to
    # clip it -- the boundary is sized generously and shouldn't be the
    # thing deciding where a track ends.
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
# Create VIRTUE Header
# ============================================================================

output = {
    "header": {
        "version": "3.2.0",
        "experiment": "Hall C HMS",
        "energy_unit": "GeV",
        "color_bar": "Log",
        "length_unit": "mm",
        # Auto-load the matching geometry alongside this event file.
        "model_file": os.path.basename(GEOMETRY_FILE),
        # time_after covers the longest track's full 6-segment duration
        # (~87-91 ns, verified against the actual generated events) with
        # some margin, so every track reaches the detector hut's back
        # plane before the event auto-advances. time_before is set to
        # roughly the Beampipe's own length (see HallC_HMS.json) so the
        # animated beam particle appears to originate from about where
        # the pipe starts, rather than the shorter default.
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
            # HMS is a dipole: the field is vertical (+Y in this frame),
            # perpendicular to the central ray, bending tracks in the
            # horizontal X-Z (dispersive) plane -- not solenoidal (+Z).
            "B_field_direction": [0.0, 1.0, 0.0],
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

    momentum0 = np.array([track_px[n], track_py[n], track_pz[n]])
    vertex0 = np.array(track_vertex[n])
    segments = build_track_segments(vertex0, momentum0, track_p[n])

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
