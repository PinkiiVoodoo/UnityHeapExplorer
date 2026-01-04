using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HeapExplorer
{
    public class RootPathUtility
    {
        List<RootPath> m_Items = new List<RootPath>();
        bool m_Abort;
        bool m_IsBusy;
        int m_ScanCount;

        static int s_IterationLoopGuard = 1000000;

        public bool isBusy
        {
            get
            {
                return m_IsBusy;
            }
        }

        /// <summary>
        /// Gets the number of scanned objects. Updates as the RootPathUtility is busy.
        /// </summary>
        public int scanned
        {
            get
            {
                return m_ScanCount;
            }
        }

        /// <summary>
        /// Gets the number of root paths that were found.
        /// </summary>
        public int count
        {
            get
            {
                return m_Items.Count;
            }
        }

        public RootPath this[int index]
        {
            get
            {
                return m_Items[index];
            }
        }

        /// <summary>
        /// Gets the shortest root path.
        /// </summary>
        public RootPath shortestPath
        {
            get
            {
                RootPath value = null;

                if (m_Items.Count > 0)
                {
                    value = m_Items[0];

                    // Find the shortest path
                    foreach (var p in m_Items)
                    {
                        if (p.count < value.count && value.reason != RootPathReason.Static)
                            value = p;

                        // Assign if it's a path to static
                        if (p.reason == RootPathReason.Static && value.reason != RootPathReason.Static)
                            value = p;

                        // Find the shortest path to static
                        if (p.reason == RootPathReason.Static && p.count < value.count)
                            value = p;
                    }
                }

                if (value == null)
                    value = new RootPath(RootPathReason.None, new ObjectProxy[0]);

                return value;
            }
        }

        public void Abort()
        {
            m_Abort = true;
        }

        public void Find(ObjectProxy obj)
        {
            m_IsBusy = true;
            m_Items = new List<RootPath>();
            var seen = new HashSet<long>();

            var queue = new Queue<List<ObjectProxy>>();
            queue.Enqueue(new List<ObjectProxy> { obj });

            int issues = 0;
            int guard = 0;
            while (queue.Any())
            {
                if (m_Abort)
                    break;

                if (++guard > s_IterationLoopGuard)
                {
                    Debug.LogWarning("RootPath iteration loop guard kicked in.");
                    //m_Items = new List<RootPath>();
                    break;
                }

                var pop = queue.Dequeue();
                var tip = pop.Last();

                RootPathReason reason;
                if (IsRoot(tip, out reason))
                {
                    m_Items.Add(new RootPath(reason, pop.ToArray()));
                    continue;
                }

                var referencedBy = GetReferencedBy(tip, ref issues);
                foreach (var next in referencedBy)
                {
                    if (seen.Contains(next.id))
                        continue;
                    seen.Add(next.id);

                    var dupe = new List<ObjectProxy>(pop) { next };
                    queue.Enqueue(dupe);

                    m_ScanCount++;
                }
            }

            m_Items.Sort();
            m_IsBusy = false;

            if (issues > 0)
                Debug.LogWarningFormat("{0} issues have been detected while finding root-paths. This is most likely related to an earlier bug in Heap Explorer, that started to occur with Unity 2019.3 and causes that (some) object connections are invalid. Please capture a new memory snapshot.", issues);
        }

        public static List<ObjectProxy> GetReferencedBy(ObjectProxy obj, ref int issues)
        {
            var referencedBy = new List<PackedConnection>(32);

            if (obj.staticField.isValid)
                obj.snapshot.GetConnections(obj.staticField.packed, null, referencedBy);

            if (obj.native.isValid)
                obj.snapshot.GetConnections(obj.native.packed, null, referencedBy);

            if (obj.managed.isValid)
                obj.snapshot.GetConnections(obj.managed.packed, null, referencedBy);

            if (obj.gcHandle.isValid)
                obj.snapshot.GetConnections(obj.gcHandle.packed, null, referencedBy);

            var value = new List<ObjectProxy>(referencedBy.Count);
            foreach (var c in referencedBy)
            {
                switch (c.fromKind)
                {
                    case PackedConnection.Kind.Native:
                        if (c.from < 0 || c.from >= obj.snapshot.nativeObjects.Length)
                        {
                            issues++;
                            continue;
                        }
                        value.Add(new ObjectProxy(obj.snapshot, obj.snapshot.nativeObjects[c.from]));
                        break;

                    case PackedConnection.Kind.Managed:
                        if (c.from < 0 || c.from >= obj.snapshot.managedObjects.Length)
                        {
                            issues++;
                            continue;
                        }
                        value.Add(new ObjectProxy(obj.snapshot, obj.snapshot.managedObjects[c.from]));
                        break;

                    case PackedConnection.Kind.GCHandle:
                        if (c.from < 0 || c.from >= obj.snapshot.gcHandles.Length)
                        {
                            issues++;
                            continue;
                        }
                        value.Add(new ObjectProxy(obj.snapshot, obj.snapshot.gcHandles[c.from]));
                        break;

                    case PackedConnection.Kind.StaticField:
                        if (c.from < 0 || c.from >= obj.snapshot.managedStaticFields.Length)
                        {
                            issues++;
                            continue;
                        }
                        value.Add(new ObjectProxy(obj.snapshot, obj.snapshot.managedStaticFields[c.from]));
                        break;
                }
            }
            return value;
        }


        public static bool IsRoot(ObjectProxy thing, out RootPathReason reason)
        {
            reason = RootPathReason.None;

            if (thing.staticField.isValid)
            {
                reason = RootPathReason.Static;
                return true;
            }

            if (thing.managed.isValid)
            {
                return false;
            }

            if (thing.gcHandle.isValid)
            {
                return false;
            }

            if (!thing.native.isValid)
                throw new System.ArgumentException("Unknown type: " + thing.GetType());

            if (thing.native.isManager)
            {
                reason = RootPathReason.UnityManager;
                return true;
            }

            if (thing.native.isDontDestroyOnLoad)
            {
                reason = RootPathReason.DontDestroyOnLoad;
                return true;
            }

            if ((thing.native.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0)
            {
                reason = RootPathReason.DontUnloadUnusedAsset;
                return true;
            }

            if (thing.native.isPersistent)
            {
                return false;
            }

            if (thing.native.type.IsSubclassOf(thing.snapshot.coreTypes.nativeComponent))
            {
                reason = RootPathReason.Component;
                return true;
            }

            if (thing.native.type.IsSubclassOf(thing.snapshot.coreTypes.nativeGameObject))
            {
                reason = RootPathReason.GameObject;
                return true;
            }

            if (thing.native.type.IsSubclassOf(thing.snapshot.coreTypes.nativeAssetBundle))
            {
                reason = RootPathReason.AssetBundle;
                return true;
            }

            reason = RootPathReason.Unknown;
            return true;
        }
    }
}
