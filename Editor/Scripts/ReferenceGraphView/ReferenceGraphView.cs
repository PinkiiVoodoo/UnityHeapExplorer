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

        public ReferenceGraphViewIMGUI(PackedMemorySnapshot snapshot, Action<AbstractThreadJob> jobRunner)
        {
            m_Snapshot = snapshot;
            m_JobRunner = jobRunner;
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
                Vector2 mouseInGraphSpace = e.mousePosition;
                
                foreach (var node in m_Nodes.Values)
                {
                    Rect nodeRect = node.rect;
                    
                    if (nodeRect.Contains(mouseInGraphSpace))
                    {
                        // Check if clicking the expand to root button
                        if (!node.isExpanded)
                        {
                            Rect expandToRootButtonRect = new Rect(nodeRect.x + nodeRect.width - 38, nodeRect.y + 5, 15, 15);
                            if (node.CanExpandToRoot() && expandToRootButtonRect.Contains(mouseInGraphSpace))
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
                Vector2 mouseInGraphSpace = e.mousePosition;
                
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

        void DrawNode(GraphNodeData node, Rect containerRect)
        {
            Rect nodeRect = new Rect(node.position, node.rect.size);

            if (!nodeRect.Overlaps(containerRect))
            {
                return;
            }
            
            if (node.isRoot)
            {
                // Draw outer golden glow border for root nodes
                Rect glowRect1 = new Rect(nodeRect.x - 3, nodeRect.y - 3, nodeRect.width + 6, nodeRect.height + 6);
                EditorGUI.DrawRect(glowRect1, new Color(1.0f, 0.84f, 0.0f, 0.8f)); // Gold
                
                Rect glowRect2 = new Rect(nodeRect.x - 2, nodeRect.y - 2, nodeRect.width + 4, nodeRect.height + 4);
                EditorGUI.DrawRect(glowRect2, new Color(1.0f, 0.9f, 0.3f, 0.9f)); // Lighter gold
            }
            
            // Draw background and border
            Rect borderRect = new Rect(nodeRect.x - 1, nodeRect.y - 1, nodeRect.width + 2, nodeRect.height + 2);
            EditorGUI.DrawRect(borderRect, Color.black);
            EditorGUI.DrawRect(nodeRect, node.color);
            
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
                if (node.CanExpandToRoot())
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
                }
                
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
