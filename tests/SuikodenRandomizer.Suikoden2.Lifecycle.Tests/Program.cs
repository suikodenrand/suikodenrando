using System.Reflection;
using System.Text;
using System.Text.Json;
using SuikodenRandomizer.Suikoden2Plugin;

// Links the actual production lifecycle/capture files. Fake API methods throw
// if invoked, and no shipped game assembly is referenced or executed.
var root = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "sandbox-" + Guid.NewGuid().ToString("N"));
BepInEx.Paths.GameRootPath = Path.Combine(root, "fake-game");
Directory.CreateDirectory(BepInEx.Paths.GameRootPath);
File.WriteAllText(Path.Combine(BepInEx.Paths.GameRootPath, "GameAssembly.dll"), "test-only-binary");
string metadataDirectory = Path.Combine(BepInEx.Paths.GameRootPath, "Suikoden I and II HD Remaster_Data", "il2cpp_data", "Metadata");
Directory.CreateDirectory(metadataDirectory);
File.WriteAllText(Path.Combine(metadataDirectory, "global-metadata.dat"), "test-only-metadata");
Gsd2Plugin.NativeCheckpointExportDirectory = Path.Combine(root, "exports");
Gsd2Plugin.NativeCheckpointCapture = true;
int assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
object? Call(Type type, string name, params object?[] arguments) => type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);
bool EnterManual()
{
    object?[] args = [false];
    Call(typeof(Gsd2ManualSaveCaptureScope), "Prefix", args);
    return (bool)args[0]!;
}
Exception? ExitManual(bool state, Exception? exception = null) => (Exception?)Call(typeof(Gsd2ManualSaveCaptureScope), "Finalizer", exception, state);
Gsd2SaveScope? EnterSave(bool clear, bool expectedNativeExecution = true)
{
    object?[] args = [null, clear, false, null];
    Check((bool)Call(typeof(Gsd2SavePreparationPatch), "Prefix", args)! == expectedNativeExecution,
        expectedNativeExecution ? "SaveFile prefix preserves native execution" : "pending save stays refused outside verified boundary");
    return (Gsd2SaveScope?)args[3];
}
Exception? ExitSave(Gsd2SaveScope? scope, Exception? exception = null) => (Exception?)Call(typeof(Gsd2SavePreparationPatch), "Finalizer", exception, scope);
void Storage(string json)
{
    object?[] args = [json, null, false];
    Check((bool)Call(typeof(Gsd2StorageBindingPatch), "Prefix", args)!, "SystemSave prefix preserves native execution");
    Check((bool)args[2]! == false, "capture does not replace native save result");
}
int CandidateCount() => Directory.Exists(Gsd2Plugin.NativeCheckpointExportDirectory)
    ? Directory.GetFiles(Gsd2Plugin.NativeCheckpointExportDirectory, "*.json").Count(path => !path.EndsWith(".receipt.json")) : 0;
const string nativeJson = "{\r\n \"native\":\"Riou テスト\", \"flags\":[0,255] }\n";

bool manual = EnterManual();
var ordinary = EnterSave(false);
Check(Gsd2ManualSaveCaptureScope.Depth == 1 && ordinary!.CaptureAllowed, "ordinary confirmed save is admitted for capture");
Storage(nativeJson);
ExitSave(ordinary);
ExitManual(manual);
Check(CandidateCount() == 1, "ordinary manual save exports one native candidate");
string file = Directory.GetFiles(Gsd2Plugin.NativeCheckpointExportDirectory, "*.json").Single(path => !path.EndsWith(".receipt.json"));
Check(File.ReadAllBytes(file).SequenceEqual(new UTF8Encoding(false, true).GetBytes(nativeJson)), "lifecycle exports exact native JSON bytes");
Check(Gsd2SaveScope.Current is null && Gsd2ManualSaveCaptureScope.Depth == 0, "ordinary save restores both scopes");

manual = EnterManual();
var clear = EnterSave(true);
Check(!clear!.CaptureAllowed, "confirmed clear save is excluded");
Storage(nativeJson);
ExitSave(clear);
ExitManual(manual);
Check(CandidateCount() == 1, "clear save exports no candidate");

var automatic = EnterSave(false);
Check(!automatic!.CaptureAllowed, "ordinary save outside confirmation is excluded");
Storage(nativeJson);
ExitSave(automatic);
Check(CandidateCount() == 1, "automatic or reusable-wrapper save exports no candidate");

manual = EnterManual();
Storage(nativeJson);
ExitManual(manual);
Check(CandidateCount() == 1, "direct SystemSave without SaveFile scope is excluded");

var sentinel = new Gsd2SaveScope();
Gsd2SaveScope.Current = sentinel;
bool outerManual = EnterManual();
bool innerManual = EnterManual();
Check(Gsd2ManualSaveCaptureScope.Depth == 2, "nested confirmation depth is retained");
var outerSave = EnterSave(false);
var innerSave = EnterSave(true);
Check(ReferenceEquals(innerSave!.Previous, outerSave) && ReferenceEquals(outerSave!.Previous, sentinel), "nested SaveFile scopes chain previous state");
var nativeException = new InvalidOperationException("injected native failure");
Check(ReferenceEquals(ExitSave(innerSave, nativeException), nativeException) && ReferenceEquals(Gsd2SaveScope.Current, outerSave), "exceptional inner save finalizer restores outer scope and preserves exception");
Check(ReferenceEquals(ExitManual(innerManual, nativeException), nativeException) && Gsd2ManualSaveCaptureScope.Depth == 1, "exceptional inner confirmation restores outer depth and preserves exception");
Check(ReferenceEquals(ExitSave(outerSave, nativeException), nativeException) && ReferenceEquals(Gsd2SaveScope.Current, sentinel), "exceptional outer save restores pre-existing scope");
Check(ReferenceEquals(ExitManual(outerManual, nativeException), nativeException) && Gsd2ManualSaveCaptureScope.Depth == 0, "exceptional outer confirmation restores zero depth");
Gsd2SaveScope.Current = null;

