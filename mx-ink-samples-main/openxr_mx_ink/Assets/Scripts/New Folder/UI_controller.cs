using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_controller : MonoBehaviour
{
    [Header("Refs")]
    public StylusHandler stylus;
    public Transform stylusTip;
    public Camera uiCamera;
    public GraphicRaycaster raycaster;
    public EventSystem eventSystem;

    private readonly List<RaycastResult> _results = new();
    private GameObject _hovered;
    private GameObject _pressedTarget;    // 接到 PointerDown 的物件（可能是子物件）
    private GameObject _dragTarget;       // 會吃 Drag 的最終目標（例如 Slider）
    private bool _lastPressed;

    // 拖曳所需：持續性的 PointerEventData 與上次座標
    private PointerEventData _pressData;
    private Vector2 _lastScreenPos;

    void Reset() {
        if (!uiCamera) uiCamera = Camera.main;
        if (!eventSystem) eventSystem = FindObjectOfType<EventSystem>();
        if (!raycaster) raycaster = FindObjectOfType<GraphicRaycaster>();
    }

    void Update()
    {
        if (!stylus || !stylusTip || !uiCamera || !eventSystem || !raycaster) return;

        // 1) 筆尖 -> 螢幕座標
        Vector2 screenPos = uiCamera.WorldToScreenPoint(stylusTip.position);

        // 2) UI Raycast
        _results.Clear();
        var tmpData = new PointerEventData(eventSystem) { position = screenPos };
        raycaster.Raycast(tmpData, _results);
        RaycastResult? top = _results.Count > 0 ? _results[0] : (RaycastResult?)null;
        GameObject target = top.HasValue ? top.Value.gameObject : null;

        // 3) Hover 進出
        if (target != _hovered)
        {
            if (_hovered) ExecuteEvents.Execute(_hovered, tmpData, ExecuteEvents.pointerExitHandler);
            if (target)   ExecuteEvents.Execute(target,   tmpData, ExecuteEvents.pointerEnterHandler);
            _hovered = target;
        }

        // 4) 讀取筆按鍵
        bool pressed = stylus.CurrentState.cluster_front_value;

        // ── 邊緣：按下 ─────────────────────────────────────────────
        if (pressed && !_lastPressed)
        {
            _pressedTarget = target;

            // 建立一份會持續沿用的 PointerEventData
            _pressData = new PointerEventData(eventSystem) {
                position = screenPos,
                pressPosition = screenPos,
                button = PointerEventData.InputButton.Left,
                pointerId = 0,
                pointerEnter = target
            };
            if (top.HasValue) {
                _pressData.pointerPressRaycast = top.Value;
                _pressData.pointerCurrentRaycast = top.Value;
            }

            // PointerDown（用 Hierarchy 讓事件往上冒泡）
            if (_pressedTarget)
            {
                var downTarget = ExecuteEvents.GetEventHandler<IPointerDownHandler>(_pressedTarget);
                ExecuteEvents.Execute(downTarget ?? _pressedTarget, _pressData, ExecuteEvents.pointerDownHandler);
            }

            // 找會吃 Drag 的對象（如 Slider 在父物件）
            _dragTarget = _pressedTarget ? ExecuteEvents.GetEventHandler<IDragHandler>(_pressedTarget) : null;
            if (_dragTarget)
            {
                _pressData.pointerDrag = _dragTarget;
                ExecuteEvents.Execute(_dragTarget, _pressData, ExecuteEvents.beginDragHandler);
            }

            _lastScreenPos = screenPos;
        }

        // ── 按住期間：送 Drag（讓 Slider 跟著滑） ────────────────
        if (pressed && _pressedTarget)
        {
            // 更新持續資料
            _pressData.position = screenPos;
            _pressData.delta = screenPos - _lastScreenPos;

            // 重新取當前 raycast（滑到別的 UI 上也能正確）
            _results.Clear();
            raycaster.Raycast(_pressData, _results);
            if (_results.Count > 0) _pressData.pointerCurrentRaycast = _results[0];

            // 送 drag
            if (_dragTarget)
                ExecuteEvents.Execute(_dragTarget, _pressData, ExecuteEvents.dragHandler);

            _lastScreenPos = screenPos;
        }

        // ── 邊緣：放開 ─────────────────────────────────────────────
        if (!pressed && _lastPressed)
        {
            if (_dragTarget)
            {
                ExecuteEvents.Execute(_dragTarget, _pressData, ExecuteEvents.endDragHandler);
            }

            if (_pressedTarget)
            {
                ExecuteEvents.Execute(_pressedTarget, _pressData, ExecuteEvents.pointerUpHandler);

                // 若在同一個物件上放開就當作 Click
                if (_pressedTarget == _hovered)
                {
                    ExecuteEvents.Execute(_pressedTarget, _pressData, ExecuteEvents.pointerClickHandler);
                }
            }

            _pressedTarget = null;
            _dragTarget = null;
            _pressData = null;
        }

        _lastPressed = pressed;

        Debug.Log("====================");
        Debug.Log("Hover on: " + (_hovered ? _hovered.name : "none"));
        Debug.Log("Pressed on: " + (_pressedTarget ? _pressedTarget.name : "none"));
        Debug.Log("Dragging: " + (_dragTarget ? _dragTarget.name : "none"));
        Debug.Log("====================");
        
    }
}