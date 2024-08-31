namespace Meddle.Plugin.Utils;

public static class ScopedStack
{
    public static ScopedStack<T> PushScoped<T>(this Stack<T> stack, T instance)
    {
        return new ScopedStack<T>(stack, instance);
    }
}

public class ScopedStack<T> : IDisposable
{
    private readonly Stack<T> stack;
    private readonly T instance;

    public ScopedStack(Stack<T> stack, T instance)
    {
        this.stack = stack;
        this.instance = instance;
        stack.Push(instance);
    }

    public void Dispose()
    {
        stack.Pop();
    }
}