manual = EnterManual();
ordinary = EnterSave(false);
bool childFresh = false, childRestored = false;
var thread = new Thread(() =>
{
    childFresh = Gsd2ManualSaveCaptureScope.Depth == 0 && Gsd2SaveScope.Current is null;
    bool childState = EnterManual();
    var childSave = EnterSave(true);
    ExitSave(childSave);
    ExitManual(childState);
    childRestored = Gsd2ManualSaveCaptureScope.Depth == 0 && Gsd2SaveScope.Current is null;
});
thread.Start(); thread.Join();
Check(childFresh && childRestored, "capture scopes are isolated and restored on another thread");
Check(Gsd2ManualSaveCaptureScope.Depth == 1 && ReferenceEquals(Gsd2SaveScope.Current, ordinary), "other thread leaves main thread scopes intact");
ExitSave(ordinary); ExitManual(manual);

Gsd2Plugin.NativeCheckpointCapture = false;
manual = EnterManual();
Check(!manual && Gsd2ManualSaveCaptureScope.Depth == 0, "disabled mode does not enter manual scope");
automatic = EnterSave(false);
Check(automatic is null && Gsd2SaveScope.Current is null, "disabled mode with no avatar runtime does not create save scope");
Storage(nativeJson); ExitSave(automatic); ExitManual(manual);
Check(CandidateCount() == 1, "disabled mode exports no candidate");

Gsd2Runtime.Current = new Gsd2Runtime { ThrowEndSave = true };
sentinel = new Gsd2SaveScope(); Gsd2SaveScope.Current = sentinel;
var avatarSave = EnterSave(false);
Check(avatarSave!.BindCampaign && Gsd2Runtime.Current.BeginSaveCalls == 1, "avatar campaign scope still begins normally");
bool endThrew = false;
try { ExitSave(avatarSave, nativeException); }
catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { endThrew = true; }
Check(endThrew && ReferenceEquals(Gsd2SaveScope.Current, sentinel), "save scope restores even if runtime EndSave throws");
Gsd2Runtime.Current = null; Gsd2SaveScope.Current = null;
Check(Gsd2ManualSaveCaptureScope.Depth == 0 && Gsd2SaveScope.Current is null, "all lifecycle scopes finish clean");

bool EnterNativeNewGame(bool isNewGame)
{
    object?[] args = [new TitleChapter._MapSetup_d__43 { isNewgame = isNewGame }, false];
    Call(typeof(Gsd2ModernNewGamePatch), "Prefix", args);
    return (bool)args[1]!;
}
Exception? ExitNativeNewGame(bool state, Exception? exception = null) =>
    (Exception?)Call(typeof(Gsd2ModernNewGamePatch), "Finalizer", exception, state);

Gsd2Runtime.Current = new Gsd2Runtime { PendingNewGame = true };
bool nativeScope = EnterNativeNewGame(true);
Check(nativeScope && Gsd2ModernNewGamePatch.NativeNewGameDepth == 1, "new-game MoveNext opens native initialization scope");
var initialAutoSave = EnterSave(false);
Check(initialAutoSave!.BindCampaign && !Gsd2Runtime.Current.PendingNewGame && Gsd2Runtime.Current.Initializations == 1,
    "initial native autosave is admitted only after completion");
Check(Gsd2Runtime.Current.CallOrder.SequenceEqual(new[] { "complete-new-game", "begin-save" }),
    "completion precedes BeginSave at native final autosave boundary");
Storage(nativeJson);
Check(Gsd2Runtime.Current.BindCalls == 1, "initial native autosave reaches profile snapshot binding");
ExitSave(initialAutoSave);
Call(typeof(Gsd2ModernNewGamePatch), "Postfix", new TitleChapter._MapSetup_d__43 { isNewgame = true }, false);
ExitNativeNewGame(nativeScope);
Check(Gsd2Runtime.Current.CompletionCalls == 2 && Gsd2Runtime.Current.Initializations == 1,
    "fallback postfix preserves one-shot runtime completion contract");
Check(CandidateCount() == 1, "initial avatar autosave does not enter native manual-capture exporter");

Gsd2Runtime.Current = new Gsd2Runtime { PendingNewGame = true };
var refused = EnterSave(false, false);
Check(refused is null && Gsd2Runtime.Current.PendingNewGame && Gsd2Runtime.Current.CompletionCalls == 0,
    "pending ordinary save outside new-game scope does not initialize campaign");
nativeScope = EnterNativeNewGame(false);
Check(!nativeScope && Gsd2ModernNewGamePatch.NativeNewGameDepth == 0, "non-new-game MoveNext does not open initialization scope");
refused = EnterSave(false, false);
Check(refused is null && Gsd2Runtime.Current.CompletionCalls == 0, "non-new-game save remains refused while pending");
ExitNativeNewGame(nativeScope);
nativeScope = EnterNativeNewGame(true);
refused = EnterSave(true, false);
Check(refused is null && Gsd2Runtime.Current.PendingNewGame && Gsd2Runtime.Current.CompletionCalls == 0,
    "clear save cannot trigger pending new-game completion");
