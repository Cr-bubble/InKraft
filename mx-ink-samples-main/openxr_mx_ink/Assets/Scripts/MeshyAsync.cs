// Assets/api/MeshyAsync.cs
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;   // GraphicsSettings.currentRenderPipeline
// glTFast
using GLTFast;

namespace Meshy
{
    public static class MeshyAsync
    {
        // ---------- DTO ----------
        [Serializable] class Payload
        {
            public string ai_model;
            public string image_url;
            public bool enable_pbr;
            public bool should_remesh;
            public bool should_texture;
        }

        [Serializable] class TaskError { public string message; }
        [Serializable] class ModelUrls { public string glb; public string fbx; public string obj; public string usdz; }

        // POST 通常回 { "result": "<task id>" }，偶爾可能含 model_urls
        [Serializable] class CreateResp
        {
            public string result;     // 主要的 task id
            public string id;         // 有些情況回 id
            public string status;
            public ModelUrls model_urls;
            public TaskError task_error;
        }

        [Serializable] class TaskResp
        {
            public string id;
            public string status;     // PENDING / IN_PROGRESS / SUCCEEDED / FAILED
            public float progress;
            public ModelUrls model_urls;
            public TaskError task_error;
        }

        // ---------- Public: 一次包到底 ----------
        /// <summary>
        /// 傳入 base64（或 data URI），呼叫 Meshy 產生 3D，成功後用 glTFast 載入，
        /// 並（預設）自動加上 Collider + Rigidbody（useGravity=true），回傳根 GameObject。
        /// </summary>
        public static async Task<GameObject> CreateGlbFromBase64Async(
            string apiKey,
            string base64OrDataUri,
            Transform parent          = null,
            bool enablePbr            = true,
            bool shouldRemesh         = true,
            bool shouldTexture        = true,                     // Meshy 規則：enablePbr 依附在貼圖流程上
            string ai_model           = "latest",
            string baseUrl            = "https://api.meshy.ai/openapi/v1",
            int    requestTimeoutSec  = 120,
            int    totalTimeoutSec    = 300,
            int    pollIntervalMs     = 2000,
            CancellationToken ct      = default,
            // 物理設定（方案 B）
            bool addPhysics           = true,
            bool addColliders         = true,
            bool convex               = true,
            float mass                = 1f
        )
        {
            Debug.Log("Starting MeshyAsync.CreateGlbFromBase64Async...");
            if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("apiKey is empty.");
            if (string.IsNullOrEmpty(base64OrDataUri)) throw new ArgumentException("base64 is empty.");

            string dataUri = MakeDataUri(base64OrDataUri);
            if (string.IsNullOrEmpty(dataUri)) throw new Exception("Invalid base64 / data URI.");

            // 規則：enable_pbr 只有在 should_texture=true 時有效
            bool finalShouldTexture = shouldTexture || enablePbr;

            // 1) 建任務
            var createUrl = $"{baseUrl}/image-to-3d";
            var payload = new Payload {
                ai_model       = ai_model,
                image_url      = dataUri,
                enable_pbr     = enablePbr,
                should_remesh  = shouldRemesh,
                should_texture = finalShouldTexture
            };
            string createJson = JsonUtility.ToJson(payload);
            string createRaw  = await PostJsonAsync(createUrl, apiKey, createJson, requestTimeoutSec, ct);
            var    create     = SafeFromJson<CreateResp>(createRaw, "CreateResp");

            // 2) 可能直接拿到 glb，否則以 task id 輪詢
            string glbUrl = create?.model_urls?.glb;
            string taskId = !string.IsNullOrEmpty(create?.id) ? create.id : create?.result;

            if (string.IsNullOrEmpty(glbUrl))
            {
                if (string.IsNullOrEmpty(taskId))
                    throw new Exception($"Meshy create response missing both glbUrl and id.\n{createRaw}");

                glbUrl = await PollForGlbUrlAsync(
                    apiKey, taskId, baseUrl,
                    totalTimeoutSec, pollIntervalMs, requestTimeoutSec, ct
                );
            }

            if (string.IsNullOrEmpty(glbUrl))
                throw new Exception("GLB url not found.");

            // 3) 用 glTFast 匯入（方案 B：自動加物理）
            return await LoadGlbWithGltfFastAsync(
                glbUrl, parent, ct,
                addPhysics: addPhysics,
                addColliders: addColliders,
                convex: convex,
                mass: mass
            );
        }

