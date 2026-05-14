using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using TMPro;
using SimpleJSON;
using System;
using System.Collections.Generic;
using UnityEngine.UI;
using static CharacterManager;
using UnityEditor;
using System.IO;
using Debug = UnityEngine.Debug;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
public class AIStoryBrancher : MonoBehaviour
{
    public Characters Aoi;
    // --- OpenAI API Configuration ---
    [SerializeField] private string openaiApiKey = "sk-YOUR_OPENAI_API_KEY_HERE";
    private string openaiEndpoint = "https://api.openai.com/v1/chat/completions";

    // --- Story Text Source ---
    public TextAsset rawStoryTextFile;
    [Header("挂载书籍主目录（位于 StreamingAssets 下）")]
    public string bookFolderName = "MyBook"; // 书名目录

    // --- UI Component References ---
    public TextMeshProUGUI storyTextDisplay;
    public TextMeshProUGUI speakerNameDisplay;
    public TextMeshProUGUI statusTextDisplay; // Used to display loading or error status

    // --- Option UI References ---
    public GameObject optionsPanel; // Options panel (contains all buttons)
    public GameObject optionButtonPrefab; // Your option button prefab
    public ButtonManager bm;

    // --- Internal State Variables ---
    private string[] processedDialogueLines; // AI generated story text
    private int currentLineIndex = 0;
    private bool isProcessingAI = false;
    private bool isTypingText = false;
    private bool isWaitingForUserInput = false; // Indicates waiting for user to click screen to advance dialogue
    public bool isAuto = false;
    private bool isShowingOptions = false;     // Indicates currently displaying options, waiting for user selection
    private List<GameObject> currentOptionButtons = new List<GameObject>();
    public bool isPlayerFinishedSlice = false;

    public float typeSpeed = 0.05f;
    [SerializeField] private float optionButtonSpacing = 20f; // Spacing between option buttons

    // History
    private List<string> DialogueHistory = new List<string>();
    public List<string> DH_total = new List<string>();
    [SerializeField] private int maxSimpleHistoryLines = 5; // Maximum number of history lines, settable in Inspector

    void Start()
    {
        // Check if UI components and option components are assigned
        Aoi = CharacterManager.instance.GetCharacter("Aoi", enableCreatedCharacterOnStart: false);
        if (storyTextDisplay == null || speakerNameDisplay == null || statusTextDisplay == null ||
            optionsPanel == null || optionButtonPrefab == null)
        {
            Debug.LogError("VNStoryLoader: Some UI components or option prefab not assigned! Please check Inspector.");
            if (statusTextDisplay != null) statusTextDisplay.text = "Error: UI/Option components not assigned.";
            return;
        }

        // Bind option button click events (dynamically added in ShowOptions)
        optionsPanel.SetActive(false); // Ensure options panel is initially hidden

        // Check raw story text file
        if (rawStoryTextFile == null)
        {
            StartCoroutine(ProcessAllChapters());
            //statusTextDisplay.text = "Raw Story Text File (TextAsset) is not assigned!";
            //Debug.LogError("VNStoryLoader: Raw Story Text File (TextAsset) is not assigned!");
            //return;
        }
        StartCoroutine(ProcessAllChapters());
        // Start AI processing flow
        //StartCoroutine(ProcessStoryWithAI());
    }

    void Update()
    {
        //if (!isProcessingAI && !isTypingText && !isShowingOptions && isWaitingForUserInput && !isAuto)
        //{
        //    if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
        //    {
        //        DisplayNextLine();
        //    }
        //}
        //if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
        //{
        //    isAuto = false;
        //}


        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
        {
            //if (storyTextDisplay.text == "The end" || storyTextDisplay.text == "The end.")
            //{
            //    // 导出历史文本
            //    ExportHistoryLines();

            //    EditorApplication.isPlaying = false;
            //}
            if (!isProcessingAI && !isTypingText && !isShowingOptions && isWaitingForUserInput && !isAuto)
                DisplayNextLine();
            else if (isAuto)
            {
                bm.cancelInvoke();
            }
        }
    }
    IEnumerator ProcessAllChapters()
    {
        string bookPath = Path.Combine(Application.streamingAssetsPath, bookFolderName);
        if (!Directory.Exists(bookPath))
        {
            Debug.LogError("找不到书籍目录：" + bookPath);
            yield break;
        }
        Debug.Log("finding all chapters");
        // 获取所有章节文件夹（CHAPTER I, CHAPTER II ...）
        var chapterFolders = Directory.GetDirectories(bookPath)
            .OrderBy(path => ExtractChapterNumber(Path.GetFileName(path))) // 按章节编号排序
            .ToList();

        foreach (string chapterPath in chapterFolders)
        {
            string chapterName = Path.GetFileName(chapterPath);
            Debug.Log($"📖 开始处理章节：{chapterName}");

            // 获取章节内的所有 Slice 文件，按 Slice 编号排序
            var sliceFiles = Directory.GetFiles(chapterPath, "Slice*.txt")
                .OrderBy(path => ExtractSliceNumber(Path.GetFileName(path)))
                .ToList();

            foreach (string slicePath in sliceFiles)
            {
                string content = File.ReadAllText(slicePath);
                Debug.Log($"✏️ 开始处理文件：{Path.GetFileName(slicePath)}");

                yield return StartCoroutine(ProcessStoryWithAI(content));

                // 可选：两个切片间等待（防止API压力太大）
                //yield return new WaitForSeconds(100f);
                yield return StartCoroutine(WaitForPlayerToFinish());
            }

            Debug.Log($"✅ 章节 {chapterName} 处理完毕。\n");
        }

        Debug.Log("🎉 全部章节处理完成！");
    }