ExitNativeNewGame(nativeScope);

bool outerNative = EnterNativeNewGame(true);
bool innerNative = EnterNativeNewGame(true);
Check(Gsd2ModernNewGamePatch.NativeNewGameDepth == 2, "nested native MoveNext scopes retain depth");
Check(ReferenceEquals(ExitNativeNewGame(innerNative, nativeException), nativeException)
    && Gsd2ModernNewGamePatch.NativeNewGameDepth == 1, "exceptional nested native finalizer restores outer depth");
bool workerNativeFresh = false, workerNativeRestored = false;
thread = new Thread(() =>
{
    workerNativeFresh = Gsd2ModernNewGamePatch.NativeNewGameDepth == 0;
    bool state = EnterNativeNewGame(true); ExitNativeNewGame(state, nativeException);
    workerNativeRestored = Gsd2ModernNewGamePatch.NativeNewGameDepth == 0;
});
thread.Start(); thread.Join();
Check(workerNativeFresh && workerNativeRestored && Gsd2ModernNewGamePatch.NativeNewGameDepth == 1,
    "native new-game depth is isolated across threads");
Check(ReferenceEquals(ExitNativeNewGame(outerNative, nativeException), nativeException)
    && Gsd2ModernNewGamePatch.NativeNewGameDepth == 0, "exceptional outer native finalizer restores zero depth");

Gsd2Runtime.Current = new Gsd2Runtime { PendingNewGame = true, ThrowCompletion = true };
nativeScope = EnterNativeNewGame(true);
refused = EnterSave(false, false);
Check(refused is null && Gsd2Runtime.Current.BeginSaveCalls == 0 && Gsd2Runtime.Current.PendingNewGame,
    "failed completion cannot begin native save");
ExitNativeNewGame(nativeScope, nativeException);
Check(Gsd2ModernNewGamePatch.NativeNewGameDepth == 0 && Gsd2SaveScope.Current is null,
    "completion failure leaves no native or save scope behind");

Gsd2Runtime.Current = null;
Gsd2Plugin.NativeCheckpointCapture = true;
nativeScope = EnterNativeNewGame(true);
initialAutoSave = EnterSave(false); Storage(nativeJson); ExitSave(initialAutoSave); ExitNativeNewGame(nativeScope);
Check(!initialAutoSave!.CaptureAllowed && CandidateCount() == 1,
    "native-capture mode preserves initial autosave but exports only manual confirmation");
Check(Gsd2ModernNewGamePatch.NativeNewGameDepth == 0 && Gsd2ManualSaveCaptureScope.Depth == 0 && Gsd2SaveScope.Current is null,
    "all three scopes finish clean");
int baselineLifecycleAssertions = assertions;

T Reject<T>(Action run) where T : Exception
{
    try { run(); }
    catch (TargetInvocationException exception) when (exception.InnerException is T inner) { return inner; }
    catch (T exception) { return exception; }
    throw new Exception("Expected " + typeof(T).Name);
}
GSD2SaveData.TmpSave CampSnapshot() => new()
{
    version = 100, px = -19, py = 71,
    game_data = new GAME_DATA { area_no=1,town_no=10,map_no=2,mem_type=1,area_no2=1,town_no2=1 }
};
void SerializePrefix(Il2CppSystem.Object? value) => Call(typeof(Gsd2InitialSnapshotPositionPatch), "Prefix", value);
Gsd2SaveScope NewCampScope(bool prepare = true)
{
    Gsd2Plugin.NativeCheckpointCapture = false;
    Gsd2Runtime.Current = new Gsd2Runtime { NeedsCampInitialSavePosition=true };
    var scope = new Gsd2SaveScope { Runtime=Gsd2Runtime.Current, BindCampaign=true, InitializeCampPosition=true };
    Gsd2SaveScope.Current = scope;
    if (prepare) SerializePrefix(CampSnapshot());
    return scope;
}
bool StoreResult(string json, out bool? callbackValue)
{
    bool? observed = null;
    Il2CppSystem.Action<bool> callback = result => observed=result;
    object?[] args = [json,callback,true];
    bool execute = (bool)Call(typeof(Gsd2StorageBindingPatch), "Prefix", args)!;
    callbackValue=observed;
    if (!execute) Check(!(bool)args[2]!, "refused initial storage returns false");
    return execute;
}
const string campJson = "{ \"unknown\": {\"px\":999,\"unicode\":\"Riou \\u30c6\\u30b9\\u30c8\",\"huge\":900719925474099312345678901234567890,\"exp\":1e+12345},\r\n\"py\":206,\"game_data\":{\"unknown\":1,\"area_no2\":1,\"town_no2\":1,\"mem_type\":1,\"map_no\":2,\"town_no\":10,\"area_no\":1},\"version\":100,\"px\":167,\"duplicate_unknown\":0,\"duplicate_unknown\":1 }\n";

var positionScope = NewCampScope(false);
var snapshot = CampSnapshot();var locationReference=snapshot.game_data;var payloadReference=snapshot.UntouchedPayload;IntPtr snapshotPointer=snapshot.Pointer;
int snapshotWrites=0;
GSD2SaveData.TmpSave.BeforePositionWrite=(_,_)=>snapshotWrites++;
SerializePrefix(new Il2CppSystem.Object());SerializePrefix(null);
Check(!positionScope.CampPositionPrepared && positionScope.CampSnapshot==IntPtr.Zero && positionScope.CampPositionFailure is null,
    "unrelated serializer objects cannot consume camp snapshot identity");
