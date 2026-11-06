using Il2CppInterop.Runtime.Attributes;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SuikodenRandomizer.AvatarPlugin;

public sealed class AvatarOverlay : MonoBehaviour
{
    private GUIStyle? _style;
    private bool _details = true;
    private bool _drawFailed, _inputFailed;
    public AvatarOverlay(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        var runtime = AvatarRuntime.Current;
        if (runtime is null) return;
        runtime.Tick();
        if (_inputFailed || !Application.isFocused) return;
        try
        {
            var keyboard = Keyboard.current;
            if (keyboard is null || !keyboard.f6Key.wasPressedThisFrame) return;
            _details = !_details;
            runtime.Trace("MANUAL CHECKPOINT: " + runtime.Status);
            runtime.Trace("PERFORMANCE CHECKPOINT: " + runtime.PerformanceStatus);
            runtime.TracePartyLayout("manual F6 checkpoint");
            if (AvatarPlugin.SkipToViktor) ViktorStart.TraceState("manual F6");
            if (runtime.IsActive)
            {
                try { runtime.VerifyFinalParty("manual F6 checkpoint"); }
                catch (Exception exception) { runtime.Trace("Checkpoint during a transition: " + exception.Message); }
            }
        }
        catch (Exception exception) { _inputFailed = true; runtime.Trace("F6 input unavailable: " + exception.Message); }
    }

    public void OnGUI()
    {
        if (_drawFailed) return;
        try
        {
            var runtime = AvatarRuntime.Current;
            if (runtime is null || Event.current is null || Event.current.type != EventType.Repaint
                || !Application.isFocused || Screen.width < 480 || Screen.height < 270) return;
            Draw(runtime);
        }
        catch (Exception exception)
        {
            _drawFailed = true;
            AvatarRuntime.Current?.Trace("Pilot status overlay unavailable: " + exception.Message);
        }
    }

    [HideFromIl2Cpp]
    private void Draw(AvatarRuntime runtime)
    {
        if (_style is null)
        {
            _style = new GUIStyle { fontSize = 16 };
            _style.padding.left = _style.padding.right = _style.padding.top = _style.padding.bottom = 0;
            _style.normal.textColor = Color.white;
        }
        Color previous = GUI.color;
        Color content = GUI.contentColor;
        Matrix4x4 matrix = GUI.matrix;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            float width = Math.Min(850, Screen.width - 28);
            bool fault = runtime.FaultReason is not null;
            float height = fault ? 155 : _details ? 105 : 38;
            GUI.color = new Color(0.045f, 0.065f, 0.085f, 0.94f);
            GUI.DrawTexture(new Rect(14, 14, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.Label(new Rect(26, 22, width - 24, 25), GUIContent.Temp(AvatarPlugin.DisplayName + " " + AvatarPlugin.Version), _style);
            if (!fault && !_details) return;
            string text = runtime.Status.Replace('\r', ' ').Replace('\n', ' ');
            int lineWidth = Math.Max(30, (int)(width / 9));
            for (int line = 0; line < 3 && line * lineWidth < text.Length; line++)
            {
                int start = line * lineWidth;
                string part = text.Substring(start, Math.Min(lineWidth, text.Length - start));
                GUI.Label(new Rect(26, 49 + line * 22, width - 24, 23), GUIContent.Temp(part), _style);
            }
            if (fault)
                GUI.Label(new Rect(26, 127, width - 24, 23), GUIContent.Temp("Saving is unavailable. Reload an earlier save to continue."), _style);
        }
        finally { GUI.color = previous; GUI.contentColor = content; GUI.matrix = matrix; }
    }
}
