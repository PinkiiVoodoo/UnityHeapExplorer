using System.Collections.Generic;
using UnityEngine;

namespace HeapExplorer
{
    public class GraphNodeData : IEqualityComparer<GraphNodeData>
    {
        public readonly string title;
        public readonly string subtitle;
        public readonly Color color;

        public readonly ObjectProxy objectProxy;
        public readonly RootPathUtility paths = new();

        public HashSet<int> childNodes = new HashSet<int>();

        public Vector2 position;
        public Rect rect;
        public bool isExpanded;

        public GraphNodeData(Vector2 position, ObjectProxy objectProxy)
        {
            this.position = position;
            this.objectProxy = objectProxy;

            rect = new Rect(position, new Vector2(200, 80));
            isExpanded = false;

            title = CreateTitle(objectProxy);
            subtitle = CreateSubtitle(objectProxy);
            color = CreateColor(objectProxy);
        }

        string CreateTitle(ObjectProxy proxy)
        {
            if (proxy.managed.isValid)
            {
                return proxy.managed.type.name;
            }

            if (proxy.native.isValid)
            {
                return proxy.native.type.name;
            }

            if (proxy.staticField.isValid)
            {
                return proxy.staticField.fieldType.name;
            }

            if (proxy.gcHandle.isValid)
            {
                return "GCHandle";
            }

            return "Unknown";
        }

        string CreateSubtitle(ObjectProxy proxy)
        {
            if (proxy.managed.isValid)
            {
                return proxy.managed.ToString();
            }

            if (proxy.native.isValid)
            {
                return proxy.native.name;
            }

            if (proxy.staticField.isValid)
            {
                return $"Static Field";
            }

            if (proxy.gcHandle.isValid)
            {
                return $"GCHandle";
            }

            return "Unknown";
        }

        Color CreateColor(ObjectProxy proxy)
        {
            // return different eye caring colors that can render white text on top based on type
            if (proxy.managed.isValid)
            {
                return new Color(0.4f, 0.6f, 1.0f); // Blueish
            }

            if (proxy.native.isValid)
            {
                return new Color(0.2f, 0.6f, 0.2f); // Greenish
            }

            if (proxy.staticField.isValid)
            {
                return new Color(1.0f, 0.6f, 0.4f); // Reddish
            }

            if (proxy.gcHandle.isValid)
            {
                return new Color(0.7f, 0.7f, 0.2f); // Yellowish
            }

            return Color.gray;
        }

        public bool Equals(GraphNodeData x, GraphNodeData y)
        {
            return Equals(x?.objectProxy, y?.objectProxy);
        }

        public int GetHashCode(GraphNodeData obj)
        {
            return obj.objectProxy?.GetHashCode() ?? 0;
        }

        public override bool Equals(object obj)
        {
            if (obj is GraphNodeData other)
            {
                return Equals(this.objectProxy, other.objectProxy);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return GetHashCode(this);
        }
    }

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
            base.ThreadFunc();
            control.AddPathNodes(paths[0]);
        }
    }

}
