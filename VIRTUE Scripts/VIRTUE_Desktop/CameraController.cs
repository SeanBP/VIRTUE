using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VirtueCore.Tours;

public class CameraController : MonoBehaviour
{
    public Transform target;

    public Vector3 offset;

    public bool useOffsetValues;

    public float rotateSpeed;

    public Transform pivot;
    private bool looping = false;

    public float maxViewAngle;
    public float minViewAngle;
    public PauseMenu pauseMenu;
    private bool isPaused;
    public Camera mainCamera;
    private bool orthoView = false;

    private float sensitivity = 1f;
    private float distance = 10f;

    private bool inTour = false;
    public float targetMoveDuration = 1.0f;
    private int activeCoroutines = 0;
    private Vector3 focusPosition;
    public bool isMoving = false;

    // Front/Side/Top view: camera sits directly at target's position and
    // holds a fixed look direction, bypassing the orbit offset/LookAt(target)
    // logic below entirely (that logic also resets target to the origin
    // every frame while looping, which fights a panned position).
    private bool axisLocked = false;
    private Vector3 lockedForward = Vector3.forward;
    private Vector3 lockedUp = Vector3.up;

    public void SetAxisLock(Vector3 forward, Vector3 upwards)
    {
        axisLocked = true;
        lockedForward = forward;
        lockedUp = upwards;
    }

    public void ClearAxisLock()
    {
        axisLocked = false;
    }

    // ---- Returning from a tour ----
    // A tour takes over where the camera looks (and turns off loop/ortho
    // modes), so how the camera was set up is saved when the tour starts and
    // put back when it ends.
    private struct TourReturnState
    {
        public bool looping;
        public bool orthoView;
        public bool orthographic;
        public bool axisLocked;
        public Vector3 lockedForward;
        public Vector3 lockedUp;
        public Quaternion pivotLocalRotation;
    }

    private TourReturnState? tourReturnState;

    public void CaptureTourReturnState()
    {
        tourReturnState = new TourReturnState
        {
            looping = looping,
            orthoView = orthoView,
            orthographic = mainCamera != null && mainCamera.orthographic,
            axisLocked = axisLocked,
            lockedForward = lockedForward,
            lockedUp = lockedUp,
            pivotLocalRotation = pivot.localRotation
        };
    }

    public void RestoreTourReturnState()
    {
        // The only coroutine here is the tour's SmoothMoveFocus.
        StopAllCoroutines();
        focusCoroutine = null;
        activeCoroutines = 0;
        isMoving = false;
        inTour = false;
        tourRotationActive = false;
        tourRotationProgress = 1f;

        if (!tourReturnState.HasValue)
            return;

        TourReturnState state = tourReturnState.Value;
        tourReturnState = null;

        looping = state.looping;
        orthoView = state.orthoView;
        if (mainCamera != null) mainCamera.orthographic = state.orthographic;
        axisLocked = state.axisLocked;
        lockedForward = state.lockedForward;
        lockedUp = state.lockedUp;
        pivot.localRotation = state.pivotLocalRotation;
    }

    public Vector3 FocusPosition => focusPosition;

    // During a tour the camera's rotation is driven from here rather than by
    // re-aiming at the focus every frame: it turns smoothly from where it
    // was looking to where it will look from the end position. Re-aiming
    // every frame made the view swing sharply whenever the camera passed
    // near the focus point or near the vertical axis through it.
    private bool tourRotationActive = false;
    private Quaternion tourRotationStart;
    private Quaternion tourRotationEnd;
    private float tourRotationProgress = 1f;

    public void MoveTargetTo(Vector3 worldPosition)
    {
        MoveTargetTo(worldPosition, targetMoveDuration, target.position);
    }

    // Moves the focus to worldPosition over `duration` seconds, while the
    // camera (on its way to endCameraPosition) turns to face it.
    public void MoveTargetTo(Vector3 worldPosition, float duration, Vector3 endCameraPosition)
    {
        if (!inTour)
        {
            mainCamera.orthographic = false;
            looping = false;
            axisLocked = false;
        }

        // A newer move (the user skipping to another tour scene mid-flight)
        // replaces this one; it sets off from the focus and rotation the
        // camera has reached, so the redirect is smooth.
        if (focusCoroutine != null) StopCoroutine(focusCoroutine);
        focusCoroutine = StartCoroutine(SmoothMoveFocus(worldPosition, duration, endCameraPosition));
    }

    // Points the camera straight at worldPosition from cameraPosition with
    // no transition (the first scene of a tour).
    public void SnapTargetTo(Vector3 worldPosition, Vector3 cameraPosition)
    {
        if (focusCoroutine != null) { StopCoroutine(focusCoroutine); focusCoroutine = null; }

        if (!inTour)
        {
            mainCamera.orthographic = false;
            looping = false;
            axisLocked = false;
        }

        focusPosition = worldPosition;
        tourRotationEnd = TourMotion.LookRotation(cameraPosition, worldPosition, transform.rotation);
        tourRotationStart = tourRotationEnd;
        tourRotationProgress = 1f;
        tourRotationActive = true;
        inTour = true;
        isMoving = false;
        transform.rotation = tourRotationEnd;
    }

