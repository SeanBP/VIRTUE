using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; // For Unity's UI Text class
using UnityEngine.EventSystems;
using System.Collections.Specialized;

public class PlayerController : MonoBehaviour
{
    public Camera mainCamera;
    private bool orthoView = false;
    public GameObject platform;
    private float moveSpeed = 10f;
    private Vector3 moveDirection;
    private Vector3 verticalVelocity; // Gravity and jumping control
    public CharacterController controller;
    public GameObject player;
    private float omega = 0.2f;
    private float currentAngle = 0f;

    private float distance = 8f;

    private int viewNum = 0;

    // Touch input
    private Vector2 initialTouchPos1;
    private Vector2 initialTouchPos2;
    private float initialDistance;
    private float theta = Mathf.PI / 2f;
    private bool isPinching = false;
    private Vector3 focusPosition;
    private int activeCoroutines = 0;
    private bool inTour = false;
    public float targetMoveDuration = 1.0f;
    public bool isMoving = false;

    // Start is called before the first frame update
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    public void MoveTargetTo(Vector3 worldPosition)
    {
        if (!inTour)
        {
            mainCamera.orthographic = false;
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
            player.transform.LookAt(focusPosition);
            inTour = true;
            yield return null;
        }

        focusPosition = destination;
        activeCoroutines--;
        isMoving = false;
    }

    public void MovePlayerTo(Vector3 targetPosition, float duration = 1.0f)
    {
        StartCoroutine(MovePlayerToCoroutine(targetPosition, duration));
    }

    private IEnumerator MovePlayerToCoroutine(Vector3 targetPosition, float duration)
    {
        inTour = true;
        isMoving = true;
        viewNum = 5;
        // Disable normal movement
        controller.enabled = false;
        int previousView = viewNum;

        Vector3 startPosition = player.transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Smooth interpolation
            player.transform.position = Vector3.Lerp(startPosition, targetPosition, t);

            yield return null;
        }

        // Snap to final position
        player.transform.position = targetPosition;

