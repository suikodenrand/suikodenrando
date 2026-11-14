// Managed shapes derived from real GSD2 interop metadata. No foreign code executes.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

public static class TestIdentity { private static long _next=10000; public static IntPtr Next()=>new IntPtr(Interlocked.Increment(ref _next)); }
public class NativeArray<T> : IEnumerable<T>
{
    private readonly T[] _values;
    public NativeArray(params T[] values) { _values=values; }
    public IntPtr Pointer { get; }=TestIdentity.Next();
    public int Count=>_values.Length;
    public int Writes {get;private set;}
    public int FailOnWrite {get;set;}=-1;
    public T this[int index] {get=>_values[index];set{Writes++;if(Writes==FailOnWrite)throw new InvalidOperationException("Injected native setter failure");_values[index]=value;}}
    public IEnumerator<T> GetEnumerator()=>((IEnumerable<T>)_values).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T>:NativeArray<T> where T:struct {public Il2CppStructArray(params T[] values):base(values) {}}
    public sealed class Il2CppReferenceArray<T>:NativeArray<T> where T:class {public Il2CppReferenceArray(params T[] values):base(values) {}}
}
public sealed class UNITE_DATA
{
    public IntPtr Pointer {get;set;}=TestIdentity.Next();
    public int name {get;set;} public int text {get;set;} public byte info {get;set;} public byte atrb {get;set;}
    public byte player_kazu {get;set;}
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> player_id {get;set;}=null!;
    public byte func_no {get;set;} public int hosei {get;set;} public byte m_status {get;set;} public byte m_status_prob {get;set;}
    public byte p_status {get;set;} public byte p_status_prob {get;set;}
    public string Name=>""; public string Text=>"";
}

public sealed class BATTLE_CHARA
{
    public IntPtr Pointer {get;set;}=TestIdentity.Next();
    private int _id;
    public int IdWrites {get;private set;}
    public int FailOnIdWrite {get;set;}=-1;
    public int chara_id {get=>_id;set{IdWrites++;if(IdWrites==FailOnIdWrite)throw new InvalidOperationException("Injected graph identity setter failure");_id=value;}}
}
public sealed class BATTLE_WORK
{
    public IntPtr Pointer {get;set;}=TestIdentity.Next();
    private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UNITE_DATA> _unites=null!;
    private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BATTLE_CHARA> _characters=null!;
    public int player_kazu {get;set;}
    public int pm_kazu {get;set;}
    public sbyte battle_chara_no {get;set;}
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PM_DATA> pm_data {get;set;}=null!;
    public ACTION_DATA? action_sp {get;set;}
    public ACTION_DATA? action_top {get;set;}
    public IOverFunc? magic_func {get;set;}
    public BattleTurnStep? turn_step {get;set;}
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> flg_table {get;set;}=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(new byte[6]);
    public int NativeReads {get;private set;}
    public bool ThrowOnRead {get;set;}
    private void Read(){NativeReads++;if(ThrowOnRead)throw new InvalidOperationException("Foreign native battle state was read");}
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UNITE_DATA> unite_data_table {get{Read();return _unites;}set=>_unites=value;}
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BATTLE_CHARA> b_chara {get{Read();return _characters;}set=>_characters=value;}
}
public sealed class BattleManager
{
    public IntPtr Pointer {get;set;}=TestIdentity.Next();
    private BATTLE_WORK _work=null!;
    public int NativeReads {get;private set;}
    public bool ThrowOnRead {get;set;}
    public BATTLE_WORK battle_work {get{NativeReads++;if(ThrowOnRead)throw new InvalidOperationException("Foreign manager state was read");return _work;}set=>_work=value;}
    public byte check_unite(int slot)=>throw new NotSupportedException();
    public int make_command_data()=>throw new NotSupportedException();
    public void make_unite_window(int slot)=>throw new NotSupportedException();
    public int command_unite()=>throw new NotSupportedException();
    public int UniteSelectBeforeFunc()=>throw new NotSupportedException();
    public int command_unite01()=>throw new NotSupportedException();
    public int command_unite99()=>throw new NotSupportedException();
    public int make_unite_player_window()=>throw new NotSupportedException();
    public int battle_turn_unite01()=>throw new NotSupportedException();
    public void Release()=>throw new NotSupportedException();
    public void BattleFree()=>throw new NotSupportedException();
    public void BattleEnd()=>throw new NotSupportedException();
}

public class PM_DATA
{
    public IntPtr Pointer {get;set;}=TestIdentity.Next();
    private byte _id;
    public int IdWrites {get;private set;}
    public int FailOnIdWrite {get;set;}=-1;
    public byte chara_id {get=>_id;set{IdWrites++;if(IdWrites==FailOnIdWrite)throw new InvalidOperationException("Injected PM identity setter failure");_id=value;}}
    public int chara_no {get;set;}
    public BATTLE_WORK battle_work {get;set;}=null!;
    public short hp {get;set;}=100;
    public ushort max_hp {get;set;}=999;
    public byte dead_flg {get;set;}
    public byte finished_flg {get;set;}
    public byte action_kaisu {get;set;}
    public byte action_num {get;set;}=1;
    public uint status {get;set;}
    public sbyte unbalance_cnt {get;set;}
    public sbyte c_koudou {get;set;}
    public sbyte c_item {get;set;}
    public sbyte c_mokuhyo {get;set;}
    public ACTION_DATA? action_data {get;set;}
    public bool IsDead=>hp<=0||dead_flg!=0;
    public bool UniteCanCheck()=>!IsDead&&(status&0x1f280)==0;
    public void IncreaseActionKaisu(){action_kaisu++;if(action_kaisu>=action_num)finished_flg=1;}
    public void DamageSetReActionData(ACTION_DATA action,int slot,short amount,byte arg,byte type)=>throw new NotSupportedException();
    public void RecoverSetReActionData(ACTION_DATA action,int slot,short amount,byte arg)=>throw new NotSupportedException();
    public void StatusSet(int status)=>throw new NotSupportedException();
}
public sealed class BattlePlayerCharacter:PM_DATA
{
    public int PlayerCalcUnite()=>throw new NotSupportedException();
    public int UniteNormalFunc(byte recipe,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> slots)=>throw new NotSupportedException();
    public int DoguEqpCheck(byte item)=>throw new NotSupportedException();
    public int CalcPlayerDamage(int target)=>throw new NotSupportedException();
}
public sealed class ACTION_DATA
{
    public IntPtr Pointer {get;set;}=TestIdentity.Next();
    public sbyte chara_no {get;set;}
    public byte action_type {get;set;}
    public ACTION_DATA? next_action_data {get;set;}
    public List<REACTION_DATA> reaction_data {get;set;}=new List<REACTION_DATA>();
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> data {get;set;}=null!;
}
public sealed class BattleTurnStep {public IntPtr Pointer {get;set;}=TestIdentity.Next();}

public sealed class REACTION_DATA {public sbyte mokuhyo {get;set;} public byte reaction_type {get;set;} public ushort damage {get;set;} public uint reaction_status {get;set;}}

public class IOverFunc {public IntPtr Pointer {get;set;}=TestIdentity.Next();}
namespace Unite
{
    public sealed class Unite41:IOverFunc
    {
        public sealed class _Load_d__19
        {
            public Unite41 __4__this {get;set;}=null!;
            public bool MoveNext()=>throw new NotSupportedException("Native iterator execution is explicitly modeled by the fixture.");
        }
    }
}
