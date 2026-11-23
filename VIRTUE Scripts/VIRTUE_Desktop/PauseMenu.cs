using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using VirtueCore.Shared;
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

    // Opening the manual currently breaks on macOS Steam builds; deactivate it
    // there at startup instead of remembering to do it by hand each build.
    public GameObject manualButton;

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

    private string pdfFileName = "VIRTUE_User_Guide_V3_2_3.pdf";

    private bool isMuted = false;
    private float previousVolume = 0.05f;

    private TourMaker tourMaker;
    private ComponentMaker componentMaker;
    private EventLoader eventLoader;

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

        // Found after the initial Pause() above on purpose: that first
        // Pause() runs before the loaders have built their lists, so its
        // RefreshFileLists() call is skipped (these are still null).
        tourMaker = FindAnyObjectByType<TourMaker>();
        componentMaker = FindAnyObjectByType<ComponentMaker>();
        eventLoader = FindAnyObjectByType<EventLoader>();
        ErrorMessage.EnableClickToDismiss(errorText);

#if UNITY_STANDALONE_OSX
        if (manualButton != null) manualButton.SetActive(false);
#endif
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

        // A submenu is about to slide away (closed or swapped for another).
        DropdownUtil.CloseOpenDropdowns();

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

        RefreshFileLists();
        currentActiveMenu = menu;
        StartCoroutine(SlideMenu(currentActiveMenu, initialMenuPositions[menu]));
    }

    // Re-reads the model/event/tour folders so files added or removed while
    // the app is running appear in (or drop out of) the dropdowns. Called
    // whenever the menu or one of its submenus opens.
    private void RefreshFileLists()
    {
        if (componentMaker != null) componentMaker.RefreshFileList();
        if (eventLoader != null) eventLoader.RefreshFileList();
        if (tourMaker != null) tourMaker.RefreshFileList();
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
        // Escape first clears an error message if one is showing; only when
        // there's nothing to clear does it toggle the menu as usual.
        bool escapePressed = Input.GetKeyDown(KeyCode.Escape);
        if (escapePressed && ErrorMessage.TryDismiss(errorText))
            escapePressed = false;

        if (!inTour)
        {
            fsText.text = Screen.fullScreen ? "Exit Fullscreen" : "Enter Fullscreen";

            if (isTransitioning) return;

            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.M) || escapePressed)
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

            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.M) || escapePressed)
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

    // Wired to the tour start button after TourMaker.StartTour, so
    // TourMaker has already decided whether the tour could be opened.
    public void StartTour()
    {
        if (tourMaker != null && !tourMaker.TourActive) return;

        CloseTexts();
        inTour = true;
    }

    // Wired to the tour menu's Exit button. Returns to the normal menu and
    // the view the user had before the tour, without reloading the scene
    // (which used to reset everything, volume and HUD included).
    public void ExitTour()
    {
        if (tourMaker == null)
        {
            Reset();
            return;
        }

        DropdownUtil.CloseOpenDropdowns();
        tourMaker.ExitTour();
        inTour = false;

        // Exit is pressed from the open tour menu, so the game is paused;
        // the normal menu comes back open, as it was when the tour started.
        GameIsPaused = true;
        Cursor.lockState = CursorLockMode.None;
        navigationMenu.GetComponent<RectTransform>().anchoredPosition = initialMenuPositions[navigationMenu];
    }

    // Called by TourMaker when a tour fails to open, in case this ever runs
    // before TourMaker.StartTour instead of after.
    public void CancelTour()
    {
        inTour = false;
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

        DropdownUtil.CloseOpenDropdowns();

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

        RefreshFileLists();

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