SerializePrefix(snapshot);
Check(snapshot.px==167 && snapshot.py==206 && snapshotWrites==2 && positionScope.CampPositionPrepared && positionScope.CampSnapshot==snapshotPointer,
    "initial snapshot changes precisely the two position ints");
Check(snapshot.version==100 && snapshot.Pointer==snapshotPointer && ReferenceEquals(snapshot.game_data,locationReference) && ReferenceEquals(snapshot.UntouchedPayload,payloadReference)
    && locationReference.area_no==1 && locationReference.town_no==10 && locationReference.map_no==2 && locationReference.mem_type==1 && locationReference.area_no2==1 && locationReference.town_no2==1,
    "initial position helper preserves all native world and payload references");
SerializePrefix(snapshot);
Check(snapshotWrites==2 && positionScope.CampPositionFailure is null, "same snapshot serialization repeats without extra native writes");
GSD2SaveData.TmpSave.BeforePositionWrite=null;
Check(StoreResult(campJson,out var storageCallback) && storageCallback is null && positionScope.Ready,
    "valid initial camp JSON continues to native storage");
Check(ReferenceEquals(Gsd2Runtime.Current!.LastBoundJson,campJson) && Gsd2Runtime.Current.CampBindings==1 && !Gsd2Runtime.Current.NeedsCampInitialSavePosition,
    "unknown JSON content passes the exact same string to binding and consumes initial binding once");

positionScope=NewCampScope();var secondSnapshot=CampSnapshot();SerializePrefix(secondSnapshot);
Check(positionScope.CampPositionFailure is not null && secondSnapshot.px==-19 && secondSnapshot.py==71 && Gsd2Runtime.Current!.FaultReason is not null,
    "a second snapshot identity faults without changing that other object");
Check(!StoreResult(campJson,out storageCallback) && storageCallback==false && Gsd2Runtime.Current!.BindCalls==0 && !positionScope.Ready,
    "second snapshot failure refuses storage before binding");

positionScope=NewCampScope(false);snapshot=CampSnapshot();SerializePrefix(snapshot);snapshot.py=207;SerializePrefix(snapshot);
Check(positionScope.CampPositionFailure is not null && !StoreResult(campJson,out storageCallback) && Gsd2Runtime.Current!.BindCalls==0,
    "changed repeated snapshot cannot use a stale prepared receipt");

foreach (var invalidate in new Action<GSD2SaveData.TmpSave>[]
{
    s=>s.Pointer=IntPtr.Zero,s=>s.version=99,s=>s.game_data=null!,s=>s.game_data.area_no=0,s=>s.game_data.town_no=1,
    s=>s.game_data.map_no=0,s=>s.game_data.mem_type=0,s=>s.game_data.area_no2=0,s=>s.game_data.town_no2=0
})
{
    positionScope=NewCampScope(false);snapshot=CampSnapshot();invalidate(snapshot);int x=snapshot.px,y=snapshot.py;SerializePrefix(snapshot);
    Check(!positionScope.CampPositionPrepared && positionScope.CampPositionFailure is not null && snapshot.px==x && snapshot.py==y,
        "wrong native snapshot schema or destination is rejected before coordinate writes");
    Check(!StoreResult(campJson,out storageCallback) && storageCallback==false && Gsd2Runtime.Current!.BindCalls==0 && Gsd2Runtime.Current.FaultReason is not null,
        "snapshot schema refusal faults and never reaches binding");
}

foreach (bool afterWrite in new[] {false,true})
{
    positionScope=NewCampScope(false);snapshot=CampSnapshot();int x=snapshot.px,y=snapshot.py;bool failedSetter=false;
    System.Action<string,int> failure=(name,value)=>{if(!failedSetter && name=="py" && value==206){failedSetter=true;throw new InvalidOperationException("injected snapshot setter");}};
    if(afterWrite)GSD2SaveData.TmpSave.AfterPositionWrite=failure;else GSD2SaveData.TmpSave.BeforePositionWrite=failure;
    SerializePrefix(snapshot);GSD2SaveData.TmpSave.BeforePositionWrite=null;GSD2SaveData.TmpSave.AfterPositionWrite=null;
    Check(failedSetter && snapshot.px==x && snapshot.py==y && !positionScope.CampPositionPrepared && positionScope.CampPositionFailure is not null,
        "snapshot setter failure restores original temporary coordinates");
    Check(!StoreResult(campJson,out storageCallback) && Gsd2Runtime.Current!.BindCalls==0 && storageCallback==false,
        "partial snapshot preparation cannot store or bind");
}

