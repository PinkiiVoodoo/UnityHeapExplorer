//
// Heap Explorer for Unity. Copyright (c) 2019-2020 Peter Schraut (www.console-dev.de). See LICENSE.md
// https://github.com/pschraut/UnityHeapExplorer/
//
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.IMGUI.Controls;
using UnityEditor;

namespace HeapExplorer
{
    public class RootPathControl : AbstractTreeView
    {
        public System.Action<RootPath> onSelectionChange;

        PackedMemorySnapshot m_Snapshot;
        int m_UniqueId = 1;

        enum Column
        {
            Type,
            Name,
            Depth,
            Address,
        }

        public RootPathControl(HeapExplorerWindow window, string editorPrefsKey, TreeViewState state)
            : base(window, editorPrefsKey, state, new MultiColumnHeader(
                new MultiColumnHeaderState(new[]
                {
                new MultiColumnHeaderState.Column() { headerContent = new GUIContent("Type"), width = 200, autoResize = true },
                new MultiColumnHeaderState.Column() { headerContent = new GUIContent("C++ Name"), width = 200, autoResize = true },
                new MultiColumnHeaderState.Column() { headerContent = new GUIContent("Depth"), width = 80, autoResize = true },
                new MultiColumnHeaderState.Column() { headerContent = new GUIContent("Address"), width = 200, autoResize = true },
                })))
        {
            columnIndexForTreeFoldouts = 0;
            multiColumnHeader.canSort = false;

            Reload();
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);
            if (onSelectionChange == null)
                return;

            if (selectedIds == null || selectedIds.Count == 0)
            {
                onSelectionChange.Invoke(null);
                return;
            }

            var selectedItem = FindItem(selectedIds[0], rootItem) as Item;
            if (selectedItem == null)
            {
                onSelectionChange.Invoke(null);
                return;
            }

            onSelectionChange.Invoke(selectedItem.rootPath);
        }

        protected override int OnSortItem(TreeViewItem x, TreeViewItem y)
        {
            return 0;
        }

        public TreeViewItem BuildTree(PackedMemorySnapshot snapshot, RootPathUtility paths)
        {
            m_Snapshot = snapshot;
            m_UniqueId = 1;

            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            if (m_Snapshot == null || paths == null || paths.count == 0)
            {
                root.AddChild(new TreeViewItem { id = 1, depth = -1, displayName = "" });
                return root;
            }

            for (var j=0; j< paths.count; ++j)
            {
                if (window.isClosing) // the window is closing
                    break;

                var path = paths[j];
                var parent = root;

                for (var n = path.count - 1; n >= 0; --n)
                {
                    var obj = path[n];
                    Item newItem = null;

                    if (obj.native.isValid)
                        newItem = AddNativeUnityObject(parent, obj.native.packed);
                    else if (obj.managed.isValid)
                        newItem = AddManagedObject(parent, obj.managed.packed);
                    else if (obj.gcHandle.isValid)
                        newItem = AddGCHandle(parent, obj.gcHandle.packed);
                    else if (obj.staticField.isValid)
                        newItem = AddStaticField(parent, obj.staticField.packed);

                    if (parent == root)
                    {
                        parent = newItem;
                        newItem.rootPath = path;
                    }
                }
            }

            return root;
        }

        Item AddGCHandle(TreeViewItem parent, PackedGCHandle gcHandle)
        {
            var item = new GCHandleItem
            {
                id = m_UniqueId++,
                depth = parent.depth + 1,
            };

            item.Initialize(this, m_Snapshot, gcHandle.gcHandlesArrayIndex);
            parent.AddChild(item);
            return item;
        }

        Item AddManagedObject(TreeViewItem parent, PackedManagedObject managedObject)
        {
            var item = new ManagedObjectItem
            {
                id = m_UniqueId++,
                depth = parent.depth + 1,
            };

            item.Initialize(this, m_Snapshot, managedObject.managedObjectsArrayIndex);
            parent.AddChild(item);
            return item;
        }

        Item AddNativeUnityObject(TreeViewItem parent, PackedNativeUnityEngineObject nativeObject)
        {
            var item = new NativeObjectItem
            {
                id = m_UniqueId++,
                depth = parent.depth + 1,
            };

            item.Initialize(this, m_Snapshot, nativeObject);
            parent.AddChild(item);
            return item;
        }

        Item AddStaticField(TreeViewItem parent, PackedManagedStaticField staticField)
        {
            var item = new ManagedStaticFieldItem
            {
                id = m_UniqueId++,
                depth = parent.depth + 1,
            };

            item.Initialize(this, m_Snapshot, staticField.staticFieldsArrayIndex);
            parent.AddChild(item);
            return item;
        }


