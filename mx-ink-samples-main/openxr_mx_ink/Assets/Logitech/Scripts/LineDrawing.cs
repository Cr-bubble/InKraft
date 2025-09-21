using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Meshy; // 你已引用 Meshy SDK

public class LineDrawing : MonoBehaviour
{
    public string apiKey = "msy_Bl7SYzorFT9dx3gduyYcY0MttpeZUvgBJv79";
    public Transform parent;

    private readonly List<GameObject> _lines = new List<GameObject>();
    private LineRenderer _currentLine;
    private List<float> _currentLineWidths = new List<float>();

    [SerializeField] private float _maxLineWidth = 0.01f;
    [SerializeField] private float _minLineWidth = 0.0005f;

    [SerializeField] private Material _material;

    [SerializeField] private Color _currentColor = Color.red;
    [SerializeField] private Color highlightColor = Color.yellow;
    [SerializeField] private float highlightThreshold = 0.01f;

    public Transform xrCamera;

    private Color _cachedColor;
    private GameObject _highlightedLine;
    private Vector3 _grabStartPosition;
    private Quaternion _grabStartRotation;
    private Vector3[] _originalLinePositions;
    private bool _movingLine = false;

    public Color CurrentColor
    {
        get => _currentColor;
        set
        {
            _currentColor = value;
            Debug.Log("LineDrawing color: " + _currentColor);
        }
    }

    public float MaxLineWidth
    {
        get => _maxLineWidth;
        set => _maxLineWidth = value;
    }

    private bool _lineWidthIsFixed = false;
    public bool LineWidthIsFixed
    {
        get => _lineWidthIsFixed;
        set => _lineWidthIsFixed = value;
    }

    private bool _isDrawing = false;
    private bool _doubleTapDetected = false;

    [SerializeField] private float longPressDuration = 1.0f;
    private float buttonPressedTimestamp = 0;

    [SerializeField] private StylusHandler _stylusHandler;

    private Vector3 _previousLinePoint;
    private const float _minDistanceBetweenLinePoints = 0.0005f;

    // ---------- Drawing ----------
    private void StartNewLine()
    {
        var go = new GameObject("line");
        var lr = go.AddComponent<LineRenderer>();
        _currentLine = lr;

        // Material：若沒指定，使用 Unlit/Color 最穩
        if (_material == null)
        {
            _material = new Material(Shader.Find("Unlit/Color"));
            _material.color = _currentColor;
        }

        _currentLine.material = _material;
        _currentLine.material.color = _currentColor;
        _currentLine.positionCount = 0;
        _currentLine.loop = false;
        _currentLine.startWidth = _minLineWidth;
        _currentLine.endWidth = _minLineWidth;
        _currentLine.useWorldSpace = true;
        _currentLine.alignment = LineAlignment.View;
        _currentLine.widthCurve = new AnimationCurve();
        _currentLine.shadowCastingMode = ShadowCastingMode.Off;
        _currentLine.receiveShadows = false;

        // Layer：LineDrawing
        int lineLayer = LayerMask.NameToLayer("LineDrawing");
        if (lineLayer == -1)
        {
            Debug.LogWarning("[LineDrawing] Layer 'LineDrawing' not found; using Default layer.");
            lineLayer = LayerMask.NameToLayer("Default");
        }
        go.layer = lineLayer;

        _currentLineWidths = new List<float>();
        _lines.Add(go);
        _previousLinePoint = Vector3.zero;
    }

