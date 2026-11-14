using System;
using Il2CppInterop.Runtime.Attributes;
using SuikodenRandomizer.Suikoden2Plugin;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SuikodenRandomizer.Unified;

/// <summary>The only Unity frame/input/UI component in the combined randomizer.</summary>
public sealed class UnifiedOverlay : MonoBehaviour
{
    private GUIStyle? _style;
    private bool _inputFailed, _drawFailed;
    public UnifiedOverlay(IntPtr pointer) : base(pointer) { }
    public void Start() => Gsd2Graphics.Initialize(this);
    public void Update()
    {
        UnifiedPlugin.Observe();
        UnifiedPlugin.Ui.Tick();
        if (_inputFailed || !Application.isFocused) return;
        try
        {
            var keyboard = Keyboard.current;
            if (UnifiedPlugin.Ui.DispatchF6(Time.frameCount, Application.isFocused,
                keyboard is not null && keyboard.f6Key.wasPressedThisFrame))
                UnifiedPlugin.ViewMode = UnifiedPlugin.ViewMode switch
                {
                    PanelView.Full => PanelView.Compact,
                    PanelView.Compact => PanelView.Hidden,
                    _ => PanelView.Full
                };
        }
        catch (Exception error)
        { _inputFailed = true; UnifiedPlugin.HostLog.LogWarning("F6 status toggle unavailable: " + error.Message); }
    }
    public void OnGUI()
    {
        if (_drawFailed || !Application.isFocused || Event.current is null
            || Event.current.type != EventType.Repaint || Screen.width < 480 || Screen.height < 270) return;
        try { Draw(); }
        catch (Exception error)
        { _drawFailed = true; UnifiedPlugin.HostLog.LogWarning("Shared status panel unavailable: " + error.Message); }
    }
    [HideFromIl2Cpp]
    private void Draw()
    {
        UnifiedPlugin.Observe();
        var state = UnifiedPlugin.Ui.SelectUi();
        if (!state.HasBackend) return;
        var mode = state.Faulted ? PanelView.Full : UnifiedPlugin.ViewMode;
        if (mode == PanelView.Hidden) return;

        _style ??= new GUIStyle { fontSize = 16, wordWrap = true, richText = false };
        _style.normal.textColor = Color.white;
        _style.padding.left = _style.padding.right = _style.padding.top = _style.padding.bottom = 0;
        string hint = state.Faulted ? "Attention required" : mode == PanelView.Full ? "F6: Compact" : "F6: Hide";
        string heading = "Stars of Destiny " + UnifiedPlugin.Version + " | " + state.DisplayName + " | " + hint;
        string body = state.Status;
        if (mode == PanelView.Compact)
        {
            int detailStart = body.IndexOf('\n');
            if (detailStart >= 0) body = body.Substring(0, detailStart);
        }
        else if (!state.Faulted)
            body += "\n" + DialogueNative.Status(state.Game);

        const float margin = 14, padding = 12, gap = 6;
        float width = Math.Min(850, Screen.width - 2 * margin);
        float textWidth = width - 2 * padding;
        float headingHeight = _style.CalcHeight(GUIContent.Temp(heading), textWidth);
        float bodyHeight = _style.CalcHeight(GUIContent.Temp(body), textWidth);
        var color = GUI.color; var content = GUI.contentColor; var matrix = GUI.matrix;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            GUI.color = GUI.contentColor = Color.white;
            GUI.Label(new Rect(margin + padding, margin + padding, textWidth, headingHeight), GUIContent.Temp(heading), _style);
            GUI.Label(new Rect(margin + padding, margin + padding + headingHeight + gap, textWidth, bodyHeight), GUIContent.Temp(body), _style);
        }
        finally { GUI.color = color; GUI.contentColor = content; GUI.matrix = matrix; }
    }
}
