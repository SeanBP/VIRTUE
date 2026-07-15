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

    // Start is called before the first frame update
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    public void MovePlayerTo(Vector3 targetPosition, float duration = 1.0f)
    {
        StartCoroutine(MovePlayerToCoroutine(targetPosition, duration));
    }

    private IEnumerator MovePlayerToCoroutine(Vector3 targetPosition, float duration)
    {
        isMoving = true;
        viewNum = 5;
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
            if (viewNum == 1)
            {
                player.transform.position = new Vector3(distance, 0f, 0f);
            }
            if (viewNum == 2)
            {
                player.transform.position = new Vector3(0f, 0f, distance);
            }
            if (viewNum == 3)
            {
                player.transform.position = new Vector3(0.1f, distance, 0f);
            }
        }

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

    }

    public void ResetPosition()
    {
        currentAngle = 0f;
        viewNum = 0;
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
        }

    }

    public void FrontView()
    {
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
    }

    public void SideView()
    {
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
    }

    public void TopView()
    {
        player.transform.position = new Vector3(0.1f, distance, 0f);
        if (looping)
        {
            StopLooping();
        }
        if (applyGravity)
        {
            OffGravity();
        }
        viewNum = 3;
    }

    public void MoveSpeed(float newValue)
    {
        moveSpeed = newValue;
    }
}
