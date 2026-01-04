namespace HeapExplorer
{
    public class RootPathJob : AbstractThreadJob
    {
        public ObjectProxy objectProxy;
        public PackedMemorySnapshot snapshot;

        // in/out
        public RootPathUtility paths;
            
        public override void ThreadFunc()
        {
            if (objectProxy != null)
                paths.Find(objectProxy);
        }
    }
}