    private Coroutine focusCoroutine;

    private IEnumerator SmoothMoveFocus(Vector3 destination, float duration, Vector3 endCameraPosition)
    {
        isMoving = true;

        Vector3 startPos = focusPosition;
        tourRotationStart = transform.rotation;
        tourRotationEnd = TourMotion.LookRotation(endCameraPosition, destination, tourRotationStart);
        tourRotationProgress = 0f;
        tourRotationActive = true;
        inTour = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float eased = TourMotion.Ease(duration > 0f ? elapsed / duration : 1f);

            focusPosition = Vector3.Lerp(startPos, destination, eased);
            tourRotationProgress = eased;
            yield return null;
        }

        focusPosition = destination;
        tourRotationProgress = 1f;
        isMoving = false;
        focusCoroutine = null;
    }


    // Start is called before the first frame update
    void Start()
    {

        if (!useOffsetValues)
        {
            offset = target.position - transform.position;
        }
        pivot.transform.position = target.transform.position;
        pivot.transform.parent = target.transform;

    }

    // Update is called once per frame
    void Update()
    {
        if (Cursor.lockState == CursorLockMode.None)
            isPaused = true;
        else
            isPaused = false;

        if (axisLocked)
        {
            transform.position = target.position;
            transform.rotation = Quaternion.LookRotation(lockedForward, lockedUp);

            if (orthoView)
            {
                mainCamera.orthographicSize = distance / 3f;
            }
            return;
        }

        string[] texts = Input.GetJoystickNames();
        for (int i = 0; i < texts.Length; i++)
        {
            if (!string.IsNullOrEmpty(texts[i]))
            {
                sensitivity = 0.1f;
            }
        }
        float horizontal = Input.GetAxis("Mouse X") * rotateSpeed * sensitivity;
        float vertical = Input.GetAxis("Mouse Y") * rotateSpeed * sensitivity;
        if (!isPaused && !looping && !inTour)
        {
            pivot.Rotate(-vertical, 0, 0);
            target.Rotate(0, horizontal, 0);
        }
        else
        {
            pivot.Rotate(0, 0, 0);
            target.Rotate(0, 0, 0);
        }

        if (pivot.rotation.eulerAngles.x > maxViewAngle && pivot.rotation.eulerAngles.x < 180f)
        {
            pivot.rotation = Quaternion.Euler(maxViewAngle, 0, 0);
        }
        if (pivot.rotation.eulerAngles.x > 180 && pivot.rotation.eulerAngles.x < 360f + minViewAngle)
        {
            pivot.rotation = Quaternion.Euler(360f + minViewAngle, 0, 0);
        }

        float desiredYAngle = target.eulerAngles.y;
        float desiredXAngle = pivot.eulerAngles.x;

        Quaternion rotation = Quaternion.Euler(desiredXAngle, desiredYAngle, 0);
        transform.position = target.position - (rotation * offset);

        if (transform.position.y < target.position.y - 0.5f)
        {
            transform.position = new Vector3(transform.position.x, target.position.y - 0.5f, transform.position.z);
        }
        if (looping == true)
        {
            target.transform.position = new Vector3(0f, 0f, 0f);
        }
        if (!inTour)
            transform.LookAt(target);
        else if (tourRotationActive)
            transform.rotation = Quaternion.Slerp(tourRotationStart, tourRotationEnd, tourRotationProgress);
        else
            transform.LookAt(focusPosition);

        if (orthoView)
        {
            mainCamera.orthographicSize = distance / 3f;
        }

    }

    public void ChangeDist(string newValue)
    {
        try
        {
            distance = Mathf.Abs(float.Parse(newValue));
        }
        catch
        {
            distance = 10f;
        }
        
        if (distance < 0.2f)
        {
            distance = 0.2f;
        }

    }

    public void StopLooping()
    {
        if (mainCamera != null)
        {
            mainCamera.orthographic = false; 
        }
        if (looping)
        {
            if (!useOffsetValues)
            {
                offset = target.position - transform.position;
            }

        }
        orthoView = false;
        looping = false;

        target.transform.position = new Vector3(0f, 0f, 0f);
        // Reset pivot rotation
        pivot.localRotation = Quaternion.identity;

        // Reset camera offset
        transform.position = target.position - offset;
        transform.LookAt(target);
    }

    public void Looping()
    {
        orthoView = false;
        looping = true;
        if (mainCamera != null)
        {
            mainCamera.orthographic = false; 
        }
    }

    public void SwitchToOrthographic()
    {
        looping = true;
        orthoView = true;
        if (mainCamera != null)
        {
            mainCamera.orthographic = true; // Enable orthographic mode
        }
    }

    public void Sensitivity(float newValue)
    {
        sensitivity = newValue;
    }

}
