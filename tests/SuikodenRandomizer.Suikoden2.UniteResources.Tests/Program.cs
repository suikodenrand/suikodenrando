using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using SuikodenRandomizer.Suikoden2Plugin;
using NativeIterator=Il2CppSystem.Collections.IEnumerator;

internal static class Program
{
    private static bool _raw;
    private static void Check(bool valid,string message) { if(!valid)throw new Exception(message); }
    private static void Eq<T>(T expected,T actual) => Check(Equals(expected,actual),"Expected "+expected+", got "+actual);
    private static object? Call(Type patch,string name,params object?[] arguments)
    {
        try{return patch.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,arguments);}
        catch(TargetInvocationException e) when(e.InnerException is not null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw; }
    }
    private static Gsd2Runtime Reset()
    {
        DepthsClear();_raw=false;Gsd2Presentation.PreserveRawResourceIds=()=>_raw;
        var runtime=new Gsd2Runtime();Gsd2Runtime.Current=runtime;return runtime;
    }
    private static void DepthsClear()
    {
        foreach(string field in new[]{"_faceDepth","_offsetDepth"})
            Eq(0,(int)typeof(Gsd2Presentation).GetField(field,BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!);
    }
    private static int Face(Type patch,int id)
    {
        object?[] args={id,false};Call(patch,"Prefix",args);
        try{return (int)args[0]!;}
        finally{Call(patch,"Finalizer",null,args[1]);}
    }
    private static int Path(int id)=>Face(typeof(Gsd2PortraitResourcePathPatch),id);
    private static (NativeIterator Wrapped,FakeImageIterator Native,int Stored) Construct(int requested,Exception? constructorError=null)
    {
        object?[] prefix={requested,false};Call(typeof(Gsd2AsynchronousPortraitPatch),"Prefix",prefix);
        Exception? error=null;
        try
        {
            if(constructorError is not null)throw constructorError;
            int stored=(int)prefix[0]!;
            Eq(stored,Path(stored)); // Native constructor's nested path resolution.
            var native=new FakeImageIterator(stored);
            object?[] postfix={native,prefix[1]};Call(typeof(Gsd2AsynchronousPortraitPatch),"Postfix",postfix);
            return ((NativeIterator)postfix[0]!,native,stored);
        }
        catch(Exception e){error=e;throw;}
        finally{Check(ReferenceEquals(error,Call(typeof(Gsd2AsynchronousPortraitPatch),"Finalizer",error,prefix[1])),"Finalizer replaced native exception");}
    }
    private static void Drain(NativeIterator iterator)
    {
        while(iterator.MoveNext())Check(iterator.Current is YieldToken,"Yielded native object changed");
    }
    private static void OrdinaryMappingResumes(Gsd2Runtime runtime)
    {
        _raw=false;DepthsClear();int before=runtime.MapCalls;
        Eq(33,Path(32));Eq(before+1,runtime.MapCalls);DepthsClear();
    }
    private static void NormalFaces()
    {
        var r=Reset();
        foreach(var patch in new[]{typeof(Gsd2SynchronousPortraitPatch),typeof(Gsd2CachedPortraitPatch),typeof(Gsd2PortraitResourcePathPatch)})
            Eq(33,Face(patch,32));
        Eq(3,r.MapCalls);DepthsClear();
        object?[] character={(byte)32,false};Call(typeof(Gsd2ClassicDialoguePortraitPatch),"Prefix",character);
        try{Eq((byte)33,(byte)character[0]!);Eq(33,Path((byte)character[0]!));}
        finally{Call(typeof(Gsd2ClassicDialoguePortraitPatch),"Finalizer",null,character[1]);}
        Eq(4,r.MapCalls);OrdinaryMappingResumes(r);
    }
    private static void NormalAsync()
    {
        var r=Reset();var x=Construct(32);Eq(33,x.Stored);Eq(1,r.MapCalls);DepthsClear();
        Drain(x.Wrapped);Check(x.Native.Resolved.All(id=>id==33),"Async steps remapped already projected donor");
        Eq(1,r.MapCalls);Eq(1,x.Native.Disposals);OrdinaryMappingResumes(r);
    }
    private static void FancyLadRaw()
    {
        var r=Reset();var jobs=new List<(NativeIterator Wrapped,FakeImageIterator Native,int Stored)>();
        _raw=true;
        foreach(int donor in new[]{32,33,2}){jobs.Add(Construct(donor));Eq(donor,jobs[^1].Stored);DepthsClear();}
        Eq(0,r.MapCalls);_raw=false; // Unity resumes original iterators after the raw outer MoveNext returns.
        foreach(var job in jobs)
        {
            while(job.Wrapped.MoveNext())
            {
                Check(job.Wrapped.Current is YieldToken,"Yield changed");DepthsClear();
                OrdinaryMappingResumes(r); // A separate normal portrait is free to map between yields.
            }
            Check(job.Native.Resolved.All(id=>id==job.Stored),"Explicit donor face changed after yield");
            Eq(1,job.Native.Disposals);DepthsClear();
        }
        OrdinaryMappingResumes(r);
    }
    private static void InterleavedAsync()
    {
        var r=Reset();_raw=true;var raw=Construct(32);_raw=false;var normal=Construct(33);
        Eq(32,raw.Stored);Eq(2,normal.Stored);
        for(int n=0;n<2;n++){Check(raw.Wrapped.MoveNext(),"Raw iterator ended early");DepthsClear();Check(normal.Wrapped.MoveNext(),"Normal iterator ended early");DepthsClear();}
        Check(!normal.Wrapped.MoveNext()&&!raw.Wrapped.MoveNext(),"Iterator did not complete");
        Check(raw.Native.Resolved.All(id=>id==32)&&normal.Native.Resolved.All(id=>id==2),"Interleaved face scopes crossed");
        Eq(1,raw.Native.Disposals);Eq(1,normal.Native.Disposals);OrdinaryMappingResumes(r);
    }
    private static void ConstructorException()
    {
        var r=Reset();var expected=new InvalidOperationException("native constructor failed");
        _raw=true;try{Construct(32,expected);throw new Exception("Expected constructor failure");}
        catch(InvalidOperationException e){Check(ReferenceEquals(expected,e),"Constructor exception changed");}
        OrdinaryMappingResumes(r);
    }
    private static void IteratorException(bool current)
    {
        var r=Reset();_raw=true;var x=Construct(32);_raw=false;
        var expected=new InvalidOperationException(current?"native Current failed":"native MoveNext failed");
        if(current)x.Native.CurrentError=expected;else x.Native.MoveError=expected;
        try{x.Wrapped.MoveNext();throw new Exception("Expected iterator failure");}
        catch(InvalidOperationException e){Check(ReferenceEquals(expected,e),"Native iterator exception changed");}
        Eq(1,x.Native.Disposals);OrdinaryMappingResumes(r);
    }
    private static void EarlyDispose()
    {
        var r=Reset();_raw=true;var x=Construct(33);_raw=false;
        Check(x.Wrapped.MoveNext(),"Missing first yield");DepthsClear();
        ((Il2CppSystem.IDisposable)x.Wrapped).Dispose();Eq(1,x.Native.Disposals);OrdinaryMappingResumes(r);
    }
    private static void RawOffsetAndCharacter()
    {
        var r=Reset();_raw=true;
        object?[] offset={(ushort)32,false};Call(typeof(Gsd2PortraitOffsetPatch),"Prefix",offset);
        try{Eq((ushort)32,(ushort)offset[0]!);}
        finally{Call(typeof(Gsd2PortraitOffsetPatch),"Finalizer",null,offset[1]);}
        object?[] character={(byte)33,false};Call(typeof(Gsd2ClassicDialoguePortraitPatch),"Prefix",character);
        try{Eq((byte)33,(byte)character[0]!);Eq(33,Path(33));}
        finally{Call(typeof(Gsd2ClassicDialoguePortraitPatch),"Finalizer",null,character[1]);}
        Eq(0,r.MapCalls);OrdinaryMappingResumes(r);
        offset=new object?[]{(ushort)32,false};Call(typeof(Gsd2PortraitOffsetPatch),"Prefix",offset);
        try{Eq((ushort)33,(ushort)offset[0]!);}
        finally{Call(typeof(Gsd2PortraitOffsetPatch),"Finalizer",null,offset[1]);}
        DepthsClear();
    }
    private static void Inactive()
    {
        var r=Reset();r.IsActive=false;var x=Construct(32);Eq(32,x.Stored);Check(ReferenceEquals(x.Wrapped,x.Native),"Inactive iterator was wrapped");
        Drain(x.Wrapped);Eq(0,r.MapCalls);r.IsActive=true;OrdinaryMappingResumes(r);
    }
    private static void Main()
    {
        var tests=new (string Name,Action Run)[]{
            ("normal portrait and nested character-face calls map once",NormalFaces),
            ("normal async constructor maps once; later native steps preserve that donor",NormalAsync),
            ("raw Fancy Lad faces32/33/2 survive constructor and later yields; normal mapping resumes between steps",FancyLadRaw),
            ("interleaved raw and ordinary image iterators preserve distinct face resources",InterleavedAsync),
            ("native constructor exception leaves no face-depth scope",ConstructorException),
            ("native MoveNext exception disposes and restores normal mapping",()=>IteratorException(false)),
            ("native Current exception disposes and restores normal mapping",()=>IteratorException(true)),
            ("disposing a yielded iterator restores scope and native cleanup",EarlyDispose),
            ("raw offsets and character-face calls preserve original IDs then resume normal projection",RawOffsetAndCharacter),
            ("inactive runtime leaves native constructor and iterator unchanged",Inactive),
        };
        foreach(var t in tests){t.Run();Console.WriteLine("PASS "+t.Name);}
        Console.WriteLine($"{tests.Length}/{tests.Length} production-linked S2 unite resource tests passed. Native game not executed.");
    }
    private sealed class YieldToken : Il2CppSystem.Object { }
    private sealed class FakeImageIterator : NativeIterator,Il2CppSystem.IDisposable
    {
        private readonly int _stored;private int _steps;private readonly YieldToken _yield=new();
        internal readonly List<int> Resolved=new();internal int Disposals;
        internal Exception? MoveError,CurrentError;
        internal FakeImageIterator(int stored)=>_stored=stored;
        public override bool MoveNext()
        {if(MoveError is not null)throw MoveError;Resolved.Add(Path(_stored));return ++_steps<=2;}
        public override Il2CppSystem.Object Current
        {get{if(CurrentError is not null)throw CurrentError;return _yield;}}
        public void Dispose()=>Disposals++;
    }
}
