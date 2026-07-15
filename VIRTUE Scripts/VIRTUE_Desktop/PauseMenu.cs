using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
#pragma warning disable 0618

public class PauseMenu : MonoBehaviour
{
    public bool GameIsPaused = false;

    public GameObject navigationMenu;
    public GameObject mainMenu;
    public GameObject modelMenu;
    public GameObject eventMenu;
    public GameObject cameraMenu;
    public GameObject tourMenu;

    public GameObject credits;
    public GameObject controls;
    public GameObject AudioSource;
    public GameObject menuText;

    public bool HUDActive = true;
    public UnityEngine.UI.Text hudText;
    GameObject[] HUDs;

    public UnityEngine.UI.Text fsText;
    public UnityEngine.UI.Text errorText;

    private GameObject currentActiveMenu;
    private Dictionary<GameObject, Vector3> menuPositions = new Dictionary<GameObject, Vector3>();
    private Dictionary<GameObject, Vector3> initialMenuPositions = new Dictionary<GameObject, Vector3>();
    private float slideSpeed = 10f;
    private bool isTransitioning = false;
    private float offset = 2000f;
    private bool inTour = false;
    public GameObject tourNavigationMenu;
    private float navigationMenuHideOffset = -920f;
    private float tourNavigationMenuHideOffset = -754f;

    private string pdfFileName = "VIRTUE_User_Guide_V3_1_0.pdf";

    private bool isMuted = false;
    private float previousVolume = 0.05f;

    public void ChangeVolume(float volume)
    {
        AudioSource.GetComponent<AudioSource>().volume = volume;
    }

    void Start()
    {
        AudioSource.GetComponent<AudioSource>().volume = previousVolume;
        HUDs = GameObject.FindGameObjectsWithTag("HUD");

        InitializeMenuPositions();
        MoveMenusToUnselectedPositions();
        currentActiveMenu = null;
        fsText.text = Screen.fullScreen ? "Exit Fullscreen" : "Enter Fullscreen";
        Pause();
     
    }

    private void InitializeMenuPositions()
    {
        initialMenuPositions[tourMenu] = tourMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[mainMenu] = mainMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[modelMenu] = modelMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[eventMenu] = eventMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[cameraMenu] = cameraMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[navigationMenu] = navigationMenu.GetComponent<RectTransform>().anchoredPosition;
        initialMenuPositions[tourNavigationMenu] = tourNavigationMenu.GetComponent<RectTransform>().anchoredPosition;

        foreach (var menu in initialMenuPositions.Keys)
        {
            menuPositions[menu] = initialMenuPositions[menu] + new Vector3(offset, 0, 0);
        }
        
    }

    private void MoveMenusToUnselectedPositions()
    {
        foreach (var menu in menuPositions.Keys)
        {
            if ((menu == tourNavigationMenu) || (menu == navigationMenu))
            {
                menu.GetComponent<RectTransform>().anchoredPosition =
                    initialMenuPositions[menu];
                continue;
            }

            menu.GetComponent<RectTransform>().anchoredPosition =
                initialMenuPositions[menu] + new Vector3(offset, 0, 0);
        }
    }

    public void OpenMenu(GameObject menu)
    {
        if (isTransitioning) return;

        if (currentActiveMenu == menu)
        {
            StartCoroutine(SlideMenu(currentActiveMenu, initialMenuPositions[menu] + new Vector3(offset, 0, 0)));
            currentActiveMenu = null;
            return;
        }

        if (currentActiveMenu != null)
        {
            StartCoroutine(SlideMenu(currentActiveMenu, initialMenuPositions[currentActiveMenu] + new Vector3(offset, 0, 0)));
        }

        currentActiveMenu = menu;
        StartCoroutine(SlideMenu(currentActiveMenu, initialMenuPositions[menu]));
    }

