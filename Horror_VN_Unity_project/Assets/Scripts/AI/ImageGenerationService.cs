using SimpleJSON;
using System.Collections;
using System.IO; // For file operations
using System.Text; // For StringBuilder and Encoding
using UnityEngine;
using UnityEngine.Networking; // For UnityWebRequest

public class ImageGenerationService : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static ImageGenerationService instance { get; private set; }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            instance = this;
            // DontDestroyOnLoad(gameObject); // If this service needs to persist across scenes, uncomment
        }
    }

    // --- API Configuration ---
    [SerializeField] private string imageGenApiKey = "YOUR_OPENAI_API_KEY_HERE";
    // OpenAI DALL-E Image Generation Endpoint
    private string imageGenEndpoint = "https://api.openai.com/v1/images/generations";

    // --- Image Generation Prompt Source File ---
    public TextAsset imagePromptFile;

    // --- File Storage Directory ---
    private const string GENERATED_BG_SUBFOLDER = "GeneratedBackgrounds";

    // --- Background Manager Reference ---
    public BackgroundManager backgroundManager;

    // --- Image Generation Parameters ---
    [SerializeField] private string imageFileName = "generated_default_bg";
    [SerializeField] private float imageTransitionSpeed = 0.5f;
    [SerializeField] private bool imageSmoothTransition = true;
    [SerializeField] private string dalleImageSize = "1024x1024"; // DALL-E sizes: "1024x1024", "1792x1024", "1024x1792"
    [SerializeField] private string dalleImageQuality = "standard"; // "standard" or "hd"

    // --- Start Method: Begins generation immediately upon activation ---
    void Start()
    {
        // 1. Basic Checks
        if (backgroundManager == null)
        {
            Debug.LogError("ImageGenerationService: BackgroundManager is not assigned! Cannot set background.");
            return;
        }
        if (imagePromptFile == null)
        {
            Debug.LogError("ImageGenerationService: Image Prompt file (TextAsset) is not assigned! Cannot generate image.");
            return;
        }

        // 2. Start Coroutine: Auto-generate and set background
        StartCoroutine(GenerateAndSetBackgroundRoutine());
    }

    // --- Private Coroutine: Generates image and applies it as background ---
    private IEnumerator GenerateAndSetBackgroundRoutine()
    {
        // 1. Key and Input Validation
        if (string.IsNullOrEmpty(imageGenApiKey) || imageGenApiKey == "YOUR_OPENAI_API_KEY_HERE")
        {
            Debug.LogError("ImageGenerationService: OpenAI API Key is missing or set to default!");
            yield break;
        }

        // --- Translated System Prompt for DALL-E ---
        string ImagePrompt = "You are an expert at generating background art for visual novels. " +
                             "Based on the following description, select one scene and generate a detailed, realistic, and cinematic visual novel background. " +
                             "Do not include any characters. Aspect ratio should be 16:9. Use a high-quality Japanese anime art style. " +
                             "Please add black bars to the top and bottom to create a cinematic 16:9 look within the frame if necessary, but ensure no black bars on the sides. " +
                             "\n\nDescription:\n";

        ImagePrompt = ImagePrompt + imagePromptFile.text.Trim();

        if (string.IsNullOrEmpty(imagePromptFile.text.Trim()))
        {
            Debug.LogError("ImageGenerationService: Image Prompt file content is empty! Cannot generate image.");
            yield break;
        }

        Debug.Log($"ImageGenerationService: Generating image '{imageFileName}' using OpenAI DALL-E for prompt: '{ImagePrompt}'...");

        // 2. Construct File Path and Directory
        string fullSaveDirPath = Path.Combine(Application.persistentDataPath, GENERATED_BG_SUBFOLDER);
        string fullSaveFilePath = Path.Combine(fullSaveDirPath, imageFileName + ".png");

        if (!Directory.Exists(fullSaveDirPath))
        {
            Directory.CreateDirectory(fullSaveDirPath);
        }

        // --- Build OpenAI DALL-E API Request JSON ---
        string jsonBody = "{" +
                          "\"prompt\": \"" + EscapeJsonString(ImagePrompt) + "\"," +
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

        // 3. Send API Request
        yield return request.SendWebRequest();

        // 4. Handle API Response
        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            string errorDetails = $"OpenAI DALL-E Generation Failed: {request.error}. HTTP Status: {request.responseCode}. Response: {request.downloadHandler?.text}";
            Debug.LogError($"ImageGenerationService: {errorDetails}");
        }
        else
        {
            // Parse DALL-E response to get the Image URL
            string rawResponse = request.downloadHandler.text;
            Debug.Log($"ImageGenerationService: DALL-E Raw Response: {rawResponse}");
            string imageUrl = null;
            try
            {
                // DALL-E response format: {"data": [{"url": "..."}]}
                JSONNode jsonResponse = SimpleJSON.JSON.Parse(rawResponse);
                imageUrl = jsonResponse["data"][0]["url"].Value;
                Debug.Log($"ImageGenerationService: Generated Image URL: {imageUrl}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ImageGenerationService: Failed to parse DALL-E response: {e.Message}. Raw: {rawResponse}");
            }

            if (!string.IsNullOrEmpty(imageUrl))
            {
                // 5. Download the generated image from the provided URL
                yield return DownloadImageFromUrl(imageUrl, fullSaveFilePath, imageFileName);
            }
            else
            {
                Debug.LogError("ImageGenerationService: Failed to retrieve Image URL from DALL-E response.");
            }
        }
    }

    // --- Download image from URL and set as background ---
    private IEnumerator DownloadImageFromUrl(string imageUrl, string saveFilePath, string fileName)
    {
        Debug.Log($"ImageGenerationService: Downloading image from URL: {imageUrl}");
        UnityWebRequest imageDownloadRequest = UnityWebRequest.Get(imageUrl);
        imageDownloadRequest.downloadHandler = new DownloadHandlerBuffer();
        yield return imageDownloadRequest.SendWebRequest();

        if (imageDownloadRequest.result == UnityWebRequest.Result.ConnectionError || imageDownloadRequest.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"ImageGenerationService: Image download failed: {imageDownloadRequest.error}");
        }
        else
        {
            byte[] imageBytes = imageDownloadRequest.downloadHandler.data;
            try
            {
                // 6. Save image locally
                Directory.CreateDirectory(Path.GetDirectoryName(saveFilePath));
                File.WriteAllBytes(saveFilePath, imageBytes);
                Debug.Log($"ImageGenerationService: Image saved to: {saveFilePath}");

                // 7. Load image as Texture2D
                Texture2D generatedTexture = new Texture2D(2, 2);
                generatedTexture.LoadImage(imageBytes);
                generatedTexture.name = fileName;

                // 8. Apply to BackgroundManager
                if (backgroundManager != null)
                {
                    backgroundManager.SetGeneratedBackground(generatedTexture, imageTransitionSpeed, imageSmoothTransition);
                    Debug.Log($"ImageGenerationService: Successfully set '{fileName}' as background.");
                }
                else
                {
                    Debug.LogError("ImageGenerationService: BackgroundManager is missing! Cannot apply background.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ImageGenerationService: Failed to save/load image: {e.Message}");
            }
        }
    }

    // --- Helper Method: Escape JSON String ---
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