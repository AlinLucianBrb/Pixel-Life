using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
//using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public enum GameState { MainMenu, Game, Pause, Win }
    public static GameState gameState { get; private set; }

    const string PP_RES_INDEX = "res_index";
    const string PP_FULLSCREEN = "fullscreen";
    const string PP_BGM = "bgm";
    const string PP_SFX = "sfx";

    int SelectedResolution = -1;
    bool IsFullScreen = true;
    [Range(0f, 1f)] float bgmVolume = 0.5f;
    [Range(0f, 1f)] float sfxVolume = 0.5f;

    public bool persistAcrossScenes = true;

    [Header("BGM Settings")]
    public AudioClip bgmClip;
    
    [Header("SFX Library")]
    public List<AudioClip> sfxList = new List<AudioClip>();

    AudioSource bgmSource;
    AudioSource[] sfxPool;
    int poolIndex;

    [Header("UI References")]
    TMP_Dropdown ResolutionDropDown;
    Toggle FullScreenToggle;
    Scrollbar Volume;

    Resolution[] AllResolutions;
    List<string> resolutionStringList = new List<string>();
    List<Resolution> SelectedResolutionList = new List<Resolution>();

    //public InputActionAsset inputActions;
    public GameObject playerPrefab;
    [HideInInspector]
    public GameObject player;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);

        LoadSettings();

        InitializeBgmAndSfx();
        InitializeResolutionsAndFullScreen();
        InitializeVolume();
    }

    public void NewGame()
    {
        GameEvents.GameStart();
        SetGameState(GameState.Game);
    }

    public void SetGameState(GameState _gameState)
    {
        gameState = _gameState;
        Time.timeScale = gameState == GameState.Pause ? 0f : 1f;
        GameEvents.GameStateChanged(_gameState);
    }

    void InitializeBgmAndSfx()
    {
        // BGM setup
        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.spatialBlend = 0f;
        bgmSource.playOnAwake = false;
        bgmSource.volume = bgmVolume;

        if (bgmClip)
        {
            bgmSource.clip = bgmClip;
            bgmSource.Play();
        }

        // SFX pool
        sfxPool = new AudioSource[sfxList.Count];
        for (int i = 0; i < sfxList.Count; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.loop = false;
            src.playOnAwake = false;
            src.spatialBlend = 0f; // 2D sound
            sfxPool[i] = src;
        }
    }

    private void OnLevelWasLoaded(int level)
    {
        InitializeResolutionsAndFullScreen();
        InitializeVolume();
    }

    void InitializeVolume()
    {
        Volume = FindAnyObjectByType<Scrollbar>(FindObjectsInactive.Include);

        if(Volume == null)
        {
            return;
        }

        Volume.value = Instance.bgmVolume;
    }

    void InitializeResolutionsAndFullScreen()
    {
        ResolutionDropDown = FindAnyObjectByType<TMP_Dropdown>(FindObjectsInactive.Include);
        FullScreenToggle = FindAnyObjectByType<Toggle>(FindObjectsInactive.Include);

        if(ResolutionDropDown == null || FullScreenToggle == null)
        {
            return;
        }

        // Build resolution list only once
        if (Instance.SelectedResolutionList.Count == 0)
        {
            AllResolutions = Screen.resolutions;

            foreach (Resolution res in AllResolutions)
            {
                string newRes = $"{res.width} x {res.height}";
                if (!Instance.resolutionStringList.Contains(newRes))
                {
                    Instance.resolutionStringList.Add(newRes);
                    Instance.SelectedResolutionList.Add(res);
                }
            }
        }

        ResolutionDropDown.ClearOptions();
        ResolutionDropDown.AddOptions(Instance.resolutionStringList);

        // First run ever? Map to monitor’s current resolution
        if (Instance.SelectedResolution < 0 || Instance.SelectedResolution >= Instance.SelectedResolutionList.Count)
        {
            var cur = Screen.currentResolution;
            int idx = Instance.SelectedResolutionList.FindIndex(r => r.width == cur.width && r.height == cur.height);
            if (idx < 0) idx = 0; // fallback
            Instance.SelectedResolution = idx;
        }

        // Apply stored values
        ResolutionDropDown.value = Instance.SelectedResolution;
        FullScreenToggle.isOn = Instance.IsFullScreen;

        // Actually set them again in case the scene load reset the resolution
        Resolution selected = Instance.SelectedResolutionList[Instance.SelectedResolution];
        Screen.SetResolution(selected.width, selected.height, Instance.IsFullScreen);

        ResolutionDropDown.RefreshShownValue();
    }

    void SaveSettings()
    {
        PlayerPrefs.SetFloat(PP_BGM, bgmVolume);
        PlayerPrefs.SetFloat(PP_SFX, sfxVolume);
        PlayerPrefs.SetInt(PP_FULLSCREEN, IsFullScreen ? 1 : 0);
        PlayerPrefs.SetInt(PP_RES_INDEX, SelectedResolution);
        PlayerPrefs.Save();
    }

    void LoadSettings()
    {
        // volumes (default 0.6 if never saved)
        bgmVolume = PlayerPrefs.GetFloat(PP_BGM, bgmVolume);
        sfxVolume = PlayerPrefs.GetFloat(PP_SFX, sfxVolume);

        // fullscreen (default to current)
        IsFullScreen = PlayerPrefs.GetInt(PP_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1;

        // resolution index (default -1 = not set yet)
        SelectedResolution = PlayerPrefs.GetInt(PP_RES_INDEX, -1);
    }

    public void PlayBGM(AudioClip clip = null)
    {
        if (clip != null) bgmSource.clip = clip;
        if (!bgmSource.isPlaying) bgmSource.Play();
    }

    public void StopBGM() => bgmSource.Stop();

    public void SetBGMVolume(float v)
    {
        bgmVolume = Mathf.Clamp01(v);
        bgmSource.volume = bgmVolume;
    }

    public void PlaySFX(string name, float pitchVariance = 0f)
    {
        AudioClip clip = sfxList.Find(c => c.name == name);
        if (clip == null)
        {
            Debug.LogWarning("SFX not found: " + name);
            return;
        }

        var src = sfxPool[poolIndex];
        poolIndex = (poolIndex + 1) % sfxList.Count;

        src.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
        src.volume = sfxVolume;
        src.PlayOneShot(clip);
    }

    public void SetSFXVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
        foreach (var src in sfxPool) src.volume = sfxVolume;
    }

    public void LoadScene(int index)
    {
        SceneManager.LoadScene(index);
    }

    public void QuitGame()
    {
        Application.Quit();
        SaveSettings();
    }

    public void SetResolution()
    {
        SelectedResolution = ResolutionDropDown.value;
        Resolution res = SelectedResolutionList[SelectedResolution];
        Screen.SetResolution(res.width, res.height, IsFullScreen);

        Instance.SelectedResolution = SelectedResolution;
    }

    public void SetFullScreen()
    {
        IsFullScreen = FullScreenToggle.isOn;
        Screen.fullScreen = IsFullScreen;

        Instance.IsFullScreen = IsFullScreen;
    }
}