        ///////////////////////////////////////////////////////////////////////////
        // TreeViewItem's
        ///////////////////////////////////////////////////////////////////////////

        class Item : AbstractTreeViewItem
        {
            public RootPath rootPath;

            protected RootPathControl m_Owner;
            protected string m_Value;
            protected System.UInt64 m_Address;

            public override void GetItemSearchString(string[] target, out int count, out string type, out string label)
            {
                base.GetItemSearchString(target, out count, out type, out label);

                type = displayName;
                target[count++] = displayName;
                target[count++] = m_Value;
                target[count++] = string.Format(StringFormat.Address, m_Address);
            }

            public override void OnGUI(Rect position, int column)
            {
                if (column == 0 && depth == 0)
                {
                    switch(rootPath.reason)
                    {
                        case RootPathReason.Static:
                        case RootPathReason.DontDestroyOnLoad:
                        case RootPathReason.DontUnloadUnusedAsset:
                        case RootPathReason.UnityManager:
                            GUI.Box(HeEditorGUI.SpaceL(ref position, position.height), new GUIContent(HeEditorStyles.warnImage, rootPath.reasonString), HeEditorStyles.iconStyle);
                            break;
                    }
                }

                switch ((Column)column)
                {
                    case Column.Type:
                        HeEditorGUI.TypeName(position, displayName);
                        break;

                    case Column.Name:
                        EditorGUI.LabelField(position, m_Value);
                        break;

                    case Column.Address:
                        if (m_Address != 0) // statics dont have an address in PackedMemorySnapshot and I don't want to display a misleading 0
                            HeEditorGUI.Address(position, m_Address);
                        break;

                    case Column.Depth:
                        if (rootPath != null)
                            EditorGUI.LabelField(position, rootPath.count.ToString());
                        break;
                }
            }
        }

        // ------------------------------------------------------------------------

        class GCHandleItem : Item
        {
            PackedMemorySnapshot m_Snapshot;
            RichGCHandle m_GCHandle;

            public void Initialize(RootPathControl owner, PackedMemorySnapshot snapshot, int gcHandleArrayIndex)
            {
                m_Owner = owner;
                m_Snapshot = snapshot;
                m_GCHandle = new RichGCHandle(m_Snapshot, gcHandleArrayIndex);

                displayName = "GCHandle";
                m_Value = m_GCHandle.managedObject.isValid ? m_GCHandle.managedObject.type.name : "";
                m_Address = m_GCHandle.managedObjectAddress;
            }

            public override void OnGUI(Rect position, int column)
            {
                if (column == 0)
                {
                    if (HeEditorGUI.GCHandleButton(HeEditorGUI.SpaceL(ref position, position.height)))
                    {
                        m_Owner.window.OnGoto(new GotoCommand(m_GCHandle));
                    }

                    if (m_GCHandle.nativeObject.isValid)
                    {
                        if (HeEditorGUI.CppButton(HeEditorGUI.SpaceR(ref position, position.height)))
                        {
                            m_Owner.window.OnGoto(new GotoCommand(m_GCHandle.nativeObject));
                        }
                    }

                    if (m_GCHandle.managedObject.isValid)
                    {
                        if (HeEditorGUI.CsButton(HeEditorGUI.SpaceR(ref position, position.height)))
                        {
                            m_Owner.window.OnGoto(new GotoCommand(m_GCHandle.managedObject));
                        }
                    }
                }

                base.OnGUI(position, column);
            }
        }

        // ------------------------------------------------------------------------

        class ManagedObjectItem : Item
        {
            RichManagedObject m_ManagedObject;

            public void Initialize(RootPathControl owner, PackedMemorySnapshot snapshot, int arrayIndex)
            {
                m_Owner = owner;
                m_ManagedObject = new RichManagedObject(snapshot, arrayIndex);

                displayName = m_ManagedObject.type.name;
                m_Address = m_ManagedObject.address;
                m_Value = m_ManagedObject.nativeObject.isValid ? m_ManagedObject.nativeObject.name : "";
            }

