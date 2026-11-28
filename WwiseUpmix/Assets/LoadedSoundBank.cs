using System;

// AK_BankAlreadyLoaded borrows a bank owned by another component.
internal sealed class LoadedSoundBank : IDisposable
{
    private readonly uint id;
    private bool owned;

    private LoadedSoundBank(uint id, bool owned)
    {
        this.id = id;
        this.owned = owned;
    }

    public static LoadedSoundBank Load(string name, out AKRESULT result)
    {
        result = AkUnitySoundEngine.LoadBank(name, out uint id);
        if (result == AKRESULT.AK_Success) return new LoadedSoundBank(id, true);
        if (result == AKRESULT.AK_BankAlreadyLoaded) return new LoadedSoundBank(id, false);
        return null;
    }

    public void Dispose()
    {
        if (owned && AkUnitySoundEngine.IsInitialized())
            AkUnitySoundEngine.UnloadBank(id, IntPtr.Zero);
        owned = false;
    }
}
