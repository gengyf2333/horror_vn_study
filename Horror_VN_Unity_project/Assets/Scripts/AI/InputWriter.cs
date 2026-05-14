using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;

public class InputWriter : MonoBehaviour
{
    [Header("UI 组件")]
    public Button writeButton;
    public TMP_InputField inputField;
    public TextAsset defaultFile;
    public AIStoryBrancher AIStoryBrancher;
    public MultiImageGenerationService multiImageGeneration;

    private string saveFilePath;

    void Start()
    {
        saveFilePath = Path.Combine(Application.persistentDataPath, defaultFile.name + ".txt");

        // 如果 persistentDataPath 下文件不存在，先创建默认内容
        if (!File.Exists(saveFilePath) && defaultFile != null)
        {
            File.WriteAllText(saveFilePath, defaultFile.text);
        }

        // 初始化 InputField
        inputField.text = File.ReadAllText(saveFilePath);

        // 绑定按钮事件
        writeButton.onClick.AddListener(OnWriteButtonClicked);
    }

    void OnWriteButtonClicked()
    {
        string userInput = inputField.text;

        // 写入临时文件（保证 AIStoryBrancher 可以读取）
        string tempFilePath = Path.Combine(Application.persistentDataPath, "tempStory.txt");
        File.WriteAllText(tempFilePath, userInput);

        // 创建临时 TextAsset
        TextAsset tempText = new TextAsset(File.ReadAllText(tempFilePath));

        // 调用 AIStoryBrancher 的方法挂载
        if (AIStoryBrancher != null)
        {
            AIStoryBrancher.SetStoryText(tempText);

            
        }

        // 如果 multiImageGeneration 也需要
        if (multiImageGeneration != null)
        {
            multiImageGeneration.SetStoryText(tempText);
        }

        Debug.Log("玩家输入内容已生成临时 TextAsset 并挂载！");
    }
}
