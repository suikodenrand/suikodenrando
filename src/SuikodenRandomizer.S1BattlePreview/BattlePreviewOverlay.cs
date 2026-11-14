using System;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SuikodenRandomizer.S1BattlePreview;

public sealed class BattlePreviewOverlay : MonoBehaviour
{
    private GUIStyle? _style;
    private bool _drawFailed;
    private bool _keyboardFailed;
    private HeroCommandPrompt? _prompt;
    private HeroCommandKind _commandKind = HeroCommandKind.Attack;
    private int _resourceIndex;
    private int _targetIndex;

    public BattlePreviewOverlay(IntPtr pointer) : base(pointer) { }

    public void OnGUI()
    {
        if (_drawFailed || !Application.isFocused || Event.current is not { } guiEvent) return;
        try
        {
            if (guiEvent.type == EventType.Repaint && Screen.width >= 480 && Screen.height >= 270 && BattlePreviewState.Visible
                && (!BattlePreviewPlugin.NativeSpdMetersEnabled || NativeSpdMeters.UsingFallback))
                Draw();
            if (ActiveBattleState.PendingHeroPrompt is { } prompt)
                DrawCommand(prompt);
        }
        catch (Exception error)
        {
            _drawFailed = true;
            ActiveBattleState.CommandOverlayFailed(error.Message);
            BattlePreviewPlugin.PreviewLog?.LogWarning("S1 battle preview could not draw. " + error.Message);
        }
    }

    public void LateUpdate() => NativeSpdMeters.Update();

    public void Update()
    {
        if (_keyboardFailed || _drawFailed || !Application.isFocused || ActiveBattleState.PendingHeroPrompt is not { } prompt)
            return;
        try
        {
            if (Keyboard.current is not { } keyboard) return;
            UsePrompt(prompt);
            if (keyboard.rKey.wasPressedThisFrame) SelectKind(HeroCommandKind.Rune);
            if (keyboard.iKey.wasPressedThisFrame) SelectKind(HeroCommandKind.Item);
            int vertical = (keyboard.downArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.upArrowKey.wasPressedThisFrame ? 1 : 0);
            int horizontal = (keyboard.rightArrowKey.wasPressedThisFrame ? 1 : 0) - (keyboard.leftArrowKey.wasPressedThisFrame ? 1 : 0);
            if (_commandKind == HeroCommandKind.Attack)
                MoveTarget(prompt.Targets, vertical + horizontal);
            else
            {
                MoveResource(ResourceOptions(prompt), vertical);
                if (SelectedResource(prompt) is { } option) MoveTarget(option.Targets, horizontal);
            }
            if (keyboard.dKey.wasPressedThisFrame)
                ActiveBattleState.ChooseHeroCommand(prompt, new HeroCommand(HeroCommandKind.Defend, 0, IntPtr.Zero));
            else if (keyboard.aKey.wasPressedThisFrame)
            {
                if (_commandKind == HeroCommandKind.Attack) ChooseCurrent(prompt);
                else SelectKind(HeroCommandKind.Attack);
            }
            else if (keyboard.enterKey.wasPressedThisFrame && _commandKind != HeroCommandKind.Attack)
                ChooseCurrent(prompt);
        }
        catch (Exception error)
        {
            _keyboardFailed = true;
            BattlePreviewPlugin.PreviewLog?.LogWarning("S1 ATB command keyboard input stopped. " + error.Message);
        }
    }