    // 提取 CHAPTER 编号
    int ExtractChapterNumber(string chapterFolderName)
    {
        //// 例："CHAPTER I" → 1, "CHAPTER XII" → 12
        //string[] parts = chapterFolderName.Split(' ');
        //if (parts.Length < 2) return 0;
        //return RomanToInt(parts[1]);
        // 统一大小写处理
        chapterFolderName = chapterFolderName.ToUpper();

        // 尝试匹配罗马数字（如 CHAPTER I, CHAPTER XII）
        var romanMatch = System.Text.RegularExpressions.Regex.Match(chapterFolderName, @"(CHAPTER|BOOK)[_\s]+([IVXLCDM]+)");
        if (romanMatch.Success)
            return RomanToInt(romanMatch.Groups[2].Value);

        // 尝试匹配阿拉伯数字（如 Chapter_1, Chapter1, CH1, CHAPTER_12）
        var numMatch = System.Text.RegularExpressions.Regex.Match(chapterFolderName, @"(CHAPTER|BOOK)[_\s]*(\d+)|CH[_\s]*(\d+)|(\d+)");
        if (numMatch.Success)
        {
            // 找到第一个非空捕获组
            foreach (Group g in numMatch.Groups)
            {
                if (int.TryParse(g.Value, out int n))
                    return n;
            }
        }

        // 默认返回0（排序时会放最前）
        return 0;
    }

    // 提取 Slice 编号
    int ExtractSliceNumber(string sliceFileName)
    {
        // 例："Slice3.txt" → 3
        string numPart = new string(sliceFileName.Where(char.IsDigit).ToArray());
        return int.TryParse(numPart, out int n) ? n : 0;
    }

    // 罗马数字转整数（简单版）
    public int RomanToInt(string roman)
    {
        Dictionary<char, int> romanMap = new Dictionary<char, int>()
    {
        {'I', 1},
        {'V', 5},
        {'X', 10},
        {'L', 50},
        {'C', 100},
        {'D', 500},
        {'M', 1000}
    };

        int total = 0;

        for (int i = 0; i < roman.Length; i++)
        {
            char c = roman[i];

            if (!romanMap.ContainsKey(c))
            {
                Debug.LogWarning($"Invalid Roman numeral character: {c} in {roman}");
                return 0; // 或者抛异常，看你希望如何处理
            }

            if (i + 1 < roman.Length && romanMap[c] < romanMap[roman[i + 1]])
                total -= romanMap[c];
            else
                total += romanMap[c];
        }

        return total;
    }
    IEnumerator WaitForPlayerToFinish()
    {
        isPlayerFinishedSlice = false;
        statusTextDisplay.text = " Click to continue...";

        // 等待玩家点击
        while (!isPlayerFinishedSlice)
            yield return null;
    }
    public void ExportHistoryLines()
    {
        // === 1️⃣ 新建导出文件夹（以当前时间命名） ===
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string exportRoot = Application.persistentDataPath;
        string exportFolder = Path.Combine(exportRoot, "Export_" + timestamp);

        Directory.CreateDirectory(exportFolder);

        // === 2️⃣ 导出历史记录文本 ===
        string historyFilePath = Path.Combine(exportFolder, "StoryHistory_" + timestamp + ".txt");
        File.WriteAllLines(historyFilePath, DH_total);
        Debug.Log("历史记录已导出至: " + historyFilePath);

        // === 3️⃣ 复制 GeneratedBackgrounds 文件夹 ===
        string sourceFolder = Path.Combine(Application.persistentDataPath, "GeneratedBackgrounds"); // 你生成图片的原始目录
        string targetFolder = Path.Combine(exportFolder, "GeneratedBackgrounds");

        if (Directory.Exists(sourceFolder))
        {
            CopyDirectory(sourceFolder, targetFolder);
            Debug.Log("图片文件夹已复制至: " + targetFolder);
        }
        else
        {
            Debug.LogWarning("未找到 GeneratedBackgrounds 文件夹: " + sourceFolder);
        }
        // === 4️⃣ 自动打开文件夹（仅在桌面平台有效） ===
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        try
        {
            Process.Start("explorer.exe", exportFolder.Replace("/", "\\"));
            Debug.Log("已自动打开导出文件夹: " + exportFolder);
        }
        catch (Exception e)
        {
            Debug.LogWarning("无法自动打开文件夹: " + e.Message);
        }
#endif
        Debug.Log("全部导出完成！");
    }

