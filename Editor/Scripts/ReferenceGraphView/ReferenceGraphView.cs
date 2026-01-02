//
// Heap Explorer for Unity. Copyright (c) 2019-2024 Peter Schraut (www.console-dev.de). See LICENSE.md
// https://github.com/pschraut/UnityHeapExplorer/
//

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace HeapExplorer
{
    // Simple graph node data structure
    public class GraphNodeData : IEqualityComparer<GraphNodeData>
    {
        public readonly int id;
        public readonly string title;
        public readonly string subtitle;
        public readonly bool isManaged;
        public readonly int objectIndex;
        
        public HashSet<int> childNodes = new HashSet<int>();
        
        public Vector2 position;
        public Rect rect;
        public bool isExpanded;
        
        public GraphNodeData(int id, string title, string subtitle, Vector2 position, bool isManaged, int objectIndex)
        {
            this.id = id;
            this.title = title;
            this.subtitle = subtitle;
            this.position = position;
            this.isManaged = isManaged;
            this.objectIndex = objectIndex;
            this.rect = new Rect(position, new Vector2(200, 80));
            this.isExpanded = false;
        }

        public bool Equals(GraphNodeData other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return isManaged == other.isManaged && objectIndex == other.objectIndex;
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((GraphNodeData)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(isManaged, objectIndex);
        }

        public bool Equals(GraphNodeData x, GraphNodeData y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null)
            {
                return false;
            }

            if (y is null)
            {
                return false;
            }

            if (x.GetType() != y.GetType())
            {
                return false;
            }

            return x.isManaged == y.isManaged && x.objectIndex == y.objectIndex;
        }

        public int GetHashCode(GraphNodeData obj)
        {
            return HashCode.Combine(obj.isManaged, obj.objectIndex);
        }
    }

    public class ReferenceGraphViewIMGUI
    {
        const int MAX_CHILD_NODES = 10;           // Maximum number of child nodes to display per expansion
        const float NODE_VERTICAL_SPACING = 120f; // Vertical spacing between child nodes

        PackedMemorySnapshot m_Snapshot;
        Dictionary<int, GraphNodeData> m_Nodes = new Dictionary<int, GraphNodeData>();
        HashSet<GraphNodeData> m_NodesSet = new HashSet<GraphNodeData>();
        int m_NextNodeId = 0;
        Vector2 m_ScrollPosition;
        Vector2 m_GraphOffset = Vector2.zero;
        float m_Zoom = 1.0f;
        bool m_IsPanning = false;
        Vector2 m_PanStart;
        GraphNodeData m_DraggingNode = null;
        Vector2 m_DragOffset;

        public ReferenceGraphViewIMGUI(PackedMemorySnapshot snapshot)
        {
            m_Snapshot = snapshot;
        }

        public void Clear()
        {
            m_Nodes.Clear();
            m_NextNodeId = 0;
            m_GraphOffset = Vector2.zero;
        }

        public void ShowManagedObject(PackedManagedObject obj, Vector2 position)
        {
            Clear();
            AddManagedObjectNode(obj, position, null);
        }
        
        public void ShowNativeObject(PackedNativeUnityEngineObject obj, Vector2 position)
        {
            Clear();
            AddNativeObjectNode(obj, position, null);
        }

        void AddManagedObjectNode(PackedManagedObject obj, Vector2 position, GraphNodeData parent)
        {
            var type = m_Snapshot.managedTypes[obj.managedTypesArrayIndex];
            var title = type.name;
            var subtitle = $"Size: {EditorUtility.FormatBytes(obj.size)}\nAddr: 0x{obj.address:X}";

            var node = new GraphNodeData(m_NextNodeId++, title, subtitle, position, true, obj.managedObjectsArrayIndex);
            AddNode(parent, node);
        }

        void AddNativeObjectNode(PackedNativeUnityEngineObject obj, Vector2 position, GraphNodeData parent)
        {
            var type = m_Snapshot.nativeTypes[obj.nativeTypesArrayIndex];
            var title = type.name;
            var subtitle = !string.IsNullOrEmpty(obj.name) ? $"Name: {obj.name}\n" : "";
            subtitle += $"Size: {EditorUtility.FormatBytes(obj.size)}";

            var node = new GraphNodeData(m_NextNodeId++, title, subtitle, position, false, obj.nativeObjectsArrayIndex);
            AddNode(parent, node);
        }
        
        void AddNode(GraphNodeData parent, GraphNodeData node)
        {
            if (m_NodesSet.Add(node))
            {
                m_Nodes[node.id] = node;
            }
            else
            {
                // This is a hack because I'm lazy to implement the comparation before the creation of the node
                m_NodesSet.TryGetValue(node, out node);
            }

            if (parent != null)
            {
                parent.childNodes.Add(node.id);
            }
        }

        public void OnGUI(Rect rect)
        {
            // Handle events
            HandleEvents(rect);
            
            // Begin scrollable area
            GUI.Box(rect, "", EditorStyles.helpBox);
            
            Rect graphArea = new Rect(0, 0, rect.width * 2, rect.height * 2);
            
            GUI.BeginGroup(rect);
            
            // Apply zoom and offset
            Matrix4x4 originalMatrix = GUI.matrix;
            Vector2 pivot = rect.size * 0.5f;
            GUIUtility.ScaleAroundPivot(Vector2.one * m_Zoom, pivot);
            
            // Draw connections first
            DrawConnections();
            
            // Draw nodes
            foreach (var node in m_Nodes.Values)
            {
                DrawNode(node, rect);
            }
            
            GUI.matrix = originalMatrix;
            GUI.EndGroup();
            
            // Draw instructions overlay
            DrawInstructions(rect);
        }

        void HandleEvents(Rect rect)
        {
            Event e = Event.current;
            
            if (!rect.Contains(e.mousePosition))
                return;
            
            // Handle zoom
            if (e.type == EventType.ScrollWheel)
            {
                float zoomDelta = -e.delta.y * 0.01f;
                m_Zoom = Mathf.Clamp(m_Zoom + zoomDelta, 0.5f, 2.0f);
                e.Use();
            }
            
            // Handle panning with middle mouse button
            if (e.type == EventType.MouseDown && e.button == 2)
            {
                m_IsPanning = true;
                m_PanStart = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 2)
            {
                m_IsPanning = false;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && m_IsPanning)
            {
                Vector2 delta = e.mousePosition - m_PanStart;
                foreach (var node in m_Nodes.Values)
                {
                    node.position += delta / m_Zoom;
                    node.rect = new Rect(node.position, node.rect.size);
                }
                m_PanStart = e.mousePosition;
                e.Use();
            }
            
            // Handle node dragging with left mouse button
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // Transform mouse position to account for zoom pivot
                Vector2 mouseInGraphSpace = TransformMouseToGraphSpace(e.mousePosition, rect, m_Zoom);
                
                foreach (var node in m_Nodes.Values)
                {
                    Rect nodeRect = node.rect;
                    
                    if (nodeRect.Contains(mouseInGraphSpace))
                    {
                        // Check if clicking the expand to root button
                        if (!node.isExpanded)
                        {
                            Rect expandToRootButtonRect = new Rect(nodeRect.x + nodeRect.width - 38, nodeRect.y + 5, 15, 15);
                            if (expandToRootButtonRect.Contains(mouseInGraphSpace))
                            {
                                ExpandToRoot(node);
                                e.Use();
                                break;
                            }
                            
                            // Check if clicking the expand button
                            Rect expandButtonRect = new Rect(nodeRect.x + nodeRect.width - 20, nodeRect.y + 5, 15, 15);
                            if (expandButtonRect.Contains(mouseInGraphSpace))
                            {
                                ExpandNode(node);
                                e.Use();
                                break;
                            }
                        }
                        
                        // Start dragging the node
                        m_DraggingNode = node;
                        m_DragOffset = mouseInGraphSpace - node.position;
                        e.Use();
                        break;
                    }
                }
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && m_DraggingNode != null)
            {
                // Transform mouse position to account for zoom pivot
                Vector2 mouseInGraphSpace = TransformMouseToGraphSpace(e.mousePosition, rect, m_Zoom);
                
                // Update node position during drag
                Vector2 newPosition = mouseInGraphSpace - m_DragOffset;
                m_DraggingNode.position = newPosition;
                m_DraggingNode.rect = new Rect(newPosition, m_DraggingNode.rect.size);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (m_DraggingNode != null)
                {
                    m_DraggingNode = null;
                    e.Use();
                }
            }
        }

        // Transform mouse position from screen space to graph space, accounting for zoom pivot
        Vector2 TransformMouseToGraphSpace(Vector2 mousePos, Rect rect, float zoom)
        {
            // Inverse transform: translate to pivot, scale, translate back
            mousePos -= rect.position;
            Vector2 pivot = rect.size * 0.5f;
            Vector2 relativeToCenter = mousePos - pivot;
            Vector2 scaledRelative = relativeToCenter / zoom;
            return scaledRelative + pivot;
        }

        void DrawNode(GraphNodeData node, Rect containerRect)
        {
            Rect nodeRect = new Rect(node.position, node.rect.size);
            
            // Draw node background
            Color nodeColor = node.isManaged ? new Color(0.3f, 0.5f, 0.7f) : new Color(0.7f, 0.5f, 0.3f);
            EditorGUI.DrawRect(nodeRect, nodeColor);
            
            // Draw border
            Rect borderRect = new Rect(nodeRect.x - 1, nodeRect.y - 1, nodeRect.width + 2, nodeRect.height + 2);
            EditorGUI.DrawRect(borderRect, Color.black);
            EditorGUI.DrawRect(nodeRect, nodeColor);
            
            // Draw title
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.UpperCenter;
            titleStyle.wordWrap = true;
            titleStyle.fontSize = 11;
            
            Rect titleRect = new Rect(nodeRect.x + 5, nodeRect.y + 5, nodeRect.width - 10, 20);
            GUI.Label(titleRect, node.title, titleStyle);
            
            // Draw subtitle
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label);
            subtitleStyle.normal.textColor = Color.white;
            subtitleStyle.fontSize = 9;
            subtitleStyle.wordWrap = true;
            
            Rect subtitleRect = new Rect(nodeRect.x + 5, nodeRect.y + 25, nodeRect.width - 10, nodeRect.height - 35);
            GUI.Label(subtitleRect, node.subtitle, subtitleStyle);
            
            // Draw expand buttons if not expanded
            if (!node.isExpanded)
            {
                // Draw expand to root button (left button with "R")
                Rect expandToRootButtonRect = new Rect(nodeRect.x + nodeRect.width - 38, nodeRect.y + 5, 15, 15);
                
                // Draw button background
                EditorGUI.DrawRect(expandToRootButtonRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                
                // Draw button border
                Rect rootButtonBorder = new Rect(expandToRootButtonRect.x - 1, expandToRootButtonRect.y - 1, expandToRootButtonRect.width + 2, expandToRootButtonRect.height + 2);
                EditorGUI.DrawRect(rootButtonBorder, new Color(0.8f, 0.6f, 0.2f)); // Orange border for distinction
                EditorGUI.DrawRect(expandToRootButtonRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                
                // Draw R symbol
                GUIStyle rootButtonStyle = new GUIStyle(EditorStyles.boldLabel);
                rootButtonStyle.normal.textColor = new Color(1.0f, 0.8f, 0.3f); // Orange text
                rootButtonStyle.fontSize = 10;
                rootButtonStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(expandToRootButtonRect, "R", rootButtonStyle);
                
                // Draw expand one level button (right button with "+")
                Rect expandButtonRect = new Rect(nodeRect.x + nodeRect.width - 20, nodeRect.y + 5, 15, 15);
                
                // Draw button background
                EditorGUI.DrawRect(expandButtonRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                
                // Draw button border
                Rect buttonBorder = new Rect(expandButtonRect.x - 1, expandButtonRect.y - 1, expandButtonRect.width + 2, expandButtonRect.height + 2);
                EditorGUI.DrawRect(buttonBorder, Color.white);
                EditorGUI.DrawRect(expandButtonRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                
                // Draw + symbol
                GUIStyle buttonStyle = new GUIStyle(EditorStyles.boldLabel);
                buttonStyle.normal.textColor = Color.white;
                buttonStyle.fontSize = 12;
                buttonStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(expandButtonRect, "+", buttonStyle);
            }
        }

        void DrawConnections()
        {
            Handles.BeginGUI();
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            
            foreach (var node in m_Nodes.Values)
            {
                foreach (var childId in node.childNodes)
                {
                    if (m_Nodes.TryGetValue(childId, out GraphNodeData childNode))
                    {
                        // Draw line from node's left center to child's right center
                        Vector2 start = new Vector2(node.position.x, node.position.y + node.rect.height / 2);
                        Vector2 end = new Vector2(childNode.position.x + childNode.rect.width, childNode.position.y + childNode.rect.height / 2);
                        
                        Handles.DrawAAPolyLine(3f, start, end);
                        
                        // Draw arrow head
                        Vector2 direction = (end - start).normalized;
                        Vector2 arrowLeft = end - direction * 10 + new Vector2(-direction.y, direction.x) * 5;
                        Vector2 arrowRight = end - direction * 10 - new Vector2(-direction.y, direction.x) * 5;
                        Handles.DrawAAPolyLine(3f, arrowLeft, end, arrowRight);
                    }
                }
            }
            
            Handles.EndGUI();
        }

        void DrawInstructions(Rect rect)
        {
            GUIStyle style = new GUIStyle(EditorStyles.helpBox);
            style.normal.textColor = Color.white;
            style.fontSize = 10;
            
            Rect instructionRect = new Rect(rect.x + 5, rect.y + 5, 350, 75);
            GUI.Label(instructionRect, "Click [+]: Expand one level\nClick [R]: Expand to root\nDrag: Move node\nMiddle-click drag: Pan view\nScroll: Zoom", style);
        }
        
        void ExpandNode(GraphNodeData node)
        {
            if (node.isExpanded)
                return;
            
            node.isExpanded = true;
            
            // Find all objects that reference this object
            var referencedBy = new List<PackedConnection>();
            
            if (node.isManaged)
            {
                foreach (var conn in m_Snapshot.connections)
                {
                    if (conn.toKind == PackedConnection.Kind.Managed && conn.to == node.objectIndex)
                    {
                        referencedBy.Add(conn);
                    }
                }
            }
            else
            {
                foreach (var conn in m_Snapshot.connections)
                {
                    if (conn.toKind == PackedConnection.Kind.Native && conn.to == node.objectIndex)
                    {
                        referencedBy.Add(conn);
                    }
                }
            }
            
            // Create child nodes
            int count = Mathf.Min(referencedBy.Count, MAX_CHILD_NODES);
            float startY = node.position.y - (count - 1) * NODE_VERTICAL_SPACING / 2f;
            
            for (int i = 0; i < count; i++)
            {
                var conn = referencedBy[i];
                Vector2 childPosition = new Vector2(node.position.x - 250, startY + i * NODE_VERTICAL_SPACING);
                
                if (conn.fromKind == PackedConnection.Kind.Managed && conn.from >= 0 && conn.from < m_Snapshot.managedObjects.Length)
                {
                    var fromObj = m_Snapshot.managedObjects[conn.from];
                    AddManagedObjectNode(fromObj, childPosition, node);
                }
                else if (conn.fromKind == PackedConnection.Kind.Native && conn.from >= 0 && conn.from < m_Snapshot.nativeObjects.Length)
                {
                    var fromObj = m_Snapshot.nativeObjects[conn.from];
                    AddNativeObjectNode(fromObj, childPosition, node);
                }
            }
        }

        void ExpandToRoot(GraphNodeData startNode)
        {
            // Expand the starting node and continue expanding until we reach a root
            var currentNodes = new List<GraphNodeData> { startNode };
            int iterationCount = 0;
            const int MAX_ITERATIONS = 16; // Safety limit to prevent infinite loops
            
            while (currentNodes.Count > 0 && iterationCount < MAX_ITERATIONS)
            {
                var nextNodes = new List<GraphNodeData>();
                
                foreach (var node in currentNodes)
                {
                    // Check if this node is a root
                    if (IsNodeRoot(node))
                    {
                        // This is a root, mark it but don't expand further
                        continue;
                    }
                    
                    // Expand this node
                    if (!node.isExpanded)
                    {
                        ExpandNode(node);
                        
                        // Add the child nodes to the next iteration
                        foreach (var childId in node.childNodes)
                        {
                            if (m_Nodes.TryGetValue(childId, out GraphNodeData childNode))
                            {
                                nextNodes.Add(childNode);
                            }
                        }
                    }
                }
                
                currentNodes = nextNodes;
                iterationCount++;
            }
        }

        bool IsNodeRoot(GraphNodeData node)
        {
            // Check if this node is a root based on connection type
            // Static fields are roots
            foreach (var conn in m_Snapshot.connections)
            {
                if (node.isManaged)
                {
                    if (conn.toKind == PackedConnection.Kind.Managed && 
                        conn.to == node.objectIndex && 
                        conn.fromKind == PackedConnection.Kind.StaticField)
                    {
                        return true;
                    }
                }
                else
                {
                    if (conn.toKind == PackedConnection.Kind.Native && conn.to == node.objectIndex)
                    {
                        // Check if referenced by a static field
                        if (conn.fromKind == PackedConnection.Kind.StaticField)
                        {
                            return true;
                        }
                    }
                }
            }
            
            // For native objects, check additional root conditions
            if (!node.isManaged && node.objectIndex >= 0 && node.objectIndex < m_Snapshot.nativeObjects.Length)
            {
                var nativeObj = m_Snapshot.nativeObjects[node.objectIndex];
                
                // Check if it's a manager
                if (nativeObj.isManager)
                    return true;
                
                // Check if it's marked as DontDestroyOnLoad
                if (nativeObj.isDontDestroyOnLoad)
                    return true;
                
                // Check if it has DontUnloadUnusedAsset flag
                if ((nativeObj.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0)
                    return true;
                
                // Check if it's a GameObject or Component (these are scene roots)
                var nativeType = m_Snapshot.nativeTypes[nativeObj.nativeTypesArrayIndex];
                if (m_Snapshot.coreTypes.nativeGameObject >= 0 && 
                    nativeType.IsSubclassOf(m_Snapshot.coreTypes.nativeGameObject))
                    return true;
                
                if (m_Snapshot.coreTypes.nativeComponent >= 0 && 
                    nativeType.IsSubclassOf(m_Snapshot.coreTypes.nativeComponent))
                    return true;
            }
            
            // Check if there are no references to this object (it's a root by isolation)
            bool hasIncomingReferences = false;
            foreach (var conn in m_Snapshot.connections)
            {
                if (node.isManaged)
                {
                    if (conn.toKind == PackedConnection.Kind.Managed && conn.to == node.objectIndex)
                    {
                        hasIncomingReferences = true;
                        break;
                    }
                }
                else
                {
                    if (conn.toKind == PackedConnection.Kind.Native && conn.to == node.objectIndex)
                    {
                        hasIncomingReferences = true;
                        break;
                    }
                }
            }
            
            // If no incoming references, it's a root
            if (!hasIncomingReferences)
                return true;
            
            return false;
        }
    }
}
