// Assets/api/MeshyAsyncDemo.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Meshy;
using System.IO; // 引入 System.IO 命名空間
using System.Text; // 引入 System.Text 命名空間

public class MeshyAsyncDemo : MonoBehaviour
{
    [TextArea(3,10)] public string base64OrDataUri = "test.txt";  // 可貼純 base64 或 data:image/png;base64,...
    public string apiKey = "msy_Bl7SYzorFT9dx3gduyYcY0MttpeZUvgBJv79";
    public Transform parent;

    string ResolvePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        // 已經是絕對路徑
        if (Path.IsPathRooted(input) && File.Exists(input)) return input;

        // 專案 Assets 根目錄
        var p = Path.Combine(Application.dataPath, input);
        if (File.Exists(p)) return p;

        // Assets/api/
        p = Path.Combine(Application.dataPath, "api", input);
        if (File.Exists(p)) return p;

        // StreamingAssets/
        p = Path.Combine(Application.streamingAssetsPath, input);
        if (File.Exists(p)) return p;

        // 原字串（也許使用者本來就給了可用的相對路徑）
        return input;
    }

    async void Start()
    {
        try
        {
            string path = ResolvePath(base64OrDataUri);
        if (!File.Exists(path))
        {
            Debug.LogError($"讀不到檔案：{path}");
            return;
        }

        string fileContent = File.ReadAllText(path, Encoding.UTF8);
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(5));

            var go = await MeshyAsync.CreateGlbFromBase64Async(
                apiKey,
                fileContent,
                parent: parent,
                enablePbr: false,
                shouldRemesh: true,
                shouldTexture: false,
                totalTimeoutSec: 300,
                pollIntervalMs: 2000,
                ct: cts.Token
            );

            go.name = "Meshy_GLTFast_Result";
            go.transform.position = Vector3.zero;
            Debug.Log($"[Demo] Loaded: {go.name}");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
        }
    }
}