    // === 文件夹递归复制函数 ===
    private void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        // 复制文件
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(targetDir, fileName);
            File.Copy(file, destFile, true);
        }

        // 递归复制子文件夹
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string subDirName = Path.GetFileName(subDir);
            string newTargetDir = Path.Combine(targetDir, subDirName);
            CopyDirectory(subDir, newTargetDir);
        }
    }
    // --- AI Story Text Processing Flow (remains unchanged) ---
    IEnumerator ProcessStoryWithAI()
    {
        isProcessingAI = true;
        statusTextDisplay.text = "waiting ...";
        speakerNameDisplay.text = "";
        storyTextDisplay.text = "";

        if (string.IsNullOrEmpty(openaiApiKey) || openaiApiKey == "sk-YOUR_OPENAI_API_KEY_HERE")
        {
            string errorMsg = "Error: OpenAI API Key not set or incorrect! Unable to process story text.";
            statusTextDisplay.text = errorMsg;
            Debug.LogError("VNStoryLoader: " + errorMsg);
            isProcessingAI = false;
            yield break;
        }
        string storyContent = rawStoryTextFile.text;
        if (string.IsNullOrEmpty(storyContent))
        {
            string errorMsg = "Error: Story text file is empty!";
            statusTextDisplay.text = errorMsg;
            Debug.LogError("VNStoryLoader: " + errorMsg);
            isProcessingAI = false;
            yield break;
        }

        Debug.Log("VNStoryLoader: Sending story text to OpenAI for processing...");

        string prompt = GenerateAIPrompt(storyContent);
        string jsonBody = "{" +
                          "\"model\": \"gpt-4o\"," +
                          "\"messages\": [" +
                          "{\"role\": \"system\", \"content\": \"" + EscapeJsonString(prompt) + "\"}" +
                          "]," +
                          "\"temperature\": 0.7," +
                          "\"max_tokens\": 2000" +
                          "}";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        UnityWebRequest request = new UnityWebRequest(openaiEndpoint, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + openaiApiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            string errorMsg = "AI processing failed: Network or API error.\n" + request.error + "\nStatus Code: " + request.responseCode;
            if (request.downloadHandler != null && !string.IsNullOrEmpty(request.downloadHandler.text))
            {
                errorMsg += "\nResponse: " + request.downloadHandler.text;
            }
            statusTextDisplay.text = errorMsg;
            Debug.LogError("VNStoryLoader: " + errorMsg);
        }
        else
        {
            string rawResponse = request.downloadHandler.text;
            Debug.Log("VNStoryLoader: OpenAI Raw Response: " + rawResponse);

            try
            {
                JSONNode jsonResponse = SimpleJSON.JSON.Parse(rawResponse);
                string aiFormattedText = jsonResponse["choices"][0]["message"]["content"].Value;
                Debug.Log("VNStoryLoader: AI Formatted Text:\n" + aiFormattedText);

                processedDialogueLines = aiFormattedText.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                statusTextDisplay.text = "";
                storyTextDisplay.text = "Success.Click the screen to start the story.";
                speakerNameDisplay.text = "";

                currentLineIndex = 0;
                isWaitingForUserInput = true;
            }
            catch (System.Exception e)
            {
                string parseError = "AI response parsing error: " + e.Message + "\nRaw Response: " + rawResponse;
                statusTextDisplay.text = parseError;
                Debug.LogError("VNStoryLoader: " + parseError);
            }
        }
        isProcessingAI = false;
    }
    IEnumerator ProcessStoryWithAI(string storyContent)
    {
        isProcessingAI = true;
        statusTextDisplay.text = "waiting ...";
        speakerNameDisplay.text = "";
        storyTextDisplay.text = "";

        if (string.IsNullOrEmpty(openaiApiKey) || openaiApiKey == "sk-YOUR_OPENAI_API_KEY_HERE")
        {
            string errorMsg = "Error: OpenAI API Key not set or incorrect! Unable to process story text.";
            statusTextDisplay.text = errorMsg;
            Debug.LogError("VNStoryLoader: " + errorMsg);
            isProcessingAI = false;
            yield break;
        }
        if (string.IsNullOrEmpty(storyContent))
        {
            string errorMsg = "Error: Story text file is empty!";
            statusTextDisplay.text = errorMsg;
            Debug.LogError("VNStoryLoader: " + errorMsg);
            isProcessingAI = false;
            yield break;
        }

        Debug.Log("VNStoryLoader: Sending story text to OpenAI for processing...");

        string prompt = GenerateAIPrompt(storyContent);
        string jsonBody = "{" +
                          "\"model\": \"gpt-4o\"," +
                          "\"messages\": [" +
                          "{\"role\": \"system\", \"content\": \"" + EscapeJsonString(prompt) + "\"}" +
                          "]," +
                          "\"temperature\": 0.7," +
                          "\"max_tokens\": 2000" +
                          "}";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        UnityWebRequest request = new UnityWebRequest(openaiEndpoint, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + openaiApiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            string errorMsg = "AI processing failed: Network or API error.\n" + request.error + "\nStatus Code: " + request.responseCode;
            if (request.downloadHandler != null && !string.IsNullOrEmpty(request.downloadHandler.text))
            {
                errorMsg += "\nResponse: " + request.downloadHandler.text;
            }
            statusTextDisplay.text = errorMsg;
            Debug.LogError("VNStoryLoader: " + errorMsg);
        }
        else
        {
            string rawResponse = request.downloadHandler.text;
            Debug.Log("VNStoryLoader: OpenAI Raw Response: " + rawResponse);

            try
            {
                JSONNode jsonResponse = SimpleJSON.JSON.Parse(rawResponse);
                string aiFormattedText = jsonResponse["choices"][0]["message"]["content"].Value;
                Debug.Log("VNStoryLoader: AI Formatted Text:\n" + aiFormattedText);

                processedDialogueLines = aiFormattedText.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                statusTextDisplay.text = "";
                storyTextDisplay.text = "Success.Click the screen to start the story.";
                speakerNameDisplay.text = "";

                currentLineIndex = 0;
                isWaitingForUserInput = true;
            }
            catch (System.Exception e)
            {
                string parseError = "AI response parsing error: " + e.Message + "\nRaw Response: " + rawResponse;
                statusTextDisplay.text = parseError;
                Debug.LogError("VNStoryLoader: " + parseError);
            }
        }
        isProcessingAI = false;
    }

    public IEnumerator Auto()
    {
        isAuto = true;
        if (isTypingText)
        {

        }
        else
        {
            DisplayNextLine();
            yield return new WaitForEndOfFrame();
        }

    }

    // --- Display Next Line ---
    void DisplayNextLine()
    {
        if (isTypingText)
        {
            StopAllCoroutines();
            if (currentLineIndex > 0 && processedDialogueLines != null && currentLineIndex <= processedDialogueLines.Length)
            {
                string prevLine = processedDialogueLines[currentLineIndex - 1];
                string[] parts = prevLine.Split(new char[] { ':' }, 2);
                storyTextDisplay.text = parts[0].Replace("\"", "");
            }
            isTypingText = false;
            isWaitingForUserInput = true;
            return;
        }

        isWaitingForUserInput = false;

        if (processedDialogueLines == null || currentLineIndex >= processedDialogueLines.Length)
        {
            storyTextDisplay.text = " ";
            speakerNameDisplay.text = "";
            isWaitingForUserInput = false;
            Debug.Log("VNStoryLoader: Dialogue ended.");
            return;
        }

        string line = processedDialogueLines[currentLineIndex];

        // --- Correction: More precisely identify ":[OPTIONS]..." at the end of the sentence ---
        // Ensure line has removed outermost double quotes and possible trailing colon (if AI outputs it)
        string cleanedLineForOptionsCheck = line.Trim();
        if (cleanedLineForOptionsCheck.StartsWith("\"") && cleanedLineForOptionsCheck.EndsWith("\""))
        {
            cleanedLineForOptionsCheck = cleanedLineForOptionsCheck.Substring(1, cleanedLineForOptionsCheck.Length - 2);
        }
        if (cleanedLineForOptionsCheck.EndsWith(":")) // If AI also adds a colon after OPTIONS
        {
            cleanedLineForOptionsCheck = cleanedLineForOptionsCheck.TrimEnd(':');
        }

        // Check if it contains ":[OPTIONS]" pattern
        // StringComparison.Ordinal ensures exact match, unaffected by culture
        int optionsIndex = cleanedLineForOptionsCheck.IndexOf(":[OPTIONS]", StringComparison.Ordinal);
        //!!new here
        // Try to detect other malformed [OPTIONS]
        if (optionsIndex == -1 && cleanedLineForOptionsCheck.ToUpper().Contains("[OPTIONS]"))
        {
            Debug.LogWarning("VNStoryLoader: Detected malformed [OPTIONS] format. Attempting to auto-correct...");

            // Try to locate the "[OPTIONS]" pattern (regardless of extra quotes or missing colon)
            int bracketIndex = cleanedLineForOptionsCheck.ToUpper().IndexOf("[OPTIONS]");
            string contentBeforeOptions = cleanedLineForOptionsCheck.Substring(0, bracketIndex).Trim().TrimEnd(':', '"');
            string optionsData = cleanedLineForOptionsCheck.Substring(bracketIndex).Trim().TrimStart(':', '"');

            // Continue as usual
            string[] dialogueParts = line.Split(new char[] { ':' }, 2);
            string speaker = (dialogueParts.Length > 1) ? dialogueParts[1].Trim() : "";

            speakerNameDisplay.text = "";
            StartCoroutine(TypeTextAndShowOptions(contentBeforeOptions, optionsData));

            AddSimpleLineToHistory(speaker + ":" + contentBeforeOptions);
            currentLineIndex++;
            return;
        }


        if (optionsIndex != -1) // Found ":[OPTIONS]"
        {
            Debug.Log($"VNStoryLoader: Detected end-of-sentence option command at index {optionsIndex} in line: '{line}'");

            // Split text content and option command
            string contentBeforeOptions = cleanedLineForOptionsCheck.Substring(0, optionsIndex); // Text content
            string optionsData = cleanedLineForOptionsCheck.Substring(optionsIndex + ":".Length); // Part after ":[OPTIONS]..."

            // Process text content (typewriter effect)
            string[] dialogueParts = line.Split(new char[] { ':' }, 2); // Still use original line to parse speaker
            string speaker = (dialogueParts.Length > 1) ? dialogueParts[1].Trim() : "";
            
            speakerNameDisplay.text = "";
            // speakerNameDisplay.text = speaker;
            StartCoroutine(TypeTextAndShowOptions(contentBeforeOptions, optionsData));

            // Call AddSimpleLineToHistory, passing dialogue content
            AddSimpleLineToHistory(speaker + ":" + contentBeforeOptions); // Format as "Character Name:Text"

            currentLineIndex++; // Advance to next line, as current line is processed
            return; // Pause dialogue flow, wait for user selection
        }

        // --- Below is regular dialogue and narration processing (if no options matched above) ---
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("#"))
        {
            Debug.Log($"VNStoryLoader: Skipping empty/comment line: '{line}'");

            currentLineIndex++;
            DisplayNextLine();
            return;
        }

        string[] regularDialogueParts = line.Split(new char[] { ':' }, 2);
        string regularContent = regularDialogueParts[0].Replace("\"", "");
        string regularSpeaker = (regularDialogueParts.Length > 1) ? regularDialogueParts[1].Trim() : "";

        if (regularContent.StartsWith("--") && regularContent.EndsWith("--"))
        {
            speakerNameDisplay.text = "";
            StartCoroutine(TypeText(regularContent.Replace("--", "").Trim()));
        }
        else
        {
            speakerNameDisplay.text = regularSpeaker;
            Debug.Log("Speaker parsed as: '" + regularSpeaker + "'");
            if (regularSpeaker == "Aoi")
            {
                int index = UnityEngine.Random.Range(0, 11);
                Aoi.SetBody(index);
                //Aoi.Activate();
                //Aoi.SetAlpha(0.9f,true);

            }
            StartCoroutine(TypeText(regularContent));
        }
        // Call AddSimpleLineToHistory, passing dialogue content
        AddSimpleLineToHistory("--" + regularContent.Replace("--", "").Trim() + "--:"); // Keep "--Text--:" format
        currentLineIndex++;
    }
    public void ChangeFace()
    {
        int index = UnityEngine.Random.Range(0, 11);
        Aoi.SetBody(index);
    }
    // --- New: Coroutine to type text and show options ---
    IEnumerator TypeTextAndShowOptions(string fullText, string optionsData)
    {
        isTypingText = true;
        storyTextDisplay.text = "";

        for (int i = 0; i < fullText.Length; i++)
        {
            storyTextDisplay.text += fullText[i];
            yield return new WaitForSeconds(typeSpeed);
        }
        isTypingText = false;
        isWaitingForUserInput = true; // Text finished displaying

        // Once text is displayed, immediately show options
        ShowOptionsInternal(optionsData); // Internal call to ShowOptions
    }

    // --- Typewriter text effect (remains unchanged) ---
    IEnumerator TypeText(string fullText)
    {
        isTypingText = true;
        storyTextDisplay.text = "";

        for (int i = 0; i < fullText.Length; i++)
        {
            storyTextDisplay.text += fullText[i];
            yield return new WaitForSeconds(typeSpeed);
        }
        isTypingText = false;
        isWaitingForUserInput = true;
    }

    // --- Option Logic: Dynamically create buttons (extracted from ShowOptions into internal method) ---
    // This method is now only responsible for processing the option string and displaying the UI,
    // not directly called by DisplayNextLine.
    void ShowOptionsInternal(string optionsDataString)
    {
        isShowingOptions = true; // Mark as showing options
        isWaitingForUserInput = false; // No longer waiting to advance dialogue, but waiting for option selection
        // storyTextDisplay.text is no longer cleared, as text has already been displayed
        // speakerNameDisplay.text is also not cleared

        optionsPanel.SetActive(true); // Show options panel

        // Clear all old dynamically created buttons
        foreach (GameObject btn in currentOptionButtons)
        {
            Destroy(btn);
        }
        currentOptionButtons.Clear();

        // Parse option text: optionsDataString already contains [OPTIONS]...
        string optionsString = optionsDataString.Replace("[OPTIONS]", "").Trim();
        string[] options = optionsString.Split('|');

        if (options.Length == 0)
        {
            Debug.LogWarning("VNStoryLoader: No option text parsed! Please check format after :[OPTIONS].");
            HideOptions(); // Unable to create options, hide panel directly
            return;
        }

        RectTransform panelRect = optionsPanel.GetComponent<RectTransform>();
        float currentYOffset = 0f; // Used for vertical button stacking

        // Dynamically create and configure buttons
        for (int i = 0; i < options.Length; i++)
        {
            if (optionButtonPrefab == null)
            {
                Debug.LogError("VNStoryLoader: Option button prefab not assigned! Cannot create options.");
                return;
            }

            GameObject newButtonGO = Instantiate(optionButtonPrefab, optionsPanel.transform);
            newButtonGO.name = $"OptionButton_{i}";
            currentOptionButtons.Add(newButtonGO); // Add to list for later management

            Button newButton = newButtonGO.GetComponent<Button>();
            TextMeshProUGUI buttonText = newButtonGO.GetComponentInChildren<TextMeshProUGUI>();

            if (newButton == null || buttonText == null)
            {
                Debug.LogError($"VNStoryLoader: Prefab '{optionButtonPrefab.name}' is missing Button or TextMeshProUGUI component.");
                Destroy(newButtonGO);
                continue;
            }

            buttonText.text = options[i].Trim();
            newButton.gameObject.SetActive(true);

            // Set button position (simple vertical stacking)
            RectTransform buttonRect = newButtonGO.GetComponent<RectTransform>();
            if (buttonRect != null)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 1f); // Anchor at top-center of parent panel
                buttonRect.anchorMax = new Vector2(0.5f, 1f);
                buttonRect.pivot = new Vector2(0.5f, 1f); // Pivot point at top-center of button

                buttonRect.anchoredPosition = new Vector2(0f, -currentYOffset);
                currentYOffset += buttonRect.sizeDelta.y + optionButtonSpacing; // Accumulate offset for next button
            }

            Image targetImg = newButtonGO.GetComponentInChildren<Image>(true); // 包含未激活的

            if (targetImg != null)
            {
                newButton.targetGraphic = targetImg;  // 关键！！！

                ColorBlock cb = newButton.colors;
                cb.normalColor = Color.black;
                cb.highlightedColor = new Color(1f, 1f, 0.8f);     // 悬停浅黄
                cb.pressedColor = new Color(1f, 0.9f, 0.5f);     // 点击深黄
                cb.selectedColor = Color.white;
                cb.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
                cb.fadeDuration = 0.12f;
                newButton.colors = cb;

                // 强制测试：运行时立刻看到效果
                targetImg.color = new Color(0.3f, 0.8f, 1f);  // 蓝绿色，超级明显
            }
            // Add click event listener
            int optionIndex = i; // Capture loop variable
            newButton.onClick.AddListener(() => OnOptionSelected(optionIndex));
        }
    }

    void HideOptions()
    {
        isShowingOptions = false;
        optionsPanel.SetActive(false);
        // Destroy all dynamically created buttons
        foreach (GameObject btn in currentOptionButtons)
        {
            Destroy(btn);
        }
        currentOptionButtons.Clear();
    }

    // Called when the user clicks an option button
    void OnOptionSelected(int optionIndex)
    {
        if (optionIndex < 0 || optionIndex >= currentOptionButtons.Count) return;

        TextMeshProUGUI selectedButtonText = currentOptionButtons[optionIndex].GetComponentInChildren<TextMeshProUGUI>();
        string selectedOptionText = (selectedButtonText != null) ? selectedButtonText.text : "Unknown Option";

        Debug.Log("VNStoryLoader: User selected: " + selectedOptionText);

        HideOptions(); // Hide option interface

        StartCoroutine(SendOptionToAI(selectedOptionText));
    }
    //new!!!!
    private string lastSelectedOption = null;
    private string lastPrompt = null;
    private bool lastSendFailed = false;
    IEnumerator SendOptionToAI(string selectedOption)
    {
        isProcessingAI = true;
        statusTextDisplay.text = "Now waiting AI reply...";
        speakerNameDisplay.text = "";
        lastSelectedOption = selectedOption; // 保存选择
        lastSendFailed = false;
        storyTextDisplay.text = selectedOption + "..."; // Display user selected option
        // --- Build history string ---
        StringBuilder historyBuilder = new StringBuilder();
        foreach (string historicalLine in DialogueHistory)
        {
            historyBuilder.AppendLine(historicalLine); // Add a newline after each history line
        }
        string historyContext = historyBuilder.ToString().Trim(); // Remove trailing newline
        Debug.Log("VNStoryLoader: Previous story text: " + historyContext);
        string prompt = GenerateAIPrompt();//, but generate a option in the end
        prompt += $"User selected: \"{selectedOption}\", the previous story text was \"{historyContext}\". Based on this selection, please continue the visual novel story, generating a few lines of dialogue or narration. " +
            $"Only generate a few lines, not too long. Be reasonable and imaginative and based on the format. 请尽量不要轻易结局来给玩家美好的体验. 假如你认为这个故事应该完结了请生成无选项的结局，根据selection来生成坏结局和解释，注意解释生成要在结局前面，结局的话请在解释后末尾新一行加上 The end。";
        string jsonBody = "{" +
                          "\"model\": \"gpt-4o\"," +
                          "\"messages\": [" +
                          "{\"role\": \"system\", \"content\": \"" + EscapeJsonString(prompt) + "\"}" +
                          "]," +
                          "\"temperature\": 0.7," +
                          "\"max_tokens\": 500" +
                          "}";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        lastPrompt = prompt; // 保存 prompt

        UnityWebRequest request = new UnityWebRequest(openaiEndpoint, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + openaiApiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            string errorMsg = "Failed to get AI reply: Network or API error.\n" + request.error;
            statusTextDisplay.text = errorMsg;
            Debug.LogError("VNStoryLoader: " + errorMsg);
            storyTextDisplay.text = "AI reply failed.";
            lastSendFailed = true; // 标记失败
        }
        else
        {
            string rawResponse = request.downloadHandler.text;
            Debug.Log("VNStoryLoader: AI Reply Raw Response: " + rawResponse);

            try
            {
                JSONNode jsonResponse = SimpleJSON.JSON.Parse(rawResponse);
                string aiReplyText = jsonResponse["choices"][0]["message"]["content"].Value;
                Debug.Log("VNStoryLoader: AI Reply Content:\n" + aiReplyText);

                InsertAILinesIntoDialogue(aiReplyText);

                statusTextDisplay.text = "";
                isWaitingForUserInput = true;
                DisplayNextLine(); // Display the first line of AI reply
            }
            catch (System.Exception e)
            {
                string parseError = "AI reply parsing error: " + e.Message + "\nRaw Response: " + rawResponse;
                statusTextDisplay.text = parseError;
                Debug.LogError("VNStoryLoader: " + parseError);
                storyTextDisplay.text = "AI reply parsing failed.";
                lastSendFailed = true;
            }
        }
        isProcessingAI = false;
    }

    public void RetryLastOption()
    {
        if (string.IsNullOrEmpty(lastSelectedOption) || isProcessingAI)
        {
            Debug.LogWarning("VNStoryLoader: No previous option to retry or AI is busy.");
            return;
        }

        Debug.Log("VNStoryLoader: Retrying previous option: " + lastSelectedOption);
        StartCoroutine(SendOptionToAI(lastSelectedOption));
    }

    // Inserts AI reply into the current dialogue array (remains unchanged)
    void InsertAILinesIntoDialogue(string aiReply)
    {
        string[] newLines = aiReply.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (newLines.Length == 0) return;

        List<string> tempDialogueList = new List<string>(processedDialogueLines);
        // Insert new lines after the current dialogue line
        // currentLineIndex points to the line after the option command, so inserting here is appropriate
        tempDialogueList.InsertRange(currentLineIndex, newLines);

        processedDialogueLines = tempDialogueList.ToArray();
        Debug.Log($"VNStoryLoader: Inserted {newLines.Length} lines of AI reply. Current dialogue array total length: {processedDialogueLines.Length}");
    }

    // --- New: Add formatted dialogue line to simple history ---
    void AddSimpleLineToHistory(string formattedLine)
    {
        DialogueHistory.Add(formattedLine);
        DH_total.Add(formattedLine);

        // Keep history within max line count
        if (DialogueHistory.Count > maxSimpleHistoryLines)
        {
            DialogueHistory.RemoveAt(0); // Remove oldest line
        }
        Debug.Log($"VNStoryLoader: History updated - Added: '{formattedLine}' (Current total lines: {DialogueHistory.Count})");
    }

    // --- Helper Method: Generate AI Prompt ---
    private string GenerateAIPrompt(string storyText)
    {
        return $"You are a professional Visual Novel script writer and text segmentation expert.You should rewrite and condense the story text while keeping the main plot, characters, and important events intact. Remove redundant or overly detailed passages, but maintain coherence and narrative flow.\n" +
               $"Below is the story text. You need to segment it into the smallest units suitable for visual novel display (usually based on punctuation, visual actions, scene change points, etc.). Paying attention to keeping the language consistent with the input.\n\n" +
               $"For each segmented text, please strictly follow the rules below for formatting and output:\n\n" +
               $"1.  **Narration or Description:**\n" +
               $"    * `\"Text Content\":` (no character name, one sentence for narration or scene description).\n" +
               $"2.  **Character Dialogue:**\n" +
               $"    * `\"Dialogue Content\":Character Name` (character name after the colon, character name can be in Japanese, Chinese, or English, character name should be within 8 words)\n" +
               $"3.  **Scene/Time Change Point:**\n" +
               $"    * `\"--Description--:\"` (e.g., `\"--The next morning--:\"`, with double dashes before and after the description)\n" +
               $"4.  **Option Command (appended to the end of the sentence):**\n" + // <-- New: Explicitly state appended to end of sentence
               $"    * `\"Text Content\":[OPTIONS]Option A Text|Option B Text|Option C Text` (immediately follows the sentence content, separated by `:[OPTIONS]`, followed by option texts separated by `|`. **This line must be self-contained and not contain extra double quotes or trailing colons outside the entire string.**)\n" +
               $"**Strictly follow the output format above. Do not output any extra explanations or guiding words other than the formatted text. Do not include extra blank lines. All text content that should be enclosed in double quotes must be enclosed.**\n\n" +
               $"Below is the story text:\n" +
               $"---\n" +
               $"{storyText}\n" +
               $"---\n\n" +
               $"Remember only one sentence for narration or scene description, and insert huge amouts of options corresponding to the plot as you like. Now, start outputting the formatted text:";
    }
    private string GenerateAIPrompt()
    {
        return $"You are a professional Visual Novel script writer and text segmentation expert.\n" +
               $"Below is the story text. You need to segment it into the smallest units suitable for visual novel display (usually based on punctuation, visual actions, scene change points, etc.).Paying attention to keeping the language consistent with the input.\n\n" +
               $"For each segmented text, please strictly follow the rules below for formatting and output:\n\n" +
               $"1.  **Narration or Description:**\n" +
               $"    * `\"Text Content\":` (no character name after the colon, indicates narration or scene description)\n" +
               $"2.  **Character Dialogue:**\n" +
               $"    * `\"Dialogue Content\":Character Name` (character name after the colon, character name can be in Japanese, Chinese, or English)\n" +
               $"3.  **Scene/Time Change Point:**\n" +
               $"    * `\"--Description--:\"` (e.g., `\"--The next morning--:\"`, with double dashes before and after the description)\n" +
               $"4.  **Option Command (appended to the end of the sentence):**\n" + // <-- New: Explicitly state appended to end of sentence
               $"    * `\"Text Content\":[OPTIONS]Option A Text|Option B Text|Option C Text` (immediately follows the sentence content, separated by `:[OPTIONS]`, followed by option texts separated by `|`. **This line must be self-contained and not contain extra double quotes or trailing colons outside the entire string.**)\n" +
               $"**Strictly follow the output format above. Do not output any extra explanations or guiding words other than the formatted text. Do not include extra blank lines. All text content that should be enclosed in double quotes must be enclosed.**\n\n" +
               $"Now, start outputting the formatted text:";
    }
    public void SetStoryText(TextAsset newText)
    {
        rawStoryTextFile = newText;
    }
    public void SetStoryTXT(String FolderName)
    {
        bookFolderName = FolderName;
    }
    public void ToBeContinued()
    {
        isPlayerFinishedSlice = true;
    }
    // --- Helper Method: Safely escape JSON string (remains unchanged) ---
    private string EscapeJsonString(string text)
    {
        if (string.IsNullOrEmpty(text)) { return ""; }
        StringBuilder sb = new StringBuilder();
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '/': sb.Append("\\/"); break;
                default:
                    if (c < 32 || (c > 126 && c < 160))
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("X4"));
                    }
                    else { sb.Append(c); }
                    break;
            }
        }
        return sb.ToString();
    }
}