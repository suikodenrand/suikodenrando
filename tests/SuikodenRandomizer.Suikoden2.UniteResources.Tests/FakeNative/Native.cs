namespace Il2CppSystem
{
    public class Object { public T? TryCast<T>() where T : class => this as T; }
    public interface IDisposable { void Dispose(); }
}
namespace Il2CppSystem.Collections
{
    public abstract class IEnumerator : Il2CppSystem.Object
    {
        public abstract bool MoveNext();
        public abstract Il2CppSystem.Object? Current { get; }
    }
}
// The production Harmony attributes only need these declaring types. Native
// ImageLoader work is modeled explicitly by the fixture callback pipeline.
public static class ImageLoader { }
public sealed class Tc_face { }
public sealed class WindowManager { }