var malformedJson = new[]
{
    "not JSON", "[]", "null", "{}", campJson+"{}",
    campJson.Replace("\"version\":100","\"version\":99"),campJson.Replace("\"version\":100","\"version\":100,\"version\":100"),
    campJson.Replace("\"px\":167","\"px\":168"),campJson.Replace("\"py\":206","\"py\":207"),
    campJson.Replace("\"px\":167","\"px\":\"167\""),campJson.Replace("\"px\":167","\"px\":167.0"),campJson.Replace("\"px\":167","\"px\":1.67e2"),
    campJson.Replace("\"px\":167","\"px\":167,\"px\":167"),campJson.Replace("\"px\":167","\"px\":167,\"p\\u0078\":167"),
    campJson.Replace("\"py\":206,", ""),campJson.Replace("\"game_data\":{", "\"game_data\":null,\"game_data\":{"),
    campJson.Replace("\"area_no\":1", "\"area_no\":0"),campJson.Replace("\"town_no\":10", "\"town_no\":1"),campJson.Replace("\"map_no\":2", "\"map_no\":0"),
    campJson.Replace("\"mem_type\":1", "\"mem_type\":0"),campJson.Replace("\"area_no2\":1", "\"area_no2\":0"),campJson.Replace("\"town_no2\":1", "\"town_no2\":0"),
    campJson.Replace("\"area_no\":1", "\"area_no\":1,\"area_no\":1")
};
foreach (string json in malformedJson)
{
    positionScope=NewCampScope();
    Check(!StoreResult(json,out storageCallback) && storageCallback==false && !positionScope.Ready && Gsd2Runtime.Current!.BindCalls==0 && Gsd2Runtime.Current.FaultReason is not null,
        "invalid or ambiguous initial JSON is refused before binding and faults admission");
}
positionScope=NewCampScope(false);
Check(!StoreResult(campJson,out storageCallback) && Gsd2Runtime.Current!.BindCalls==0 && storageCallback==false,
    "storage cannot bypass the native snapshot boundary with plausible JSON alone");
positionScope=NewCampScope();Gsd2Runtime.Current!.ThrowBind=true;
Check(!StoreResult(campJson,out storageCallback) && !positionScope.Ready && Gsd2Runtime.Current.CampBindings==0 && storageCallback==false && Gsd2Runtime.Current.FaultReason is not null,
    "binding failure refuses storage without consuming camp initial receipt");

Gsd2SaveScope.Current=null;Gsd2Runtime.Current=new Gsd2Runtime{PendingNewGame=true,NeedsCampInitialSavePosition=true};
nativeScope=EnterNativeNewGame(true);positionScope=EnterSave(false)!;
Check(positionScope.InitializeCampPosition && Gsd2Runtime.Current.CompletionBoundaryModes.SequenceEqual(new[]{true}),
    "only exact final native boundary arms initial camp position preparation");
snapshot=CampSnapshot();SerializePrefix(snapshot);Check(StoreResult(campJson,out storageCallback),"scoped first camp storage succeeds");ExitSave(positionScope);ExitNativeNewGame(nativeScope);
var firstBoundRuntime=Gsd2Runtime.Current;
positionScope=EnterSave(false)!;snapshot=CampSnapshot();SerializePrefix(snapshot);Check(!positionScope.InitializeCampPosition && snapshot.px==-19 && snapshot.py==71,
    "subsequent ordinary save retains actual coordinates");Storage(nativeJson);ExitSave(positionScope);
Check(firstBoundRuntime.CampBindings==1 && Gsd2SaveScope.Current is null,"later manual binding does not reuse initial camp coordinate receipt");

foreach (bool clearSave in new[]{false,true})
{
    Gsd2Runtime.Current=new Gsd2Runtime{NeedsCampInitialSavePosition=clearSave};
    nativeScope=EnterNativeNewGame(true);positionScope=EnterSave(clearSave)!;snapshot=CampSnapshot();SerializePrefix(snapshot);
    Check(!positionScope.InitializeCampPosition && snapshot.px==-19 && snapshot.py==71,"Opening or clear snapshots are not rewritten");
    Storage(nativeJson);ExitSave(positionScope);ExitNativeNewGame(nativeScope);
}
Gsd2Plugin.NativeCheckpointCapture=true;Gsd2Runtime.Current=null;manual=EnterManual();positionScope=EnterSave(false)!;snapshot=CampSnapshot();SerializePrefix(snapshot);
Check(!positionScope.InitializeCampPosition && snapshot.px==-19 && snapshot.py==71,"native capture never rewrites original snapshot coordinates");
ExitSave(positionScope);ExitManual(manual);Gsd2Plugin.NativeCheckpointCapture=false;

positionScope=NewCampScope(false);var outerPositionScope=positionScope;
var innerPositionScope=new Gsd2SaveScope{Previous=positionScope,BindCampaign=true};Gsd2SaveScope.Current=innerPositionScope;
snapshot=CampSnapshot();SerializePrefix(snapshot);Check(snapshot.px==-19 && !outerPositionScope.CampPositionPrepared,"nested unrelated scope cannot consume outer camp expectation");
Check(ReferenceEquals(ExitSave(innerPositionScope,nativeException),nativeException)&&ReferenceEquals(Gsd2SaveScope.Current,outerPositionScope),"nested position finalizer restores outer expectation");
SerializePrefix(snapshot);Check(outerPositionScope.CampPositionPrepared && StoreResult(campJson,out storageCallback),"outer camp expectation still works after nested finalizer");
ExitSave(outerPositionScope,nativeException);Check(Gsd2SaveScope.Current is null,"exceptional position scope cannot leak into later serializers");
snapshot=CampSnapshot();SerializePrefix(snapshot);Check(snapshot.px==-19 && snapshot.py==71,"serializer after finalization is unaffected");

