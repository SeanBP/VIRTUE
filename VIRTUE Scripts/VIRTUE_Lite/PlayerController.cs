using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; // For Unity's UI Text class

public class PlayerController : MonoBehaviour
{
    public GameObject platform;
    private float moveSpeed = 4f;
    private Vector3 moveDirection;
    private Vector3 verticalVelocity; // Gravity and jumping control
    private bool looping = false;
    public CharacterController controller;
    public GameObject player;
    private float omega = 0.2f;
    private float radius = 8f;
    private float height = 3f;
    private float currentAngle = 0f;

    public UnityEngine.UI.Text gravText;
    private float gravityStrength = -20f; // Gravity strength
    private float jumpStrength = 10f; // Jump force
    private bool applyGravity = false; // Gravity toggled by default

    // New variables to control jump state and ledge detection
    private bool isGroundedLastFrame = false;  // Tracks last grounded state
    private bool hasJumped = false; // To prevent toggling grounded state prematurely
    private int notGroundedFrames = 0; // Count frames player has been in the air
    private int ledgeFallThreshold = 10; // Number of frames to wait before detecting ledge fall

    private float distance = 10f;

    private int viewNum = 0;

    public bool isMoving = false;

    // Pan offset within the current ortho view's plane (reset whenever a
    // Front/Side/Top view button is pressed, so each view starts centered).
    private float panHorizontal = 0f;
    private float panVertical = 0f;
    private float panSpeed = 5f;
    private float zoomSpeed = 5f;
    private CameraController cameraController;

    // Whether the current Front/Side/Top view is looking from the opposite
    // side (e.g. Top becomes bottom-up). Reset to false whenever a view
    // button is pressed; toggled by FlipView().
    private bool viewFlipped = false;

    // Start is called before the first frame update
    void Start()
    {
        controller = GetComponent<CharacterController>();
        cameraController = FindAnyObjectByType<CameraController>();
    }

    public void MovePlayerTo(Vector3 targetPosition, float duration = 1.0f)
    {
        StartCoroutine(MovePlayerToCoroutine(targetPosition, duration));
    }

