using System.Collections;
using UnityEngine;

public class VRModeController : MonoBehaviour
{
    public PlayerController playerController;
    public Camera mainCamera;
    public Camera vrCamera;
    // Renders nothing (empty culling mask) and just clears the full screen
    // to black every frame, at a lower depth than mainCamera/vrCamera. Each
    // eye camera only clears within its own square viewport, so without
    // this the space outside both squares (the gaps at the edges and
    // between them) would never get cleared and would show stale pixels
    // from whatever was last rendered there instead of solid black.
    public Camera backgroundCamera;
    public GameObject navigationContainer;
    public GameObject vrOverlay;
    public PauseMenu pauseMenu;
    public GameObject[] logos;
    public RectTransform calibrationDotLeft;
    public RectTransform calibrationDotRight;

    // Horizontal shift (normalized screen-width units) applied symmetrically
    // to each eye's square viewport, moving them closer together or farther
    // apart from their default position (centered in their half of the
    // screen). This is purely a 2D screen-space adjustment for aligning the
    // two rendered squares with the user's actual eye spacing when viewed
    // through a cardboard-style viewer -- it's unrelated to the 3D stereo
    // separation below, and never touches camera position or rotation.
    // Positive values move the squares apart.
    public float eyeSeparation = -0.08f;

    // How far apart the two eye cameras sit in 3D world space, as a ratio
    // of the camera-to-model viewing distance at the moment VR mode is
    // entered, so each eye renders a genuinely different viewpoint (real
    // stereo parallax) instead of both eyes seeing the identical image.
    // Expressed as a ratio of view distance rather than a fixed world-unit
    // separation because zoom (PlayerController) is disabled while in VR
    // mode, so the view distance at entry could be anywhere the user last
    // left it in normal mode -- scaling by that distance keeps the stereo
    // effect at a roughly consistent strength instead of being subtle at
    // one zoom level and extreme at another. Unverified: the right value
    // depends on how this scene's world units relate to a comfortable
    // stereo effect, which needs eyes-on tuning on-device.
    public float stereoEyeSeparationRatio = 0.03f;

    // Vertical field of view (degrees) used for both eye cameras while in
    // VR mode, wider than the normal (non-VR) FOV to better fill a
    // cardboard-style viewer's field of vision. mainCamera's normal-mode
    // FOV is captured on entry and restored on exit. Unverified: 90 is a
    // guessed starting point, needs on-device tuning -- too wide will
    // distort the edges of the view since there's no lens-distortion
    // correction being applied to compensate.
    public float vrFieldOfView = 90f;

    // Hold-to-move along the gaze direction while in VR mode: volume-up
    // moves forward, volume-down moves back. Speeds and limits are ratios of
    // the camera-to-model viewing distance at the moment VR mode is entered
    // (the same reference stereoEyeSeparationRatio uses), so movement feels
    // the same regardless of model scale or the zoom level the user left
    // normal mode at. Unverified: these are starting points that need
    // on-device tuning for comfort.
    //
    // Top speed, in view-distances per second.
    public float moveSpeedRatio = 0.25f;
    // Time (seconds) to ease from stopped to top speed and back. A short ramp
    // instead of an instant start/stop reduces motion sickness.
    public float moveEaseSeconds = 0.3f;
    // Furthest the head may get from the world origin (where the model is
    // centered), in view-distances, so the user can't fly off into empty
    // space and lose the model entirely.
    public float maxDistanceRatio = 3f;

    // How long the calibration dots stay visible after eyeSeparation was
    // last changed, so they read as "while adjusting" without needing
    // separate start/end event wiring.
    private const float calibrationDotsHoldSeconds = 1f;

    // How much eyeSeparation changes per volume button press in calibration
    // mode. Android-only. UnityEngine.Input has no way to read hardware
    // volume keys, so this is driven by a native plugin
    // (Assets/Plugins/Android/VRVolumeActivity.java) that intercepts them at
    // the Activity level while VR mode is active and forwards them via the
    // OnVolume*Pressed/Released handlers below.
    private const float eyeSeparationStep = 0.01f;