GAME_WORK FreshWorld() => new(){sys_work=new SYS_WORK{game_mode=1,game_step=Enumerable.Repeat(0,13).ToList()}};
MACHICON CampMap() => new(){ano=1,vno=10,mno=2,memtyp=1,px=1336,py=1648};
var freshWorld=FreshWorld();var systemReference=freshWorld.sys_work;var stepReference=freshWorld.sys_work.game_step;
Gsd2CampNativeGuard.ValidateFresh(freshWorld);
Check(ReferenceEquals(systemReference,freshWorld.sys_work)&&ReferenceEquals(stepReference,freshWorld.sys_work.game_step)&&freshWorld.sys_work.game_step.All(step=>step==0),"actual fresh guard is read-only");
foreach (var invalidate in new Action<GAME_WORK>[] {g=>g.sys_work=null!,g=>g.sys_work.game_mode=0,g=>g.sys_work.game_mode=2,g=>g.sys_work.game_step=null!,g=>g.sys_work.game_step=new List<int>(new int[12]),g=>g.sys_work.game_step[1]=1,g=>g.sys_work.mcon=CampMap()})
{
    freshWorld=FreshWorld();invalidate(freshWorld);Reject<InvalidOperationException>(()=>Gsd2CampNativeGuard.ValidateFresh(freshWorld));Check(true,"actual native guard rejects a nonfresh chapter/controller state");
}
Reject<InvalidOperationException>(()=>Gsd2CampNativeGuard.ValidateFresh(null!));Check(true,"actual native guard rejects missing GAME_WORK");
var map=CampMap();Gsd2CampNativeGuard.ValidateInitialMap(map);Check(true,"actual initial-map guard accepts pixel coordinates1336,1648");
foreach(var invalidate in new Action<MACHICON>[] {m=>m.ano=0,m=>m.vno=1,m=>m.mno=0,m=>m.memtyp=0,m=>m.px=167,m=>m.py=206})
{
    map=CampMap();invalidate(map);Reject<InvalidOperationException>(()=>Gsd2CampNativeGuard.ValidateInitialMap(map));Check(true,"actual initial-map guard rejects wrong scene or save-unit coordinates");
}
map=CampMap();Check(!Gsd2CampNativeGuard.TryVerifyPlayer(map),"player guard waits for native event controller");map.econ=new EVENTCON{px=167,py=206};
Check(Gsd2CampNativeGuard.TryVerifyPlayer(map),"actual player guard accepts saved-unit coordinates167,206");
foreach(var invalidate in new Action<MACHICON>[] {m=>m.ano=0,m=>m.vno=1,m=>m.mno=0,m=>m.memtyp=0,m=>m.econ!.px=1336,m=>m.econ!.py=1648})
{
    map=CampMap();map.econ=new EVENTCON{px=167,py=206};invalidate(map);Reject<InvalidOperationException>(()=>Gsd2CampNativeGuard.TryVerifyPlayer(map));Check(true,"actual player guard rejects wrong scene or pixel-unit coordinates");
}

Gsd2SaveScope.Current=null;Check(Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"mirror guard permits unrelated calls without a save scope");
foreach(bool bind in new[]{false,true})foreach(bool initial in new[]{false,true})foreach(bool ready in new[]{false,true})
{
    Gsd2SaveScope.Current=new Gsd2SaveScope{BindCampaign=bind,InitializeCampPosition=initial,Ready=ready,CaptureAllowed=true};
    Check(Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite()==(!bind||!initial||ready),"mirror predicate permits capture/later/success and blocks unready initial campaign only");
}
foreach(bool bind in new[]{false,true})foreach(bool initial in new[]{false,true})foreach(bool ready in new[]{false,true})
{
    Gsd2SaveScope.Current=new Gsd2SaveScope{BindCampaign=bind,InitializeCampPosition=initial,Ready=ready,StorageRejected=true};
    Check(Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite()==!bind,"storage rejection blocks ordinary and camp S2 mirrors, preserving native capture");
}
positionScope=NewCampScope();Check(!StoreResult("{}",out storageCallback)&&!Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"invalid initial save cannot write the known Fix mirror");
positionScope=NewCampScope();Check(StoreResult(campJson,out storageCallback)&&Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"successful initial save may retain its ordinary Fix mirror");

// The second storage call must be rejected before writing a second sidecar.
positionScope=NewCampScope();Check(StoreResult(campJson,out storageCallback),"first storage call consumes camp receipt");
bool duplicateStored=StoreResult(campJson,out storageCallback);
Check(!duplicateStored && Gsd2Runtime.Current!.BindCalls==1 && Gsd2Runtime.Current.CampBindings==1&&!positionScope.Ready&&!Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"duplicate initial storage cannot bind twice or leave a ready Fix mirror scope");
Gsd2SaveScope.Current=null;Gsd2Runtime.Current=null;GSD2SaveData.TmpSave.BeforePositionWrite=null;GSD2SaveData.TmpSave.AfterPositionWrite=null;