    private IEnumerator MovePlayerToCoroutine(Vector3 targetPosition, float duration)
    {
        isMoving = true;
        viewNum = 5;
        cameraController?.ClearAxisLock();
        // Disable normal movement
        controller.enabled = false;
        int previousView = viewNum;
                       
        applyGravity = false;
  
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
        if (viewNum == 0)
        {
            // Gravity and jumping logic
            if (applyGravity)
            {
                // Base movement logic
                moveDirection = (transform.forward * Input.GetAxis("Vertical")) +
                                (transform.right * Input.GetAxis("Horizontal"));

                if (moveDirection.magnitude > 1)
                    moveDirection = moveDirection.normalized * moveSpeed;
                else
                    moveDirection *= moveSpeed;

                // Check for ground state and toggle hasJumped
                if (controller.isGrounded)
                {
                    // Reset vertical velocity and jump state when grounded
                    if (!isGroundedLastFrame)
                    {
                        verticalVelocity.y = 0; // Reset vertical velocity when grounded
                        hasJumped = false; // Allow jumping again after landing
                        notGroundedFrames = 0; // Reset the "not grounded" frame counter
                    }

                    // If the jump button is pressed and player is grounded, jump
                    if (Input.GetButton("Jump") && !hasJumped)
                    {
                        verticalVelocity.y = jumpStrength;
                        hasJumped = true; // Set flag to prevent re-jumping too soon
                        notGroundedFrames = 0; // Reset the "not grounded" frame counter immediately when jumping
                    }
                }
                else
                {
                    // Apply gravity over time
                    verticalVelocity.y += gravityStrength * Time.deltaTime;

                    // Increment the counter when the player is not grounded
                    notGroundedFrames++;

                    // Check if the player has fallen off a ledge after staying airborne for several frames
                    if (notGroundedFrames >= ledgeFallThreshold && !hasJumped)
                    {
                        // Trigger falling off ledge behavior
                        HandleLedgeFall();
                    }

                    // If jump button was pressed before, allow the player to jump while airborne (if hasn't jumped yet)
                    if (Input.GetButton("Jump") && !hasJumped)
                    {
                        verticalVelocity.y = jumpStrength;
                        hasJumped = true; // Set flag to prevent re-jumping too soon
                    }
                }

                // Teleport if falling below y = -10
                if (player.transform.position.y < -10)
                {
                    ResetPosition();
                }
            }
            else
            {
                moveDirection = (transform.forward * Input.GetAxis("Vertical")) + (transform.right * Input.GetAxis("Horizontal")) + (transform.up * Input.GetAxis("Jump") * 0.5f);

                if (moveDirection.magnitude > 1)
                {
                    moveDirection = moveDirection.normalized * moveSpeed;
                }
                else
                {
                    moveDirection = moveDirection * moveSpeed;
                }
            }

            // Update ground state
            isGroundedLastFrame = controller.isGrounded;

            // Combine movement and vertical velocity

            controller.Move((moveDirection + verticalVelocity) * Time.deltaTime);
        }
        else if (viewNum == 4)
        {
            // Circular movement logic
            currentAngle += omega * Time.deltaTime;

            if (currentAngle > 2 * Mathf.PI)
                currentAngle -= 2 * Mathf.PI;
            else if (currentAngle < 0)
                currentAngle += 2 * Mathf.PI;

            player.transform.position = new Vector3(
                radius * Mathf.Cos(currentAngle),
                height,
                radius * Mathf.Sin(currentAngle)
            );
        }
        else
        {
            if (viewNum == 1 || viewNum == 2 || viewNum == 3)
            {
                float horizontal = Input.GetAxis("Horizontal");
                float vertical = Input.GetAxis("Vertical");
                panHorizontal += horizontal * panSpeed * Time.deltaTime;
                panVertical += vertical * panSpeed * Time.deltaTime;

                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    ZoomBy(-zoomSpeed * Time.deltaTime);
                }
                if (Input.GetKey(KeyCode.Space))
                {
                    ZoomBy(zoomSpeed * Time.deltaTime);
                }
            }

            // Flipping negates forward while the up reference stays fixed, so
            // screen-right (= cross(up, forward)) flips sign along with it;
            // screen-up doesn't, since up stays perpendicular to both. So the
            // same sign applies to the fixed (distance) axis and the
            // horizontal pan axis, but never to the vertical pan axis.
            float axisSign = viewFlipped ? -1f : 1f;

            if (viewNum == 1)
            {
                // Looking along X toward the origin: screen-right is +Z, screen-up is +Y
                player.transform.position = new Vector3(axisSign * distance, panVertical, axisSign * panHorizontal);
            }
            if (viewNum == 2)
            {
                // Looking along Z toward the origin: screen-right is -X, screen-up is +Y
                player.transform.position = new Vector3(-axisSign * panHorizontal, panVertical, axisSign * distance);
            }
            if (viewNum == 3)
            {
                // Looking down Y toward the origin: screen-right is +X, screen-up is +Z
                player.transform.position = new Vector3(axisSign * panHorizontal, axisSign * distance, panVertical);
            }
        }

    }

    private void ZoomBy(float delta)
    {
        distance += delta;
        if (Mathf.Abs(distance) < 0.2f)
        {
            distance = 0.2f;
        }
        if (cameraController != null)
        {
            cameraController.ChangeDist(distance.ToString());
        }
    }

    private void ApplyAxisLockForCurrentView()
    {
        float sign = viewFlipped ? -1f : 1f;
        if (viewNum == 1)
        {
            cameraController?.SetAxisLock(sign * Vector3.left, Vector3.up);
        }
        else if (viewNum == 2)
        {
            cameraController?.SetAxisLock(sign * Vector3.back, Vector3.up);
        }
        else if (viewNum == 3)
        {
            cameraController?.SetAxisLock(sign * Vector3.down, Vector3.forward);
        }
    }

    // Flips the current Front/Side/Top view to look from the opposite side
    // (e.g. Top view becomes a bottom-up view instead of top-down).
    public void FlipView()
    {
        if (viewNum < 1 || viewNum > 3)
        {
            return;
        }
        viewFlipped = !viewFlipped;
        ApplyAxisLockForCurrentView();
    }

    public void ChangeDist(string newValue)
    {
        try
        {
            distance = float.Parse(newValue);
        }
        catch
        {
            distance = 10f;
        }

        if (Mathf.Abs(distance) < 0.2f)
        {
            distance = 0.2f;
        }

    }

    // Handles the behavior when the player falls off a ledge
    private void HandleLedgeFall()
    {

        // Reset the counter so we don't trigger ledge fall continuously
        notGroundedFrames = 0;
    }

    public void Radius(float newValue)
    {
        radius = newValue;
    }

    public void Height(float newValue)
    {
        height = newValue;
    }

    public void Speed(float newValue)
    {
        omega = newValue;
    }

    public void StopLooping()
    {

        looping = false;

    }

    public void Looping()
    {

        if (applyGravity)
        {
            ToggleGravity();
        }
        looping = true;
        viewNum = 4;
        cameraController?.ClearAxisLock();

    }

    public void ResetPosition()
    {
        currentAngle = 0f;
        viewNum = 0;
        cameraController?.ClearAxisLock();
        if (looping)
        {
            StopLooping();
        }
        controller.enabled = false; // Temporarily disable the controller
        player.transform.position = new Vector3(10, 0, 0); // Teleport the player
                                                           // Calculate direction to the origin
         
        Vector3 directionToOrigin = Vector3.zero - player.transform.position;

        // Set the player's rotation to face the origin 
        if (directionToOrigin != Vector3.zero)
        {           
            player.transform.rotation = Quaternion.LookRotation(directionToOrigin);
        }

        controller.enabled = true; // Re-enable the controller
        verticalVelocity.y = 0; // Reset velocity after teleportation
    }

    // Toggle gravity on/off
    public void ToggleGravity()
    {
 
        ResetPosition();

           
        applyGravity = !applyGravity;
        gravText.text = applyGravity ? "Disable Gravity" : "Enable Gravity";

        if (applyGravity)
        {

            platform.SetActive(true);

            if (looping)
            {
                StopLooping();
            }
        }
        else
        {
            platform.SetActive(false);
            verticalVelocity = Vector3.zero; // Reset vertical velocity when disabling gravity
        }
        viewNum = 0;
    }

    public void OffGravity()
    {
        if (applyGravity)
        {
            applyGravity = false;
            gravText.text = applyGravity ? "Disable Gravity" : "Enable Gravity";

            platform.SetActive(false);
            verticalVelocity = Vector3.zero; // Reset vertical velocity when disabling gravity

            viewNum = 0;
            cameraController?.ClearAxisLock();
        }

    }

    public void FrontView()
    {
        panHorizontal = 0f;
        panVertical = 0f;
        viewFlipped = false;
        player.transform.position = new Vector3(distance, 0f, 0f);
        if (looping)
        {
            StopLooping();
        }
        if (applyGravity)
        {
            OffGravity();
        }
        viewNum = 1;
        ApplyAxisLockForCurrentView();
    }

    public void SideView()
    {
        panHorizontal = 0f;
        panVertical = 0f;
        viewFlipped = false;
        player.transform.position = new Vector3(0f, 0f, distance);
        if (looping)
        {
            StopLooping();
        }
        if (applyGravity)
        {
            OffGravity();
        }
        viewNum = 2;
        ApplyAxisLockForCurrentView();
    }

    public void TopView()
    {
        panHorizontal = 0f;
        panVertical = 0f;
        viewFlipped = false;
        player.transform.position = new Vector3(0f, distance, 0f);
        if (looping)
        {
            StopLooping();
        }
        if (applyGravity)
        {
            OffGravity();
        }
        viewNum = 3;
        ApplyAxisLockForCurrentView();
    }

    public void MoveSpeed(float newValue)
    {
        moveSpeed = newValue;
    }
}
