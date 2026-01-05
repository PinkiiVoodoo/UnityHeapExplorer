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

    public class ReferenceGraphViewIMGUI
    {
        const float NODE_VERTICAL_SPACING = 120f; // Vertical spacing between child nodes

        readonly PackedMemorySnapshot m_Snapshot;
        readonly Action<AbstractThreadJob> m_JobRunner;
        
        Dictionary<int, GraphNodeData> m_Nodes = new Dictionary<int, GraphNodeData>();
        HashSet<GraphNodeData> m_NodesSet = new HashSet<GraphNodeData>();
        float m_Zoom = 1.0f;
        bool m_IsPanning = false;
        Vector2 m_PanStart;
        GraphNodeData m_DraggingNode = null;
        Vector2 m_DragOffset;
        GraphNodeData m_HoveredNode = null;

        public ReferenceGraphViewIMGUI(PackedMemorySnapshot snapshot, Action<AbstractThreadJob> jobRunner)
        {
            m_Snapshot = snapshot;
            m_JobRunner = jobRunner;
        }
        
        // Transform mouse position from screen space to graph space accounting for zoom
        Vector2 ScreenToGraphSpace(Vector2 screenPos, Rect graphArea)
        {
            Vector2 pivot = graphArea.size * 0.5f;
            return (screenPos - pivot) / m_Zoom + pivot;
        }

        public void Clear()
        {
            m_Nodes.Clear();
        }

        public void ShowObject(ObjectProxy obj, Vector2 position)
        {
            Clear();
            AddObjectNode(obj, position);
        }

        void AddObjectNode(ObjectProxy obj, Vector2 position)
        {
            AddObjectNode(obj, position, null);
        }
        
        void AddObjectNode(ObjectProxy obj, GraphNodeData parent)
        {
            Vector2 position = new Vector2(
                parent.position.x - 250,
                parent.position.y + parent.childNodes.Count * NODE_VERTICAL_SPACING);
            AddObjectNode(obj, position, parent);
        }
        
        void AddObjectNode(ObjectProxy obj, Vector2 position, GraphNodeData parent)
        {
            var node = new GraphNodeData(position, obj);
            AddNode(parent, node);
        }
        
        void AddNode(GraphNodeData parent, GraphNodeData node)
        {
            if (m_NodesSet.Add(node))
            {
                m_Nodes[node.GetHashCode()] = node;
            }
            else
            {
                // This is a hack because I'm lazy to implement the comparation before the creation of the node
                m_NodesSet.TryGetValue(node, out node);
            }

            if (parent != null)
            {
                parent.childNodes.Add(node.GetHashCode());
            }
        }

        public void OnGUI(Rect rect)
        {
            // Begin scrollable area
            GUI.Box(rect, "", EditorStyles.helpBox);
            
            Rect graphArea = new Rect(0, 0, rect.width * 2, rect.height * 2);
            
            GUI.BeginGroup(graphArea);
            
            // Apply zoom and offset
            Matrix4x4 originalMatrix = GUI.matrix;
            Vector2 pivot = graphArea.size * 0.5f;
            GUIUtility.ScaleAroundPivot(Vector2.one * m_Zoom, pivot);
            
            // Handle events
            HandleEvents(graphArea);
            
            // Draw connections first
            DrawConnections();
            
            // Draw nodes
            foreach (var node in m_Nodes.Values)
            {
                DrawNode(node, graphArea);
            }
            
            GUI.matrix = originalMatrix;
            
            // Draw hover inspector outside of the zoom matrix
            if (m_HoveredNode != null)
            {
                DrawHoverInspector(m_HoveredNode, graphArea);
            }
            
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
            
            // Update hover state on mouse move
            if (e.type == EventType.MouseMove)
            {
                Vector2 mouseInGraphSpace = ScreenToGraphSpace(e.mousePosition, rect);
                m_HoveredNode = null;
                
                foreach (var node in m_Nodes.Values)
                {
                    // Check if mouse is within circle radius
                    float distance = Vector2.Distance(mouseInGraphSpace, node.position);
                    if (distance <= node.radius)
                    {
                        m_HoveredNode = node;
                        break;
                    }
                }
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
                    node.rect = new Rect(node.position - new Vector2(node.radius, node.radius), new Vector2(node.radius * 2, node.radius * 2));
                }
                m_PanStart = e.mousePosition;
                e.Use();
            }
            
            // Handle node dragging with left mouse button
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // Transform mouse position to account for zoom
                Vector2 mouseInGraphSpace = ScreenToGraphSpace(e.mousePosition, rect);
                
                foreach (var node in m_Nodes.Values)
                {
                    float distance = Vector2.Distance(mouseInGraphSpace, node.position);
                    
                    if (distance <= node.radius)
                    {
                        // Check if clicking the expand to root button
                        if (!node.isExpanded)
                        {
                            // Buttons are positioned at the top right of the circle
                            Vector2 buttonBasePos = node.position + new Vector2(node.radius - 38, -node.radius + 5);
                            Rect expandToRootButtonRect = new Rect(buttonBasePos.x, buttonBasePos.y, 15, 15);
                            if (node.CanExpandToRoot() && expandToRootButtonRect.Contains(mouseInGraphSpace))
                            {
                                ExpandToRoot(node);
                                e.Use();
                                break;
                            }
                            
                            // Check if clicking the expand button
                            Vector2 expandButtonPos = node.position + new Vector2(node.radius - 20, -node.radius + 5);
                            Rect expandButtonRect = new Rect(expandButtonPos.x, expandButtonPos.y, 15, 15);
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
                // Transform mouse position to account for zoom
                Vector2 mouseInGraphSpace = ScreenToGraphSpace(e.mousePosition, rect);
                
                // Update node position during drag
                Vector2 newPosition = mouseInGraphSpace - m_DragOffset;
                m_DraggingNode.position = newPosition;
                m_DraggingNode.rect = new Rect(newPosition - new Vector2(m_DraggingNode.radius, m_DraggingNode.radius), new Vector2(m_DraggingNode.radius * 2, m_DraggingNode.radius * 2));
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

        void DrawNode(GraphNodeData node, Rect containerRect)
        {
            Rect nodeRect = node.rect;

            if (!nodeRect.Overlaps(containerRect))
            {
                return;
            }
            
            // Draw using Unity Handles for circles
            Handles.BeginGUI();
            
            if (node.isRoot)
            {
                // Draw outer golden glow for root nodes
                Handles.color = new Color(1.0f, 0.84f, 0.0f, 0.8f); // Gold
                Handles.DrawSolidDisc(node.position, Vector3.forward, node.radius + 3);
                
                Handles.color = new Color(1.0f, 0.9f, 0.3f, 0.9f); // Lighter gold
                Handles.DrawSolidDisc(node.position, Vector3.forward, node.radius + 2);
            }
            
            if (node.isEmptyShellObject)
            {
                Handles.color = new Color(1.0f, 0.3f, 0.3f, 0.9f); // Red warning
                Handles.DrawSolidDisc(node.position, Vector3.forward, node.radius + 2);
            }
            
            // Draw border (black circle)
            Handles.color = Color.black;
            Handles.DrawSolidDisc(node.position, Vector3.forward, node.radius + 1);
            
            // Draw node background
            Handles.color = node.color;
            Handles.DrawSolidDisc(node.position, Vector3.forward, node.radius);
            
            Handles.EndGUI();
            
            // Draw title (only title, no subtitle)
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.wordWrap = true;
            titleStyle.fontSize = 11;
            
            // Title area is centered in the circle
            Rect titleRect = new Rect(node.position.x - node.radius + 5, node.position.y - 10, node.radius * 2 - 10, 20);
            GUI.Label(titleRect, node.title, titleStyle);
            
            // Draw expand buttons if not expanded
            if (!node.isExpanded)
            {
                if (node.CanExpandToRoot())
                {
                    // Draw expand to root button (left button with "R")
                    Vector2 expandToRootPos = node.position + new Vector2(node.radius - 38, -node.radius + 5);
                    Rect expandToRootButtonRect = new Rect(expandToRootPos.x, expandToRootPos.y, 15, 15);
                    
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
                }
                
                // Draw expand one level button (right button with "+")
                Vector2 expandPos = node.position + new Vector2(node.radius - 20, -node.radius + 5);
                Rect expandButtonRect = new Rect(expandPos.x, expandPos.y, 15, 15);
                
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
                        // Calculate the direction from parent to child
                        Vector2 direction = (childNode.position - node.position).normalized;
                        
                        // Start point is at the edge of parent circle in the direction of child
                        Vector2 start = node.position + direction * node.radius;
                        
                        // End point is at the edge of child circle in the direction of parent
                        Vector2 end = childNode.position - direction * childNode.radius;
                        
                        Handles.DrawAAPolyLine(3f, start, end);
                        
                        // Draw arrow head at the end
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
        
        void DrawHoverInspector(GraphNodeData node, Rect graphArea)
        {
            float inspectorWidth = 250f;
            float inspectorHeight = 100f;
            
            // Transform node position from graph space to screen space (accounting for zoom)
            Vector2 pivot = graphArea.size * 0.5f;
            Vector2 screenNodePos = (node.position - pivot) * m_Zoom + pivot;
            float screenRadius = node.radius * m_Zoom;
            
            // Position the inspector near the node but offset to not overlap
            Vector2 inspectorPos = screenNodePos + new Vector2(screenRadius + 10, -screenRadius);
            
            // Ensure inspector stays within graph area bounds
            if (inspectorPos.x + inspectorWidth > graphArea.width)
            {
                // Position to the left of the node instead
                inspectorPos.x = screenNodePos.x - screenRadius - inspectorWidth - 10;
            }
            
            if (inspectorPos.y < 0)
            {
                inspectorPos.y = 0;
            }
            else if (inspectorPos.y + inspectorHeight > graphArea.height)
            {
                inspectorPos.y = graphArea.height - inspectorHeight;
            }
            
            Rect inspectorRect = new Rect(inspectorPos.x, inspectorPos.y, inspectorWidth, inspectorHeight);
            
            // Draw background with node color
            Rect backgroundRect = new Rect(inspectorRect.x - 1, inspectorRect.y - 1, inspectorRect.width + 2, inspectorRect.height + 2);
            EditorGUI.DrawRect(backgroundRect, Color.black);
            EditorGUI.DrawRect(inspectorRect, node.color);
            
            // Draw semi-transparent overlay for better text readability
            EditorGUI.DrawRect(inspectorRect, new Color(0, 0, 0, 0.3f));
            
            // Draw title
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.UpperLeft;
            titleStyle.wordWrap = true;
            titleStyle.fontSize = 11;
            
            Rect titleRect = new Rect(inspectorRect.x + 5, inspectorRect.y + 5, inspectorRect.width - 10, 20);
            GUI.Label(titleRect, node.title, titleStyle);
            
            // Draw subtitle
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label);
            subtitleStyle.normal.textColor = Color.white;
            subtitleStyle.fontSize = 9;
            subtitleStyle.wordWrap = true;
            subtitleStyle.alignment = TextAnchor.UpperLeft;
            
            Rect subtitleRect = new Rect(inspectorRect.x + 5, inspectorRect.y + 25, inspectorRect.width - 10, inspectorRect.height - 30);
            GUI.Label(subtitleRect, node.subtitle, subtitleStyle);
        }
        
        void ExpandNode(GraphNodeData node)
        {
            if (node.isExpanded)
                return;
            
            node.isExpanded = true;

            var issues = 0;
            var referencedBy = RootPathUtility.GetReferencedBy(node.objectProxy, ref issues);
            
            for (int i = 0; i < referencedBy.Count; i++)
            {
                var conn = referencedBy[i];
                AddObjectNode(conn, node);
            }
        }

        void ExpandToRoot(GraphNodeData startNode)
        {
            m_JobRunner(new RootPathNodeJob(m_Snapshot, startNode, this));
        }
        
        public void AddPathNodes(RootPath path, GraphNodeData originNode)
        {
            var currentNode = originNode;
            
            for (int i = 1; i < path.count ; i++)
            {
                var objProxy = path[i];
                AddObjectNode(objProxy, currentNode);
                currentNode = m_Nodes[objProxy.GetHashCode()];
            }
        }
    }
}
