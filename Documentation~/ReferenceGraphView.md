# Reference Graph Visualization

## Overview

The Reference Graph window is a new visualization tool in HeapExplorer that helps identify memory leaks by showing object reference relationships in a visual graph format. This feature allows you to explore memory snapshots interactively and understand which objects are keeping other objects alive in memory.

## Features

### Visual Graph Navigation
- **Interactive Nodes**: Each node represents a memory object (managed or native)
- **Expandable References**: Click the [+] button to expand one level, or [R] to expand to the first root
- **Auto-arrange Layout**: Enable the "Auto-arrange" checkbox to automatically position nodes using a force-directed graph algorithm
- **Pan and Zoom**: Navigate large graphs easily with mouse controls
- **Clear Visual Distinction**: Different colors for managed (C#) and native (C++) objects

### Key Capabilities
- Shows object type, size, and memory address
- Displays up to 10 referencing objects per expansion (configurable)
- Supports both managed (C#) and native (Unity C++) objects
- Integrates with HeapExplorer's navigation system

## How to Use

### Opening the View
1. Capture a memory snapshot in HeapExplorer
2. Go to the View menu and select "Reference Graph"

### Exploring Objects
1. **Show an Object**: Click "Show Random Managed Object" or "Show Random Native Object" to display an initial object
2. **Expand References**: 
   - Click the [+] button to expand one level and see which objects reference it
   - Click the [R] button to automatically expand until reaching a root object
3. **Auto-arrange**: 
   - Enable the "Auto-arrange" checkbox to activate automatic node positioning
   - The graph will automatically expand and organize nodes to minimize overlaps
   - Nodes will continuously adjust their positions using a force-directed layout algorithm
   - Manually dragging a node will disable auto-arrange mode
4. **Arrange Nodes**: Drag individual nodes to reposition them manually
5. **Navigate**: 
   - Drag nodes to move them
   - Middle-click and drag to pan the entire view
   - Scroll wheel to zoom in/out
6. **Reset**: Click "Clear Graph" to start over

### Understanding the Display

#### Node Information
Each node displays:
- **Title**: The type name of the object
- **Size**: Memory consumed by the object
- **Address**: Memory address (or index for native objects)
- **Name**: Object name (for native objects that have one)

#### Node Colors
- **Blue nodes**: Managed (C#) objects
- **Orange nodes**: Native (C++) objects

#### Visual Indicators
- **+ Symbol**: Node can be expanded to show referencing objects
- **Arrows**: Show reference direction (from referrer to referenced object)

## Use Cases

### Finding Memory Leaks
1. Identify an object that should have been garbage collected
2. Use the graph to trace back through references
3. Find the root object keeping it alive
4. Determine why the root object itself isn't being released

### Understanding Object Relationships
1. Start with a specific object type
2. Expand to see what references it
3. Build a mental model of your object graph
4. Identify unexpected or circular references

### Analyzing Retention Paths
1. Select a leaked object
2. Expand it layer by layer
3. Trace the path back to static fields or other roots
4. Understand the retention chain

## Tips and Best Practices

1. **Start Small**: Begin with a single object to avoid overwhelming the view
2. **Use Auto-arrange**: Enable the "Auto-arrange" checkbox when working with complex graphs to automatically organize nodes and reduce overlaps
3. **Limit Expansions**: The tool limits to 10 child nodes per expansion to keep the graph readable
4. **Use with Other Views**: Combine with "Paths to Root" view for comprehensive leak analysis
5. **Clear Regularly**: Use "Clear Graph" when switching focus to different objects
6. **Organize Nodes**: Drag nodes to arrange them in a layout that makes sense for your analysis (this will disable auto-arrange)
7. **Pan vs Drag**: Middle-click to pan the entire view, left-click-drag to move individual nodes

## Technical Notes

- Built using Unity's IMGUI system for compatibility with HeapExplorer
- Uses existing PackedConnection data from memory snapshots
- Integrates with HeapExplorer's GotoCommand navigation system
- Maximum of 10 child nodes shown per expansion (defined by MAX_CHILD_NODES constant)
- Node spacing is 120 pixels vertically (defined by NODE_VERTICAL_SPACING constant)

## Limitations

- Shows only "referenced by" relationships (not "references to")
- Limited to 10 child nodes per expansion to maintain readability
- Performance depends on the size of the memory snapshot
- Very large object graphs may require multiple expansions to fully explore

## Future Enhancements

Potential improvements could include:
- Bidirectional navigation (show both references and referrers)
- Configurable node expansion limit
- Search/filter functionality
- Export graph to image
- Configurable force-directed layout parameters
- Collapsible nodes to hide subtrees