// The production hook chain is neutral outside its owner, even with a stale
// campaign fault and a previously entered S2 ThreadStatic transaction.
bool executionAllowed=false;long ownershipEpoch=90;
Gsd2Runtime.ExecutionAllowed=()=>executionAllowed;Gsd2Runtime.OwnershipEpoch=()=>ownershipEpoch;
var foreignRuntime=new Gsd2Runtime {FaultReason="foreign S2 fault",PendingNewGame=true};Gsd2Runtime.Current=foreignRuntime;
Gsd2Plugin.NativeCheckpointCapture=true;
manual=EnterManual();Check(!manual&&Gsd2ManualSaveCaptureScope.Depth==0,"foreign manual confirmation opens no capture scope");
var foreignSave=EnterSave(false);Check(foreignSave is null&&foreignRuntime.BeginSaveCalls==0,"foreign SaveFile preserves original flow without campaign preparation");
nativeScope=EnterNativeNewGame(true);Check(!nativeScope&&Gsd2ModernNewGamePatch.NativeNewGameDepth==0,"foreign native coroutine opens no initialization scope");
Call(typeof(Gsd2NewGamePatch),"Prefix",new object?[]{null});
var staleScope=new Gsd2SaveScope {Runtime=foreignRuntime,BindCampaign=true,InitializeCampPosition=true,Ready=false};Gsd2SaveScope.Current=staleScope;
snapshot=CampSnapshot();SerializePrefix(snapshot);Check(snapshot.px==-19&&snapshot.py==71&&!staleScope.CampPositionPrepared&&staleScope.CampPositionFailure is null,"foreign shared serializer is neutral with stale camp scope");
Check(StoreResult("foreign JSON",out storageCallback)&&storageCallback is null&&foreignRuntime.BindCalls==0,"foreign shared storage neither binds nor refuses unrelated save");
Check(Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"foreign stale S2 scope does not block another title's Fix mirror");
ExitSave(staleScope);Check(foreignRuntime.EndSaveCalls==0&&Gsd2SaveScope.Current is null,"foreign save finalizer performs no native restoration");
ExitNativeNewGame(nativeScope);ExitManual(manual);

executionAllowed=true;ownershipEpoch=91;Gsd2Plugin.NativeCheckpointCapture=false;
var oldOwner=new Gsd2Runtime();Gsd2Runtime.Current=oldOwner;var oldScope=EnterSave(false)!;
ownershipEpoch++;var replacement=new Gsd2Runtime{PendingNewGame=true};Gsd2Runtime.Current=replacement;
Check(!StoreResult("new owner JSON",out storageCallback)&&storageCallback==false&&oldOwner.BindCalls==0&&replacement.BindCalls==0&&replacement.FaultReason is null,
    "stale old save scope refuses current-S2 storage without binding or faulting the replacement owner");
Check(oldScope.StorageRejected&&!Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"stale ordinary S2 storage veto also blocks Fix postfix mirror");
executionAllowed=false;Check(Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"rejected stale S2 scope stays neutral for a confirmed foreign mirror");executionAllowed=true;
ExitSave(oldScope,nativeException);Check(oldOwner.EndSaveCalls==0&&replacement.EndSaveCalls==0&&Gsd2SaveScope.Current is null,"stale finalizer restores scope without touching old or replacement native data");

var suspendedOwner=new Gsd2Runtime {ExecutionEpoch=ownershipEpoch-1,IsAdmitted=false,HasAdmittedCampaignBeforeSuspend=true};Gsd2Runtime.Current=suspendedOwner;
refused=EnterSave(false,false);Check(refused is null&&suspendedOwner.BeginSaveCalls==0,"suspended prior campaign refuses unbound current-S2 native save before preparation");
suspendedOwner.ExecutionEpoch=ownershipEpoch;
refused=EnterSave(false,false);Check(refused is null&&suspendedOwner.BeginSaveCalls==0,"an owned boundary alone cannot let the retired campaign save unbound");
suspendedOwner.ExecutionEpoch=ownershipEpoch-1;suspendedOwner.HasAdmittedCampaignBeforeSuspend=false;
var vanillaSave=EnterSave(false);Check(vanillaSave is null&&suspendedOwner.BeginSaveCalls==0,"no-profile vanilla S2 save stays neutral despite an old epoch");
suspendedOwner.HasAdmittedCampaignBeforeSuspend=true;executionAllowed=false;
vanillaSave=EnterSave(false);Check(vanillaSave is null&&suspendedOwner.BeginSaveCalls==0,"foreign save stays neutral even with a retired S2 campaign marker");
Gsd2Runtime.MayOwnSaveContext=()=>true;
int lostAdmissionCallbacks=0;bool? lostAdmissionResult=null;
Il2CppSystem.Action<bool,GSD2SaveData.TmpSave> lostAdmissionCallback=(success,_)=>{lostAdmissionCallbacks++;lostAdmissionResult=success;};
object?[] lostAdmissionArgs=[lostAdmissionCallback,false,true,null];
Check(!(bool)Call(typeof(Gsd2SavePreparationPatch),"Prefix",lostAdmissionArgs)!&&!(bool)lostAdmissionArgs[2]!&&lostAdmissionArgs[3] is null&&lostAdmissionCallbacks==1&&lostAdmissionResult==false&&suspendedOwner.BeginSaveCalls==0,
    "selected S2 with missing/probe-failed root refuses once before any save preparation");
suspendedOwner.HasAdmittedCampaignBeforeSuspend=false;
vanillaSave=EnterSave(false);Check(vanillaSave is null&&suspendedOwner.BeginSaveCalls==0,"selected S2 without any prior profile stays vanilla when its root is unavailable");
suspendedOwner.HasAdmittedCampaignBeforeSuspend=true;
var missingRootScope=new Gsd2SaveScope {Runtime=suspendedOwner,BindCampaign=true,Ready=true};Gsd2SaveScope.Current=missingRootScope;
Check(!StoreResult("selected S2 unavailable root",out storageCallback)&&storageCallback==false&&!missingRootScope.Ready&&suspendedOwner.BindCalls==0,
    "selected S2 unavailable-root storage refuses an existing scope without binding");
