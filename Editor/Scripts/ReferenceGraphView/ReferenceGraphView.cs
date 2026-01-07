//
// Heap Explorer for Unity. Copyright (c) 2019-2024 Peter Schraut (www.console-dev.de). See LICENSE.md
// https://github.com/pschraut/UnityHeapExplorer/
//

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Random = UnityEngine.Random;

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
        
        // Auto-arrange force-directed layout
        bool m_AutoArrange = false;
        const float REPULSION_STRENGTH = 10000f;
        const float ATTRACTION_STRENGTH = 0.1f;
        const float DAMPING = 0.75f;
        const float NODES_OFFSET = 30f;
        const float FORCE_SCALE = 0.05f;
        const float ZERO_DISTANCE_THRESHOLD = 0.0001f;
        Dictionary<int, Vector2> m_Velocities = new Dictionary<int, Vector2>();
        Dictionary<int, Vector2> m_Forces = new Dictionary<int, Vector2>();
        List<KeyValuePair<int, GraphNodeData>> m_NodeList = new List<KeyValuePair<int, GraphNodeData>>();
        HashSet<(int, int)> m_ProcessedEdges = new HashSet<(int, int)>();

        public ReferenceGraphViewIMGUI(PackedMemorySnapshot snapshot, Action<AbstractThreadJob> jobRunner)
        {
            m_Snapshot = snapshot;
            m_JobRunner = jobRunner;
        }

        public void Clear()
        {
            m_Nodes.Clear();
            m_Velocities.Clear();
            m_Forces.Clear();
            m_NodeList.Clear();
            m_ProcessedEdges.Clear();
        }
        
        public bool AutoArrange
        {
            get { return m_AutoArrange; }
            set { m_AutoArrange = value; }
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
            Vector2 position;
            if (parent.parentNode != null)
            {
                var grandParent = parent.parentNode;

                var randomOffset = Random.insideUnitCircle * parent.childNodes.Count;
                var direction = (parent.Position - grandParent.Position + randomOffset).normalized;
                position = parent.Position + direction * (parent.radius + GraphNodeData.Radius + NODES_OFFSET);
            }
            else {
                position = new Vector2(
                parent.Position.x - NODE_VERTICAL_SPACING,
                parent.Position.y + parent.childNodes.Count * NODE_VERTICAL_SPACING);
                
            }
            
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
                node.parentNode = parent;
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
            
            // Apply auto-arrange force-directed layout
            if (m_AutoArrange)
            {
                ApplyForceDirectedLayout();
            }
            
            // Handle events
            HandleEvents(graphArea);
            
            // Draw connections first
            DrawConnections();
            
            // Draw nodes
            foreach (var node in m_Nodes.Values)
            {
                DrawNode(node, graphArea);
            }
            
            // Draw hover inspector on top of everything
            if (m_HoveredNode != null)
            {
                DrawHoverInspector(m_HoveredNode);
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
            
            // Update hover state
            if (e.type == EventType.MouseMove || e.type == EventType.Repaint)
            {
                Vector2 mouseInGraphSpace = e.mousePosition;
                m_HoveredNode = null;
                
                foreach (var node in m_Nodes.Values)
                {
                    // Check if mouse is within circle radius
                    float distance = Vector2.Distance(mouseInGraphSpace, node.Position);
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
                    node.Position += delta / m_Zoom;
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
                    float distance = Vector2.Distance(mouseInGraphSpace, node.Position);
                    
                    if (distance <= node.radius)
                    {
                        // Check if clicking the expand to root button
                        if (!node.isExpanded)
                        {
                            // Buttons are positioned at the top right of the circle
                            Vector2 buttonBasePos = node.Position + new Vector2(node.radius - 38, -node.radius + 5);
                            Rect expandToRootButtonRect = new Rect(buttonBasePos.x, buttonBasePos.y, 15, 15);
                            if (node.CanExpandToRoot() && expandToRootButtonRect.Contains(mouseInGraphSpace))
                            {
                                ExpandToRoot(node);
                                e.Use();
                                break;
                            }
                            
                            // Check if clicking the expand button
                            Vector2 expandButtonPos = node.Position + new Vector2(node.radius - 20, -node.radius + 5);
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
                        m_DragOffset = mouseInGraphSpace - node.Position;
                        
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
                m_DraggingNode.Position = newPosition;
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
                Handles.DrawSolidDisc(node.Position, Vector3.forward, node.radius + 3);
                
                Handles.color = new Color(1.0f, 0.9f, 0.3f, 0.9f); // Lighter gold
                Handles.DrawSolidDisc(node.Position, Vector3.forward, node.radius + 2);
            }
            
            if (node.isEmptyShellObject)
            {
                Handles.color = new Color(1.0f, 0.3f, 0.3f, 0.9f); // Red warning
                Handles.DrawSolidDisc(node.Position, Vector3.forward, node.radius + 2);
            }
            
            // Draw border (black circle)
            Handles.color = Color.black;
            Handles.DrawSolidDisc(node.Position, Vector3.forward, node.radius + 1);
            
            // Draw node background
            Handles.color = node.color;
            Handles.DrawSolidDisc(node.Position, Vector3.forward, node.radius);
            
            Handles.EndGUI();
            
            // Draw title (only title, no subtitle)
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.wordWrap = true;
            titleStyle.fontSize = 11;
            
            // Title area is centered in the circle
            Rect titleRect = nodeRect;
            GUI.Label(titleRect, node.title, titleStyle);
            
            // Draw expand buttons if not expanded
            if (!node.isExpanded)
            {
                if (node.CanExpandToRoot())
                {
                    // Draw expand to root button (left button with "R")
                    Vector2 expandToRootPos = node.Position + new Vector2(node.radius - 38, -node.radius + 5);
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
                Vector2 expandPos = node.Position + new Vector2(node.radius - 20, -node.radius + 5);
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
                        Vector2 direction = (childNode.Position - node.Position).normalized;
                        
                        // Start point is at the edge of parent circle in the direction of child
                        Vector2 start = node.Position + direction * node.radius;
                        
                        // End point is at the edge of child circle in the direction of parent
                        Vector2 end = childNode.Position - direction * childNode.radius;
                        
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
            
            string instructions = m_AutoArrange 
                ? "Auto-arrange enabled\nClick [+]: Expand one level\nClick [R]: Expand to root\nDrag: Move node (disables auto-arrange)\nMiddle-click drag: Pan view\nScroll: Zoom"
                : "Click [+]: Expand one level\nClick [R]: Expand to root\nDrag: Move node\nMiddle-click drag: Pan view\nScroll: Zoom";
            
            // Calculate instruction rect based on content
            Vector2 instructionSize = style.CalcSize(new GUIContent(instructions));
            Rect instructionRect = new Rect(rect.x + 5, rect.y + 5, Mathf.Max(350, instructionSize.x + 10), Mathf.Max(90, instructionSize.y + 10));
            GUI.Label(instructionRect, instructions, style);
        }
        
        void ApplyForceDirectedLayout()
        {
            if (m_Nodes.Count <= 1)
                return;

            // Initialize forces and velocities
            // Note: This algorithm has O(n²) complexity for repulsion calculations.
            // For typical HeapExplorer usage with small graphs (10-50 nodes), this is acceptable.
            // For large graphs (>100 nodes), consider spatial partitioning optimizations.
            m_Forces.Clear();
            
            foreach (var nodeEntry in m_Nodes)
            {
                var nodeId = nodeEntry.Key;
                m_Forces[nodeId] = Vector2.zero;
                
                if (!m_Velocities.ContainsKey(nodeId))
                {
                    m_Velocities[nodeId] = Vector2.zero;
                }
            }
            
            // Calculate repulsion forces between all pairs of nodes
            m_NodeList.Clear();
            m_NodeList.AddRange(m_Nodes);
            
            for (int i = 0; i < m_NodeList.Count; i++)
            {
                var node1 = m_NodeList[i].Value;
                for (int j = i + 1; j < m_NodeList.Count; j++)
                {
                    var node2 = m_NodeList[j].Value;
                    
                    Vector2 delta = node1.Position - node2.Position;
                    float distanceSq = delta.sqrMagnitude;
                    
                    Vector2 direction;
                    
                    // Handle very close or overlapping nodes
                    if (distanceSq < ZERO_DISTANCE_THRESHOLD)
                    {
                        // Nodes are essentially at the same position, use a deterministic fallback direction
                        // based on node IDs to ensure consistent behavior
                        int hash = Mathf.Abs(m_NodeList[i].Key ^ m_NodeList[j].Key);
                        float angle = (hash % 360) * Mathf.Deg2Rad;
                        direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    }
                    else
                    {
                        // Normal case - nodes are far enough apart
                        direction = delta.normalized;
                    }

                    var repulsionStrength = REPULSION_STRENGTH;
                    repulsionStrength = node1.radius * 2 * node2.radius * 2;
                    
                    // Coulomb's law for repulsion - apply equal and opposite forces
                    Vector2 repulsionForce = direction * (repulsionStrength / Math.Max(NODES_OFFSET * NODES_OFFSET, distanceSq));
                    
                    m_Forces[m_NodeList[i].Key] += repulsionForce;
                    m_Forces[m_NodeList[j].Key] -= repulsionForce;
                }
            }
            
            // Calculate attraction forces along edges (bidirectional)
            m_ProcessedEdges.Clear();
            
            foreach (var nodeEntry in m_Nodes)
            {
                var node = nodeEntry.Value;
                var nodeId = nodeEntry.Key;
                
                foreach (var childId in node.childNodes)
                {
                    // Skip if we've already processed this edge in the opposite direction
                    if (m_ProcessedEdges.Contains((childId, nodeId)))
                        continue;
                    
                    m_ProcessedEdges.Add((nodeId, childId));
                    
                    if (m_Nodes.TryGetValue(childId, out GraphNodeData child))
                    {
                        Vector2 delta = child.Position - node.Position;
                        float distanceSq = delta.sqrMagnitude;
                        
                        var minDistance = MinDistanceBetweenNodes(node, child);
                        
                        // Skip attraction force when nodes are too close to avoid division by zero
                        // and because repulsion forces will dominate anyway at close range
                        distanceSq = Math.Max(distanceSq - minDistance * minDistance, 0);
                        if (distanceSq < ZERO_DISTANCE_THRESHOLD)
                        {
                            continue;
                        }
                        
                        // Calculate distance and direction efficiently
                        float distance = Mathf.Sqrt(distanceSq);
                        Vector2 direction = delta / distance;
                        
                        // Hooke's law for spring attraction - apply equal and opposite forces
                        Vector2 attractionForce = direction * (distance * ATTRACTION_STRENGTH);
                        m_Forces[nodeId] += attractionForce;
                        m_Forces[childId] -= attractionForce;
                    }
                }
            }
            
            // Update velocities and positions
            foreach (var nodeEntry in m_Nodes)
            {
                var node = nodeEntry.Value;
                var nodeId = nodeEntry.Key;
                
                if (nodeId == m_DraggingNode?.GetHashCode())
                    continue; // Skip updating position of dragged node
                
                // Update velocity with damping
                m_Velocities[nodeId] = (m_Velocities[nodeId] + m_Forces[nodeId] * FORCE_SCALE) * DAMPING;
                
                // Apply velocity to position
                node.Position += m_Velocities[nodeId];
            }
        }
        
        float MinDistanceBetweenNodes(GraphNodeData nodeA, GraphNodeData nodeB)
        {
            return nodeA.radius + nodeB.radius + NODES_OFFSET * (1 + Math.Max(0, nodeA.childNodes.Count + nodeB.childNodes.Count - 4) * 0.5f);
        }
        
        void DrawHoverInspector(GraphNodeData node)
        {
            // Position the inspector near the node but offset to not overlap
            Vector2 inspectorPos = node.Position + new Vector2(node.radius + 10, -node.radius);
            float inspectorWidth = 250f;
            float inspectorHeight = 100f;
            
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