        /// <summary>
        /// 已有 task id（例如先 POST 得到的 result），輪詢直到拿到 GLB，匯入並回傳根 GameObject（同樣自動加物理）。
        /// </summary>
        public static async Task<GameObject> CreateGlbFromTaskIdAsync(
            string apiKey,
            string taskId,
            Transform parent          = null,
            string baseUrl            = "https://api.meshy.ai/openapi/v1",
            int    requestTimeoutSec  = 60,
            int    totalTimeoutSec    = 300,
            int    pollIntervalMs     = 2000,
            CancellationToken ct      = default,
            // 物理設定（方案 B）
            bool addPhysics           = true,
            bool addColliders         = true,
            bool convex               = true,
            float mass                = 1f
        )
        {
            if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("apiKey is empty.");
            if (string.IsNullOrEmpty(taskId)) throw new ArgumentException("taskId is empty.");

            string glbUrl = await PollForGlbUrlAsync(
                apiKey, taskId, baseUrl,
                totalTimeoutSec, pollIntervalMs, requestTimeoutSec, ct
            );

            if (string.IsNullOrEmpty(glbUrl))
                throw new Exception($"Task {taskId} has no GLB url (maybe failed or expired).");

            return await LoadGlbWithGltfFastAsync(
                glbUrl, parent, ct,
                addPhysics: addPhysics,
                addColliders: addColliders,
                convex: convex,
                mass: mass
            );
        }

        /// <summary>
        /// 從文字檔讀入 base64 或 dataURI，然後走 CreateGlbFromBase64Async。
        /// </summary>
        public static async Task<GameObject> CreateGlbFromFileAsync(
            string apiKey,
            string textFilePath,
            Transform parent          = null,
            bool enablePbr            = true,
            bool shouldRemesh         = true,
            bool shouldTexture        = true,
            string ai_model           = "latest",
            string baseUrl            = "https://api.meshy.ai/openapi/v1",
            int    requestTimeoutSec  = 120,
            int    totalTimeoutSec    = 300,
            int    pollIntervalMs     = 2000,
            CancellationToken ct      = default,
            // 物理設定（方案 B）
            bool addPhysics           = true,
            bool addColliders         = true,
            bool convex               = true,
            float mass                = 1f
        )
        {
            if (string.IsNullOrEmpty(textFilePath)) throw new ArgumentException("textFilePath is empty.");
            string content = File.ReadAllText(textFilePath, Encoding.UTF8);
            return await CreateGlbFromBase64Async(
                apiKey, content, parent,
                enablePbr, shouldRemesh, shouldTexture,
                ai_model, baseUrl, requestTimeoutSec, totalTimeoutSec, pollIntervalMs, ct,
                addPhysics, addColliders, convex, mass
            );
        }

        // ---------- Loader + Physics (方案 B) ----------
        /// <summary>
        /// 用 glTFast 載入 GLB，並（可選）自動加 Collider / Rigidbody。
        /// </summary>
        static async Task<GameObject> LoadGlbWithGltfFastAsync(
            string glbUrl,
            Transform parent,
            CancellationToken ct,
            bool addPhysics   = true,
            bool addColliders = true,
            bool convex       = true,
            float mass        = 1f
        )
        {
            var import = new GltfImport();

            // 1) 載入 GLB
            bool ok = await import.Load(glbUrl, cancellationToken: ct);
            if (!ok) throw new Exception($"glTFast Load() failed for URL:\n{glbUrl}");

            // 2) 建容器，將匯入節點掛在底下
            var container = new GameObject("Meshy_GLTFast_Container");
            if (parent != null) container.transform.SetParent(parent, false);

            // 3) 同步實例化主場景
            bool instOk = import.InstantiateMainScene(container.transform);
            if (!instOk)
            {
                UnityEngine.Object.Destroy(container);
                throw new Exception("glTFast InstantiateMainScene() failed.");
            }

            // 4) 物理補件
            if (addPhysics)
            {
                if (addColliders)
                    AddCollidersRecursive(container, convex);

                var rb = container.GetComponent<Rigidbody>();
                if (!rb) rb = container.AddComponent<Rigidbody>();

                rb.useGravity  = true;
                rb.isKinematic = false;
                rb.mass        = Mathf.Max(0.0001f, mass);
                // rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 視需求開
            }

            return container;
        }

