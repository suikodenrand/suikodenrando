using SuikodenRandomizer.AvatarPlugin;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static void Case(string name,Action action) { action();_passed++;Console.WriteLine("PASS "+name); }

    private sealed class Scenario
    {
        internal readonly NuzlockeRetryResourceGate Gate;
        internal readonly List<Exception> Faults=new();
        internal double Now;
        internal int LoadCalls,ReadyCalls,ResumeCalls,ResumeThread;
        internal bool ResourceReady=true,SynchronousCompletion,ThrowLoad,ThrowReady,ThrowResume;
        internal Action? Completion;
        internal Scenario()
        {
            Gate=new NuzlockeRetryResourceGate(
                completion=>{
                    LoadCalls++;Completion=completion;
                    if(ThrowLoad) throw new InvalidOperationException("Native resource load failed.");
                    if(SynchronousCompletion) completion();
                },
                ()=>{
                    ReadyCalls++;
                    if(ThrowReady) throw new InvalidOperationException("Native shared battle resource could not be read.");
                    return ResourceReady;
                },
                ()=>{
                    ResumeCalls++;ResumeThread=Environment.CurrentManagedThreadId;
                    if(ThrowResume) throw new InvalidOperationException("Native retry scheduling failed.");
                },
                error=>Faults.Add(error),()=>Now);
        }
        internal void Complete() => (Completion??throw new Exception("Load was not started."))();
    }

    private static void Main()
    {
        Case("One load waits for native completion before verifying resources and scheduling retry",()=>{
            var s=new Scenario();int gameThread=Environment.CurrentManagedThreadId;
            Check(s.Gate.Status==NuzlockeRetryResourceStatus.Idle,"New retry gate was not idle");
            s.Gate.Poll();
            for(int poll=0;poll<100;poll++) s.Gate.Poll();
            Check(s.LoadCalls==1&&s.ResumeCalls==0&&s.ReadyCalls==0&&s.Gate.Status==NuzlockeRetryResourceStatus.Loading,
                "Pending asynchronous load was duplicated or native battle data was used before completion");
            Task.Run(s.Complete).GetAwaiter().GetResult();
            Check(s.ResumeCalls==0,"Native completion callback entered the retry on its own thread");
            s.Gate.Poll();
            s.Complete();
            for(int poll=0;poll<20;poll++) s.Gate.Poll();
            Check(s.Gate.Status==NuzlockeRetryResourceStatus.Completed&&s.ResumeCalls==1&&s.ResumeThread==gameThread
                &&s.LoadCalls==1&&s.ReadyCalls==1&&s.Faults.Count==0,"Ready resources did not produce exactly one game-thread retry");
        });

        Case("Synchronous resource completion still waits until the next loader step",()=>{
            var s=new Scenario{SynchronousCompletion=true};
            s.Gate.Poll();
            Check(s.ResumeCalls==0&&s.ReadyCalls==0,"Synchronous completion reentered battle during resource loading");
            s.Gate.Poll();
            Check(s.ResumeCalls==1&&s.Gate.Status==NuzlockeRetryResourceStatus.Completed,"Completed synchronous load did not resume later");
        });

        Case("A completed load with a missing shared resource fails before battle can restart",()=>{
            var s=new Scenario{ResourceReady=false};
            s.Gate.Poll();s.Complete();s.Gate.Poll();
            s.ResourceReady=true;s.Gate.Poll();
            Check(s.Gate.Status==NuzlockeRetryResourceStatus.Failed&&s.Faults.Count==1&&s.ResumeCalls==0&&s.LoadCalls==1,
                "Null shared battle resources reached the original battle or silently retried loading");
        });

        Case("A stalled resource load times out once and cannot resume from a late callback",()=>{
            var s=new Scenario();s.Gate.Poll();
            s.Now=29;s.Gate.Poll();
            Check(s.Faults.Count==0&&s.ResumeCalls==0,"Load timed out before its deadline");
            s.Now=31;s.Gate.Poll();s.Complete();s.Gate.Poll();
            Check(s.Gate.Status==NuzlockeRetryResourceStatus.Failed&&s.Faults.Count==1&&s.ResumeCalls==0&&s.LoadCalls==1,
                "A timed-out load resumed battle or produced repeated faults");
        });

        Case("Cancelling a load prevents its stale completion from affecting a new campaign retry",()=>{
            var old=new Scenario();old.Gate.Poll();old.Gate.Cancel();
            var current=new Scenario();current.Gate.Poll();
            old.Complete();old.Gate.Poll();current.Gate.Poll();
            Check(old.Gate.Status==NuzlockeRetryResourceStatus.Cancelled&&old.ResumeCalls==0&&old.Faults.Count==0
                &&current.ResumeCalls==0&&current.Gate.Status==NuzlockeRetryResourceStatus.Loading,
                "A cancelled native completion started a stale or newer campaign battle");
            current.Complete();current.Gate.Poll();
            Check(current.ResumeCalls==1&&old.ResumeCalls==0,"The current campaign could not resume independently");
        });

        Case("Cancelling before loading prevents any native work",()=>{
            var s=new Scenario();s.Gate.Cancel();s.Gate.Poll();
            Check(s.Gate.Status==NuzlockeRetryResourceStatus.Cancelled&&s.LoadCalls==0&&s.ReadyCalls==0
                &&s.ResumeCalls==0&&s.Faults.Count==0,"An inactive cancelled retry touched native resources");
        });

        Case("Native load exceptions stop the retry without repeated scheduling",()=>{
            var s=new Scenario{ThrowLoad=true};s.Gate.Poll();s.Gate.Poll();
            Check(s.Gate.Status==NuzlockeRetryResourceStatus.Failed&&s.Faults.Count==1&&s.LoadCalls==1&&s.ResumeCalls==0,
                "A native loading exception escaped the gate or restarted the load");
        });

        Case("Native resource validation exceptions stop the retry before original battle entry",()=>{
            var s=new Scenario{ThrowReady=true};s.Gate.Poll();s.Complete();s.Gate.Poll();s.Gate.Poll();
            Check(s.Gate.Status==NuzlockeRetryResourceStatus.Failed&&s.Faults.Count==1&&s.ReadyCalls==1&&s.ResumeCalls==0,
                "Native resource read failure reached the original battle");
        });

        Case("Retry scheduling exceptions are reported once without another battle entry",()=>{
            var s=new Scenario{ThrowResume=true};s.Gate.Poll();s.Complete();s.Gate.Poll();s.Gate.Poll();
            Check(s.Gate.Status==NuzlockeRetryResourceStatus.Failed&&s.Faults.Count==1&&s.ResumeCalls==1,
                "A scheduling failure entered the original battle more than once");
        });

        Console.WriteLine(_passed+"/"+_passed+" Nuzlocke retry resource gate checks passed.");
        Console.WriteLine("Scope: actual production resource gate with asynchronous managed loader callbacks; native OPD allocation and encounter reconstruction require in-game validation.");
    }
}
