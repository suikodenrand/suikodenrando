extern alias GSD1;

using SuikodenRandomizer.AvatarPlugin;
using System.Linq;

internal static partial class Program
{
    private static string DiagnosticWorldFingerprint(World world) => string.Join("\n",
        world.Runtime.CanonicalLayoutFingerprint(),
        world.Game.playerName,
        string.Join(",",world.Game.party_data.vil_no,world.Game.party_data.map_no,world.Game.party_data.area_no,
            world.Game.party_data.x,world.Game.party_data.y),
        string.Join(";",world.Bases.OrderBy(entry=>entry.Key).Select(entry=>AvatarNativeData.Fingerprint(entry.Value))),
        string.Join(";",world.Cache.OrderBy(entry=>entry.Key).Select(entry=>entry.Key+":"+entry.Value?.Pointer)),
        string.Join(";",world.Game.player_data.Select(resident=>resident?.Pointer.ToString()??"null")));

    private static void RunDiagnosticChecks()
    {
        Case("A post-KO scene diagnostic observes the live roster without changing native game state",()=>{
            var w=NuzlockeWorld(true);
            var battle=StartNuzlockeBattle(w);KnockOut(w,battle,8);KnockOut(w,battle,1);w.Runtime.FinishNuzlockeBattle(false);
            w.Game.playerName="Chosen Hero";
            w.Game.party_data.vil_no=7;w.Game.party_data.map_no=12;w.Game.party_data.area_no=2;
            w.Game.party_data.x=432;w.Game.party_data.y=123;
            w.Base(1).exp=912;w.Base(1).Equipment=501;
            string before=DiagnosticWorldFingerprint(w);
            int copies=AvatarNativeData.CopyCalls;
            CrashDiagnostics.Breadcrumbs.Clear();CrashDiagnostics.Exceptions.Clear();

            w.Runtime.DiagnosticCheckpoint("post-KO scene boundary");
            w.Runtime.Tick();

            Check(CrashDiagnostics.Breadcrumbs.Count>0,"Diagnostic checkpoint emitted no state evidence");
            string state=CrashDiagnostics.Breadcrumbs.Single(entry=>entry.EventName=="post-KO scene boundary.state").Detail??"";
            Check(state.Contains("game=0x"+w.Game.Pointer.ToString("X"))&&state.Contains("location=7/12/2")
                &&state.Contains("position=432,123")&&state.Contains("count="+w.Game.party_data.player_kazu),
                "Diagnostic snapshot omitted the native world, transition location or active party count");
            foreach(int role in new[]{8,1}) {
                int slot=w.Game.party_data.chara_code.IndexOf((short)role);
                var resident=w.Game.player_data[slot]!;
                Check(state.Contains("slot="+slot+" role="+role+" donor="+w.Runtime.MapDisplayId(role)
                        +" resident=0x"+resident.Pointer.ToString("X"))
                    &&state.Contains("base=0x"+w.Base(role).Pointer.ToString("X")+" baseRole="+role),
                    "Diagnostic snapshot did not associate a canonical role with its actual donor and native pointers");
            }
            Check(before==DiagnosticWorldFingerprint(w)&&AvatarNativeData.CopyCalls==copies,
                "Recording diagnostics mutated progression, pointers, location or native party membership");
            Check(w.Runtime.MapDisplayId(8)==8&&w.Runtime.ProxyFor(8) is null,
                "Diagnostic observation projected protected Tir");
        });

        Case("Diagnostics tolerate native world teardown instead of interrupting the game",()=>{
            var w=NuzlockeWorld();
            string before=DiagnosticWorldFingerprint(w);
            CrashDiagnostics.Breadcrumbs.Clear();CrashDiagnostics.Exceptions.Clear();
            GSD1.OldSrcBase.game_work=null!;
            GSD1.GlobalWork.Instance=null;
            GSD1.ChapterManager.GR1Instance=null;

            w.Runtime.DiagnosticCheckpoint("native scene teardown");
            w.Runtime.Tick();

            Check(CrashDiagnostics.Breadcrumbs.Count+CrashDiagnostics.Exceptions.Count>0,"Unavailable native state left no diagnostic evidence");
            Check(CrashDiagnostics.Breadcrumbs.Any(entry=>entry.EventName=="native scene teardown.state"&&entry.Detail=="game=null"),
                "Teardown evidence did not identify the missing native game object");
            // Keep the model references alive so the comparison itself performs no
            // native discovery after the global handles have been cleared.
            GSD1.OldSrcBase.game_work=w.Game;
            Check(before==DiagnosticWorldFingerprint(w),"Teardown diagnostics changed retained game objects");
        });

        Case("A transient native party-array failure is logged without faulting the runtime",()=>{
            var w=new World(false);
            var codes=w.Game.party_data.chara_code;
            var order=w.Game.party_data.narabi;
            string tir=AvatarNativeData.Fingerprint(w.Base(8));
            int copies=AvatarNativeData.CopyCalls;
            w.Game.party_data.chara_code=null!;
            w.Game.party_data.narabi=null!;

            w.Runtime.Tick();

            Check(CrashDiagnostics.Exceptions.Any(entry=>entry.Operation=="diagnostic world observation"),
                "A managed snapshot failure produced no diagnostic exception evidence");
            Check(w.Runtime.FaultReason is null&&!w.Runtime.IsAdmitted&&AvatarNativeData.CopyCalls==copies
                &&AvatarNativeData.Fingerprint(w.Base(8))==tir,"A diagnostic read failure stopped the runtime or mutated native Tir");
            w.Game.party_data.chara_code=codes;
            w.Game.party_data.narabi=order;
            w.Advance();w.Runtime.Tick();
            Check(w.Runtime.FaultReason is null&&CrashDiagnostics.Breadcrumbs.Any(entry=>entry.EventName=="world.changed.state"),
                "Diagnostic observation did not recover when native party arrays became available");
        });

        Case("Ordinary frame observation records each world change without flooding an unchanged scene",()=>{
            var w=NuzlockeWorld();
            CrashDiagnostics.Breadcrumbs.Clear();CrashDiagnostics.Exceptions.Clear();
            int copies=AvatarNativeData.CopyCalls;
            w.Runtime.Tick();
            for(int frame=0;frame<1000;frame++) w.Runtime.Tick();
            w.Advance();w.Runtime.Tick();
            Check(CrashDiagnostics.Breadcrumbs.Count(entry=>entry.EventName=="world.changed")==1,
                "Unchanged frame observation repeatedly emitted world-change diagnostics");
            w.Game.party_data.map_no++;w.Advance();w.Runtime.Tick();
            Check(CrashDiagnostics.Breadcrumbs.Count(entry=>entry.EventName=="world.changed")==2,
                "A changed native map was not recorded at the next observation boundary");
            Check(AvatarNativeData.CopyCalls==copies&&w.Runtime.ProxyFor(8) is null,"Diagnostic frame observation reinitialized the roster or projected Tir");
        });
    }
}
