extern alias GSD1;

using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Share.Battle;
using SuikodenRandomizer.Core.Combat;
using UnityEngine;
using NativeStep = Il2CppSystem.Func<int, int>;
using Battle = GSD1::BattleBase;
using Manager = GSD1::UIBattleManager;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private const int RetreatRow = 4;
    private const int AutoCommand = 3;
    private const int CommandRows = RetreatRow + AutoCommand;
    private const int ConfirmedLastWindowStep = 3;
    private const uint ConfirmButton = 0x20;
    private const uint CancelButton = 0x40;
    private const uint UpButton = 0x1000;
    private const uint DownButton = 0x4000;

    private sealed class NativeHeroMenu(int depth, int slot, GSD1.PM_DATA actor, IntPtr window, int enemyCount)
    {
        internal readonly int Depth = depth;
        internal readonly int Slot = slot;
        internal readonly IntPtr Actor = actor.Pointer;
        internal readonly IntPtr Window = window;
        internal readonly int EnemyCount = enemyCount;
        internal NativeCommand Draft = new(actor.c_koudou, 0, actor.c_mokuhyo);
        internal NativeCommand StockCommand;
        internal IntPtr TargetIdentity;
        internal IntPtr Step;
        internal bool Complete;
        internal bool Back;
        internal int PartyCommand;
        internal string? Failure;
        internal int LastPumpFrame = -1;
        internal bool WindowsClosed;
        internal bool Queued;
        internal bool ItemHandedToQueue;
        internal CtbMenuRoster? OpeningRoster;
        internal CtbActionCost? CtbPreview = CtbActionTiming.Describe(CtbActionKind.Attack);
        internal string CtbTransition = "";
        internal string CtbLoggedPreview = "";
    }

    private readonly record struct NativeCommand(sbyte Kind, sbyte Resource, sbyte Target)
    {
        internal static NativeCommand Read(GSD1.PM_DATA actor) => new(actor.c_koudou, actor.c_item, actor.c_mokuhyo);

        internal void Apply(GSD1.PM_DATA actor)
        {
            actor.c_item = Resource;
            actor.c_mokuhyo = Target;
            actor.c_koudou = Kind;
        }
    }

    private static NativeHeroMenu? _nativeMenu;
    private static bool _nativeMenusFailed;
    private static bool _pumpingNativeMenu;
    private static bool _nativeTargetPanelsHeld;
    private static readonly WindowType[] HeroCommandWindows =
    {
        WindowType.Command, WindowType.Item, WindowType.Emblem, WindowType.Unite, WindowType.Bag, WindowType.Magic
    };
    private static int _turnDepth = -1;
    private static IntPtr _ctbTurnStep;
    private static int _escapePenaltyRound = -1;
    private enum FailedRunDialog { None, AwaitingConfirm, Closing }
    private static FailedRunDialog _ctbFailedRunDialog;
    private static int _ctbRunDialogInputFrame;
    private static NativeStep? _heroCommandStep;
    private static NativeStep? _partyCommandStep;
    private static NativeStep? _retreatStep;
    private static NativeStep? _bribeStep;
    private static NativeStep? _autoStep;

    internal static bool NativeAutoOwnsBattle { get; private set; }

    internal static bool TrySkipNativeOuterMenu()
    {
        if (!BattlePreviewPlugin.NativeReadyMenusEnabled || _commandOverlayFailed
            || global::SystemObject.mode != global::SystemObject.Mode.GSD1) return false;

        try
        {
            var work = Battle.battle_work;
            var window = Battle.window_work;
            if (work is null || work.battle_phase_no != 2 || NativeOnlyReason(work) is not null
                || window is null || window.window_step != 0 || !Manager.IsInstantiated
                || work.pm_data is not { } rows || work.player_kazu is < 1 or > 6
                || work.pm_kazu <= work.player_kazu || work.pm_kazu >= rows.Count
                || work.monster_party_data is null || window.player_window_data is not { } players
                || players.Count < work.player_kazu || window.monster_window_data is null
                || window.monster_position_data is null) return false;
            if (_battle != work.Pointer)
            {
                ResetEncounter();
                _battle = work.Pointer;
            }
            if (_fellBack || _nativeMenusFailed) return false;

            // Match the stock Fight path before its nested party command batch.
            for (int slot = 1; slot <= work.player_kazu; slot++)
                if (rows[slot] is null) return false;
            var manager = Manager.Instance;
            if (!_nativeTargetPanelsHeld) manager.CloseAllWindowUI();
            manager.CloseTipsUI();
            for (int slot = 1; slot <= work.player_kazu; slot++)
                rows[slot].c_koudou = UnselectedCommand;
            window.window_player_index = -1;
            window.selected_player = -1;
            window.window_index = 0;
            window.return_value = 0;
            _readyCommandRound = work.battle_kaisuu + 1;
            if (NativeAutoOwnsBattle)
            {
                NativeAutoOwnsBattle = false;
                Trace($"ATB resumed manual commands after native Auto in round {_readyCommandRound}.");
            }
            Trace($"ATB entered round {_readyCommandRound} without opening the outer party menu.");
            return true;
        }
        catch (Exception error)
        {
            _nativeMenusFailed = true;
            Trace("ATB kept the stock party menu because " + error.Message);
            return false;
        }
    }

    private static bool TryOpenNativeReadyMenu(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA actor, int turnDepth, bool queued = false)
    {
        if (!BattlePreviewPlugin.NativeReadyMenusEnabled || _nativeMenusFailed) return false;
        try
        {
            var window = Battle.window_work;
            var chapter = GSD1.ChapterManager.GR1Instance;
            int depth = turnDepth + 1;
            if (turnDepth < 1 || chapter?.g1_game_step is not { } steps || depth >= steps.Count
                || steps[depth] is not null || window?.player_window_data is not { } players
                || work.p_battle_data is not { } party || slot >= party.Count
                || party[slot]?.player_data is not { } player || !Manager.IsInstantiated
                || window.command_flg is not { Count: >= RetreatRow })
                throw new InvalidOperationException("the native command window or its child step was unavailable");

            int playerIndex = 0;
            while (playerIndex < work.player_kazu && playerIndex < players.Count && players[playerIndex] != slot)
                playerIndex++;
            if (playerIndex >= work.player_kazu || playerIndex >= players.Count)
                throw new InvalidOperationException("the ready hero had no native formation entry");

            _heroCommandStep ??= WrapStep(Battle.command01);
            _partyCommandStep ??= WrapStep(Battle.player_command);
            _retreatStep ??= WrapStep(Battle.nige_window);
            _bribeStep ??= WrapStep(Battle.okane_window);
            int enemyCount = RefreshNativeEnemyTargets(work, window);
            if (enemyCount == 0) return false;
            var menu = new NativeHeroMenu(depth, slot, actor, window.Pointer, enemyCount);
            if (queued)
            {
                menu.Queued = true;
                menu.OpeningRoster = new CtbMenuRoster(work, slot);
                menu.LastPumpFrame = Time.frameCount;
            }
            _nativeMenu = menu;
            window.window_step = 0;
            window.window_index = 0;
            window.window_player_index = playerIndex;
            window.selected_player = slot;
            window.selected_player_data = player;
            int stockEnemyCount = work.nokori_monster;
            var stockCommand = NativeCommand.Read(actor);
            work.nokori_monster = enemyCount;
            try
            {
                Battle.make_monster_data(slot);
                Battle.make_command_data(slot);
            }
            finally
            {
                work.nokori_monster = stockEnemyCount;
                if (queued) stockCommand.Apply(actor);
            }
            for (int row = 0; row < RetreatRow; row++)
            {
                if (window.command_flg[row] == 0) continue;
                window.window_index = row;
                break;
            }
            var manager = Manager.Instance;
            Battle.disp_hp();
            bool panelsWereHeld = _nativeTargetPanelsHeld;
            _nativeTargetPanelsHeld = BattlePreviewPlugin.ActiveInputEnabled || queued;
            if (!panelsWereHeld || manager.IsCloseWindowUI(WindowType.Unit)) manager.OpenWindowUI(WindowType.Unit, true);
            if (!panelsWereHeld || manager.IsCloseWindowUI(WindowType.Enemy)) manager.OpenWindowUI(WindowType.Enemy, true);
            Battle.InitGameStep(_heroCommandStep, turnDepth, "ATB ready hero");
            menu.Step = steps[depth]?.Pointer ?? IntPtr.Zero;
            Trace($"ATB opened native menu for hero slot {slot} {actor.name}.");
            return true;
        }
        catch (Exception error)
        {
            if (queued)
            {
                ClearCtbHeroQueue();
                Trace("CTB deferred the full menu until the hero's turn: " + error.Message);
            }
            else DisableNativeReadyMenus(error.Message);
            return false;
        }
    }

    private static NativeStep WrapStep(Func<int, int> step) => DelegateSupport.ConvertDelegate<NativeStep>(step)
        ?? throw new InvalidOperationException("the native battle step could not be created");

    private static bool IsLivingFormationEnemy(int slot, GSD1.PM_DATA? actor)
        => IsLiving(actor) && actor!.kougeki_position is > 0 and <= 6 && !Battle.dead_check(slot);

    private static bool HasLiveFormationEnemy(GSD1.BATTLE_WORK work)
    {
        if (work.pm_data is not { } rows || work.pm_kazu >= rows.Count) return false;
        for (int slot = work.player_kazu + 1; slot <= work.pm_kazu; slot++)
            if (IsLivingFormationEnemy(slot, rows[slot])) return true;
        return false;
    }

    private static int RefreshNativeEnemyTargets(GSD1.BATTLE_WORK work, Battle.WINDOW_WORK window)
    {
        var slots = window.monster_window_data;
        var positions = window.monster_position_data;
        var actors = work.pm_data;
        if (slots is null || positions is null || actors is null || work.pm_kazu >= actors.Count)
            throw new InvalidOperationException("the native enemy targeting arrays were unavailable");

        Span<int> byPosition = stackalloc int[7];
        byPosition.Clear();
        for (int slot = work.player_kazu + 1; slot <= work.pm_kazu; slot++)
        {
            var actor = actors[slot];
            if (!IsLivingFormationEnemy(slot, actor)) continue;
            int position = actor!.kougeki_position;
            if (byPosition[position] != 0)
                throw new InvalidOperationException("the native enemy formation could not be represented");
            byPosition[position] = slot;
        }

        int count = 0;
        for (int position = 1; position < byPosition.Length; position++)
        {
            int slot = byPosition[position];
            if (slot == 0) continue;
            if (count >= slots.Count || count >= positions.Count)
                throw new InvalidOperationException("the native enemy targeting arrays were full");
            slots[count] = slot;
            positions[count] = (byte)position;
            count++;
        }
        return count;
    }

    internal static int EnterNativeTurn(int depth)
    {
        int previous = _turnDepth;
        _turnDepth = depth;
        return previous;
    }

    internal static void LeaveNativeTurn(int previous) => _turnDepth = previous;

    internal static bool TryHandleNativeTurnInput(int depth)
    {
        try
        {
            if (BattlePreviewPlugin.CtbEnabled)
            {
                if (PumpCtbFailedRunDialog()) return true;
                _inputTurnDepth = depth;
                _ctbTurnStep = ReadMenuStep(depth);
                TryAdvanceCtbEnemyQueue();
                // The frame callback pumps an early menu. The turn driver must
                // still run so the previous action finishes and CTB can select.
                if (_nativeMenu is { Queued: true }) return false;
            }
            if (!BattlePreviewPlugin.ActiveInputEnabled) return TryPumpNativeMenu(depth);
            _inputTurnDepth = depth;
            ObserveQueuedTurnReturn();
            return _nativeMenu is { PartyCommand: > 0 } && Scheduler.PendingSlot == 0 && TryPumpNativeMenu(depth);
        }
        catch (Exception error) { return Fallback(error.Message); }
    }

    private static IntPtr ReadMenuStep(int depth)
    {
        var chapter = GSD1.ChapterManager.GR1Instance;
        return chapter?.g1_game_step is { } steps && depth >= 0 && depth < steps.Count
            ? steps[depth]?.Pointer ?? IntPtr.Zero : IntPtr.Zero;
    }

    private static bool TryPumpNativeMenu(int depth, bool allowPartyCommand = true)
    {
        var menu = _nativeMenu;
        if (menu is null || menu.Depth != depth + 1 || (menu.Complete && menu.PartyCommand == 0)) return false;
        try
        {
            if (!TryGetNativeMenu(menu, out var work, out var window, out var actor)
                || !UsesReadyCommands(work) || work.battle_phase_no != 4 || work.battle_kaisuu != _round)
                throw new InvalidOperationException("the ready hero's native command context changed");
            bool activeInput = BattlePreviewPlugin.ActiveInputEnabled;
            bool ownsTurn = NativeHeroMenuInputPolicy.OwnsTurn(menu.Slot, Scheduler.PendingSlot, menu.Queued, activeInput);
            if (!IsLiving(actor) || (!activeInput && ownsTurn && !Battle.ukeire_check(menu.Slot)))
            {
                Trace($"ATB canceled hero slot {menu.Slot}'s native menu after its eligibility changed.");
                ClearHeroCommand(cancelReservation: !menu.Queued, keepTargetPanels: _nativeTargetPanelsHeld);
                return false;
            }
            if (!Application.isFocused || Time.timeScale <= 0f) return true;
            if (menu.PartyCommand != 0)
            {
                if (!HasLiveFormationEnemy(work))
                {
                    ClearHeroCommand(cancelReservation: !menu.Queued);
                    return false;
                }
                if (!ownsTurn || !allowPartyCommand) return false;
                HandOffPartyCommand(depth, work, window, menu);
                return true;
            }
            if (!ownsTurn && NativeOnlyReason(work) is not null) return false;
            int frame = Time.frameCount;
            if (menu.LastPumpFrame == frame) return ownsTurn;
            if (window.selected_player != menu.Slot || menu.Step == IntPtr.Zero || ReadMenuStep(menu.Depth) != menu.Step)
                throw new InvalidOperationException("the native battle replaced the menu child step");
            menu.LastPumpFrame = frame;

            int result;
            menu.StockCommand = NativeCommand.Read(actor);
            int stockEnemyCount = work.nokori_monster;
            if (BattlePreviewPlugin.CtbEnabled) ClearCtbTarget();
            _pumpingNativeMenu = true;
            try
            {
                // Native selectors need their own draft values while the enemy action sees the stock state.
                menu.Draft.Apply(actor);
                work.nokori_monster = menu.EnemyCount;
                result = Battle._ExecGameStep(menu.Depth, "ATB ready hero");
                menu.Draft = NativeCommand.Read(actor);
            }
            finally
            {
                _pumpingNativeMenu = false;
                work.nokori_monster = stockEnemyCount;
                menu.StockCommand.Apply(actor);
            }
            if (menu.Failure is not null) throw new InvalidOperationException(menu.Failure);
            menu.Step = ReadMenuStep(menu.Depth);
            if (result == Wait) return true;

            if (menu.PartyCommand != 0)
            {
                if (ownsTurn && allowPartyCommand) HandOffPartyCommand(depth, work, window, menu);
                else
                {
                    CloseNativeHeroWindows(window, keepTargetPanels: _nativeTargetPanelsHeld);
                    menu.WindowsClosed = true;
                }
                return true;
            }
            if (menu.Back)
            {
                ClearNativeReadyMenu(keepTargetPanels: _nativeTargetPanelsHeld);
                if (!TryOpenNativeReadyMenu(work, menu.Slot, actor, depth, menu.Queued))
                {
                    if (menu.Queued) ClearHeroCommand(cancelReservation: false);
                    else OpenOverlayPrompt(work, menu.Slot, actor);
                }
                return true;
            }
            if (!menu.Complete)
                throw new InvalidOperationException("the native command step ended without a hero action");

            menu.TargetIdentity = ReadNativeTarget(work, menu.Draft.Target)?.Pointer ?? IntPtr.Zero;
            CloseNativeHeroWindows(window, keepTargetPanels: _nativeTargetPanelsHeld);
            menu.WindowsClosed = true;
            if (!ownsTurn)
                Trace($"ATB accepted hero slot {menu.Slot}'s native command while slot {Scheduler.PendingSlot} was acting.");
        }
        catch (Exception error)
        {
            if (menu.Queued)
            {
                _ctbEarlyMenuUnavailable = true;
                ClearCtbHeroQueue();
                Trace("CTB deferred the full menu after its native context changed: " + error.Message);
                return false;
            }
            DisableNativeReadyMenus(error.Message);
            var work = Battle.battle_work;
            int slot = _heroCommand.Slot;
            if (slot > 0 && work?.pm_data is { } rows && work.Pointer == _battle && slot < rows.Count
                && rows[slot] is { } actor && actor.Pointer == _heroCommand.Identity)
                OpenOverlayPrompt(work, slot, actor);
            else
                ClearHeroCommand(cancelReservation: true);
        }
        return true;
    }

    internal static bool TryEndNativeReadyMenu(int depth, bool canceled, out int result)
    {
        result = 0;
        var menu = _nativeMenu;
        if (!_pumpingNativeMenu || menu is null || menu.Depth != depth) return false;
        menu.Back = canceled;
        menu.Complete = !canceled;
        return true;
    }

    internal static bool HandleNativeHeroRows(int depth, out bool refreshRows)
    {
        refreshRows = false;
        var menu = _nativeMenu;
        if (!_pumpingNativeMenu || menu is null || menu.Depth != depth) return false;
        if (!TryGetNativeMenu(menu, out _, out var window, out _) || window.selected_player != menu.Slot)
            throw new InvalidOperationException("the native command menu changed heroes");
        refreshRows = window.window_step == 0;
        if (window.window_step != 2) return false;

        int index = window.window_index;
        if (index is < 0 or >= CommandRows) throw new InvalidOperationException("the native command cursor was out of range");
        var manager = Manager.Instance;
        var pad = GSD1.OldSrcBase.sys_work;
        if (pad is null || !manager.IsChoosableWindowUI(WindowType.Command)) return true;
        uint pressed = pad.PadTrig;
        uint direction = pad.PadRTrig & (UpButton | DownButton);
        Span<bool> heroEnabled = stackalloc bool[RetreatRow];
        for (int row = 0; row < heroEnabled.Length; row++)
            heroEnabled[row] = window.command_flg[row] != 0;
        var input = NativeHeroMenuInputPolicy.Evaluate(index, heroEnabled,
            (pressed & ConfirmButton) != 0, (pressed & CancelButton) != 0,
            (direction & UpButton) != 0, (direction & DownButton) != 0);
        switch (input.Kind)
        {
            case NativeHeroMenuInputKind.PassNative:
                return false;
            case NativeHeroMenuInputKind.ConfirmParty:
                GSD1.P_sound_c.Qsd_call(0x503, false);
                menu.PartyCommand = input.PartyCommand;
                return true;
            case NativeHeroMenuInputKind.Cancel:
                // Native targeting and resource submenus still handle Back themselves.
                GSD1.P_sound_c.Qsd_call(0x504, false);
                return true;
            case NativeHeroMenuInputKind.Move:
                window.window_index = input.Row;
                manager.SetCursorUI(WindowType.Command, input.Row);
                if (input.Row < RetreatRow) manager.SetTipsTextFightCommand(input.Row);
                else if (BattlePreviewPlugin.CtbEnabled && input.Row == CommandRows - 1)
                    manager.CloseTipsUI();
                else manager.SetTipsTextTopCommand(input.Row - RetreatRow + 1);
                GSD1.P_sound_c.Qsd_call(0x502, false);
                return true;
            case NativeHeroMenuInputKind.Consume:
                return true;
            default:
                throw new InvalidOperationException("Unexpected native hero menu input.");
        }
    }

    internal static int NativeHeroRowResult => _nativeMenu is { PartyCommand: > 0 } ? 0 : Wait;

    internal static void NoteNativeMenuError(Exception error)
    {
        if (_nativeMenu is { } menu) menu.Failure = error.Message;
    }

    internal static void RefreshNativeHeroRows()
    {
        var menu = _nativeMenu;
        if (menu is null || !TryGetNativeMenu(menu, out var work, out var window, out _)) return;
        var messages = Battle.b_message;
        if (messages is null || messages.Count < 2 || messages[0] is not { Count: > AutoCommand } partyLabels
            || messages[1] is not { Count: >= RetreatRow } commandLabels)
            throw new InvalidOperationException("the native battle command labels were unavailable");
        var labels = new Il2CppSystem.Collections.Generic.List<string>();
        var enabled = new Il2CppSystem.Collections.Generic.List<bool>();
        for (int row = 0; row < RetreatRow; row++)
        {
            labels.Add(GSD1.G1_string_id_h.G1_SID_MSG(commandLabels[row]));
            enabled.Add(window.command_flg[row] != 0);
        }
        bool letGo = work.monster_party_data is { party_flg: >= 0 } && work.monster_kazu > 0
            && work.monster_party_level * 60 / work.monster_kazu <= work.player_party_level * 60 / work.player_kazu;
        labels.Add(GSD1.G1_string_id_h.G1_SID_MSG(letGo ? 0x103d7 : partyLabels[1]));
        enabled.Add(true);
        labels.Add(GSD1.G1_string_id_h.G1_SID_MSG(partyLabels[2]));
        enabled.Add(true);
        labels.Add(BattlePreviewPlugin.CtbEnabled ? "Wait" : GSD1.G1_string_id_h.G1_SID_MSG(partyLabels[AutoCommand]));
        enabled.Add(true);
        var manager = Manager.Instance;
        manager.battleWindow.UpdateCommandText(WindowType.Command, labels, enabled, null);
        manager.SetCursorUI(WindowType.Command, window.window_index);
        manager.SetCursorUI(WindowType.Unit, window.window_player_index);
        if (window.kurukuru is { } rings && menu.Slot < rings.Count && rings[menu.Slot] is { } ring)
        {
            ring.SetCharacter(menu.Slot);
            ring.Open();
        }
    }

    private static bool TryDispatchNativeReadyCommand(GSD1.BATTLE_WORK work, GSD1.PM_DATA actor, int slot,
        double now, bool canDispatch, out int result)
    {
        result = Wait;
        var menu = _nativeMenu;
        if (menu is null) return false;
        if (!menu.Complete || !canDispatch || menu.PartyCommand != 0) return true;

        var command = menu.Draft;
        if (command.Kind is not (Attack or Defend or Rune or Item))
        {
            DisableNativeReadyMenus($"the native menu produced command {command.Kind}");
            OpenOverlayPrompt(work, slot, actor);
            return true;
        }

        var target = ReadNativeTarget(work, command.Target);
        bool validSelection = menu.OpeningRoster?.Accepts(work, slot, actor, command)
            ?? (command.Kind != Item || ReadQueuedResource(work, slot, command, reservedItem: true).Id > 0);
        if ((menu.TargetIdentity != IntPtr.Zero && target?.Pointer != menu.TargetIdentity)
            || (command.Kind == Attack && (!IsLiving(target) || Battle.dead_check(command.Target)))
            || !validSelection)
        {
            ClearNativeReadyMenu();
            if (!TryOpenNativeReadyMenu(work, slot, actor, _turnDepth)) OpenOverlayPrompt(work, slot, actor);
            Trace($"ATB refreshed hero slot {slot}'s menu after the confirmed target or resource changed.");
            return true;
        }

        command.Apply(actor);
        Trace($"ATB confirmed native command {command.Kind} for hero slot {slot} {actor.name} "
            + $"in round {_round} (resource index {command.Resource}, target slot {command.Target}).");
        _nativeMenu = null;
        ClearHeroCommand(cancelReservation: false);
        Scheduler.Tick(now, charging: !_drainingRound);
        result = Dispatch(work, slot, actor, now);
        return true;
    }

    private static GSD1.PM_DATA? ReadNativeTarget(GSD1.BATTLE_WORK work, int slot)
        => slot > 0 && slot <= work.pm_kazu && work.pm_data is { } rows && slot < rows.Count ? rows[slot] : null;

    private static void HandOffPartyCommand(int depth, GSD1.BATTLE_WORK work, Battle.WINDOW_WORK window, NativeHeroMenu menu)
    {
        if (BattlePreviewPlugin.CtbEnabled && menu.PartyCommand == AutoCommand)
        {
            PerformCtbWait(work, menu);
            return;
        }
        bool auto = menu.PartyCommand == AutoCommand;
        bool ctbRun = BattlePreviewPlugin.CtbEnabled && menu.PartyCommand == 1;
        if (ctbRun)
        {
            if (Scheduler.IsAdmitted || Scheduler.PendingSlot != menu.Slot || _heroCommand.Slot != menu.Slot
                || _heroCommand.Identity != menu.Actor)
                throw new InvalidOperationException("the CTB Run reservation changed");
            if (Battle.nigeru_check() == 0)
            {
                if (!Scheduler.SpendCtbMenuTurn(menu.Slot, menu.Actor, 1.0))
                    throw new InvalidOperationException("the failed CTB Run could not spend its turn");
                ClearHeroCommand(cancelReservation: false);
                Manager.Instance.OpenDialogWindowUI(GSD1.G1_string_id_h.G1_SID_MSG(0x103d3));
                _ctbFailedRunDialog = FailedRunDialog.AwaitingConfirm;
                _ctbRunDialogInputFrame = Time.frameCount;
                Trace(FormattableString.Invariant($"CTB Run failed for slot {menu.Slot}; recovery x1.00, next opportunity {Scheduler.Ctb.GetNextReady(menu.Slot):F1}."));
                RefreshCtbPresentation(work);
                return;
            }
        }
        var step = auto ? (_autoStep ??= WrapStep(Battle.omakase_window))
            : menu.PartyCommand == 1 ? _retreatStep : _bribeStep;
        CloseNativeHeroWindows(window);
        // nige_window stage 0 rolls for escape and assigns Defend to the whole party.
        // CTB already rolled. Stage 10 displays stock success and continues the normal battle exit.
        window.window_step = ctbRun ? 10 : 0;
        window.window_player_index = -1;
        window.window_index = menu.PartyCommand;
        window.return_value = menu.PartyCommand;
        if (auto)
        {
            // Initialize the same timer and turn counter as the original Auto row.
            Battle.next_chara_timer = 10;
            work.auto_turn = 0;
            NativeAutoOwnsBattle = true;
        }
        else if (!ctbRun)
        {
            // Stock owns this round's escape and, on failure, the next round of Defend.
            _escapePenaltyRound = work.battle_kaisuu + 1;
        }
        Battle.NextGameStep(_partyCommandStep, depth - 1, "ATB party command");
        Battle.NextGameStep(step, depth, auto ? "ATB native Auto" : "ATB native escape");
        _nativeMenu = null;
        DiscardQueuedHeroes();
        ClearHeroCommand(cancelReservation: true);
        if (auto) Scheduler.Reset();
        _waitStarted = -1;
        string label = auto ? "Auto" : menu.PartyCommand == 1 ? "Run / Let Go" : "Bribe";
        Trace($"ATB handed {label} to the native battle flow.");
        if (BattlePreviewPlugin.CtbEnabled)
            Fallback(ctbRun ? "the party escaped" : "CTB handed Bribe to native battle flow");
    }

    private static bool PumpCtbFailedRunDialog()
    {
        if (_ctbFailedRunDialog == FailedRunDialog.None) return false;
        if (Battle.battle_work is not { battle_phase_no: 4 } work || work.Pointer != _battle || _fellBack)
        {
            _ctbFailedRunDialog = FailedRunDialog.None;
            return false;
        }
        var manager = Manager.Instance;
        if (_ctbFailedRunDialog == FailedRunDialog.AwaitingConfirm)
        {
            if (Time.frameCount != _ctbRunDialogInputFrame && Application.isFocused && Time.timeScale > 0f
                && manager.IsOpenedDialogUI() && GSD1.OldSrcBase.sys_work is { } pad && (pad.PadTrig & ConfirmButton) != 0)
            {
                manager.CloseDialogWindowUI();
                _ctbFailedRunDialog = FailedRunDialog.Closing;
                _ctbRunDialogInputFrame = Time.frameCount;
            }
            return true;
        }
        if (Time.frameCount == _ctbRunDialogInputFrame || !manager.IsClosedDialogUI()) return true;
        _ctbFailedRunDialog = FailedRunDialog.None;
        Trace("CTB resumed turn order after the failed Run message closed.");
        return false;
    }

    private static bool NativeEscapeOwnsRound(int round)
        => _escapePenaltyRound >= 0 && round <= _escapePenaltyRound;

    internal static void SkipNativeAutoConfirmation()
    {
        if (!NativeAutoOwnsBattle) return;
        try
        {
            var work = Battle.battle_work;
            var window = Battle.window_work;
            if (work is not { battle_phase_no: 2, is_auto: true, auto_turn: 0 } || work.Pointer != _battle
                || window is not { window_step: 0 } || !Manager.IsInstantiated) return;

            // Enter the stock confirmed branch so it still initializes the Auto cancel controls.
            var manager = Manager.Instance;
            manager.HideMarkerUI();
            manager.SetCursorUI(WindowType.Unit, -1);
            work.auto_turn = 1;
            window.window_index = 0;
            window.window_step = ConfirmedLastWindowStep;
            Trace("ATB skipped the native Auto confirmation.");
        }
        catch (Exception error) { Trace("ATB could not skip the native Auto confirmation: " + error.Message); }
    }

    private static bool TryGetNativeMenu(NativeHeroMenu menu, out GSD1.BATTLE_WORK work,
        out Battle.WINDOW_WORK window, out GSD1.PM_DATA actor)
    {
        work = Battle.battle_work!;
        window = Battle.window_work!;
        actor = null!;
        if (work is null || work.Pointer != _battle || window is null || window.Pointer != menu.Window
            || work.pm_data is not { } rows || menu.Slot >= rows.Count) return false;
        actor = rows[menu.Slot];
        return actor is not null && actor.Pointer == menu.Actor;
    }

    private static void DisableNativeReadyMenus(string reason)
    {
        _nativeMenusFailed = true;
        ClearNativeReadyMenu();
        ReleaseNativeTargetPanels();
        Trace("ATB switched to the preview command prompt because " + reason + ".");
    }

    private static void ClearNativeReadyMenu(bool keepTargetPanels = false)
    {
        var menu = _nativeMenu;
        _nativeMenu = null;
        if (menu is null) return;
        try
        {
            if (Battle.battle_work?.Pointer != _battle || Battle.window_work is not { } window
                || window.Pointer != menu.Window) return;
            if (menu.Draft.Kind == Item && !menu.ItemHandedToQueue) SharedBattleItemBridge.Release(menu.Slot);
            if (!_pumpingNativeMenu && menu.Step != IntPtr.Zero)
            {
                var chapter = GSD1.ChapterManager.GR1Instance;
                if (chapter?.g1_game_step is { } steps && menu.Depth < steps.Count
                    && steps[menu.Depth]?.Pointer == menu.Step)
                {
                    steps[menu.Depth] = null;
                    chapter.game_seq[menu.Depth] = 0;
                    chapter.current_func_name[menu.Depth] = string.Empty;
                }
            }
            if (!menu.WindowsClosed) CloseNativeHeroWindows(window, keepTargetPanels || menu.Queued);
        }
        catch (Exception error) { Trace("ATB native menu cleanup failed: " + error.Message); }
    }

    private static void CloseNativeHeroWindows(Battle.WINDOW_WORK window, bool keepTargetPanels = false)
    {
        if (!keepTargetPanels) _nativeTargetPanelsHeld = false;
        if (Manager.IsInstantiated)
        {
            var manager = Manager.Instance;
            if (keepTargetPanels)
            {
                // Closing Unit/Enemy here leaves an animation that can hide them on the next prompt.
                foreach (var type in HeroCommandWindows)
                    if (!manager.IsCloseWindowUI(type)) manager.CloseWindowUI(type);
                manager.SetCursorUI(WindowType.Unit, -1);
                manager.SetCursorUI(WindowType.Enemy, -1);
            }
            else manager.CloseAllWindowUI();
            manager.CloseTipsUI();
        }
        if (window.kurukuru is { } rings)
            for (int i = 0; i < rings.Count; i++)
                rings[i]?.Close();
    }

    private static void ReleaseNativeTargetPanels()
    {
        if (!_nativeTargetPanelsHeld) return;
        _nativeTargetPanelsHeld = false;
        try
        {
            if (!Manager.IsInstantiated || Manager.Instance is not { battleWindow: not null } manager) return;
            manager.CloseWindowUI(WindowType.Unit);
            manager.CloseWindowUI(WindowType.Enemy);
        }
        catch (Exception error) { Trace("ATB target panel cleanup failed: " + error.Message); }
    }

    private static void RefreshHeldNativePartyStatus()
    {
        if (!_nativeTargetPanelsHeld) return;
        try
        {
            if (Manager.IsInstantiated && Battle.battle_work is { battle_phase_no: 4 })
                Battle.disp_hp();
        }
        catch (Exception error) { Trace("ATB party status refresh failed: " + error.Message); }
    }

    private static void ResetNativeReadyState()
    {
        _ctbTurnStep = IntPtr.Zero;
        ClearNativeReadyMenu();
        ReleaseNativeTargetPanels();
        _nativeMenusFailed = false;
        _escapePenaltyRound = -1;
        _ctbFailedRunDialog = FailedRunDialog.None;
        NativeAutoOwnsBattle = false;
    }
}

