using System;
using System.Diagnostics;

namespace VL.IO.NDI;

internal interface IRefCounted
{
    bool Enter();
    void Release();
}

struct Lock : IDisposable
{
    public static Lock Enter(IRefCounted obj)
    {
        if (obj.Enter())
        {
            return new Lock(obj);
        }
        else
        {
            return default;
        }
    }

    private readonly IRefCounted obj;

    private Lock(IRefCounted obj)
    {
        this.obj = obj;
    }

    public bool Aquired => obj != null;

    public Lock GetLock()
    {
        Debug.Assert(obj != null);
        var success = obj.Enter();
        Debug.Assert(success);
        return new Lock(obj);
    }

    public void Dispose() => obj?.Release();
}