    // Bounds for eyeSeparation. These previously came from the on-screen
    // slider's min/max, which was removed -- kept as the same values here.
    private const float eyeSeparationMin = -0.2f;
    private const float eyeSeparationMax = 0.05f;

    private bool vrModeActive = false;
    private Quaternion gyroCalibration = Quaternion.identity;
    private bool hudWasActiveBeforeVR = true;
    private float calibrationDotsTimer = 0f;

    // The shared "head" position both eye cameras are offset from, and
    // mainCamera's local position before VR mode overwrote it with a
    // world-space eye position -- restored on exit so PlayerController's
    // normal orbit-camera behavior isn't left permanently offset.
    private Vector3 vrHeadPosition;
    private Quaternion mainCameraLocalRotationBeforeVR;
    private Vector3 mainCameraLocalPositionBeforeVR;
    private float mainCameraFovBeforeVR;
    private float stereoEyeSeparation;

    // Volume-key state. In movement mode, holding a key moves; in
    // calibration mode, each press adjusts eyeSeparation. Pressing both keys
    // together toggles between the two modes (a "chord"), which works with
    // the phone inside a headset where the on-screen toggle can't be reached.
    private bool volumeUpHeld = false;
    private bool volumeDownHeld = false;
    // Set when both keys were down together during the current press; keeps
    // the chord's individual key presses/releases from also moving or
    // adjusting, until both keys are released again.
    private bool volumeChordActive = false;
    private bool calibrationModeActive = false;

    private float viewDistanceAtEntry;
    private float moveVelocity = 0f;

    void Update()
    {
        if (!vrModeActive) return;

        if (SystemInfo.supportsGyroscope && Input.gyro.enabled && !IsZeroQuaternion(Input.gyro.attitude))
        {
            Quaternion current = ConvertGyroRotation(Input.gyro.attitude);
            mainCamera.transform.rotation = gyroCalibration * current;
        }

        UpdateMovement();

        // Offset each eye camera symmetrically from the shared head
        // position along its own local right axis, so mainCamera (left
        // eye, rendered into the left screen square) and vrCamera (right
        // eye, right square) see genuinely different viewpoints instead of
        // an identical image -- real stereo parallax rather than a flat
        // stereogram.
        Vector3 right = mainCamera.transform.rotation * Vector3.right;
        Vector3 eyeOffset = right * (stereoEyeSeparation * 0.5f);
        mainCamera.transform.position = vrHeadPosition - eyeOffset;
        vrCamera.transform.position = vrHeadPosition + eyeOffset;
        vrCamera.transform.rotation = mainCamera.transform.rotation;

        // While calibration mode is on, the dots stay up the whole time
        // instead of on SetEyeSeparation's short hold timer.
        if (!calibrationModeActive && calibrationDotsTimer > 0f)
        {
            calibrationDotsTimer -= Time.deltaTime;
            if (calibrationDotsTimer <= 0f)
            {
                calibrationDotLeft.gameObject.SetActive(false);
                calibrationDotRight.gameObject.SetActive(false);
            }
        }
    }

    // Applies a new eyeSeparation value and shows the two calibration dots
    // for a short hold afterward, so they read as visible "while adjusting".
    public void SetEyeSeparation(float value)
    {
        eyeSeparation = value;
        UpdateStereoLayout();
        calibrationDotLeft.gameObject.SetActive(true);
        calibrationDotRight.gameObject.SetActive(true);
        calibrationDotsTimer = calibrationDotsHoldSeconds;
    }

    // Called by the native Android plugin (VRVolumeActivity.java, under
    // Assets/Plugins/Android) via UnitySendMessage when a hardware volume
    // button goes down or up (auto-repeat while held is filtered out on the
    // Java side). The string parameter is required by UnitySendMessage's
    // calling convention even though it's unused here.
    //
    // Movement mode (default): hold up = forward, hold down = back, along
    // the gaze direction (applied each frame in UpdateMovement).
    // Calibration mode: each up/down press nudges eyeSeparation, applied on
    // release so a two-key chord never also counts as an adjustment.
    // Both keys together: toggle between the two modes.
    public void OnVolumeUpPressed(string message)
    {
        if (!vrModeActive) return;
        volumeUpHeld = true;
        if (volumeDownHeld) OnVolumeChord();
    }

