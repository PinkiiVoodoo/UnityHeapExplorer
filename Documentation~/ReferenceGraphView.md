# Reference Graph Visualization

## Overview

The Reference Graph window is a new visualization tool in HeapExplorer that helps identify memory leaks by showing object reference relationships in a visual graph format. This feature allows you to explore memory snapshots interactively and understand which objects are keeping other objects alive in memory.

## Features

### Visual Graph Navigation
- **Interactive Nodes**: Each node represents a memory object (managed or native)
- **Expandable References**: Double-click any node to see all objects that reference it
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
2. **Expand References**: Double-click on any node to see which objects reference it
3. **Navigate**: 
   - Middle-click and drag to pan the view
   - Scroll wheel to zoom in/out
4. **Reset**: Click "Clear Graph" to start over

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
2. **Limit Expansions**: The tool limits to 10 child nodes per expansion to keep the graph readable
3. **Use with Other Views**: Combine with "Paths to Root" view for comprehensive leak analysis
4. **Clear Regularly**: Use "Clear Graph" when switching focus to different objects
5. **Pan Don't Scroll**: Use middle-click to pan instead of scrollbars for better control

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
- Automatic layout algorithms for better organization
- Collapsible nodes to hide subtrees
