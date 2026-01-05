//
// Heap Explorer for Unity. Copyright (c) 2019-2024 Peter Schraut (www.console-dev.de). See LICENSE.md
// https://github.com/pschraut/UnityHeapExplorer/
//
using UnityEngine;
using UnityEditor;

namespace HeapExplorer
{
    public class ReferenceGraphWindow : HeapExplorerView
    {
        ReferenceGraphViewIMGUI m_GraphView;
        ObjectProxy m_InitialObjectProxy;
        bool m_NeedsRebuild;
        Rect m_GraphRect;

        [InitializeOnLoadMethod]
        static void Register()
        {
            HeapExplorerWindow.Register<ReferenceGraphWindow>();
        }

        public override void Awake()
        {
            base.Awake();

            titleContent = new GUIContent("Reference Graph", "Visualize object references to find memory leaks");
            viewMenuOrder = 500;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_NeedsRebuild = true;
        }

        protected override void OnShow()
        {
            base.OnShow();
            m_NeedsRebuild = true;
        }

        public override void OnGUI()
        {
            base.OnGUI();

            // Instructions area at the top
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Reference Graph", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Click [+] to expand one level, [R] to expand to root. Drag nodes to reposition.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Clear Graph", GUILayout.Width(120)))
                    {
                        if (m_GraphView != null)
                        {
                            m_GraphView.Clear();
                        }
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Show Random Managed Object", GUILayout.Width(220)))
                    {
                        ShowRandomManagedObject();
                    }

                    if (GUILayout.Button("Show Random Native Object", GUILayout.Width(200)))
                    {
                        ShowRandomNativeObject();
                    }
                }
            }

            // Graph view area
            if (m_GraphView == null && snapshot != null)
            {
                m_GraphView = new ReferenceGraphViewIMGUI(snapshot, ScheduleJob);
                m_NeedsRebuild = true;
            }

            if (m_NeedsRebuild && m_GraphView != null)
            {
                m_NeedsRebuild = false;

                if (m_InitialObjectProxy != null)
                {
                    ShowObject(m_InitialObjectProxy);
                }
                else
                {
                    // Show first valid managed object by default
                    ShowRandomManagedObject();
                }
            }

            // Draw the graph view
            if (m_GraphView != null)
            {
                m_GraphRect = GUILayoutUtility.GetRect(0, 10000, 0, 10000, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
                m_GraphView.OnGUI(m_GraphRect);
            }
        }

        void ShowRandomManagedObject()
        {
            if (snapshot == null || snapshot.managedObjects == null || snapshot.managedObjects.Length == 0)
            {
                Debug.LogWarning("No managed objects available in snapshot");
                return;
            }

            // Find a valid managed object to display
            for (int i = 0; i < snapshot.managedObjects.Length && i < 100; i++)
            {
                var obj = snapshot.managedObjects[Random.Range(0, snapshot.managedObjects.Length)];
                if (obj.address != 0 && obj.size > 0)
                {
                    ShowObject(new ObjectProxy(snapshot, obj));
                    return;
                }
            }
        }

        void ShowRandomNativeObject()
        {
            if (snapshot == null || snapshot.nativeObjects == null || snapshot.nativeObjects.Length == 0)
            {
                Debug.LogWarning("No native objects available in snapshot");
                return;
            }

            // Find a valid native object to display
            for (int i = 0; i < snapshot.nativeObjects.Length && i < 100; i++)
            {
                var obj = snapshot.nativeObjects[Random.Range(0, snapshot.nativeObjects.Length)];
                if (obj.size > 0)
                {
                    ShowObject(new ObjectProxy(snapshot, obj));
                    return;
                }
            }
        }

        void ShowObject(ObjectProxy obj)
        {
            if (m_GraphView != null)
            {
                m_GraphView.ShowObject(obj, new Vector2(400, 200));
            }
            else
            {
                m_InitialObjectProxy = obj;
                m_NeedsRebuild = true;
            }
        }

        public override int CanProcessCommand(GotoCommand command)
        {
            if (!command.ShowInReferencesView) 
                return 0;
            
            // We can handle managed and native objects
            if (command.toManagedObject.isValid || command.toNativeObject.isValid || command.toStaticField.isValid || command.toGCHandle.isValid)
                return 100;

            return 0;
        }

        public override void RestoreCommand(GotoCommand command)
        {
            if (command.toManagedObject.isValid)
            {
                ShowObject(new ObjectProxy(snapshot, command.toManagedObject.packed));
            }
            else if (command.toNativeObject.isValid)
            {
                ShowObject(new ObjectProxy(snapshot, command.toNativeObject.packed));
            }
            else if (command.toGCHandle.isValid)
            {
                ShowObject(new ObjectProxy(snapshot, command.toGCHandle.packed));
            }
            else if (command.toStaticField.isValid)
            {
                ShowObject(new ObjectProxy(snapshot, command.toStaticField.packed));
            }
        }

        protected override void OnHide()
        {
            base.OnHide();
            m_GraphView = null;
        }

        public override void OnDestroy()
        {
            m_GraphView = null;
            base.OnDestroy();
        }
    }
}
