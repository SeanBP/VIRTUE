using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    public void MoveTargetTo(Vector3 worldPosition)
    {
        if (!inTour)
        {
            mainCamera.orthographic = false;
            looping = false;
            
        }

        StartCoroutine(SmoothMoveFocus(worldPosition));
    }

    private IEnumerator SmoothMoveFocus(Vector3 destination)
    {
        isMoving = true;
        yield return new WaitUntil(() => activeCoroutines == 0);
        activeCoroutines++;

        Vector3 startPos = focusPosition;
        float elapsed = 0f;

        while (elapsed < targetMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / targetMoveDuration);

            focusPosition = Vector3.Lerp(startPos, destination, t);
            inTour = true;
            yield return null;
        }

        focusPosition = destination;
        activeCoroutines--;
        isMoving = false;
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
        if(!inTour)
            transform.LookAt(target);
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
