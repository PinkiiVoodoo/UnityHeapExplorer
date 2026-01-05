namespace HeapExplorer
{
    public class RootPathNodeJob : RootPathJob
    {
        GraphNodeData data;
        ReferenceGraphViewIMGUI control;
        
        public RootPathNodeJob(PackedMemorySnapshot packedMemorySnapshot, GraphNodeData data, ReferenceGraphViewIMGUI control)
        {
            this.snapshot = packedMemorySnapshot;
            this.paths = data.paths;
            this.objectProxy = data.objectProxy;
            this.data = data;
            this.control = control;
        }
        
        public override void ThreadFunc()
        {
            if (!paths.isBusy && paths.scanned == 0)
            {
                base.ThreadFunc();
            }
        }

        public override void IntegrateFunc()
        {
            ++data.pathIndex;
            if (data.pathIndex < paths.count)
            {
                control.AddPathNodes(paths[data.pathIndex], data);   
            }
        }
    }
}