// The first window step opens the stock party menu. Return Fight to player_command before it draws.
[HarmonyPatch(typeof(Battle), nameof(Battle.battle_window01))]
internal static class NativeReadyOuterMenuPatch
{
    private static bool Prefix(ref int __result)
    {
        if (!ActiveBattleState.TrySkipNativeOuterMenu()) return true;
        __result = 0;
        return false;
    }
}

[HarmonyPatch(typeof(Battle), nameof(Battle.last_window))]
internal static class NativeAutoConfirmationPatch
{
    private static void Prefix() => ActiveBattleState.SkipNativeAutoConfirmation();
}

[HarmonyPatch(typeof(Battle), nameof(Battle.g1_battle_turn))]
internal static class NativeReadyTurnPatch
{
    private static bool Prefix(int __0, ref int __result, out int __state)
    {
        __state = ActiveBattleState.EnterNativeTurn(__0);
        if (!ActiveBattleState.TryHandleNativeTurnInput(__0)) return true;
        __result = -1;
        return false;
    }

    private static void Postfix(int __state) => ActiveBattleState.LeaveNativeTurn(__state);
}

[HarmonyPatch(typeof(Battle), nameof(Battle.command01))]
internal static class NativeReadyCommandPatch
{
    private static bool Prefix(int __0, ref int __result, out bool __state)
    {
        try
        {
            if (!ActiveBattleState.HandleNativeHeroRows(__0, out __state)) return true;
            __result = ActiveBattleState.NativeHeroRowResult;
        }
        catch (Exception error)
        {
            ActiveBattleState.NoteNativeMenuError(error);
            __state = false;
            __result = 0;
        }
        return false;
    }

    private static void Postfix(bool __state, int __0, int __result)
    {
        if (__state)
        {
            try { ActiveBattleState.RefreshNativeHeroRows(); }
            catch (Exception error) { ActiveBattleState.NoteNativeMenuError(error); }
        }
        // Postfix runs even when our own movement/Wait-row prefix consumes input.
        ActiveBattleState.ObserveCtbCommandPreview(__0, __result);
    }
}

[HarmonyPatch(typeof(Battle), nameof(Battle.command99))]
internal static class NativeReadyBackPatch
{
    private static bool Prefix(int __0, ref int __result)
        => !ActiveBattleState.TryEndNativeReadyMenu(__0, canceled: true, out __result);
}