        /// <summary>
        /// 遞迴為含網格的子物件加上 Collider。
        /// 優先 MeshCollider（可選 convex），沒有 mesh 時退回 BoxCollider。
        /// SkinnedMeshRenderer 退回 BoxCollider（runtime 產生 skinned mesh 成本高）。
        /// </summary>
        static void AddCollidersRecursive(GameObject root, bool convex)
        {
            // MeshFilter → MeshCollider
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject;

                if (go.GetComponent<Collider>() != null) continue;

                var mesh = mf.sharedMesh;
                if (mesh != null)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;

                    // 注意：Convex 對多邊形數量有限制，過度複雜會影響效能或失敗
                    mc.convex = convex;
                }
                else
                {
                    go.AddComponent<BoxCollider>();
                }
            }

            // SkinnedMeshRenderer → BoxCollider（簡化）
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var go = smr.gameObject;
                if (go.GetComponent<Collider>() == null)
                {
                    go.AddComponent<BoxCollider>();
                }
            }
        }

        // ---------- Helpers ----------
        public static string MakeDataUri(string input, string defaultMime = "image/png")
        {
            if (string.IsNullOrEmpty(input)) return null;

            string s = input.Trim().Trim('"', '\'');
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);

            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return s.Replace("\n", "").Replace("\r", "");

            s = s.Replace("\n", "").Replace("\r", "").Replace(" ", "");

            byte[] bytes;
            try { bytes = Convert.FromBase64String(s); }
            catch { return null; }

            string mime = defaultMime;
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) mime = "image/png";
            else if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) mime = "image/jpeg";

            return $"data:{mime};base64,{s}";
        }

        static T SafeFromJson<T>(string json, string tag) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception ex) { throw new Exception($"{tag} JSON parse error: {ex.Message}\n{json}"); }
        }

        static async Task<string> PollForGlbUrlAsync(
            string apiKey,
            string taskId,
            string baseUrl,
            int totalTimeoutSec,
            int pollIntervalMs,
            int requestTimeoutSec,
            CancellationToken ct)
        {
            float deadline = Time.realtimeSinceStartup + totalTimeoutSec;
            string url = $"{baseUrl}/image-to-3d/{UnityWebRequest.EscapeURL(taskId)}";

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                string raw = await GetAsync(url, apiKey, requestTimeoutSec, ct);
                var task = SafeFromJson<TaskResp>(raw, "TaskResp");

                if (string.Equals(task.status, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                    return task.model_urls?.glb;

                if (string.Equals(task.status, "FAILED", StringComparison.OrdinalIgnoreCase))
                    throw new Exception(task.task_error?.message ?? "Task failed.");

                if (Time.realtimeSinceStartup > deadline)
                    throw new TimeoutException("Polling timed out.");

                Debug.Log($"[Meshy] progress: {task.progress * 100f:0.0}% - status: {task.status}");
                await Task.Delay(pollIntervalMs, ct);
            }
        }

        // ---------- UnityWebRequest as Task ----------
        static async Task<string> PostJsonAsync(string url, string apiKey, string json, int timeoutSec, CancellationToken ct)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = timeoutSec;

            return await SendAsync(req, ct);
        }

        static async Task<string> GetAsync(string url, string apiKey, int timeoutSec, CancellationToken ct)
        {
            var req = UnityWebRequest.Get(url);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            req.timeout = timeoutSec;

            return await SendAsync(req, ct);
        }

        static Task<string> SendAsync(UnityWebRequest req, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();

            // 取消支持
            ct.Register(() =>
            {
                try { req.Abort(); } catch { /* ignore */ }
            });

            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
#if UNITY_2020_2_OR_NEWER
                    bool ok = req.result == UnityWebRequest.Result.Success;
#else
                    bool ok = !(req.isNetworkError || req.isHttpError);
#endif
                    if (!ok)
                        tcs.TrySetException(new Exception($"HTTP {req.responseCode}: {req.error}\n{req.downloadHandler?.text}"));
                    else
                        tcs.TrySetResult(req.downloadHandler?.text ?? "");
                }
                finally { req.Dispose(); }
            };
            return tcs.Task;
        }
    }
}