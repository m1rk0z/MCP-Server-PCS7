using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Pcs7Mcp.Com;

/// <summary>Late-bound IDispatch helpers (must be called on the STA thread).</summary>
public static class ComObj
{
    public static object? Get(object obj, string name, params object?[] args) =>
        obj.GetType().InvokeMember(name, BindingFlags.GetProperty, null, obj, args);

    public static void Set(object obj, string name, object? value) =>
        obj.GetType().InvokeMember(name, BindingFlags.SetProperty, null, obj, new[] { value });

    public static object? Call(object obj, string name, params object?[] args) =>
        obj.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, obj, args);

    public static T? TryGet<T>(object obj, string name, params object?[] args)
    {
        try
        {
            var v = Get(obj, name, args);
            if (v is null) return default;
            if (v is T t) return t;
            return (T)Convert.ChangeType(v, typeof(T));
        }
        catch { return default; }
    }

    public static string? Str(object obj, string name) => TryGet<string>(obj, name);

    public static int Count(object collection) => Convert.ToInt32(Get(collection, "Count"));

    /// <summary>
    /// Enumerates a SIMATIC collection through _NewEnum (IEnumVARIANT). Item(index) is avoided:
    /// on the project collection it costs ~2 s per call, the enumerator returns all items at once.
    /// </summary>
    public static List<object> Items(object? collection)
    {
        if (collection is null) return new List<object>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = ItemsCore(collection, out var method);
        if (sw.ElapsedMilliseconds > 2000)
            Console.Error.WriteLine($"[pcs7-mcp] enumerated {result.Count} items via {method} in {sw.ElapsedMilliseconds} ms");
        return result;
    }

    private static List<object> ItemsCore(object collection, out string method)
    {
        var result = new List<object>();
        method = "IEnumerable";
        if (collection is System.Collections.IEnumerable enumerable)
        {
            try
            {
                foreach (var o in enumerable) if (o is not null) result.Add(o);
                return result;
            }
            catch { result.Clear(); }
        }
        method = "IEnumVARIANT";

        IEnumVARIANT? enumerator = null;
        foreach (var attempt in new Func<object?>[]
                 {
                     () => Get(collection, "_NewEnum"),
                     () => Get(collection, "[DISPID=-4]"),
                     () => Call(collection, "[DISPID=-4]"),
                 })
        {
            try { enumerator = attempt() as IEnumVARIANT; } catch { }
            if (enumerator is not null) break;
        }

        if (enumerator is not null)
        {
            var buffer = new object[1];
            while (true)
            {
                var hr = NextVariant(enumerator, buffer);
                if (hr != 0 || buffer[0] is null) break;
                result.Add(buffer[0]);
                buffer[0] = null!;
            }
            return result;
        }

        // Fallback: 1-based index access.
        method = "Item(index)";
        int n;
        try { n = Count(collection); } catch { return result; }
        for (int i = 1; i <= n; i++)
        {
            try { if (Get(collection, "Item", i) is { } item) result.Add(item); } catch { }
        }
        return result;
    }

    private static int NextVariant(IEnumVARIANT e, object[] buffer)
    {
        var ptr = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            var hr = e.Next(1, buffer, ptr);
            return Marshal.ReadInt32(ptr) == 1 ? 0 : (hr == 0 ? 1 : hr);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    public static string Describe(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: not null } tie) ex = tie.InnerException;
        return ex is COMException ce ? $"{ce.Message} (HRESULT 0x{ce.HResult:X8})" : ex.Message;
    }
}