    [HideFromIl2Cpp]
    private void DrawCommand(HeroCommandPrompt prompt)
    {
        UsePrompt(prompt);
        float width = Math.Min(464, Screen.width - 16);
        float left = (Screen.width - width) / 2;
        float top = Math.Max(8, Screen.height - 202);
        var oldColor = GUI.color;
        try
        {
            Fill(new Rect(left, top, width, 188), new Color(0.03f, 0.05f, 0.08f, 0.96f));
            GUI.color = Color.white;
            string timing = BattlePreviewPlugin.ActiveInputEnabled ? "Meters charging" : "Meters paused";
            GUI.Label(new Rect(left + 12, top + 6, width - 24, 22), prompt.Name + " ready at 100%  |  " + timing);
            float tab = (width - 32) / 4;
            if (GUI.Button(new Rect(left + 12, top + 31, tab, 25), "Attack")) SelectKind(HeroCommandKind.Attack);
            if (GUI.Button(new Rect(left + 14 + tab, top + 31, tab, 25), "Rune [R]")) SelectKind(HeroCommandKind.Rune);
            if (GUI.Button(new Rect(left + 16 + 2 * tab, top + 31, tab, 25), "Item [I]")) SelectKind(HeroCommandKind.Item);
            if (GUI.Button(new Rect(left + 18 + 3 * tab, top + 31, tab, 25), "Defend [D]"))
                ActiveBattleState.ChooseHeroCommand(prompt, new HeroCommand(HeroCommandKind.Defend, 0, IntPtr.Zero));

            bool attack = _commandKind == HeroCommandKind.Attack;
            var options = ResourceOptions(prompt);
            if (!attack)
            {
                if (options.Length == 0)
                {
                    GUI.Label(new Rect(left + 12, top + 66, width - 24, 25),
                        _commandKind == HeroCommandKind.Rune ? "No rune spells available" : "No usable battle items");
                    return;
                }
                if (GUI.Button(new Rect(left + 12, top + 64, 34, 25), "<")) MoveResource(options, -1);
                if (GUI.Button(new Rect(left + width - 46, top + 64, 34, 25), ">")) MoveResource(options, 1);
                var option = options[_resourceIndex];
                string uses = option.Uses is int count ? $" ({count} left)" : "";
                GUI.Label(new Rect(left + 54, top + 64, width - 108, 25), option.Name + uses);
            }

            var targets = attack ? prompt.Targets : options[_resourceIndex].Targets;
            if (targets.Length > 0)
            {
                if (GUI.Button(new Rect(left + 12, top + 96, 34, 25), "<")) MoveTarget(targets, -1);
                if (GUI.Button(new Rect(left + width - 46, top + 96, 34, 25), ">")) MoveTarget(targets, 1);
                var target = targets[_targetIndex];
                GUI.Label(new Rect(left + 54, top + 96, width - 108, 25), $"Target {target.Name} (slot {target.Slot})");
            }
            else
            {
                GUI.Label(new Rect(left + 12, top + 96, width - 24, 25),
                    attack ? "No enemy in weapon range" : "No target selection");
            }

            if ((!attack || targets.Length > 0)
                && GUI.Button(new Rect(left + 12, top + 128, width - 24, 27), attack ? "Attack [A]" : "Use [Enter]"))
                ChooseCurrent(prompt);
            GUI.Label(new Rect(left + 12, top + 160, width - 24, 22),
                attack ? "Arrow keys change target" : "Up / Down selects ability   Left / Right selects target");
        }
        finally { GUI.color = oldColor; }
    }

    [HideFromIl2Cpp]
    private void UsePrompt(HeroCommandPrompt prompt)
    {
        if (ReferenceEquals(_prompt, prompt)) return;
        _prompt = prompt;
        _commandKind = HeroCommandKind.Attack;
        _resourceIndex = 0;
        _targetIndex = 0;
    }

    [HideFromIl2Cpp]
    private void SelectKind(HeroCommandKind kind)
    {
        if (_commandKind == kind) return;
        _commandKind = kind;
        _resourceIndex = 0;
        _targetIndex = 0;
    }

    [HideFromIl2Cpp]
    private HeroResourceOption[] ResourceOptions(HeroCommandPrompt prompt)
        => _commandKind == HeroCommandKind.Rune ? prompt.Runes : prompt.Items;

    [HideFromIl2Cpp]
    private HeroResourceOption? SelectedResource(HeroCommandPrompt prompt)
    {
        var options = ResourceOptions(prompt);
        return options.Length == 0 ? null : options[_resourceIndex];
    }

    [HideFromIl2Cpp]
    private void MoveResource(HeroResourceOption[] options, int direction)
    {
        if (direction == 0 || options.Length == 0) return;
        _resourceIndex = Wrap(_resourceIndex + direction, options.Length);
        _targetIndex = 0;
    }

    [HideFromIl2Cpp]
    private void MoveTarget(HeroCommandTarget[] targets, int direction)
    {
        if (direction != 0 && targets.Length > 0) _targetIndex = Wrap(_targetIndex + direction, targets.Length);
    }

    private static int Wrap(int index, int count) => (index % count + count) % count;

