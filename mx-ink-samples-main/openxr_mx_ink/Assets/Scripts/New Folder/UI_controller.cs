using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_controller : MonoBehaviour
{
    [Header("Refs")]
    public StylusHandler stylus;          // 你現有的筆資料來源
    public Transform stylusTip;           // 筆尖在世界座標的位置（可用 inkingPose + 一點 offset）
    public Camera uiCamera;               // VR 主相機，且要被指到 Canvas.worldCamera
    public GraphicRaycaster raycaster;    // 你的 World Space Canvas 上的 GraphicRaycaster
    public EventSystem eventSystem;

    private readonly List<RaycastResult> _results = new();
    private GameObject _hovered;          // 目前指到的 UI 物件
    private GameObject _pressedTarget;    // 按下時的目標
    private bool _lastPressed;

    void Reset()
    {
        if (!uiCamera) uiCamera = Camera.main;
        if (!eventSystem) eventSystem = FindObjectOfType<EventSystem>();
        if (!raycaster) raycaster = FindObjectOfType<GraphicRaycaster>();
    }

    void Update()
    {
        if (stylus == null || stylusTip == null || uiCamera == null || eventSystem == null || raycaster == null)
            return;

        // 1) 筆尖 -> 螢幕座標
        Vector2 screenPos = uiCamera.WorldToScreenPoint(stylusTip.position);

        // 2) UI Raycast
        var data = new PointerEventData(eventSystem) { position = screenPos };
        _results.Clear();
        raycaster.Raycast(data, _results);
        var target = _results.Count > 0 ? _results[0].gameObject : null;

        // 3) Hover 進出
        if (target != _hovered)
        {
            if (_hovered) ExecuteEvents.Execute(_hovered, data, ExecuteEvents.pointerExitHandler);
            if (target)   ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler);
            _hovered = target;
        }

        // 4) 讀取筆的按鍵（你的 boolean）
        bool pressed = stylus.CurrentState.cluster_front_value;

        // 邊緣觸發：按下
        if (pressed && !_lastPressed)
        {
            _pressedTarget = target;
            if (_pressedTarget)
            {
                ExecuteEvents.Execute(_pressedTarget, data, ExecuteEvents.pointerDownHandler);
            }
        }

        // 邊緣觸發：放開
        if (!pressed && _lastPressed)
        {
            if (_pressedTarget)
            {
                // 先送 Up 到按下時的物件
                ExecuteEvents.Execute(_pressedTarget, data, ExecuteEvents.pointerUpHandler);

                // 若在同一個物件上放開，視為 Click
                if (_pressedTarget == _hovered)
                {
                    ExecuteEvents.Execute(_pressedTarget, data, ExecuteEvents.pointerClickHandler);
                }
            }
            _pressedTarget = null;
        }

        _lastPressed = pressed;
    }
}