            public override void OnGUI(Rect position, int column)
            {
                if (column == 0)
                {
                    if (HeEditorGUI.ReferenceButton(HeEditorGUI.SpaceL(ref position, position.height)))
                    {
                        m_Owner.window.OnGoto(new GotoReferenceCommand(m_ManagedObject));
                    }
                    
                    if (HeEditorGUI.CsButton(HeEditorGUI.SpaceL(ref position, position.height)))
                    {
                        m_Owner.window.OnGoto(new GotoCommand(m_ManagedObject));
                    }

                    if (m_ManagedObject.gcHandle.isValid)
                    {
                        if (HeEditorGUI.GCHandleButton(HeEditorGUI.SpaceR(ref position, position.height)))
                        {
                            m_Owner.window.OnGoto(new GotoCommand(m_ManagedObject.gcHandle));
                        }
                    }

                    if (m_ManagedObject.nativeObject.isValid)
                    {
                        if (HeEditorGUI.CppButton(HeEditorGUI.SpaceR(ref position, position.height)))
                        {
                            m_Owner.window.OnGoto(new GotoCommand(m_ManagedObject.nativeObject));
                        }
                    }
                }

                base.OnGUI(position, column);
            }
        }

        // ------------------------------------------------------------------------

        class ManagedStaticFieldItem : Item
        {
            PackedMemorySnapshot m_Snapshot;
            PackedManagedStaticField m_StaticField;

            public void Initialize(RootPathControl owner, PackedMemorySnapshot snapshot, int arrayIndex)
            {
                m_Owner = owner;
                m_Snapshot = snapshot;
                m_StaticField = m_Snapshot.managedStaticFields[arrayIndex];

                var staticClassType = m_Snapshot.managedTypes[m_StaticField.managedTypesArrayIndex];
                var staticField = staticClassType.fields[m_StaticField.fieldIndex];

                m_Address = 0;
                displayName = staticClassType.name;
                m_Value = "static " + staticField.name;
            }

            public override void OnGUI(Rect position, int column)
            {
                if (column == 0)
                {
                    if (HeEditorGUI.CsStaticButton(HeEditorGUI.SpaceL(ref position, position.height)))
                    {
                        m_Owner.window.OnGoto(new GotoCommand(new RichStaticField(m_Snapshot, m_StaticField.staticFieldsArrayIndex)));
                    }
                }

                base.OnGUI(position, column);
            }
        }

        // ------------------------------------------------------------------------

        class NativeObjectItem : Item
        {
            PackedMemorySnapshot m_Snapshot;
            RichNativeObject m_NativeObject;

            public void Initialize(RootPathControl owner, PackedMemorySnapshot snapshot, PackedNativeUnityEngineObject nativeObject)
            {
                m_Owner = owner;
                m_Snapshot = snapshot;
                m_NativeObject = new RichNativeObject(snapshot, nativeObject.nativeObjectsArrayIndex);

                m_Value = m_NativeObject.name;
                m_Address = m_NativeObject.address;
                displayName = m_NativeObject.type.name;

                // If it's a MonoBehaviour or ScriptableObject, use the C# typename instead
                // It makes it easier to understand what it is, otherwise everything displays 'MonoBehaviour' only.
                // TODO: Move to separate method
                if (m_NativeObject.type.IsSubclassOf(m_Snapshot.coreTypes.nativeMonoBehaviour) || m_NativeObject.type.IsSubclassOf(m_Snapshot.coreTypes.nativeScriptableObject))
                {
                    string monoScriptName;
                    if (m_Snapshot.FindNativeMonoScriptType(m_NativeObject.packed.nativeObjectsArrayIndex, out monoScriptName) != -1)
                    {
                        if (!string.IsNullOrEmpty(monoScriptName))
                            displayName = monoScriptName;
                    }
                }
            }

            public override void OnGUI(Rect position, int column)
            {
                if (column == 0)
                {
                    if (HeEditorGUI.ReferenceButton(HeEditorGUI.SpaceL(ref position, position.height)))
                    {
                        m_Owner.window.OnGoto(new GotoReferenceCommand(m_NativeObject));
                    }
                    
                    if (HeEditorGUI.CppButton(HeEditorGUI.SpaceL(ref position, position.height)))
                    {
                        m_Owner.window.OnGoto(new GotoCommand(m_NativeObject));
                    }

                    if (m_NativeObject.gcHandle.isValid)
                    {
                        if (HeEditorGUI.GCHandleButton(HeEditorGUI.SpaceR(ref position, position.height)))
                        {
                            m_Owner.window.OnGoto(new GotoCommand(m_NativeObject.gcHandle));
                        }
                    }

                    if (m_NativeObject.managedObject.isValid)
                    {
                        if (HeEditorGUI.CsButton(HeEditorGUI.SpaceR(ref position, position.height)))
                        {
                            m_Owner.window.OnGoto(new GotoCommand(m_NativeObject.managedObject));
                        }
                    }
                }

                base.OnGUI(position, column);
            }
        }
    }
}
