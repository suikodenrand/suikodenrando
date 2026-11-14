using System;
using System.Collections;
using System.Collections.Generic;

namespace SuikodenRandomizer.Suikoden2Plugin
{
    // IsActive/MapDisplayId are the boundary used by the real presentation file.
    internal sealed class Gsd2Runtime
    {
        internal static Gsd2Runtime? Current;
        internal bool IsActive = true;
        internal readonly Dictionary<int,int> Map = new() { [32]=33,[33]=2,[2]=50 };
        internal int MapCalls;
        internal int MapDisplayId(int role) { MapCalls++; return Map.TryGetValue(role,out int donor)?donor:role; }
    }
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections
{
    // Behavioral bridge double only. It exposes C# iterator MoveNext/Current
    // and disposal through the Il2Cpp-facing interface consumed by production.
    public static class CollectionExtensions
    {
        public static Il2CppSystem.Collections.IEnumerator WrapToIl2Cpp(this IEnumerator original)
            => new ManagedNativeIterator(original);
        private sealed class ManagedNativeIterator : Il2CppSystem.Collections.IEnumerator,Il2CppSystem.IDisposable
        {
            private readonly IEnumerator _original;
            internal ManagedNativeIterator(IEnumerator original)=>_original=original;
            public override bool MoveNext()=>_original.MoveNext();
            public override Il2CppSystem.Object? Current=>_original.Current as Il2CppSystem.Object;
            public void Dispose()=>(_original as System.IDisposable)?.Dispose();
        }
    }
}