    private IEnumerator SlideMenu(GameObject menu, Vector3 targetPosition)
    {
        isTransitioning = true;
        RectTransform menuRect = menu.GetComponent<RectTransform>();

        while (Vector3.Distance(menuRect.anchoredPosition, targetPosition) > 20f)
        {
            menuRect.anchoredPosition = Vector3.Lerp(menuRect.anchoredPosition, targetPosition, Time.deltaTime * slideSpeed);
            yield return null;
        }

        menuRect.anchoredPosition = targetPosition;
        isTransitioning = false;
    }

    void Update()
    {
        if (!inTour)
        {
            fsText.text = Screen.fullScreen ? "Exit Fullscreen" : "Enter Fullscreen";

            if (isTransitioning) return;

            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Escape))
            {
                menuText.SetActive(false);
                if (GameIsPaused) Resume();
                else Pause();
            }

            if (!GameIsPaused && Input.GetKeyDown(KeyCode.R))
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.None ? CursorLockMode.Locked : CursorLockMode.None;
            }
            if (Input.GetKeyDown(KeyCode.H)) ToggleHUD();
        }
        else
        {
            if (isTransitioning) return;

            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Escape))
            {
                if (GameIsPaused) Resume();
                else Pause();
            }
        }
        
        if (Input.GetKeyDown(KeyCode.F)) ToggleFullscreen();
        if (Input.GetKeyDown(KeyCode.V)) ToggleMute(); // Mute/unmute feature
    }

    public void ToggleMute()
    {
        AudioSource audio = AudioSource.GetComponent<AudioSource>();
        if (isMuted)
        {
            audio.volume = previousVolume;
            isMuted = false;
        }
        else
        {
            previousVolume = audio.volume;
            audio.volume = 0f;
            isMuted = true;
        }
    }

    public void StartTour()
    {
        CloseTexts();
        inTour = true;
    }

    public void Manual()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, pdfFileName);
#if UNITY_STANDALONE_OSX
        filePath = "file://" + filePath;
#endif
        Application.OpenURL(filePath);
    }

    public void ToggleHUD()
    {
        HUDActive = !HUDActive;
        foreach (var hud in HUDs) hud.SetActive(HUDActive);
        hudText.text = HUDActive ? "Disable HUD" : "Enable HUD";
    }

    public void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        fsText.text = Screen.fullScreen ? "Exit Fullscreen" : "Enter Fullscreen";
    }

    void Resume()
    {
        if (isTransitioning) return;

        GameObject navMenu = inTour ? tourNavigationMenu : navigationMenu;

        float hideOffset = inTour
            ? tourNavigationMenuHideOffset
            : navigationMenuHideOffset;

        StartCoroutine(
            SlideMenu(
                navMenu,
                initialMenuPositions[navMenu] + new Vector3(0, hideOffset, 0)
            )
        );

        credits.SetActive(false);
        controls.SetActive(false);

        GameIsPaused = false;
        if (!inTour)
        {
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    void Pause()
    {
        if (isTransitioning) return;

        GameObject navMenu = inTour ? tourNavigationMenu : navigationMenu;

        StartCoroutine(
            SlideMenu(
                navMenu,
                initialMenuPositions[navMenu]
            )
        );

        GameIsPaused = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void Reset() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

    public void MainMenu() => OpenMenu(mainMenu);
    public void EventMenu() => OpenMenu(eventMenu);
    public void CameraMenu() => OpenMenu(cameraMenu);
    public void ModelMenu() => OpenMenu(modelMenu);
    public void TourMenu() => OpenMenu(tourMenu);

    public void Exit() => Application.Quit();

    public void Credits()
    {
        controls.SetActive(false);
        credits.SetActive(!credits.activeSelf);
    }

    public void Controls()
    {
        credits.SetActive(false);
        controls.SetActive(!controls.activeSelf);
    }

    public void CloseTexts()
    {
        controls.SetActive(false);
        credits.SetActive(false);
    }
}
