using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using static System.Net.Mime.MediaTypeNames;
using System.Diagnostics;
using UnityEngine.EventSystems;
using System.Security.Permissions;
#pragma warning disable 0618

public class PauseMenu : MonoBehaviour
{

    private Vector2 touchStartPos;
    private bool isSwiping = false;


    public RectTransform swipeAreaImage;

    public bool GameIsPaused = false;

    public GameObject navigationMenu;
    public GameObject mainMenu;
    public GameObject modelMenu;
    public GameObject eventMenu;
    public GameObject tourMenu;
    public GameObject cameraMenu;

    public GameObject credits;
    public GameObject controls;
    public GameObject AudioSource;
    public GameObject menuText;

    public bool HUDActive = true;
    public UnityEngine.UI.Text hudText;
    GameObject[] HUDs;

    public UnityEngine.UI.Text fsText;

    private GameObject currentActiveMenu;
    private Dictionary<GameObject, Vector3> menuPositions = new Dictionary<GameObject, Vector3>();
    private Dictionary<GameObject, Vector3> initialMenuPositions = new Dictionary<GameObject, Vector3>();
    private float slideSpeed = 10f;
    private bool isTransitioning = false;
    private float offset = 3000f;
    private float containerOffset = -1015f;
    public GameObject swiper;


    public UnityEngine.UI.Text errorText;

    private string pdfFileName = "VIRTUE_User_Guide_V3_1_0.pdf";

    public void ChangeVolume(float volume)
    {
        AudioSource.GetComponent<AudioSource>().volume = volume;
    }

    void Start()
    {
        AudioSource.GetComponent<AudioSource>().volume = 0.05f;
        HUDs = GameObject.FindGameObjectsWithTag("HUD");

        InitializeMenuPositions();
        MoveMenusToUnselectedPositions();
        currentActiveMenu = null;

        SetMenu(navigationMenu, initialMenuPositions[navigationMenu] + new Vector3(0, containerOffset, 0));
        GameIsPaused = false;
    }

    private void InitializeMenuPositions()
    {
        // Store the initial position of each menu (relative to its starting position)
        initialMenuPositions[mainMenu] = mainMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[modelMenu] = modelMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[eventMenu] = eventMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[tourMenu] = tourMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[cameraMenu] = cameraMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[navigationMenu] = navigationMenu.GetComponent<RectTransform>().anchoredPosition;

        // Set the initial positions
        menuPositions[mainMenu] = initialMenuPositions[mainMenu] + new Vector3(offset, 0, 0);
        menuPositions[modelMenu] = initialMenuPositions[modelMenu] + new Vector3(offset, 0, 0);
        menuPositions[eventMenu] = initialMenuPositions[eventMenu] + new Vector3(offset, 0, 0);
        menuPositions[tourMenu] = initialMenuPositions[tourMenu] + new Vector3(offset, 0, 0);
        menuPositions[cameraMenu] = initialMenuPositions[cameraMenu] + new Vector3(offset, 0, 0);
    }

    private void MoveMenusToUnselectedPositions()
    {
        foreach (var menu in menuPositions.Keys)
        {
            // Move unselected menus to the unselected position relative to their initial position

            var rectTransform = menu.GetComponent<RectTransform>();
            rectTransform.anchoredPosition = initialMenuPositions[menu] + new Vector3(offset, 0, 0);

        }
    }

    public void OpenMenu(GameObject menu)
    {
        if (isTransitioning)
            return;

        if (currentActiveMenu == menu)
        {
            StartCoroutine(SlideMenu(currentActiveMenu, initialMenuPositions[menu] + new Vector3(offset, 0, 0)));
            currentActiveMenu = null;
            return;
        }

        if (currentActiveMenu != null)
        {
            // Slide current active menu to unselected position relative to its initial position       
            StartCoroutine(SlideMenu(currentActiveMenu, initialMenuPositions[currentActiveMenu] + new Vector3(offset, 0, 0)));
  
        }

        currentActiveMenu = menu;
        StartCoroutine(SlideMenu(currentActiveMenu, initialMenuPositions[menu]));


    }