    [HideFromIl2Cpp]
    private void ChooseCurrent(HeroCommandPrompt prompt)
    {
        bool attack = _commandKind == HeroCommandKind.Attack;
        var resource = attack ? null : SelectedResource(prompt);
        if ((attack && prompt.Targets.Length == 0) || (!attack && resource is null)) return;
        var targets = attack ? prompt.Targets : resource!.Targets;
        var target = targets.Length == 0 ? default : targets[_targetIndex];
        ActiveBattleState.ChooseHeroCommand(prompt,
            new HeroCommand(_commandKind, target.Slot, target.Identity, resource?.Resource));
    }

    [HideFromIl2Cpp]
    private void Draw()
    {
        _style ??= new GUIStyle { fontSize = 13, clipping = TextClipping.Clip };
        _style.normal.textColor = Color.white;
        const float margin = 12, rowHeight = 28;
        float width = Math.Min(512, Screen.width - 2 * margin);
        float columnWidth = (width - 3 * margin) / 2;
        float x = Screen.width - width - margin;
        const float y = 12;
        int partyRows = 0, enemyRows = 0;
        for (int slot = 1; slot < BattlePreviewState.SlotCount; slot++)
        {
            var actor = BattlePreviewState.GetCombatant(slot);
            if (actor.Identity == IntPtr.Zero) continue;
            if (actor.PartyMember) partyRows++; else enemyRows++;
        }
        int visibleRows = Math.Min(Math.Max(partyRows, enemyRows), (Screen.height - 100) / (int)rowHeight);
        Color old = GUI.color;
        try
        {
            Fill(new Rect(x, y, width, 68 + rowHeight * visibleRows), new Color(0.03f, 0.05f, 0.08f, 0.91f));
            GUI.color = Color.white;
            int waitingSlot = BattlePreviewState.WaitingSlot;
            string title = "S1 ATB meter preview  |  Native turns";
            if (BattlePreviewPlugin.ActiveAtbEnabled)
            {
                title = ActiveBattleState.FellBack ? "S1 ATB  |  Native fallback"
                    : BattlePreviewPlugin.ReadyCommandsEnabled ? "S1 ATB  |  Ready hero commands"
                    : BattlePreviewPlugin.RepeatTurnsEnabled ? "S1 ATB  |  Same-round repeat turns"
                    : "S1 ATB  |  Hero and enemy SPD turns";
            }
            else if (waitingSlot > 0)
                title = "S1 ATB  |  Waiting for " + BattlePreviewState.GetCombatant(waitingSlot).Name + "'s SPD meter";
            else if (BattlePreviewPlugin.OneShotAttackEnabled)
                title = "S1 ATB  |  One-shot SPD attack";
            GUI.Label(new Rect(x + margin, y + 4, width - 2 * margin, 20), title, _style);
            GUI.Label(new Rect(x + margin, y + 24, columnWidth, 20), "Party", _style);
            GUI.Label(new Rect(x + 2 * margin + columnWidth, y + 24, columnWidth, 20), "Enemies", _style);
            int party = 0, enemy = 0;
            for (int slot = 1; slot < BattlePreviewState.SlotCount; slot++)
            {
                var actor = BattlePreviewState.GetCombatant(slot);
                if (actor.Identity == IntPtr.Zero) continue;
                int row = actor.PartyMember ? party++ : enemy++;
                if (row >= visibleRows) continue;
                float left = x + (actor.PartyMember ? margin : 2 * margin + columnWidth);
                float top = y + 44 + row * rowHeight;
                float gauge = (float)BattlePreviewState.GetGauge(slot);
                string state = actor.Alive ? ((int)(gauge * 100)).ToString() + "%" : "KO";
                GUI.color = Color.white;
                GUI.Label(new Rect(left, top, columnWidth, 19), actor.Name + "  SPD " + actor.Speed + "  " + state, _style);
                Fill(new Rect(left, top + 19, columnWidth, 5), new Color(0.24f, 0.30f, 0.36f, 1f));
                if (gauge > 0)
                    Fill(new Rect(left, top + 19, columnWidth * gauge, 5),
                        gauge >= 1 ? new Color(0.98f, 0.77f, 0.27f, 1f) : new Color(0.23f, 0.71f, 0.86f, 1f));
            }
            if (partyRows > visibleRows || enemyRows > visibleRows)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(x + margin, y + 44 + rowHeight * visibleRows, width - margin, 20),
                    "More combatants in the battle log", _style);
            }
        }
        finally { GUI.color = old; }
    }

    private static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }
}
