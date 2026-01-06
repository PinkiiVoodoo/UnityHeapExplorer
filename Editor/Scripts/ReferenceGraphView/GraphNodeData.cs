using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeapExplorer
{
    public class GraphNodeData : IEqualityComparer<GraphNodeData>
    {
        const int Radius = 45;
        const int extraRadiusPerChild = 5;
        
        public readonly string title;
        public readonly string subtitle;
        public readonly Color color;

        public readonly ObjectProxy objectProxy;
        public readonly RootPathUtility paths = new();
        public int pathIndex = -1;

        public readonly RootPathReason rootReason;
        public readonly bool isRoot;
        public readonly bool isEmptyShellObject;

        public HashSet<int> childNodes = new HashSet<int>();

        Vector2 position;
        public Vector2 Position
        {
            get => position;
            set
            {
                position = value;
                rect = new Rect(position - new Vector2(radius, radius), new Vector2(radius * 2, radius * 2));
            }
        }

        public Rect rect;
        public float radius => Radius + Math.Max(0, childNodes.Count - 1) * extraRadiusPerChild;
        public bool isExpanded;

        public GraphNodeData(Vector2 position, ObjectProxy objectProxy)
        {
            this.position = position;
            this.objectProxy = objectProxy;

            isRoot = RootPathUtility.IsRoot(objectProxy, out rootReason);
            isEmptyShellObject = IsEmptyShellObject();
            
            rect = new Rect(position - new Vector2(radius, radius), new Vector2(radius * 2, radius * 2));
            isExpanded = false;

            title = CreateTitle(objectProxy);
            subtitle = CreateSubtitle(objectProxy) + (isRoot ? $"\n({rootReason})" : "");
            color = CreateColor(objectProxy);
        }

        string CreateTitle(ObjectProxy proxy)
        {
            if (proxy.managed.isValid)
            {
                return proxy.managed.type.name.Replace('.', ' ');
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

        public bool CanExpandToRoot()
        {
            return !isRoot && (paths.scanned == 0 || pathIndex < paths.count);
        }
        
        bool IsEmptyShellObject()
        {
            if (!objectProxy.managed.isValid) 
                return false;
            
            var obj = objectProxy.managed;
            if (obj.address == 0)
                return false; // points to null

            if (obj.nativeObject.isValid)
                return false; // has a native object, thus can't be an empty shell object 

            var richType = obj.type;
            var type = richType.packed;

            // Only UnityEngine.Object objects can have a m_CachedPtr connection to a native object.
            if (!type.isUnityEngineObject)
                return false;

            // Could be an array of an UnityEngine.Object, such as Texture[]
            if (type.isArray)
                return false;
                
            return true;
        }
    }
}
