using SimpleJSON;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class MultiImageFromOneStory : MonoBehaviour
{
    public static MultiImageFromOneStory instance { get; private set; }

    // Callbacks
    public System.Action<List<string>> OnImagesGenerated;
    public System.Action<List<Texture2D>> OnTexturesGenerated;

    [Header("API Configuration")]
    [SerializeField] private string imageGenApiKey = "YOUR_OPENAI_API_KEY_HERE";
    private string chatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";
    private string imageGenEndpoint = "https://api.openai.com/v1/images/generations";

    [Header("Input Content")]
    [Tooltip("The text file containing the story to process.")]
    public TextAsset rawStoryTextFile;

    [Header("References")]
    public BackgroundManager backgroundManager;

    [Header("Generation Settings")]
    [SerializeField] private string outputFileNamePrefix = "scene";
    [SerializeField] private float imageTransitionSpeed = 0.5f;
    [SerializeField] private bool imageSmoothTransition = true;
    [SerializeField] private string dalleImageSize = "1024x1024";
    [SerializeField] private string dalleImageQuality = "standard";

    // System prompt for image generation
    private const string SYSTEM_PROMPT =
        "You are an expert at generating background art for visual novels. " +
        "Based on the following scene description, generate a detailed, realistic, and cinematic background. " +
        "Aspect ratio should be 16:9. Use a high-quality Japanese anime art style. " +
        "Please add black bars to the top and bottom to create a cinematic 16:9 look within the frame if necessary, but ensure no black bars on the sides.";

    // Prompt for GPT-4o to extract visual scenes from the story
    private const string SCENE_EXTRACTION_PROMPT =
        "You are a professional scenario writer. Read the following story text and extract the 6 most visually important scenes for generating visual novel backgrounds. " +
        "For each scene, concisely describe the location, time, atmosphere, and key visual elements. " +
        "Strictly follow the format below and use line breaks for each scene.\n" +
        "Format:\n" +
        "Scene 1: [Description of Scene 1]\n" +
        "Scene 2: [Description of Scene 2]\n" +
        "Scene 3: [Description of Scene 3]\n" +
        "Scene 4: [Description of Scene 4]\n" +
        "Scene 5: [Description of Scene 5]\n" +
        "Scene 6: [Description of Scene 6]";

    private const string GENERATED_BG_SUBFOLDER = "GeneratedBackgrounds";

    void Awake()
    {
        if (instance != null && instance != this)
            Destroy(gameObject);
        else
            instance = this;
    }

    void Start()
    {
        // Automatically start process if rawStoryTextFile is assigned
        if (rawStoryTextFile != null)
        {
            StartCoroutine(ProcessSingleStoryFile());
        }
        else
        {
            Debug.LogWarning("MultiImageGenerationService: No rawStoryTextFile assigned in the inspector.");
        }
    }

    /// <summary>
    /// Processes the assigned TextAsset: Extracts scenes and then generates images.
    /// </summary>
    public IEnumerator ProcessSingleStoryFile()
    {
        if (rawStoryTextFile == null)
        {
            Debug.LogError("MultiImageGenerationService: rawStoryTextFile is null.");
            yield break;
        }

        string storyContent = rawStoryTextFile.text;
        Debug.Log("MultiImageGenerationService: Starting scene extraction from story file...");

        // 1. Extract scene descriptions using GPT
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
            Debug.LogError($"MultiImageGenerationService: Scene extraction failed: {request.error}\n{request.downloadHandler.text}");
            yield break;
        }

        JSONNode jsonResponse = JSON.Parse(request.downloadHandler.text);
        string extractedScenesText = jsonResponse["choices"][0]["message"]["content"];

        if (string.IsNullOrEmpty(extractedScenesText))
        {
            Debug.LogError("MultiImageGenerationService: AI response for scene extraction was empty.");
            yield break;
        }

        Debug.Log("MultiImageGenerationService: Scenes extracted successfully. Starting DALL-E image generation...");

        // 2. Generate images based on the extracted descriptions
        yield return StartCoroutine(GenerateImagesForAllScenes(extractedScenesText));
    }

    public IEnumerator GenerateImagesForAllScenes(string extractedText)
    {
        string baseSaveDirPath = Path.Combine(Application.persistentDataPath, GENERATED_BG_SUBFOLDER);
        if (!Directory.Exists(baseSaveDirPath)) Directory.CreateDirectory(baseSaveDirPath);

        List<string> scenes = ParseScenesFromText(extractedText);
        if (scenes.Count == 0)
        {
            Debug.LogError("MultiImageGenerationService: No scenes could be parsed from the AI response.");
            yield break;
        }

        List<string> generatedImageNames = new List<string>();
        List<Texture2D> textureList = new List<Texture2D>();

        for (int i = 0; i < scenes.Count; i++)
        {
            string sceneDescription = scenes[i].Trim();
            if (string.IsNullOrEmpty(sceneDescription)) continue;

            string fileName = $"{outputFileNamePrefix}_{i + 1}";
            string fullSaveFilePath = Path.Combine(baseSaveDirPath, fileName + ".png");

            Debug.Log($"MultiImageGenerationService: Generating image {i + 1}/{scenes.Count}...");

            string fullPrompt = SYSTEM_PROMPT + "\n\nScene Description:\n" + sceneDescription;

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
                Debug.LogError($"MultiImageGenerationService: Failed at Scene {i + 1}: {request.error}");
                continue;
            }

            JSONNode jsonResponse = JSON.Parse(request.downloadHandler.text);
            string imageUrl = jsonResponse["data"][0]["url"];
            if (string.IsNullOrEmpty(imageUrl)) continue;

            // Download the image
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

            // Preview background
            SetBackgroundTexture(tex);

            // Wait to avoid hitting rate limits
            yield return new WaitForSeconds(2f);
        }

        OnImagesGenerated?.Invoke(generatedImageNames);
        OnTexturesGenerated?.Invoke(textureList);
        Debug.Log($"MultiImageGenerationService: Finished! Generated {generatedImageNames.Count} images.");
    }

    private List<string> ParseScenesFromText(string text)
    {
        List<string> scenes = new List<string>();
        string[] lines = text.Split('\n');
        string currentScene = "";

        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            // Look for patterns like "Scene 1:" or "Scene 1："
            if (trimmedLine.StartsWith("Scene") && (trimmedLine.Contains(":") || trimmedLine.Contains("：")))
            {
                if (!string.IsNullOrEmpty(currentScene)) scenes.Add(currentScene.Trim());
                int colonIndex = Mathf.Max(trimmedLine.IndexOf(":"), trimmedLine.IndexOf("："));
                currentScene = colonIndex >= 0 ? trimmedLine.Substring(colonIndex + 1).Trim() : "";
            }
            else if (!string.IsNullOrEmpty(trimmedLine))
            {
                if (!string.IsNullOrEmpty(currentScene)) currentScene += " ";
                currentScene += trimmedLine;
            }
        }
        if (!string.IsNullOrEmpty(currentScene)) scenes.Add(currentScene.Trim());

        return scenes;
    }

    private void SetBackgroundTexture(Texture2D tex)
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