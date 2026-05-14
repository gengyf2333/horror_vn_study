using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Text.RegularExpressions;

public class MultiImageGenerationService : MonoBehaviour
{
    public static MultiImageGenerationService instance { get; private set; }

    public System.Action<List<string>> OnImagesGenerated;
    public System.Action<List<Texture2D>> OnTexturesGenerated;

    [SerializeField] private string imageGenApiKey = "YOUR_OPENAI_API_KEY_HERE";
    private string chatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";
    private string imageGenEndpoint = "https://api.openai.com/v1/images/generations";

    public string bookFolderName = "MyBook";

    [Header("场景设置")]
    public TextAsset rawStoryTextFile;
    public BackgroundManager backgroundManager;
    public bool isPlayerFinishedSlice = false;

    [Header("生成设置")]
    [SerializeField] private string outputFileNamePrefix = "scene";
    [SerializeField] private float imageTransitionSpeed = 0.5f;
    [SerializeField] private bool imageSmoothTransition = true;
    [SerializeField] private string dalleImageSize = "1024x1024";
    [SerializeField] private string dalleImageQuality = "standard";

    private const string SYSTEM_PROMPT = "あなたはビジュアルノベルの背景画像を生成する専門家です。" +
                                        "以下の場景描述に基づいて、詳細で写実的かつ映画のようなビジュアルノベルの背景画像を生成してください。" +
                                        "アスペクト比は16:9でお願いします。" +
                                        "日本語アニメーションスタイル, Please add black bars to the top and bottom to create a 16:9 aspect ratio. " +
                                        "Do not add black bars to the sides。";

    private const string SCENE_EXTRACTION_PROMPT =
       "あなたはプロのシナリオライターです。以下の長い物語のテキストを読み、ビジュアルノベルの背景画像を生成するのに最も適した、視覚的に重要な場面を6つ抽出してください。" +
       "各場面は、場所、時間、雰囲気、そして重要な視覚的要素を簡潔に記述してください。" +
       "必ず以下のフォーマットを厳守し、場面ごとに改行して出力してください。\n" +
       "フォーマット：\n" +
       "場面1：[ここに場面1の説明]\n" +
       "場面2：[ここに場面2の説明]\n" +
       "場面3：[ここに場面3の説明]\n" +
       "場面4：[ここに場面4の説明]\n" +
       "場面5：[ここに場面5の説明]\n" +
       "場面6：[ここに場面6の説明]";

    private const string GENERATED_BG_SUBFOLDER = "GeneratedBackgrounds";
    private const string SCENE_SEPARATOR = "---";

    // 新增成员：记录当前章节和Slice
    private string currentChapterName;
    private string currentSliceName;

    void Awake()
    {
        if (instance != null && instance != this)
            Destroy(gameObject);
        else
            instance = this;
    }

    public void SetStoryText(TextAsset newText)
    {
        rawStoryTextFile = newText;
    }

    public void SetStoryTXT(String FolderName)
    {
        bookFolderName = FolderName;
    }

    void Start()
    {
        StartCoroutine(ProcessAllChapters());
    }

    public IEnumerator ExtractScenesAndGenerateImages()
    {
        string storyContent = rawStoryTextFile.text;
        yield return StartCoroutine(ExtractScenesAndGenerateImages(storyContent));
    }

    public IEnumerator ExtractScenesAndGenerateImages(string storyContent)
    {
        Debug.Log("物語から場面の抽出を開始します...");

        string jsonBody = "{" +
            "\"model\": \"gpt-4o\"," +
            "\"messages\": [" +
                "{\"role\": \"system\", \"content\": \"" + EscapeJsonString(SCENE_EXTRACTION_PROMPT) + "\"}," +
                "{\"role\": \"user\", \"content\": \"" + EscapeJsonString(storyContent) + "\"}" +
            "]," +
            "\"temperature\": 0.5" +
        "}";

        UnityWebRequest request = new UnityWebRequest(chatCompletionsEndpoint, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + imageGenApiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"場面抽出に失敗しました: {request.error}\n{request.downloadHandler.text}");
            yield break;
        }

