using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using static System.Net.Mime.MediaTypeNames;
public class VerticalMovement : MonoBehaviour
{
    public GameObject player;
    public CharacterController controller;
    public ActionBasedSnapTurnProvider snapTurnProvider;
    public ActionBasedContinuousTurnProvider continuousTurnProvider;
    public ActionBasedContinuousMoveProvider moveProvider;
    public Button turnButton;
    public UnityEngine.UI.Text turnButtonText;
    public Slider moveSpeedSlider;
    private bool isSmoothTurning = false; // Default to Snap Turn

    private float moveSpeed = 3f;

    // Start is called before the first frame update
    void Start()
    {
        // Set initial states
        UpdateTurnMode();
        moveSpeedSlider.value = moveProvider.moveSpeed;

        // Add listeners
        //turnButton.onClick.AddListener(ToggleTurnMode);
        //moveSpeedSlider.onValueChanged.AddListener(SetMoveSpeed);

        controller = GetComponent<CharacterController>();
        XRSettings.eyeTextureResolutionScale = 2f;
    }

    // Update is called once per frame
    void Update()
    {

       float vertMag = 0f;

       var rightHandDevices = new List<UnityEngine.XR.InputDevice>();
       UnityEngine.XR.InputDevices.GetDevicesAtXRNode(UnityEngine.XR.XRNode.RightHand, rightHandDevices);
       foreach (var device in rightHandDevices)
       {       
           Vector2 joyPos;
           if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out joyPos))
           {
                vertMag = moveSpeed * joyPos.y;
           }
       }


        controller.Move(new Vector3(0f, vertMag * Time.deltaTime, 0f));
    
    }

    public void ChangeSpeed(float speed)
    {
        moveProvider.moveSpeed = speed;
        moveSpeed = speed;
    }

    public void ToggleTurnMode()
    {
        isSmoothTurning = !isSmoothTurning;
        UpdateTurnMode();
    }

    private void UpdateTurnMode()
    {
        continuousTurnProvider.enabled = isSmoothTurning;
        snapTurnProvider.enabled = !isSmoothTurning;
        turnButtonText.text = isSmoothTurning ? "Snap Turn" : "Smooth Turn";
    }

    private void SetMoveSpeed(float speed)
    {
        moveProvider.moveSpeed = speed;
    }
}