    public void OnVolumeDownPressed(string message)
    {
        if (!vrModeActive) return;
        volumeDownHeld = true;
        if (volumeUpHeld) OnVolumeChord();
    }

    public void OnVolumeUpReleased(string message)
    {
        if (!volumeUpHeld) return;
        volumeUpHeld = false;
        if (vrModeActive && calibrationModeActive && !volumeChordActive)
            AdjustEyeSeparation(eyeSeparationStep);
        EndChordIfReleased();
    }

    public void OnVolumeDownReleased(string message)
    {
        if (!volumeDownHeld) return;
        volumeDownHeld = false;
        if (vrModeActive && calibrationModeActive && !volumeChordActive)
            AdjustEyeSeparation(-eyeSeparationStep);
        EndChordIfReleased();
    }

    private void OnVolumeChord()
    {
        if (volumeChordActive) return;
        volumeChordActive = true;
        ToggleCalibrationMode();
    }

    private void EndChordIfReleased()
    {
        if (!volumeUpHeld && !volumeDownHeld) volumeChordActive = false;
    }

    private void ReleaseVolumeKeys()
    {
        volumeUpHeld = false;
        volumeDownHeld = false;
        volumeChordActive = false;
    }

    // If the app loses focus mid-press (notification shade, app switch),
    // the matching key-up may never arrive -- don't keep moving forever.
    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ReleaseVolumeKeys();
    }

    void OnDestroy()
    {
        SetAndroidVolumeInterception(false);
    }

    // Switches the volume keys between movement and eye-separation
    // calibration. Bind to a button on the VR overlay; also triggered by
    // pressing both volume keys together. Calibration mode stays on until
    // toggled off again (or VR mode is exited).
    public void ToggleCalibrationMode()
    {
        if (!vrModeActive) return;
        SetCalibrationMode(!calibrationModeActive);
    }

    private void SetCalibrationMode(bool active)
    {
        calibrationModeActive = active;
        calibrationDotsTimer = 0f;
        calibrationDotLeft.gameObject.SetActive(active);
        calibrationDotRight.gameObject.SetActive(active);
    }

    // Volume-button shortcut for SetEyeSeparation.
    private void AdjustEyeSeparation(float delta)
    {
        float newValue = Mathf.Clamp(eyeSeparation + delta, eyeSeparationMin, eyeSeparationMax);
        SetEyeSeparation(newValue);
    }

    // Moves the shared head position (which both eye cameras are offset
    // from in Update) along the current gaze direction while a volume key
    // is held in movement mode, easing in and out over moveEaseSeconds.
    private void UpdateMovement()
    {
        float input = 0f;
        if (!calibrationModeActive && !volumeChordActive)
            input = (volumeUpHeld ? 1f : 0f) - (volumeDownHeld ? 1f : 0f);

        float maxSpeed = moveSpeedRatio * viewDistanceAtEntry;
        float acceleration = moveEaseSeconds > 0f ? maxSpeed / moveEaseSeconds : float.PositiveInfinity;
        moveVelocity = Mathf.MoveTowards(moveVelocity, input * maxSpeed, acceleration * Time.deltaTime);

        if (moveVelocity == 0f) return;

        vrHeadPosition += mainCamera.transform.forward * (moveVelocity * Time.deltaTime);

        float maxDistance = maxDistanceRatio * viewDistanceAtEntry;
        if (vrHeadPosition.magnitude > maxDistance)
            vrHeadPosition = vrHeadPosition.normalized * maxDistance;
    }

    // Tells the Android plugin whether to intercept the volume keys. Only
    // while VR mode is active -- otherwise they reach Android normally and
    // change the phone's volume as usual.
    private static void SetAndroidVolumeInterception(bool active)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var activityClass = new AndroidJavaClass("com.Quantanaut.VIRTUE.VRVolumeActivity"))
            {
                activityClass.CallStatic("setVRModeActive", active);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[VRMode] Could not toggle volume-key interception: " + e.Message);
        }