Check(missingRootScope.StorageRejected&&!Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"cached selected-S2 unavailable-root veto also blocks Fix mirror");
ExitSave(missingRootScope);Gsd2Runtime.MayOwnSaveContext=()=>Gsd2Runtime.ExecutionAllowed();
executionAllowed=true;Gsd2Runtime.Current=replacement;

replacement.PendingNewGame=false;replacement.BindReturnsFalse=true;
var noBindingScope=EnterSave(false)!;
Check(!StoreResult("no binding JSON",out storageCallback)&&storageCallback==false&&!noBindingScope.Ready,"a binder returning false cannot mark a native snapshot ready");
Check(noBindingScope.StorageRejected&&!Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite(),"ordinary false binder cannot leak rejected JSON to Fix mirror");
ExitSave(noBindingScope);replacement.BindReturnsFalse=false;
var changedBindingScope=EnterSave(false)!;replacement.DuringBind=()=>ownershipEpoch++;
Check(!StoreResult("changed binding epoch",out storageCallback)&&storageCallback==false&&!changedBindingScope.Ready&&replacement.FaultReason is null,"epoch change during binding refuses without faulting another campaign");
ExitSave(changedBindingScope);replacement.DuringBind=null;replacement.ExecutionEpoch=ownershipEpoch;
replacement.DuringBeginSave=()=>ownershipEpoch++;
refused=EnterSave(false,false);Check(refused is null&&Gsd2SaveScope.Current is null,"admission loss during BeginSave refuses before native SaveFile and Fix storage postfix");
replacement.DuringBeginSave=null;replacement.ExecutionEpoch=ownershipEpoch;
replacement.DuringBeginSave=()=>Gsd2Runtime.Current=new Gsd2Runtime();
refused=EnterSave(false,false);Check(refused is null&&Gsd2SaveScope.Current is null,"runtime replacement during BeginSave cannot allocate a misleading readyable scope");
Gsd2Runtime.Current=replacement;replacement.DuringBeginSave=null;

replacement.PendingNewGame=false;nativeScope=EnterNativeNewGame(true);ownershipEpoch++;var newEpochRuntime=new Gsd2Runtime{PendingNewGame=true};Gsd2Runtime.Current=newEpochRuntime;
refused=EnterSave(false,false);Check(refused is null&&newEpochRuntime.CompletionCalls==0,"old coroutine depth cannot complete a new epoch's pending campaign");
Call(typeof(Gsd2ModernNewGamePatch),"Postfix",new TitleChapter._MapSetup_d__43{isNewgame=true},false);
Check(newEpochRuntime.CompletionCalls==0,"old coroutine postfix cannot initialize replacement campaign");ExitNativeNewGame(nativeScope);
Check(!Gsd2ModernNewGamePatch.CurrentBoundaryIsOwned&&Gsd2ModernNewGamePatch.NativeNewGameDepth==0,"stale coroutine finalizer clears only its managed scope");

Gsd2LoadPatch.Call? EnterLoad(string json,bool expected=true)
{
    object?[] args=[json,null];Check((bool)Call(typeof(Gsd2LoadPatch),"Prefix",args)! == expected,"load prefix has expected native execution decision");return (Gsd2LoadPatch.Call?)args[1];
}
var loadOwner=new Gsd2Runtime();Gsd2Runtime.Current=loadOwner;var loadCall=EnterLoad("owned native load");
Check(loadOwner.PrepareLoadCalls==1&&loadCall!.Prepared,"owned load captures prepared runtime and epoch");
Call(typeof(Gsd2LoadPatch),"Postfix",loadCall);Check(loadOwner.CompleteLoadCalls==1,"owned load completion reconstructs captured owner once");
ownershipEpoch++;var newLoadOwner=new Gsd2Runtime();Gsd2Runtime.Current=newLoadOwner;
Call(typeof(Gsd2LoadPatch),"Postfix",loadCall);
Check(ReferenceEquals(Call(typeof(Gsd2LoadPatch),"Finalizer",nativeException,loadCall),nativeException)&&loadOwner.CompleteLoadCalls==1&&newLoadOwner.CompleteLoadCalls==0,"stale load postfix/finalizer preserve exception and do not touch replacement owner");
newLoadOwner.ThrowPrepareLoad=true;loadCall=EnterLoad("rejected binding",false);Call(typeof(Gsd2LoadPatch),"Postfix",loadCall);
Check(!loadCall!.Prepared&&newLoadOwner.CompleteLoadCalls==0,"rejected load prefix cannot run successful native reconstruction postfix");
executionAllowed=false;var foreignLoadOwner=new Gsd2Runtime();Gsd2Runtime.Current=foreignLoadOwner;loadCall=EnterLoad("foreign native load");
Call(typeof(Gsd2LoadPatch),"Postfix",loadCall);Call(typeof(Gsd2LoadPatch),"Finalizer",nativeException,loadCall);
Check(loadCall is null&&foreignLoadOwner.PrepareLoadCalls==0&&foreignLoadOwner.CompleteLoadCalls==0,"foreign load chain is neutral");
Gsd2Runtime.ExecutionAllowed=()=>true;Gsd2Runtime.OwnershipEpoch=()=>0;Gsd2Runtime.Current=null;Gsd2SaveScope.Current=null;
Check(Gsd2ModernNewGamePatch.NativeNewGameDepth==0&&Gsd2ManualSaveCaptureScope.Depth==0,"extended lifecycle cases leave all scopes clear");
Console.WriteLine($"{assertions} lifecycle assertions passed ({baselineLifecycleAssertions} baseline plus camp/guard coverage); isolated fixture directory: {root}");