        JSONNode jsonResponse = JSON.Parse(request.downloadHandler.text);
        string extractedScenesText = jsonResponse["choices"][0]["message"]["content"];

        if (string.IsNullOrEmpty(extractedScenesText))
        {
            Debug.LogError("AIからの応答が空でした。");
            yield break;
        }

        // 调用生成函数
        yield return StartCoroutine(GenerateImagesForAllScenes(extractedScenesText));
    }

    IEnumerator ProcessAllChapters()
    {
        string bookPath = Path.Combine(Application.streamingAssetsPath, bookFolderName);
        if (!Directory.Exists(bookPath))
        {
            Debug.LogError("找不到书籍目录：" + bookPath);
            yield break;
        }

        var chapterFolders = Directory.GetDirectories(bookPath)
            .OrderBy(path => ExtractChapterNumber(Path.GetFileName(path)))
            .ToList();

        foreach (string chapterPath in chapterFolders)
        {
            currentChapterName = Path.GetFileName(chapterPath);
            var sliceFiles = Directory.GetFiles(chapterPath, "Slice*.txt")
                .OrderBy(path => ExtractSliceNumber(Path.GetFileName(path)))
                .ToList();

            foreach (string slicePath in sliceFiles)
            {
                currentSliceName = Path.GetFileNameWithoutExtension(slicePath);
                string content = File.ReadAllText(slicePath);
                Debug.Log($"📖 开始处理章节：{currentChapterName} | Slice：{currentSliceName}");
                yield return StartCoroutine(ExtractScenesAndGenerateImages(content));
                yield return StartCoroutine(WaitForPlayerToFinish());
            }

            Debug.Log($"✅ 章节 {currentChapterName} 处理完毕。\n");
        }

        Debug.Log("🎉 全部章节处理完成！");
    }

    int ExtractChapterNumber(string chapterFolderName)
    {
        chapterFolderName = chapterFolderName.ToUpper();
        var romanMatch = Regex.Match(chapterFolderName, @"(CHAPTER|BOOK)[_\s]+([IVXLCDM]+)");
        if (romanMatch.Success)
            return RomanToInt(romanMatch.Groups[2].Value);

        var numMatch = Regex.Match(chapterFolderName, @"(CHAPTER|BOOK)[_\s]*(\d+)|CH[_\s]*(\d+)|(\d+)");
        if (numMatch.Success)
        {
            foreach (Group g in numMatch.Groups)
            {
                if (int.TryParse(g.Value, out int n))
                    return n;
            }
        }

        return 0;
    }

    int ExtractSliceNumber(string sliceFileName)
    {
        string numPart = new string(sliceFileName.Where(char.IsDigit).ToArray());
        return int.TryParse(numPart, out int n) ? n : 0;
    }

    public int RomanToInt(string roman)
    {
        Dictionary<char, int> romanMap = new Dictionary<char, int>()
        {
            {'I', 1},{'V', 5},{'X', 10},{'L', 50},{'C', 100},{'D', 500},{'M', 1000}
        };

        int total = 0;
        for (int i = 0; i < roman.Length; i++)
        {
            char c = roman[i];
            if (!romanMap.ContainsKey(c))
                return 0;
            if (i + 1 < roman.Length && romanMap[c] < romanMap[roman[i + 1]])
                total -= romanMap[c];
            else
                total += romanMap[c];
        }
        return total;
    }

    public void ToBeContinued() => isPlayerFinishedSlice = true;

    IEnumerator WaitForPlayerToFinish()
    {
        isPlayerFinishedSlice = false;
        while (!isPlayerFinishedSlice)
            yield return null;
    }

    public IEnumerator GenerateImagesForAllScenes(string ParseStory)
    {
        string baseSaveDirPath = Path.Combine(Application.persistentDataPath, GENERATED_BG_SUBFOLDER);
        Directory.CreateDirectory(baseSaveDirPath);

        List<string> scenes = ParseScenesFromText(ParseStory);
        if (scenes.Count == 0)
        {
            Debug.LogError("未找到任何场景描述！");
            yield break;
        }

        List<string> generatedImageNames = new List<string>();
        List<Texture2D> textureList = new List<Texture2D>();

        for (int i = 0; i < scenes.Count; i++)
        {
            string sceneDescription = scenes[i].Trim();
            if (string.IsNullOrEmpty(sceneDescription)) continue;

            string fileName = $"{outputFileNamePrefix}_{i + 1}";
            string chapterDirPath = Path.Combine(baseSaveDirPath, currentChapterName);
            string sliceDirPath = Path.Combine(chapterDirPath, currentSliceName);
            Directory.CreateDirectory(sliceDirPath);
            string fullSaveFilePath = Path.Combine(sliceDirPath, fileName + ".png");

            string fullPrompt = SYSTEM_PROMPT + "\n\n场景描述：\n" + sceneDescription;

            string jsonBody = "{" +
                              "\"prompt\": \"" + EscapeJsonString(fullPrompt) + "\"," +
                              "\"model\": \"dall-e-3\"," +
                              "\"n\": 1," +
                              "\"size\": \"" + dalleImageSize + "\"," +
                              "\"quality\": \"" + dalleImageQuality + "\"" +
                              "}";

            UnityWebRequest request = new UnityWebRequest(imageGenEndpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + imageGenApiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"场景 {i + 1} 图片生成失败：{request.error}");
                continue;
            }

            JSONNode jsonResponse = JSON.Parse(request.downloadHandler.text);
            string imageUrl = jsonResponse["data"][0]["url"];
            if (string.IsNullOrEmpty(imageUrl)) continue;

            UnityWebRequest imageRequest = UnityWebRequest.Get(imageUrl);
            imageRequest.downloadHandler = new DownloadHandlerBuffer();
            yield return imageRequest.SendWebRequest();
            if (imageRequest.result != UnityWebRequest.Result.Success) continue;

            byte[] imageBytes = imageRequest.downloadHandler.data;
            File.WriteAllBytes(fullSaveFilePath, imageBytes);

            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(imageBytes);
            tex.name = fileName;
            textureList.Add(tex);
            generatedImageNames.Add(fileName + ".png");

            SetBackgroundTexture(tex);
            yield return new WaitForSeconds(3f);
        }

        OnImagesGenerated?.Invoke(generatedImageNames);
        OnTexturesGenerated?.Invoke(textureList);
        Debug.Log($"所有场景图片生成完毕，共 {generatedImageNames.Count} 张");
    }

    private List<string> ParseScenesFromText(string text)
    {
        List<string> scenes = new List<string>();
        if (string.IsNullOrEmpty(text)) return scenes;

        if (text.Contains(SCENE_SEPARATOR))
        {
            string[] parts = text.Split(new string[] { SCENE_SEPARATOR }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
                if (!string.IsNullOrEmpty(part.Trim())) scenes.Add(part.Trim());
        }
        else if (text.Contains("场景"))
        {
            string[] lines = text.Split('\n');
            string currentScene = "";
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("场景") && (trimmedLine.Contains(":") || trimmedLine.Contains("：")))
                {
                    if (!string.IsNullOrEmpty(currentScene)) scenes.Add(currentScene.Trim());
                    int colonIndex = Mathf.Max(trimmedLine.IndexOf(":"), trimmedLine.IndexOf("："));
                    currentScene = colonIndex >= 0 ? trimmedLine.Substring(colonIndex + 1).Trim() : "";
                }
                else if (!string.IsNullOrEmpty(trimmedLine))
                {
                    if (!string.IsNullOrEmpty(currentScene)) currentScene += "\n";
                    currentScene += trimmedLine;
                }
            }
            if (!string.IsNullOrEmpty(currentScene)) scenes.Add(currentScene.Trim());
        }
        else
        {
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed)) scenes.Add(trimmed);
            }
        }

        return scenes;
    }

    public void SetBackgroundTexture(Texture2D tex)
    {
        if (backgroundManager != null && tex != null)
            backgroundManager.SetGeneratedBackground(tex, imageTransitionSpeed, imageSmoothTransition);
    }

    private string EscapeJsonString(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
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
                        sb.Append("\\u" + ((int)c).ToString("X4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}


/*
 * using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
public class MultiImageGenerationService : MonoBehaviour
{
    public static MultiImageGenerationService instance { get; private set; }

    // 回调：生成的文件名列表
    public System.Action<List<string>> OnImagesGenerated;
    // 回调：生成的纹理列表
    public System.Action<List<Texture2D>> OnTexturesGenerated;

    [SerializeField] private string imageGenApiKey = "YOUR_OPENAI_API_KEY_HERE";
    private string chatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";
    private string imageGenEndpoint = "https://api.openai.com/v1/images/generations";

    public string bookFolderName = "MyBook"; // 书名目录

    [Header("场景设置")]
    public TextAsset rawStoryTextFile;
    //public TextAsset scenesTextFile; // 包含6个场景的txt文件
    public BackgroundManager backgroundManager;
    public bool isPlayerFinishedSlice = false;

    [Header("生成设置")]
    [SerializeField] private string outputFileNamePrefix = "scene";
    [SerializeField] private float imageTransitionSpeed = 0.5f;
    [SerializeField] private bool imageSmoothTransition = true;
    [SerializeField] private string dalleImageSize = "1024x1024";
    [SerializeField] private string dalleImageQuality = "standard";

    // 系统提示语
    private const string SYSTEM_PROMPT = "あなたはビジュアルノベルの背景画像を生成する専門家です。" +
                                        "以下の場景描述に基づいて、詳細で写実的かつ映画のようなビジュアルノベルの背景画像を生成してください。" +
                                        "アスペクト比は16:9でお願いします。" +
                                        "日本語アニメーションスタイル, Please add black bars to the top and bottom to create a 16:9 aspect ratio. " +
                                        "Do not add black bars to the sides。";
    //キャラクターは含めないでください。
    private const string SCENE_EXTRACTION_PROMPT =
       "あなたはプロのシナリオライターです。以下の長い物語のテキストを読み、ビジュアルノベルの背景画像を生成するのに最も適した、視覚的に重要な場面を6つ抽出してください。" +
       "各場面は、場所、時間、雰囲気、そして重要な視覚的要素を簡潔に記述してください。" +
       "必ず以下のフォーマットを厳守し、場面ごとに改行して出力してください。\n" +
       "フォーマット：\n" +
       "場面1：[ここに場面1の説明]\n" +
       "場面2：[ここに場面2の説明]\n" +
       "場面3：[ここに場面3の説明]\n" +
       "場面4：[ここに場面4の説明]\n" +
       "場面5：[ここに場面5の説明]\n" +
       "場面6：[ここに場面6の説明]";
    private const string GENERATED_BG_SUBFOLDER = "GeneratedBackgrounds";
    private const string SCENE_SEPARATOR = "---"; // 场景分隔符

    void Awake()
    {
        if (instance != null && instance != this)
            Destroy(gameObject);
        else
            instance = this;
    }
    public void SetStoryText(TextAsset newText)
    {
        rawStoryTextFile = newText;
    }
    public void SetStoryTXT(String FolderName)
    {
        bookFolderName = FolderName;
    }
    void Start()
    {
        StartCoroutine(ProcessAllChapters());
    }
    public IEnumerator ExtractScenesAndGenerateImages()
    {
        string storyContent = rawStoryTextFile.text;
        Debug.Log("物語から場面の抽出を開始します...");

        // --- APIリクエストの作成 ---
        // JSONボディの構築
        string jsonBody = "{" +
            "\"model\": \"gpt-4o\"," + // 最新で高性能なモデルを推奨
            "\"messages\": [" +
                "{\"role\": \"system\", \"content\": \"" + EscapeJsonString(SCENE_EXTRACTION_PROMPT) + "\"}," +
                "{\"role\": \"user\", \"content\": \"" + EscapeJsonString(storyContent) + "\"}" +
            "]," +
            "\"temperature\": 0.5" +
        "}";

        UnityWebRequest request = new UnityWebRequest(chatCompletionsEndpoint, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + imageGenApiKey);

        // --- APIリクエストの送信と待機 ---
        yield return request.SendWebRequest();

        // --- 結果の処理 ---
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"場面抽出に失敗しました: {request.error}\n{request.downloadHandler.text}");
            yield break;
        }

        JSONNode jsonResponse = JSON.Parse(request.downloadHandler.text);
        // AIが生成した場面テキストを取得
        string extractedScenesText = jsonResponse["choices"][0]["message"]["content"];

        if (string.IsNullOrEmpty(extractedScenesText))
        {
            Debug.LogError("AIからの応答が空でした。");
            yield break;
        }


        StartCoroutine(GenerateImagesForAllScenes(extractedScenesText));
        // --- 画像生成サービスに結果を渡す ---
    }
    public IEnumerator ExtractScenesAndGenerateImages(string storyContent)
    {
        Debug.Log("物語から場面の抽出を開始します...");

        // --- APIリクエストの作成 ---
        // JSONボディの構築
        string jsonBody = "{" +
            "\"model\": \"gpt-4o\"," + // 最新で高性能なモデルを推奨
            "\"messages\": [" +
                "{\"role\": \"system\", \"content\": \"" + EscapeJsonString(SCENE_EXTRACTION_PROMPT) + "\"}," +
                "{\"role\": \"user\", \"content\": \"" + EscapeJsonString(storyContent) + "\"}" +
            "]," +
            "\"temperature\": 0.5" +
        "}";

        UnityWebRequest request = new UnityWebRequest(chatCompletionsEndpoint, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + imageGenApiKey);

        // --- APIリクエストの送信と待機 ---
        yield return request.SendWebRequest();

        // --- 結果の処理 ---
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"場面抽出に失敗しました: {request.error}\n{request.downloadHandler.text}");
            yield break;
        }

        JSONNode jsonResponse = JSON.Parse(request.downloadHandler.text);
        // AIが生成した場面テキストを取得
        string extractedScenesText = jsonResponse["choices"][0]["message"]["content"];

        if (string.IsNullOrEmpty(extractedScenesText))
        {
            Debug.LogError("AIからの応答が空でした。");
            yield break;
        }


        StartCoroutine(GenerateImagesForAllScenes(extractedScenesText));
        // --- 画像生成サービスに結果を渡す ---
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
                Debug.Log($"✏️ MultiImageGenerator开始处理文件：{Path.GetFileName(slicePath)}");

                yield return StartCoroutine(ExtractScenesAndGenerateImages(content));

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
    public void ToBeContinued()
    {
        isPlayerFinishedSlice = true;
    }
    IEnumerator WaitForPlayerToFinish()
    {
        isPlayerFinishedSlice = false;

        // 等待玩家点击
        while (!isPlayerFinishedSlice)
            yield return null;
    }
    public IEnumerator GenerateImagesForAllScenes(string ParseStory)
    {
        // === 🧹 清空旧的 GeneratedBackgrounds 文件夹 ===
        string fullSaveDirPath = Path.Combine(Application.persistentDataPath, GENERATED_BG_SUBFOLDER);
        if (Directory.Exists(fullSaveDirPath))
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(fullSaveDirPath);
                foreach (FileInfo file in dir.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo subDir in dir.GetDirectories())
                {
                    subDir.Delete(true);
                }
                Debug.Log("旧的 GeneratedBackgrounds 文件夹已清空。");
            }
            catch (System.Exception e)
            {
                Debug.LogError("清空 GeneratedBackgrounds 文件夹失败：" + e.Message);
            }
        }
        else
        {
            Directory.CreateDirectory(fullSaveDirPath);
            Debug.Log("已创建新的 GeneratedBackgrounds 文件夹。");
        }

        // 解析场景文本
        List<string> scenes = ParseScenesFromText(ParseStory);

        if (scenes.Count == 0)
        {
            Debug.LogError("MultiImageGenerationService: 未找到任何场景描述！");
            yield break;
        }

        Debug.Log($"MultiImageGenerationService: 发现 {scenes.Count} 个场景");

        List<string> generatedImageNames = new List<string>();
        List<Texture2D> textureList = new List<Texture2D>();

        for (int i = 0; i < scenes.Count; i++)
        {
            string sceneDescription = scenes[i].Trim();
            if (string.IsNullOrEmpty(sceneDescription))
            {
                Debug.LogWarning($"场景 {i + 1} 为空，跳过");
                continue;
            }

            Debug.Log($"正在生成场景 {i + 1}/{scenes.Count}: {sceneDescription.Substring(0, Mathf.Min(50, sceneDescription.Length))}...");

            string fileName = $"{outputFileNamePrefix}_{i + 1}";
           // string fullSaveDirPath = Path.Combine(Application.persistentDataPath, GENERATED_BG_SUBFOLDER);
            string fullSaveFilePath = Path.Combine(fullSaveDirPath, fileName + ".png");

            // 构建完整的提示语：系统提示 + 场景描述
            string fullPrompt = SYSTEM_PROMPT + "\n\n场景描述：\n" + sceneDescription;

            string jsonBody = "{" +
                              "\"prompt\": \"" + EscapeJsonString(fullPrompt) + "\"," +
                              "\"model\": \"dall-e-3\"," +
                              "\"n\": 1," +
                              "\"size\": \"" + dalleImageSize + "\"," +
                              "\"quality\": \"" + dalleImageQuality + "\"" +
                              "}";

            UnityWebRequest request = new UnityWebRequest(imageGenEndpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + imageGenApiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"场景 {i + 1} 图片生成失败：{request.error}");
                Debug.LogError($"响应内容：{request.downloadHandler.text}");
                continue;
            }

            JSONNode jsonResponse = SimpleJSON.JSON.Parse(request.downloadHandler.text);
            string imageUrl = jsonResponse["data"][0]["url"];

            if (string.IsNullOrEmpty(imageUrl))
            {
                Debug.LogError($"场景 {i + 1} 无法解析图片 URL");
                continue;
            }

            UnityWebRequest imageRequest = UnityWebRequest.Get(imageUrl);
            imageRequest.downloadHandler = new DownloadHandlerBuffer();
            yield return imageRequest.SendWebRequest();

            if (imageRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"场景 {i + 1} 图片下载失败：{imageRequest.error}");
                continue;
            }

            byte[] imageBytes = imageRequest.downloadHandler.data;
            Directory.CreateDirectory(Path.GetDirectoryName(fullSaveFilePath));
            File.WriteAllBytes(fullSaveFilePath, imageBytes);
            generatedImageNames.Add(fileName + ".png");

            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(imageBytes);
            tex.name = fileName;
            textureList.Add(tex);

            // 自动调用替换背景（自动显示）
            SetBackgroundTexture(tex);

            Debug.Log($"场景 {i + 1} 生成完成：{fileName}.png");

            // 给API一些休息时间，避免请求过快
            yield return new WaitForSeconds(3f);
        }

        OnImagesGenerated?.Invoke(generatedImageNames);
        OnTexturesGenerated?.Invoke(textureList);

        Debug.Log("MultiImageGenerationService: 所有场景图片生成完毕！");
        Debug.Log($"成功生成 {generatedImageNames.Count} 张图片");
    }


    /// <summary>
    /// 从文本中解析场景列表
    /// 支持两种格式：
    /// 1. 使用 "---" 分隔符分隔场景
    /// 2. 使用 "场景1:", "场景2:" 等标记分隔场景
    /// </summary>
    private List<string> ParseScenesFromText(string text)
    {
        List<string> scenes = new List<string>();

        if (string.IsNullOrEmpty(text))
            return scenes;

        // 首先尝试使用 "---" 分隔符
        if (text.Contains(SCENE_SEPARATOR))
        {
            string[] parts = text.Split(new string[] { SCENE_SEPARATOR }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    scenes.Add(trimmed);
                }
            }
        }
        // 尝试使用 "场景" 标记分隔
        else if (text.Contains("场景"))
        {
            string[] lines = text.Split('\n');
            string currentScene = "";

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                // 检查是否是场景标题行
                if (trimmedLine.StartsWith("场景") && (trimmedLine.Contains(":") || trimmedLine.Contains("：")))
                {
                    // 保存前一个场景
                    if (!string.IsNullOrEmpty(currentScene))
                    {
                        scenes.Add(currentScene.Trim());
                    }
                    // 开始新场景，去掉场景标题
                    int colonIndex = Mathf.Max(trimmedLine.IndexOf(":"), trimmedLine.IndexOf("："));
                    currentScene = colonIndex >= 0 ? trimmedLine.Substring(colonIndex + 1).Trim() : "";
                }
                else if (!string.IsNullOrEmpty(trimmedLine))
                {
                    // 添加到当前场景描述
                    if (!string.IsNullOrEmpty(currentScene))
                        currentScene += "\n";
                    currentScene += trimmedLine;
                }
            }

            // 添加最后一个场景
            if (!string.IsNullOrEmpty(currentScene))
            {
                scenes.Add(currentScene.Trim());
            }
        }
        // 如果没有特殊分隔符，尝试按行分割（假设每行是一个场景）
        else
        {
            string[] lines = text.Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    scenes.Add(trimmed);
                }
            }
        }

        return scenes;
    }

    // 自动替换背景纹理方法
    public void SetBackgroundTexture(Texture2D tex)
    {
        if (backgroundManager != null && tex != null)
        {
            backgroundManager.SetGeneratedBackground(tex, imageTransitionSpeed, imageSmoothTransition);
            Debug.Log($"MultiImageGenerationService: 替换背景纹理成功 -> {tex.name}");
        }
        else
        {
            Debug.LogError("MultiImageGenerationService: 替换背景失败，backgroundManager 或纹理为空");
        }
    }

    private string EscapeJsonString(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
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
                        sb.Append("\\u" + ((int)c).ToString("X4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 手动生成指定场景的图片
    /// </summary>
    //public void GenerateImageForScene(int sceneIndex)
    //{
    //    if (scenesTextFile == null) return;

    //    List<string> scenes = ParseScenesFromText(scenesTextFile.text);
    //    if (sceneIndex >= 0 && sceneIndex < scenes.Count)
    //    {
    //        StartCoroutine(GenerateSingleSceneImage(scenes[sceneIndex], sceneIndex));
    //    }
    //}

    private IEnumerator GenerateSingleSceneImage(string sceneDescription, int sceneIndex)
    {
        string fileName = $"{outputFileNamePrefix}_{sceneIndex + 1}";
        string fullSaveDirPath = Path.Combine(Application.persistentDataPath, GENERATED_BG_SUBFOLDER);
        string fullSaveFilePath = Path.Combine(fullSaveDirPath, fileName + ".png");

        string fullPrompt = SYSTEM_PROMPT + "\n\n场景描述：\n" + sceneDescription;

        string jsonBody = "{" +
                          "\"prompt\": \"" + EscapeJsonString(fullPrompt) + "\"," +
                          "\"model\": \"dall-e-3\"," +
                          "\"n\": 1," +
                          "\"size\": \"" + dalleImageSize + "\"," +
                          "\"quality\": \"" + dalleImageQuality + "\"" +
                          "}";

        UnityWebRequest request = new UnityWebRequest(imageGenEndpoint, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + imageGenApiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            JSONNode jsonResponse = SimpleJSON.JSON.Parse(request.downloadHandler.text);
            string imageUrl = jsonResponse["data"][0]["url"];

            if (!string.IsNullOrEmpty(imageUrl))
            {
                UnityWebRequest imageRequest = UnityWebRequest.Get(imageUrl);
                imageRequest.downloadHandler = new DownloadHandlerBuffer();
                yield return imageRequest.SendWebRequest();

                if (imageRequest.result == UnityWebRequest.Result.Success)
                {
                    byte[] imageBytes = imageRequest.downloadHandler.data;
                    Directory.CreateDirectory(Path.GetDirectoryName(fullSaveFilePath));
                    File.WriteAllBytes(fullSaveFilePath, imageBytes);

                    Texture2D tex = new Texture2D(2, 2);
                    tex.LoadImage(imageBytes);
                    tex.name = fileName;

                    SetBackgroundTexture(tex);
                    Debug.Log($"单个场景 {sceneIndex + 1} 生成完成：{fileName}.png");
                }
            }
        }
    }
}
 * 
 * 
 */