#endif
    }

    // Recomputes each eye's square viewport (sized to exactly fill the
    // screen height, so it renders as a perfect square regardless of screen
    // aspect ratio) and repositions the calibration dots to match, based on
    // the current eyeSeparation. Purely 2D screen-space layout -- camera
    // position/rotation are never touched here.
    private void UpdateStereoLayout()
    {
        float squareWidth = (float)Screen.height / Screen.width;
        float halfSquare = squareWidth * 0.5f;

        float leftCenterX = 0.25f - eyeSeparation;
        float rightCenterX = 0.75f + eyeSeparation;

        float leftMin = leftCenterX - halfSquare;
        float leftMax = leftCenterX + halfSquare;
        float rightMin = rightCenterX - halfSquare;
        float rightMax = rightCenterX + halfSquare;

        // If the two squares would overlap, crop each one at the screen's
        // vertical midline instead of letting mainCamera/vrCamera paint over
        // each other there (which, at equal camera depth, made the left
        // square appear to shrink while the right stayed full-size instead
        // of both narrowing symmetrically).
        leftMax = Mathf.Min(leftMax, 0.5f);
        rightMin = Mathf.Max(rightMin, 0.5f);

        mainCamera.rect = new Rect(leftMin, 0f, Mathf.Max(0f, leftMax - leftMin), 1f);
        vrCamera.rect = new Rect(rightMin, 0f, Mathf.Max(0f, rightMax - rightMin), 1f);

        SetDotCenterX(calibrationDotLeft, leftCenterX);
        SetDotCenterX(calibrationDotRight, rightCenterX);
    }

    private static void SetDotCenterX(RectTransform dot, float normalizedX)
    {
        dot.anchorMin = new Vector2(normalizedX, 0.5f);
        dot.anchorMax = new Vector2(normalizedX, 0.5f);
    }

    // Input.gyro.attitude reads as the degenerate zero quaternion (0,0,0,0)
    // for at least the first frame after Input.gyro.enabled is set, before
    // the sensor produces its first real reading. Computing gyroCalibration
    // from that zero reading poisons it permanently: any quaternion
    // multiplied by a zero quaternion is itself zero, so every frame's
    // mainCamera.transform.rotation assignment in Update() would evaluate
    // to zero and get silently normalized to the same fallback rotation --
    // camera frozen forever, even though the live sensor data is fine. Wait
    // for a real (non-zero) reading before calibrating.
    private IEnumerator CalibrateGyro(Quaternion lookRotation)
    {
        Quaternion attitude = Input.gyro.attitude;
        int framesWaited = 0;
        while (IsZeroQuaternion(attitude) && framesWaited < 30)
        {
            yield return null;
            attitude = Input.gyro.attitude;
            framesWaited++;
        }
        gyroCalibration = lookRotation * Quaternion.Inverse(ConvertGyroRotation(attitude));
    }

    private static bool IsZeroQuaternion(Quaternion q)
    {
        return (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) < 0.0001f;
    }

    public void EnterVRMode()
    {
        if (vrModeActive) return;
        vrModeActive = true;

        playerController.enabled = false;
        playerController.player.transform.position = playerController.DefaultPosition;

        // Captured before mainCamera's rotation is touched below -- the
        // normal (non-VR) camera relies on mainCamera keeping this fixed
        // local rotation relative to the player while PlayerController
        // orients the player itself via LookAt each frame. Restored in
        // ExitVRMode.
        mainCameraLocalRotationBeforeVR = mainCamera.transform.localRotation;
        mainCameraLocalPositionBeforeVR = mainCamera.transform.localPosition;

        Quaternion lookRotation = Quaternion.LookRotation(-playerController.DefaultPosition.normalized, Vector3.up);
        mainCamera.orthographic = false;
        mainCamera.transform.rotation = lookRotation;

        mainCameraFovBeforeVR = mainCamera.fieldOfView;
        mainCamera.fieldOfView = vrFieldOfView;
        vrHeadPosition = mainCamera.transform.position;
        stereoEyeSeparation = stereoEyeSeparationRatio * playerController.player.transform.position.magnitude;

        // Reference distance for movement speed/limits. Floored so a head
        // that somehow starts at the origin still gets usable movement.
        viewDistanceAtEntry = Mathf.Max(playerController.player.transform.position.magnitude, vrHeadPosition.magnitude, 0.01f);
        moveVelocity = 0f;
        ReleaseVolumeKeys();
        calibrationModeActive = false;
        SetAndroidVolumeInterception(true);

        // Recenter the gyro so "forward" at the moment VR mode is entered
        // matches the app's default view direction, instead of whatever
        // absolute heading the phone happens to be pointed at.
        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
            StartCoroutine(CalibrateGyro(lookRotation));
        }
        else
        {
            Debug.LogWarning("[VRMode] SystemInfo.supportsGyroscope is false -- the camera will never respond to phone movement.");
        }

        vrCamera.CopyFrom(mainCamera);
        vrCamera.gameObject.SetActive(true);
        backgroundCamera.gameObject.SetActive(true);
        UpdateStereoLayout();

        navigationContainer.SetActive(false);
        vrOverlay.SetActive(true);

        hudWasActiveBeforeVR = pauseMenu.HUDActive;
        pauseMenu.SetHUDActive(false);

        foreach (GameObject logo in logos)
            logo.SetActive(false);

        calibrationDotLeft.gameObject.SetActive(false);
        calibrationDotRight.gameObject.SetActive(false);
        calibrationDotsTimer = 0f;
    }

    // Re-centers the gyro so "forward" is wherever the phone is currently
    // pointing, the same recentering EnterVRMode does on entry. Bound to the
    // reset-view button shown in VR mode, for when drift or an awkward
    // starting pose has thrown off the view.
    public void ResetView()
    {
        if (!vrModeActive) return;

        Quaternion lookRotation = Quaternion.LookRotation(-playerController.DefaultPosition.normalized, Vector3.up);
        if (SystemInfo.supportsGyroscope)
        {
            StartCoroutine(CalibrateGyro(lookRotation));
        }
        else
        {
            mainCamera.transform.rotation = lookRotation;
        }
    }

    public void ExitVRMode()
    {
        if (!vrModeActive) return;
        vrModeActive = false;

        SetAndroidVolumeInterception(false);
        ReleaseVolumeKeys();
        moveVelocity = 0f;
        calibrationModeActive = false;

        vrCamera.gameObject.SetActive(false);
        backgroundCamera.gameObject.SetActive(false);
        mainCamera.rect = new Rect(0f, 0f, 1f, 1f);
        mainCamera.transform.localPosition = mainCameraLocalPositionBeforeVR;
        mainCamera.transform.localRotation = mainCameraLocalRotationBeforeVR;
        mainCamera.fieldOfView = mainCameraFovBeforeVR;

        if (SystemInfo.supportsGyroscope) Input.gyro.enabled = false;

        navigationContainer.SetActive(true);
        vrOverlay.SetActive(false);

        pauseMenu.SetHUDActive(hudWasActiveBeforeVR);

        foreach (GameObject logo in logos)
            logo.SetActive(true);

        calibrationDotLeft.gameObject.SetActive(false);
        calibrationDotRight.gameObject.SetActive(false);
        calibrationDotsTimer = 0f;

        playerController.enabled = true;
    }

    // Converts a device attitude quaternion (right-handed, Z out of the
    // screen) into Unity's left-handed coordinate convention.
    //
    // This used to also apply a +/-90 degree correction around Z depending
    // on Screen.orientation, on the assumption that Input.gyro.attitude is
    // always relative to the device's portrait-native reference frame. That
    // assumption doesn't hold on every device: some phones/tablets have
    // landscape as their sensor's natural orientation, in which case the
    // raw attitude is already landscape-relative and adding a further +/-90
    // correction on top introduces a spurious swap between pitch and yaw
    // (confirmed on-device: tilting the phone panned the view instead of
    // pitching it, and turning the phone pitched it instead of panning --
    // both directions of the swap were present, and persisted after flipping
    // the correction's sign, which pointed at the correction itself being
    // the wrong thing to apply here rather than its sign being wrong).
    private static Quaternion ConvertGyroRotation(Quaternion q)
    {
        return new Quaternion(q.x, q.y, -q.z, -q.w);
    }
}
