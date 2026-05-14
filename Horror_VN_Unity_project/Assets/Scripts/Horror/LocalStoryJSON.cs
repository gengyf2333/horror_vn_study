using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LocalStoryJSON : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Relative to StreamingAssets, e.g. Pre-horror/story_horrorified.json")]
    public string inputFile = "story_horrorified.json";

    [Header("Version Selection")]
    public GameObject versionSelectPanel;

    public string neutralInputFile = "Pre-horror/story_neutral.json";
    public string horrorInputFile = "Pre-horror/story_horrorified.json";

    public string neutralBackgroundPath = "Pre-horror/neutral/backgrounds/";
    public string horrorBackgroundPath = "Pre-horror/horrorified/backgrounds/";

    public string neutralCharacterPath = "Pre-horror/neutral/characters/";
    public string horrorCharacterPath = "Pre-horror/horrorified/characters/";

    private string currentBackgroundPath = "";
    private string currentCharacterPath = "";

    private bool storyEnded = false;
    private bool endMessageShown = false;
    [SerializeField] private float quitDelay = 2.0f;

    [Header("UI References")]
    public TMP_Text storyTextDisplay;
    public TMP_Text speakerNameDisplay;
    public TMP_Text statusTextDisplay;
    public Transform optionsPanel;
    public GameObject optionButtonPrefab;
    public GameObject speakerNameBox;

    [Header("UI Root")]
    public GameObject gameplayUIRoot;

    [Header("Character Sprites")]
    public Image leftCharacterImage;
    public Image rightCharacterImage;

    [Header("Character Highlight")]
    public Color speakingCharacterColor = Color.white;
    public Color dimCharacterColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
    public float speakingScale = 1.03f;

    private string currentLeftCharacter = "";
    private string currentRightCharacter = "";

    private Vector3 leftCharacterBaseScale = Vector3.one;
    private Vector3 rightCharacterBaseScale = Vector3.one;

    [Header("Audio")]
    public AudioSource bgmSource;

    [Header("Force BGM")]
    public bool forceGlobalBGM = true;
    public string forcedBGMName = "bgm";

    private string currentBGM = "";
    [Header("Optional Background")]
    [Tooltip("Optional RawImage for background display. Leave empty if not used yet.")]
    public RawImage backgroundImage;

    [Header("Behavior")]
    public bool autoStartOnPlay = true;
    public bool allowClickAnywhereForNext = true;
    public bool logDebugInfo = true;

    [Header("Skip Settings")]
    public float ctrlAdvanceInterval = 0.08f;
    private float ctrlAdvanceTimer = 0f;

    [Header("Text Typing Effect")]
    [SerializeField] private float normalCharInterval = 0.035f;
    [SerializeField] private float fastCharInterval = 0.015f;
    [SerializeField] private float slowCharInterval = 0.06f;
    [SerializeField] private float punctuationPause = 0.12f;

    private Coroutine typingCoroutine;
    private bool isTyping = false;
    private string currentFullText = "";

    private VNChapter chapterData;
    private readonly Dictionary<string, VNScene> sceneMap = new Dictionary<string, VNScene>();
    private VNScene currentScene;
    private bool waitingForChoice = false;
    private bool storyLoaded = false;

    private List<DisplayLine> currentDisplayLines = new List<DisplayLine>();
    private int currentDisplayIndex = 0;
    private bool sceneFullyDisplayed = false;

    [Serializable]
    public class DisplayLine
    {
        public string speaker;
        public string text;
        public bool isDialogue;
    }

    private void Start()
    {
        ClearUI();
        EnterVersionSelectMode();
        if (versionSelectPanel != null)
        {
            versionSelectPanel.SetActive(true);
            versionSelectPanel.transform.SetAsLastSibling();
        }

        if (autoStartOnPlay && versionSelectPanel == null)
        {
            StartHorrorVersion();
        }
    }
    private void EnterVersionSelectMode()
    {
        // clear text
        if (storyTextDisplay != null) storyTextDisplay.text = "";
        if (speakerNameDisplay != null) speakerNameDisplay.text = "";
        if (statusTextDisplay != null) statusTextDisplay.text = "";

        // clear name box
        if (speakerNameBox != null) speakerNameBox.SetActive(false);

        // clear choices
        ClearChoices();
        if (optionsPanel != null) optionsPanel.gameObject.SetActive(false);

        // clear characters
        HideAllCharacters();

        // clear bg
        if (backgroundImage != null)
        {
            backgroundImage.texture = null;
        }

        // clear game UI
        if (gameplayUIRoot != null)
        {
            gameplayUIRoot.SetActive(false);
        }

        // show choose version
        if (versionSelectPanel != null)
        {
            versionSelectPanel.SetActive(true);
            versionSelectPanel.transform.SetAsLastSibling();
        }

        waitingForChoice = false;
        sceneFullyDisplayed = false;
    }
    private void InitAudioSource()
    {
        if (bgmSource == null)
        {
            GameObject bgmObj = new GameObject("BGM Source");
            bgmObj.transform.SetParent(transform);

            bgmSource = bgmObj.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.loop = true;
            bgmSource.volume = 0.8f;
            bgmSource.spatialBlend = 0f;
        }
    }
    private void Update()
    {
        if (!storyLoaded) return;
        if (!allowClickAnywhereForNext) return;
        if (waitingForChoice) return;
        //if click or space normally continue
        bool nextPressed =
        Input.GetMouseButtonDown(0) ||
        Input.GetKeyDown(KeyCode.Space) ||
        Input.GetKeyDown(KeyCode.Return);
        
        if (nextPressed)
        {
            NextScene();
            ctrlAdvanceTimer = 0f;
            return;
        }
        //if ctrl then skip
        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (ctrlHeld)
        {
            ctrlAdvanceTimer += Time.deltaTime;

            if (ctrlAdvanceTimer >= ctrlAdvanceInterval)
            {
                NextScene();
                ctrlAdvanceTimer = 0f;
            }
        }
        else
        {
            ctrlAdvanceTimer = 0f;
        }
    }
    private void PlayBGM(string bgmName)
    {
        if (bgmSource == null) return;

        if (string.IsNullOrWhiteSpace(bgmName)) return;

        bgmName = bgmName.Trim();

        if (currentBGM == bgmName && bgmSource.isPlaying)
        {
            return;
        }

        AudioClip clip = Resources.Load<AudioClip>("Audio/BGM/" + bgmName);

        if (clip == null)
        {
            Debug.LogWarning("[BGM] Cannot find BGM: " + bgmName);
            return;
        }

        currentBGM = bgmName;
        bgmSource.Stop();
        bgmSource.clip = clip;
        bgmSource.loop = true;
        bgmSource.Play();

        Debug.Log("[BGM] Playing forced BGM: " + bgmName);
    }
    public void StartNeutralVersion()
    {
        Debug.Log("[Version] StartNeutralVersion clicked.");

        InitAudioSource();

        if (forceGlobalBGM)
        {
            Debug.Log("[BGM] Try to play: " + forcedBGMName);
            PlayBGM(forcedBGMName);
        }
        
        inputFile = neutralInputFile;
        currentBackgroundPath = neutralBackgroundPath;
        currentCharacterPath = neutralCharacterPath;

        if (versionSelectPanel != null)
        {
            versionSelectPanel.SetActive(false);
        }

        if (gameplayUIRoot != null)
        {
            gameplayUIRoot.SetActive(true);
        }
        ClearUI();
        LoadAndStart();
    }

    public void StartHorrorVersion()
    {
        Debug.Log("[Version] StartNeutralVersion clicked.");

        InitAudioSource();

        if (forceGlobalBGM)
        {
            Debug.Log("[BGM] Try to play: " + "bgm");
            PlayBGM("bgm");
        }
        inputFile = horrorInputFile;
        currentBackgroundPath = horrorBackgroundPath;
        currentCharacterPath = horrorCharacterPath;

        if (versionSelectPanel != null)
        {
            versionSelectPanel.SetActive(false);
        }

        if (gameplayUIRoot != null)
        {
            gameplayUIRoot.SetActive(true);
        }
        ClearUI();
        LoadAndStart();
    }
    private void HideAllCharacters()
    {
        if (leftCharacterImage != null)
        {
            leftCharacterImage.sprite = null;
            leftCharacterImage.gameObject.SetActive(false);
        }

        if (rightCharacterImage != null)
        {
            rightCharacterImage.sprite = null;
            rightCharacterImage.gameObject.SetActive(false);
        }
    }
    public void LoadAndStart()
    {
        ClearUI();
        SetStatus("Loading story...");

        bool ok = LoadStoryFromFile();
        if (!ok) return;

        StartStory();
    }

    private bool LoadStoryFromFile()
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, inputFile);

        if (logDebugInfo)
        {
            Debug.Log("[LocalStoryJSON] Loading file: " + fullPath);
        }

        if (!File.Exists(fullPath))
        {
            SetStatus("File not found:\n" + fullPath);
            Debug.LogError("[LocalStoryJSON] File not found: " + fullPath);
            return false;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            chapterData = JsonUtility.FromJson<VNChapter>(json);

            if (chapterData == null)
            {
                SetStatus("JSON parse failed: root object is null.");
                Debug.LogError("[LocalStoryJSON] JSON parse failed: root object is null.");
                return false;
            }

            if (chapterData.scenes == null || chapterData.scenes.Length == 0)
            {
                SetStatus("JSON parse failed: no scenes found.");
                Debug.LogError("[LocalStoryJSON] JSON parse failed: no scenes found.");
                return false;
            }

            sceneMap.Clear();

            foreach (VNScene scene in chapterData.scenes)
            {
                if (scene == null || string.IsNullOrEmpty(scene.id))
                {
                    Debug.LogWarning("[LocalStoryJSON] Skipped scene with null/empty id.");
                    continue;
                }

                if (!sceneMap.ContainsKey(scene.id))
                {
                    sceneMap.Add(scene.id, scene);
                }
                else
                {
                    Debug.LogWarning("[LocalStoryJSON] Duplicate scene id found: " + scene.id);
                }
            }

            storyLoaded = true;

            string title = string.IsNullOrEmpty(chapterData.chapter_title) ? chapterData.title : chapterData.chapter_title;
            SetStatus("Loaded: " + title);

            if (logDebugInfo)
            {
                Debug.Log("[LocalStoryJSON] Story loaded successfully. Scene count: " + chapterData.scenes.Length);
            }

            return true;
        }
        catch (Exception ex)
        {
            SetStatus("Exception while loading JSON.");
            Debug.LogError("[LocalStoryJSON] Exception while loading JSON:\n" + ex);
            return false;
        }
    }

    private void StartStory()
    {
        storyEnded = false;
        endMessageShown = false;
        if (!storyLoaded || chapterData == null || chapterData.scenes == null || chapterData.scenes.Length == 0)
        {
            SetStatus("Cannot start story: no valid data.");
            return;
        }
        //marker 1
        DataLogger.Instance.LogEvento("GameStart", "startstory");
        ShowScene(chapterData.scenes[0].id);
    }

    private void PrepareDisplayLines(VNScene scene)
    {
        currentDisplayLines.Clear();

        if (scene.narration != null)
        {
            foreach (string line in scene.narration)
            {
                string text = Safe(line).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    currentDisplayLines.Add(new DisplayLine
                    {
                        speaker = "",
                        text = text,
                        isDialogue = false
                    });
                }
            }
        }

        if (scene.dialogue != null)
        {
            foreach (VNDialogue line in scene.dialogue)
            {
                if (line == null) continue;

                string speaker = Safe(line.speaker).Trim();
                string text = Safe(line.text).Trim();


                if (!string.IsNullOrEmpty(text))
                {
                    currentDisplayLines.Add(new DisplayLine
                    {
                        speaker = speaker,
                        text = text,
                        isDialogue = true
                    });
                }
            }
        }
    }

    public void ShowScene(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId))
        {
            SetStatus("Scene id is null or empty.");
            Debug.LogError("[LocalStoryJSON] Scene id is null or empty.");
            return;
        }

        if (!sceneMap.TryGetValue(sceneId, out currentScene))
        {
            SetStatus("Scene not found: " + sceneId);
            Debug.LogError("[LocalStoryJSON] Scene not found: " + sceneId);
            return;
        }

        if (logDebugInfo)
        {
            Debug.Log("[LocalStoryJSON] Showing scene: " + sceneId);
        }

        ClearChoices();
        waitingForChoice = false;
        sceneFullyDisplayed = false;

        UpdateBackground(currentScene.bg);
        UpdateCharacters(currentScene.leftCharacter, currentScene.rightCharacter);
        PrepareDisplayLines(currentScene);
        currentDisplayIndex = 0;

        ShowCurrentDisplayLine();

        string status = "Scene: " + currentScene.id;
        if (!string.IsNullOrEmpty(currentScene.bg))
        {
            status += " | BG: " + currentScene.bg;
        }
        SetStatus(status);
    }
    private void ShowCurrentDisplayLine()
    {
        if (currentDisplayLines.Count == 0)
        {
            if (storyTextDisplay != null) storyTextDisplay.text = "";
            if (speakerNameDisplay != null) speakerNameDisplay.text = "";
            if (speakerNameBox != null) speakerNameBox.SetActive(false);

            // no text to noraml light
            SetSpeakerHighlight("");

            sceneFullyDisplayed = true;

            if (currentScene != null && currentScene.choices != null && currentScene.choices.Length > 0)
            {
                RenderChoices(currentScene);
                waitingForChoice = true;
            }

            return;
        }

        if (currentDisplayIndex < 0 || currentDisplayIndex >= currentDisplayLines.Count)
        {
            // index illegal to noraml light
            SetSpeakerHighlight("");

            sceneFullyDisplayed = true;

            if (currentScene != null && currentScene.choices != null && currentScene.choices.Length > 0)
            {
                RenderChoices(currentScene);
                waitingForChoice = true;
            }

            return;
        }

        DisplayLine line = currentDisplayLines[currentDisplayIndex];

        if (storyTextDisplay != null)
        {
            storyTextDisplay.text = line.text;
        }

        string speaker = line.isDialogue ? Safe(line.speaker).Trim() : "";

        if (speakerNameDisplay != null)
        {
            speakerNameDisplay.text = speaker;
        }

        if (speakerNameBox != null)
        {
            speakerNameBox.SetActive(!string.IsNullOrEmpty(speaker));
        }

        // who is speaking with highlight
        SetSpeakerHighlight(speaker);

        sceneFullyDisplayed = (currentDisplayIndex >= currentDisplayLines.Count - 1);
        if (sceneFullyDisplayed && currentScene != null && string.IsNullOrEmpty(currentScene.next) && (currentScene.choices == null || currentScene.choices.Length == 0))
        {
            storyEnded = true;
        }
    }
    private void ShowEndMessage()
    {
        endMessageShown = true;

        if (storyTextDisplay != null)
        {
            storyTextDisplay.text = "This is the end of the game.";
        }

        if (speakerNameDisplay != null)
        {
            speakerNameDisplay.text = "";
        }

        if (speakerNameBox != null)
        {
            speakerNameBox.SetActive(false);
        }

        HideAllCharacters();

        ClearChoices();

        if (optionsPanel != null)
        {
            optionsPanel.gameObject.SetActive(false);
        }
        StartCoroutine(QuitGameAfterDelay());
    }
    private IEnumerator QuitGameAfterDelay()
    {
        yield return new WaitForSeconds(quitDelay);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
    Application.Quit();
#endif
    }

    private void StartTypingText(string text)
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }

        float interval = GetTextSpeedByScene();

        typingCoroutine = StartCoroutine(TypeText(text, interval));
    }
    private float GetTextSpeedByScene()
    {
        if (currentScene == null)
        {
            return normalCharInterval;
        }

        // if need speed control put it here
        // normal speed now
        return normalCharInterval;
    }
    private IEnumerator TypeText(string fullText, float charInterval)
    {
        isTyping = true;
        currentFullText = fullText;

        if (storyTextDisplay != null)
        {
            storyTextDisplay.text = "";
        }

        foreach (char c in fullText)
        {
            if (storyTextDisplay != null)
            {
                storyTextDisplay.text += c;
            }

            if (c == '.' || c == ',' || c == '?' || c == '!' ||
                c == '¡£' || c == '£¬' || c == '£¿' || c == '£¡' ||
                c == ';' || c == '£»' || c == ':' || c == '£º')
            {
                yield return new WaitForSeconds(punctuationPause);
            }
            else
            {
                yield return new WaitForSeconds(charInterval);
            }
        }

        isTyping = false;
    }
    private void RenderSceneText(VNScene scene)
    {
        if (storyTextDisplay != null)
        {
            storyTextDisplay.text = BuildSceneText(scene);
        }

        if (speakerNameDisplay != null)
        {
            if (scene.dialogue != null && scene.dialogue.Length > 0 && scene.dialogue[0] != null)
            {
                speakerNameDisplay.text = Safe(scene.dialogue[0].speaker);
            }
            else
            {
                speakerNameDisplay.text = "";
            }
        }

        string status = "Scene: " + scene.id;
        if (!string.IsNullOrEmpty(scene.bg))
        {
            status += " | BG: " + scene.bg;
        }
        SetStatus(status);
    }

    private string BuildSceneText(VNScene scene)
    {
        List<string> parts = new List<string>();

        if (scene.narration != null)
        {
            foreach (string line in scene.narration)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    parts.Add(line.Trim());
                }
            }
        }

        if (scene.dialogue != null)
        {
            foreach (VNDialogue line in scene.dialogue)
            {
                if (line == null) continue;

                string speaker = Safe(line.speaker).Trim();
                string text = Safe(line.text).Trim();

                if (string.IsNullOrEmpty(text)) continue;

                if (!string.IsNullOrEmpty(speaker))
                {
                    parts.Add(text);
                }
                else
                {
                    parts.Add(text);
                }
            }
        }

        return string.Join("\n\n", parts);
    }

    private void RenderChoices(VNScene scene)
    {
        ClearChoices();
        if (scene.choices == null || scene.choices.Length == 0)
        {
            waitingForChoice = false;
            if (optionsPanel != null) optionsPanel.gameObject.SetActive(false);
            return;
        }

        if (optionsPanel == null || optionButtonPrefab == null)
        {
            SetStatus("Choices exist, but Options Panel or Button Prefab is missing.");
            Debug.LogWarning("[LocalStoryJSON] Choices exist, but Options Panel or Button Prefab is missing.");
            waitingForChoice = false;
            return;
        }
        optionsPanel.gameObject.SetActive(true);
        waitingForChoice = true;
        //marker choice show
        if (DataLogger.Instance != null)
        {
            DataLogger.Instance.LogEvento("ChoiceShown", scene.id);
        }

        foreach (VNChoice choice in scene.choices)
        {
            if (choice == null) continue;

            GameObject btnObj = Instantiate(optionButtonPrefab, optionsPanel);

            Button button = btnObj.GetComponent<Button>();
            TMP_Text btnText = btnObj.GetComponentInChildren<TMP_Text>();

            if (btnText != null)
            {
                btnText.text = Safe(choice.text);
            }

            if (button != null)
            {
                string targetScene = choice.gotoScene;
                button.onClick.RemoveAllListeners();
                //marker choice select
                button.onClick.AddListener(() =>
                {
                    if (DataLogger.Instance != null)
                    {
                        DataLogger.Instance.LogEvento("ChoiceSelected", $"scene={scene.id}|text={choice.text}|goto={targetScene}");
                    }
                    ClearChoices();
                    ShowScene(targetScene);

                });
            }
        }
    }


    public void NextScene()
    {
        if (storyEnded)
        {
            if (!endMessageShown)
            {
                ShowEndMessage();
            }

            return;
        }
        if (!storyLoaded || currentScene == null) return;
        if (waitingForChoice) return;

        if (currentDisplayLines.Count > 0 && currentDisplayIndex < currentDisplayLines.Count - 1)
        {
            currentDisplayIndex++;
            ShowCurrentDisplayLine();
            return;
        }

        if (currentScene.choices != null && currentScene.choices.Length > 0)
        {
            if (optionsPanel != null && optionsPanel.childCount == 0)
            {
                RenderChoices(currentScene);
            }
            waitingForChoice = true;
            return;
        }

        if (string.IsNullOrEmpty(currentScene.next))
        {
            SetStatus("End of chapter.");
            if (logDebugInfo)
            {
                Debug.Log("[LocalStoryJSON] End of chapter reached.");
            }
            return;
        }

        ShowScene(currentScene.next);
    }

    public void RestartStory()
    {
        if (!storyLoaded || chapterData == null || chapterData.scenes == null || chapterData.scenes.Length == 0)
        {
            return;
        }

        ShowScene(chapterData.scenes[0].id);
    }

    private void ClearUI()
    {
        if (storyTextDisplay != null) storyTextDisplay.text = "";
        if (speakerNameDisplay != null) speakerNameDisplay.text = "";
        if (speakerNameBox != null) speakerNameBox.SetActive(false);
        ClearChoices();
        if (optionsPanel != null) optionsPanel.gameObject.SetActive(false);
        SetSpeakerHighlight("");
    }

    private void ClearChoices()
    {
        if (optionsPanel == null) return;

        for (int i = optionsPanel.childCount - 1; i >= 0; i--)
        {
            Destroy(optionsPanel.GetChild(i).gameObject);
        }

        optionsPanel.gameObject.SetActive(false);
        waitingForChoice = false;
    }

    private void UpdateBackground(string bgName)
    {
        if (backgroundImage == null)
        {
            Debug.LogError("[LocalStoryJSON] backgroundImage is not assigned.");
            return;
        }

        if (string.IsNullOrEmpty(bgName))
        {
            backgroundImage.texture = null;
            return;
        }

        string fullPath = currentBackgroundPath + bgName;

        Debug.Log("[LocalStoryJSON] Trying to load background: " + fullPath);

        Texture2D tex = Resources.Load<Texture2D>(fullPath);

        if (tex != null)
        {
            backgroundImage.texture = tex;
            Debug.Log("[LocalStoryJSON] Background loaded successfully: " + fullPath);
        }
        else
        {
            backgroundImage.texture = null;
            Debug.LogWarning("[LocalStoryJSON] Background not found: " + fullPath);
        }
    }
    private void Awake()
    {
        if (leftCharacterImage != null)
        {
            leftCharacterBaseScale = leftCharacterImage.transform.localScale;
        }

        if (rightCharacterImage != null)
        {
            rightCharacterBaseScale = rightCharacterImage.transform.localScale;
        }
    }
    private void UpdateCharacters(string leftName, string rightName)
    {
        currentLeftCharacter = leftName;
        currentRightCharacter = rightName;

        UpdateCharacterImage(leftCharacterImage, leftName);
        UpdateCharacterImage(rightCharacterImage, rightName);

        // enter new scene clear 
        SetSpeakerHighlight("");
    }
    private void SetSpeakerHighlight(string speaker)
    {
        string speakerKey = NormalizeCharacterKey(speaker);
        string leftKey = GetBaseCharacterKey(currentLeftCharacter);
        string rightKey = GetBaseCharacterKey(currentRightCharacter);

        bool leftIsSpeaking = !string.IsNullOrEmpty(speakerKey) && speakerKey == leftKey;
        bool rightIsSpeaking = !string.IsNullOrEmpty(speakerKey) && speakerKey == rightKey;

        // no speaker normal light
        if (!leftIsSpeaking && !rightIsSpeaking)
        {
            ApplyCharacterHighlight(leftCharacterImage, leftCharacterBaseScale, true, false);
            ApplyCharacterHighlight(rightCharacterImage, rightCharacterBaseScale, true, false);
            return;
        }

        ApplyCharacterHighlight(leftCharacterImage, leftCharacterBaseScale, leftIsSpeaking, true);
        ApplyCharacterHighlight(rightCharacterImage, rightCharacterBaseScale, rightIsSpeaking, true);
    }

    private void ApplyCharacterHighlight(Image targetImage, Vector3 baseScale, bool isSpeaking, bool enableDim)
    {
        if (targetImage == null || !targetImage.gameObject.activeSelf) return;

        if (!enableDim)
        {
            targetImage.color = Color.white;
            targetImage.transform.localScale = baseScale;
            return;
        }

        targetImage.color = isSpeaking ? speakingCharacterColor : dimCharacterColor;
        targetImage.transform.localScale = isSpeaking ? baseScale * speakingScale : baseScale;
    }

    private string GetBaseCharacterKey(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return "";

        string key = characterId.Trim().ToLower();

        // mira_neutral -> mira
        // theo_thinking -> theo
        // hana_focused -> hana
        if (key.Contains("_"))
        {
            key = key.Split('_')[0];
        }

        return key;
    }

    private string NormalizeCharacterKey(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        return name.Trim().ToLower().Replace(" ", "_");
    }
    private void UpdateCharacterImage(Image targetImage, string characterName)
    {

        if (targetImage == null)
        {
            Debug.LogError("[LocalStoryJSON] Character Image target is not assigned.");
            return;
        }

        if (string.IsNullOrEmpty(characterName))
        {
            targetImage.sprite = null;
            targetImage.gameObject.SetActive(false);
            return;
        }

        string fullPath = currentCharacterPath + characterName;
        Debug.Log("[LocalStoryJSON] Trying to load character: " + fullPath);

        Sprite sprite = Resources.Load<Sprite>(fullPath);

        if (sprite != null)
        {
            targetImage.sprite = sprite;
            targetImage.gameObject.SetActive(true);
            Debug.Log("[LocalStoryJSON] Character loaded successfully: " + fullPath);
        }
        else
        {
            targetImage.sprite = null;
            targetImage.gameObject.SetActive(false);
            Debug.LogWarning("[LocalStoryJSON] Character not found: " + fullPath);
        }
    }
    private void SetStatus(string msg)
    {
        if (statusTextDisplay != null)
        {
            statusTextDisplay.text = msg;
        }
    }

    private string Safe(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : s;
    }

    [Serializable]
    public class VNChapter
    {
        public string title;
        public string author;
        public string source_version;
        public string chapter_id;
        public string chapter_title;
        public int base_phase;
        public bool pacing_control_enabled;
        public VNScene[] scenes;
    }

    [Serializable]
    public class VNScene
    {
        public string id;
        public int phase;
        public string bg;
        public string time;
        public string light;
        public string[] sfx;
        public string bgm;
        public string[] narration;
        public VNDialogue[] dialogue;
        public string[] actions;
        public VNChoice[] choices;
        public string next;
        public string leftCharacter;
        public string rightCharacter;
    }

    [Serializable]
    public class VNDialogue
    {
        public string speaker;
        public string text;
    }

    [System.Serializable]
    public class VNChoice
    {
        public string text;
        public string gotoScene;
    }
}