        isMoving = false;
    }

    // Update is called once per frame
    void Update()
    {
        if (!inTour)
        {


            if (viewNum == 4)
            {
                // Circular movement logic for looping camera
                currentAngle += omega * Time.deltaTime;

                if (currentAngle > 2 * Mathf.PI)
                    currentAngle -= 2 * Mathf.PI;
                else if (currentAngle < 0)
                    currentAngle += 2 * Mathf.PI;
            }

            HandleTouchInput();

            if (viewNum == 1)  // Front view
            {
                player.transform.position = new Vector3(distance, 0f, 0f);
            }
            else if (viewNum == 2)  // Side view
            {
                player.transform.position = new Vector3(0f, 0f, -distance);
            }
            else if (viewNum == 3)  // Top view
            {
                player.transform.position = new Vector3(0.1f, distance, 0f);
            }
            else
            {
                player.transform.position = new Vector3(
                        distance * Mathf.Cos(currentAngle) * Mathf.Sin(theta), // Calculate new X
                        distance * Mathf.Cos(theta), // Calculate new Y (height based on polar angle)
                        distance * Mathf.Sin(currentAngle) * Mathf.Sin(theta) // Calculate new Z
                );
            }

            if (orthoView)
            {
                mainCamera.orthographicSize = distance / 3f;
            }


            player.transform.LookAt(new Vector3(0f, 0f, 0f));
        }
    }

    void HandleTouchInput()
    {
        if (Input.touchCount == 2)
        {
            Touch touch1 = Input.GetTouch(0);
            Touch touch2 = Input.GetTouch(1);

            if (IsTouchOverUIElement(touch1) || IsTouchOverUIElement(touch2))
                return; // Ignore input if touching UI elements

            if (!isPinching)
            {
                initialTouchPos1 = touch1.position;
                initialTouchPos2 = touch2.position;
                initialDistance = Vector2.Distance(initialTouchPos1, initialTouchPos2);
                isPinching = true;
            }

            float currentDistance = Vector2.Distance(touch1.position, touch2.position);
            float pinchDelta = Mathf.Clamp(currentDistance - initialDistance, -100f, 100f);

            distance -= pinchDelta * 0.01f * (distance / 10f);
            distance = Mathf.Max(distance, 0.2f);

            initialDistance = currentDistance;
        }
        else
        {
            isPinching = false;
        }

        if (orthoView && Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            if (IsTouchOverUIElement(touch))
                return;

            if (touch.phase == TouchPhase.Moved)
            {
                Vector2 delta = touch.deltaPosition;

                float orthoSize = mainCamera.orthographicSize;
                float aspect = mainCamera.aspect;

                float unitsPerPixelX = 2f * orthoSize * aspect / Screen.width;
                float unitsPerPixelY = 2f * orthoSize / Screen.height;

                float dx = -delta.x * unitsPerPixelX;
                float dy = -delta.y * unitsPerPixelY;

                Vector3 move = Vector3.zero;

                switch (viewNum)
                {
                    case 1: // Front view (looking down -X)
                        move = new Vector3(0f, dy, dx); // Y-Z
                        break;
                    case 2: // Side view (looking down +Z)
                        move = new Vector3(dx, dy, 0f); // X-Y
                        break;
                    case 3: // Top view (looking down -Y)
                        move = new Vector3(dy, 0f, dx); // X-Z
                        break;
                }

                mainCamera.transform.position += move;
 
            }
        }

        if (viewNum == 0 || viewNum == 4)
        {
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);

                if (IsTouchOverUIElement(touch))
                    return; // Ignore input if touching UI elements

                if (touch.phase == TouchPhase.Moved)
                {
                    float xyDistance = distance * Mathf.Sin(theta);

                    float swipeSpeedX = touch.deltaPosition.x * moveSpeed / Time.deltaTime;
                    float newAngle = Mathf.Clamp(swipeSpeedX * 0.0003f / xyDistance, -0.06f, 0.06f);
                    currentAngle -= newAngle;

                    if (currentAngle > 2 * Mathf.PI) currentAngle -= 2 * Mathf.PI;
                    else if (currentAngle < 0) currentAngle += 2 * Mathf.PI;

                    float swipeSpeedY = touch.deltaPosition.y * moveSpeed / Time.deltaTime;
                    float newTheta = Mathf.Clamp(swipeSpeedY * 0.0003f / distance, -0.06f, 0.06f);
                    theta += newTheta;

                    theta = Mathf.Clamp(theta, 0.001f, Mathf.PI - 0.001f);
                }
            }
        }
    }

    bool IsTouchOverUIElement(Touch touch)
    {
        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = touch.position
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        foreach (RaycastResult result in results)
        {
            if (result.gameObject.CompareTag("UIBlockInput")) // Check for a specific tag
            {
                return true; // Touch is over a tagged UI element
            }
        }

        return false;
    }

    public void StopLooping()
    {
        mainCamera.transform.position = new Vector3(0f, 0f, 0f);
        mainCamera.orthographic = false;
        orthoView = false;
        viewNum = 0;
        theta = Mathf.PI / 2f;
        currentAngle = 0f;
        distance = 8f;
        player.transform.position = new Vector3(distance, 0f, 0f);
        mainCamera.transform.position = new Vector3(distance, 0f, 0f);
    }

    public void Looping()
    {
        mainCamera.transform.position = new Vector3(0f, 0f, 0f);
        viewNum = 4;
        mainCamera.orthographic = false;
        orthoView = false;
        theta = Mathf.PI / 2f;
        currentAngle = 0f;
        distance = 8f;
        player.transform.position = new Vector3(distance, 0f, 0f);
        mainCamera.transform.position = new Vector3(distance, 0f, 0f);
    }

    public void FrontView()
    {
        player.transform.position = new Vector3(distance, 0f, 0f);
  
        mainCamera.orthographic = true;
        viewNum = 1;
        orthoView = true;
    }

    public void SideView()
    {
        player.transform.position = new Vector3(0f, 0f, distance);

        mainCamera.orthographic = true;
        viewNum = 2;
        orthoView = true;
    }

    public void TopView()
    {
        player.transform.position = new Vector3(0.1f, distance, 0f);

        mainCamera.orthographic = true;
        viewNum = 3;
        orthoView = true;
    }

}