    private void AddPoint(Vector3 position, float width)
    {
        if (Vector3.Distance(position, _previousLinePoint) <= _minDistanceBetweenLinePoints) return;

        TriggerHaptics();
        _previousLinePoint = position;
        _currentLine.positionCount++;
        _currentLine.SetPosition(_currentLine.positionCount - 1, position);

        float w = Mathf.Max(width * _maxLineWidth, _minLineWidth);
        _currentLineWidths.Add(w);

        // 重建寬度曲線（避免除以 0）
        var curve = new AnimationCurve();
        int n = _currentLineWidths.Count;
        if (n == 1)
        {
            curve.AddKey(0f, _currentLineWidths[0]);
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                curve.AddKey(t, _currentLineWidths[i]);
            }
        }
        _currentLine.widthCurve = curve;
    }

    private void RemoveLastLine()
    {
        if (_lines.Count == 0) return;
        GameObject lastLine = _lines[_lines.Count - 1];
        _lines.RemoveAt(_lines.Count - 1);
        Destroy(lastLine);
    }

    // ---------- Projection to 2D plane in front of user ----------
    private void to2D()
    {
        float projectionDistance = 1.0f;
        Vector3 planeOrigin = xrCamera.position + xrCamera.forward * projectionDistance;
        Vector3 planeNormal = xrCamera.forward;

        foreach (var line in _lines)
        {
            var lr = line.GetComponent<LineRenderer>();
            int count = lr.positionCount;
            if (count == 0) continue;
            Vector3[] positions = new Vector3[count];
            lr.GetPositions(positions);

            for (int i = 0; i < count; i++)
            {
                Vector3 toPoint = positions[i] - planeOrigin;
                float d = Vector3.Dot(toPoint, planeNormal);
                positions[i] = positions[i] - d * planeNormal;
            }
            lr.SetPositions(positions);
        }
    }

    private void ClearAllLines()
    {
        foreach (var line in _lines) Destroy(line);
        _lines.Clear();
        _highlightedLine = null;
        _movingLine = false;
    }

    // ---------- Camera & Capture ----------
    private Camera CreateRenderCamera(Vector3 position, Quaternion rotation, float fov = 60f)
    {
        var camObj = new GameObject("RenderCamera");
        var renderCam = camObj.AddComponent<Camera>();

        renderCam.clearFlags = CameraClearFlags.SolidColor;
        renderCam.backgroundColor = Color.white; // 白底
        renderCam.orthographic = false;
        renderCam.fieldOfView = fov;
        renderCam.nearClipPlane = 0.01f;
        renderCam.farClipPlane = 10f;

        int mask = LayerMask.GetMask("LineDrawing");
        if (mask == 0)
        {
            Debug.LogWarning("[LineDrawing] LayerMask 'LineDrawing' not found; using Default.");
            mask = LayerMask.GetMask("Default");
        }
        renderCam.cullingMask = mask;

        renderCam.transform.SetPositionAndRotation(position, rotation);

        // 在 SRP/URP 下需要啟用它，才能在該幀被渲染
        renderCam.enabled = true;
        return renderCam;
    }

    private System.Collections.IEnumerator CaptureCameraCoroutine(Camera cam, int width, int height, Action<Texture2D> onDone)
    {
        var rt = new RenderTexture(width, height, 24);
        cam.targetTexture = rt;

        bool usingSRP = GraphicsSettings.currentRenderPipeline != null;
        if (usingSRP)
        {
            // 讓 URP/HDRP 在本幀把 camera 渲染進 targetTexture
            yield return new WaitForEndOfFrame();
        }
        else
        {
            // Built-in：手動渲染
            RenderTexture.active = rt;
            GL.Clear(true, true, cam.backgroundColor);
            cam.Render();
        }

        // 讀像素
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        RenderTexture.active = prev;
        cam.targetTexture = null;
        Destroy(rt);

        onDone?.Invoke(tex);
    }

    // 封裝成 Task，供 async/await 使用
    private Task<Texture2D> CaptureCameraAsync(Camera cam, int width, int height)
    {
        var tcs = new TaskCompletionSource<Texture2D>();
        StartCoroutine(CaptureCameraCoroutine(cam, width, height, tex => tcs.SetResult(tex)));
        return tcs.Task;
    }

    public async Task<string> ExportLineDrawingToBase64Async()
    {
        // 需要平面化就保留
        to2D();

        Vector3 camPos = xrCamera.position - xrCamera.forward * 1.0f + Vector3.up * 0.1f;
        Quaternion camRot = Quaternion.LookRotation(xrCamera.forward, Vector3.up);

        var cam = CreateRenderCamera(camPos, camRot);

        Texture2D snapshot = await CaptureCameraAsync(cam, 1024, 1024);
        byte[] bytes = snapshot.EncodeToPNG();
        string base64 = Convert.ToBase64String(bytes);

        Destroy(snapshot);
        Destroy(cam.gameObject);

        return base64;
    }

    // ---------- Haptics ----------
    private void TriggerHaptics()
    {
        const float dampingFactor = 0.6f;
        const float duration = 0.01f;
        float middleButtonPressure = _stylusHandler.CurrentState.cluster_middle_value * dampingFactor;
        ((VrStylusHandler)_stylusHandler).TriggerHapticPulse(middleButtonPressure, duration);
    }

    // ---------- Highlight & Move ----------
    private GameObject FindClosestLine(Vector3 position)
    {
        GameObject closestLine = null;
        float closestDistance = float.MaxValue;

        foreach (var line in _lines)
        {
            var lr = line.GetComponent<LineRenderer>();
            int pc = lr.positionCount;
            if (pc < 2) continue;

            for (int i = 0; i < pc - 1; i++)
            {
                var p = FindNearestPointOnLineSegment(lr.GetPosition(i), lr.GetPosition(i + 1), position);
                float dist = Vector3.Distance(p, position);
                if (dist < closestDistance && dist < highlightThreshold)
                {
                    closestDistance = dist;
                    closestLine = line;
                }
            }
        }
        return closestLine;
    }

    private Vector3 FindNearestPointOnLineSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        var ab = b - a;
        float len = ab.magnitude;
        if (len <= float.Epsilon) return a;
        var dir = ab / len;
        float t = Mathf.Clamp(Vector3.Dot(p - a, dir), 0f, len);
        return a + dir * t;
    }

    private void HighlightLine(GameObject line)
    {
        _highlightedLine = line;
        var lr = line.GetComponent<LineRenderer>();
        _cachedColor = lr.material.color;
        lr.material.color = highlightColor;
        ((VrStylusHandler)_stylusHandler).TriggerHapticClick();
    }

    private void UnhighlightLine(GameObject line)
    {
        var lr = line.GetComponent<LineRenderer>();
        lr.material.color = _cachedColor;
        _highlightedLine = null;
        ((VrStylusHandler)_stylusHandler).TriggerHapticClick();
    }

    private void StartGrabbingLine()
    {
        if (!_highlightedLine) return;
        _grabStartPosition = _stylusHandler.CurrentState.inkingPose.position;
        _grabStartRotation = _stylusHandler.CurrentState.inkingPose.rotation;

        var lr = _highlightedLine.GetComponent<LineRenderer>();
        _originalLinePositions = new Vector3[lr.positionCount];
        lr.GetPositions(_originalLinePositions);
        ((VrStylusHandler)_stylusHandler).TriggerHapticPulse(1.0f, 0.03f);
    }

    private void MoveHighlightedLine()
    {
        if (!_highlightedLine) return;
        var rotation = _stylusHandler.CurrentState.inkingPose.rotation * Quaternion.Inverse(_grabStartRotation);
        var lr = _highlightedLine.GetComponent<LineRenderer>();

        var newPositions = new Vector3[_originalLinePositions.Length];
        for (int i = 0; i < _originalLinePositions.Length; i++)
        {
            newPositions[i] = rotation * (_originalLinePositions[i] - _grabStartPosition) + _stylusHandler.CurrentState.inkingPose.position;
        }
        lr.SetPositions(newPositions);
    }

    // ---------- Update ----------
    private async void Update()
    {
        var state = _stylusHandler.CurrentState;
        float analogInput = Mathf.Max(state.tip_value, state.cluster_middle_value);

        if (analogInput > 0 && _stylusHandler.CanDraw())
        {
            if (_highlightedLine)
            {
                UnhighlightLine(_highlightedLine);
                _movingLine = false;
            }

            if (!_isDrawing)
            {
                StartNewLine();
                _isDrawing = true;
            }
            AddPoint(state.inkingPose.position, _lineWidthIsFixed ? 1.0f : analogInput);
            return;
        }
        else
        {
            _isDrawing = false;
        }

        if (!_movingLine)
        {
            var closest = FindClosestLine(state.inkingPose.position);
            if (closest)
            {
                if (_highlightedLine != closest)
                {
                    if (_highlightedLine) UnhighlightLine(_highlightedLine);
                    HighlightLine(closest);
                    return;
                }
            }
            else if (_highlightedLine)
            {
                UnhighlightLine(_highlightedLine);
                return;
            }
        }

        // 觸發匯出 + Meshy
        if (state.cluster_front_value && !_movingLine)
        {
            // 投影並匯出
            string base64 = await ExportLineDrawingToBase64Async();
            Debug.Log($"Exported Base64 Length: {base64.Length}");

            // 清線（若你不想清，可移除）
            // ClearAllLines();

            // 丟給 Meshy
            var go = await MeshyAsync.CreateGlbFromBase64Async(
                apiKey,
                base64,
                parent: parent,
                enablePbr: false,
                shouldRemesh: true,
                shouldTexture: false,
                totalTimeoutSec: 300,
                pollIntervalMs: 2000
            );

            go.name = "Meshy_GLTFast_Result";
            go.transform.position = new Vector3(0f, 1f, 0f);
            Debug.Log($"[Demo] Loaded: {go.name}");

            ClearAllLines();
        }
    }
}