    private IEnumerator SlideMenu(GameObject menu, Vector3 targetPosition)
    {
        swiper.SetActive(false);
        isTransitioning = true; // Start transition
        RectTransform menuRect = menu.GetComponent<RectTransform>();

        // Smoothly interpolate position
        while (Vector3.Distance(menuRect.anchoredPosition, targetPosition) > 1f)
        {
            menuRect.anchoredPosition = Vector3.Lerp(menuRect.anchoredPosition, targetPosition, Time.deltaTime * slideSpeed);
            yield return null;
        }

        // Snap to the final position to avoid jitter
        menuRect.anchoredPosition = targetPosition;
        isTransitioning = false; // End transition
    }

    private void SetMenu(GameObject menu, Vector3 targetPosition)
    {
        RectTransform menuRect = menu.GetComponent<RectTransform>();
        menuRect.anchoredPosition = targetPosition;
    }

    void Update()
    {
        
        if (isTransitioning)
            return;

        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            // Check if the touch is within the bounds of the swipeAreaImage
            if (RectTransformUtility.RectangleContainsScreenPoint(swipeAreaImage, touch.position))
            {
                if (touch.phase == TouchPhase.Began)
                {
                    // Store the touch starting position when the user touches down
                    touchStartPos = touch.position;
                    isSwiping = true;
                }
                else if (touch.phase == TouchPhase.Moved && isSwiping)
                {
                    // Check if it's a swipe, compare to the initial touch position
                    Vector2 swipeDelta = touch.position - touchStartPos;

                    // If the swipe is detected vertically, check if it is up or down
                    if (Mathf.Abs(swipeDelta.x) < Mathf.Abs(swipeDelta.y)) // Ensure it's mostly vertical
                    {
                        if (swipeDelta.y > 0)  // Swipe up
                        {
                            if (!GameIsPaused)
                            {
                                Pause();
                            }
                        }
                        else if (swipeDelta.y < 0)  // Swipe down
                        {
                            if (GameIsPaused)
                            {
                                Resume();
                            }
                        }
                        isSwiping = false;  // Once a swipe is detected, stop detecting further moves
                    }
                }
            }
        }


    }

    public void Manual()
    {
        string filePath = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, pdfFileName);

        // Add "file://" for MacOS
#if UNITY_STANDALONE_OSX
        filePath = "file://" + filePath;
#endif

        UnityEngine.Application.OpenURL(filePath);


    }
    public void ToggleHUD()
    {
        if (HUDActive)
        {
            for (int i = 0; i < HUDs.Length; i++)
            {
                HUDs[i].SetActive(false);
            }
            HUDActive = false;
            hudText.text = "Enable HUD";
        }
        else
        {
            for (int i = 0; i < HUDs.Length; i++)
            {
                HUDs[i].SetActive(true);
            }
            HUDActive = true;
            hudText.text = "Disable HUD";
        }

    }

    public void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        fsText.text = Screen.fullScreen ? "Exit Fullscreen" : "Enter Fullscreen";
    }

    void Resume()
    {
        if (isTransitioning)
            return;
        StartCoroutine(SlideMenu(navigationMenu, initialMenuPositions[navigationMenu] + new Vector3(0, containerOffset, 0)));


        credits.SetActive(false);
        controls.SetActive(false);

        GameIsPaused = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Pause()
    {
        if (isTransitioning)
            return;

        StartCoroutine(SlideMenu(navigationMenu, initialMenuPositions[navigationMenu]));



        GameIsPaused = true;

        Cursor.lockState = CursorLockMode.None;
    }

    public void Reset()
    {
        // Get the current active scene
        Scene currentScene = SceneManager.GetActiveScene();

        // Reload the current scene
        SceneManager.LoadScene(currentScene.name);
    }

    public void MainMenu()
    {
        OpenMenu(mainMenu);
    }

    public void TourMenu()
    {
        OpenMenu(tourMenu);
    }

    public void EventMenu()
    {
        OpenMenu(eventMenu);
    }

    public void CameraMenu()
    {
        OpenMenu(cameraMenu);
    }

    public void ModelMenu()
    {
        OpenMenu(modelMenu);
    }


    public void Exit()
    {
        UnityEngine.Application.Quit();
    }

    public void CloseCredits()
    {
        credits.SetActive(false);
    }

    public void Credits()
    {
        controls.SetActive(false);
        if (credits.active)
        {
            credits.SetActive(false);
        }
        else
        {
            credits.SetActive(true);
        }
    }

    public void Controls()
    {
        credits.SetActive(false);
        if (controls.active)
        {
            controls.SetActive(false);
        }
        else
        {
            controls.SetActive(true);
        }
    }

}
