namespace HeapExplorer
{
    public enum RootPathReason
    {
        // The order of elements here reflects how rootpaths are
        // sorted in the RootPath view. Things that keep objects alive
        // are at the bottom of this enum.

        None = 0,
        AssetBundle,
        Component,
        GameObject,
        UnityManager,
        DontUnloadUnusedAsset,
        DontDestroyOnLoad,
        Static,
        Unknown, // make most important, so I easily spot if I forgot to support something
    }
}
