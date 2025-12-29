//
// Heap Explorer for Unity. Copyright (c) 2019-2024 Peter Schraut (www.console-dev.de). See LICENSE.md
// https://github.com/pschraut/UnityHeapExplorer/
//
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace HeapExplorer
{
    // Simple graph node data structure
    public class GraphNodeData
    {
        public int id;
        public string title;
        public string subtitle;
        public Vector2 position;
        public Rect rect;
        public bool isManaged;
        public int objectIndex;
        public bool isExpanded;
        public List<int> childNodes = new List<int>();
        
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
    }

    public class ReferenceGraphViewIMGUI
    {
        const int MAX_CHILD_NODES = 10; // Maximum number of child nodes to display per expansion
        const float NODE_VERTICAL_SPACING = 120f; // Vertical spacing between child nodes

        PackedMemorySnapshot m_Snapshot;
        Dictionary<int, GraphNodeData> m_Nodes = new Dictionary<int, GraphNodeData>();
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
            
            var type = m_Snapshot.managedTypes[obj.managedTypesArrayIndex];
            var title = type.name;
            var subtitle = $"Size: {EditorUtility.FormatBytes(obj.size)}\nAddr: 0x{obj.address:X}";
            
            var node = new GraphNodeData(m_NextNodeId++, title, subtitle, position, true, obj.managedObjectsArrayIndex);
            m_Nodes[node.id] = node;
        }

        public void ShowNativeObject(PackedNativeUnityEngineObject obj, Vector2 position)
        {
            Clear();
            
            var type = m_Snapshot.nativeTypes[obj.nativeTypesArrayIndex];
            var title = type.name;
            var subtitle = !string.IsNullOrEmpty(obj.name) ? $"Name: {obj.name}\n" : "";
            subtitle += $"Size: {EditorUtility.FormatBytes(obj.size)}";
            
            var node = new GraphNodeData(m_NextNodeId++, title, subtitle, position, false, obj.nativeObjectsArrayIndex);
            m_Nodes[node.id] = node;
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
                Vector2 pivot = rect.size * 0.5f;
                Vector2 mouseInGraphSpace = TransformMouseToGraphSpace(e.mousePosition, pivot, m_Zoom);
                
                foreach (var node in m_Nodes.Values)
                {
                    Rect nodeRect = new Rect(node.position, node.rect.size);
                    
                    if (nodeRect.Contains(mouseInGraphSpace))
                    {
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
                Vector2 pivot = rect.size * 0.5f;
                Vector2 mouseInGraphSpace = TransformMouseToGraphSpace(e.mousePosition, pivot, m_Zoom);
                
                // Update node position during drag
                Vector2 newPosition = mouseInGraphSpace - m_DragOffset;
                m_DraggingNode.position = newPosition;
                m_DraggingNode.rect = new Rect(newPosition, m_DraggingNode.rect.size);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                // Check if it was a click (not a drag) for expansion
                if (m_DraggingNode != null)
                {
                    // Transform mouse position to account for zoom pivot
                    Vector2 pivot = rect.size * 0.5f;
                    Vector2 mouseInGraphSpace = TransformMouseToGraphSpace(e.mousePosition, pivot, m_Zoom);
                    
                    // If the mouse hasn't moved much, treat it as a click to expand
                    float dragDistance = Vector2.Distance(mouseInGraphSpace, m_DraggingNode.position + m_DragOffset);
                    
                    if (dragDistance < 5f / m_Zoom) // Threshold for click vs drag (scaled by zoom)
                    {
                        ExpandNode(m_DraggingNode);
                    }
                    
                    m_DraggingNode = null;
                    e.Use();
                }
            }
        }

        // Transform mouse position from screen space to graph space, accounting for zoom pivot
        Vector2 TransformMouseToGraphSpace(Vector2 mousePos, Vector2 pivot, float zoom)
        {
            // Inverse transform: translate to pivot, scale, translate back
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
            
            Rect subtitleRect = new Rect(nodeRect.x + 5, nodeRect.y + 25, nodeRect.width - 10, nodeRect.height - 30);
            GUI.Label(subtitleRect, node.subtitle, subtitleStyle);
            
            // Draw expansion indicator
            if (!node.isExpanded)
            {
                Rect expandRect = new Rect(nodeRect.x + nodeRect.width - 20, nodeRect.y + 5, 15, 15);
                GUI.Label(expandRect, "+", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white }, fontSize = 14 });
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
            
            Rect instructionRect = new Rect(rect.x + 5, rect.y + 5, 280, 60);
            GUI.Label(instructionRect, "Click: Expand node\nDrag: Move node\nMiddle-click drag: Pan view\nScroll: Zoom", style);
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
                
                GraphNodeData childNode = null;
                
                if (conn.fromKind == PackedConnection.Kind.Managed && conn.from >= 0 && conn.from < m_Snapshot.managedObjects.Length)
                {
                    var fromObj = m_Snapshot.managedObjects[conn.from];
                    var type = m_Snapshot.managedTypes[fromObj.managedTypesArrayIndex];
                    var subtitle = $"Size: {EditorUtility.FormatBytes(fromObj.size)}\nAddr: 0x{fromObj.address:X}";
                    childNode = new GraphNodeData(m_NextNodeId++, type.name, subtitle, childPosition, true, fromObj.managedObjectsArrayIndex);
                }
                else if (conn.fromKind == PackedConnection.Kind.Native && conn.from >= 0 && conn.from < m_Snapshot.nativeObjects.Length)
                {
                    var fromObj = m_Snapshot.nativeObjects[conn.from];
                    var type = m_Snapshot.nativeTypes[fromObj.nativeTypesArrayIndex];
                    var subtitle = !string.IsNullOrEmpty(fromObj.name) ? $"Name: {fromObj.name}\n" : "";
                    subtitle += $"Size: {EditorUtility.FormatBytes(fromObj.size)}";
                    childNode = new GraphNodeData(m_NextNodeId++, type.name, subtitle, childPosition, false, fromObj.nativeObjectsArrayIndex);
                }
                
                if (childNode != null)
                {
                    m_Nodes[childNode.id] = childNode;
                    node.childNodes.Add(childNode.id);
                }
            }
        }
    }
}
