using System.Numerics;
using ImGuiNET;

namespace EtlNodeEditor;

public class SimpleNodeEditor
{
    private NodeGraph _graph = new();
    private Node? _selectedNode;
    private Vector2 _scrolling = Vector2.Zero;
    private bool _isDraggingNode = false;
    private Guid? _draggingNodeId = null;
    
    // Connection dragging
    private bool _isDraggingConnection = false;
    private Guid? _connectionSourceNodeId = null;
    private string? _connectionSourcePin = null;
    private Vector2 _connectionDragPos = Vector2.Zero;
    
    // Plugin editor
    private PluginEditorWindow _pluginEditor = new();
    
    // Save/Load
    private string _currentFileName = "";
    private DateTime _lastAutoSave = DateTime.Now;
    private const float AUTOSAVE_INTERVAL = 60f; // seconds
    
    private const float NODE_WIDTH = 200f;
    private const float NODE_SLOT_RADIUS = 8f;
    private const float NODE_PIN_SPACING = 25f;

    public void Render()
    {
        ImGui.DockSpaceOverViewport();
        
        // Handle keyboard shortcuts
        if (ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyPressed(ImGuiKey.S))
        {
            SaveGraph();
        }
        if (ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyPressed(ImGuiKey.O))
        {
            LoadGraph();
        }
        if (ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyPressed(ImGuiKey.N))
        {
            NewGraph();
        }
        
        // Autosave
        if ((DateTime.Now - _lastAutoSave).TotalSeconds > AUTOSAVE_INTERVAL)
        {
            AutoSave();
            _lastAutoSave = DateTime.Now;
        }
        
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New", "Ctrl+N")) NewGraph();
                if (ImGui.MenuItem("Open...", "Ctrl+O")) LoadGraph();
                if (ImGui.MenuItem("Save", "Ctrl+S")) SaveGraph();
                if (ImGui.MenuItem("Save As...")) SaveGraphAs();
                ImGui.Separator();
                if (ImGui.MenuItem("Clear")) ClearGraph();
                ImGui.EndMenu();
            }
            
            if (ImGui.BeginMenu("Execute"))
            {
                if (ImGui.MenuItem("Execute All")) Task.Run(async () => await _graph.ExecuteAllAsync());
                ImGui.EndMenu();
            }
            
            if (ImGui.BeginMenu("Plugins"))
            {
                if (ImGui.MenuItem("Create New Plugin"))
                {
                    _pluginEditor.Show();
                }
                
                ImGui.Separator();
                ImGui.MenuItem("Manage Plugins", "", false, false);
                
                ImGui.EndMenu();
            }
            
            ImGui.EndMainMenuBar();
        }
        
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(250, ImGui.GetIO().DisplaySize.Y), ImGuiCond.FirstUseEver);
        
        if (ImGui.Begin("Sidebar"))
        {
            ImGui.Text("Add Nodes");
            ImGui.Separator();
            
            // Add button to create new plugin
            if (ImGui.Button("+ Create Plugin", new Vector2(-1, 30)))
            {
                _pluginEditor.Show();
            }
            
            // List all available plugins
            var plugins = PluginManager.Instance.GetAllPlugins();
            var pluginsByCategory = plugins.GroupBy(p => p.Category).OrderBy(g => g.Key);
            
            foreach (var category in pluginsByCategory)
            {
                if (ImGui.TreeNode(category.Key))
                {
                    foreach (var plugin in category)
                    {
                        var label = $"{plugin.Icon} {plugin.Name}";
                        if (ImGui.Button(label, new Vector2(-1, 35)))
                        {
                            var node = PluginManager.Instance.CreateNodeFromPlugin(plugin.Id);
                            node.Position = new Vector2(100, 100) + _scrolling;
                            _graph.AddNode(node);
                            Logger.Instance.Info($"Added node: {plugin.Name}", "NodeEditor");
                        }
                        
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(plugin.Description);
                        }
                        
                        // Right-click context menu
                        if (ImGui.BeginPopupContextItem($"plugin_ctx_{plugin.Id}"))
                        {
                            if (ImGui.MenuItem("Edit Plugin"))
                            {
                                _pluginEditor.Show(plugin);
                            }
                            
                            if (ImGui.MenuItem("Delete Plugin"))
                            {
                                PluginManager.Instance.RemovePlugin(plugin.Id);
                            }
                            
                            ImGui.EndPopup();
                        }
                    }
                    
                    ImGui.TreePop();
                }
            }
            
            ImGui.Separator();
            ImGui.Text("Selected Node");
            ImGui.Separator();
            
            if (_selectedNode != null)
            {
                RenderNodeConfig(_selectedNode);
            }
            else
            {
                ImGui.TextDisabled("No node selected");
            }
        }
        ImGui.End();
        
        ImGui.SetNextWindowPos(new Vector2(250, 20), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(ImGui.GetIO().DisplaySize.X - 500, ImGui.GetIO().DisplaySize.Y - 250), ImGuiCond.FirstUseEver);
        
        if (ImGui.Begin("Canvas", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            // Execute button at the top
            if (ImGui.Button("▶ Execute Graph", new Vector2(150, 30)) || (ImGui.IsKeyPressed(ImGuiKey.F5)))
            {
                Task.Run(async () => await _graph.ExecuteAllAsync());
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Execute all nodes starting from inputs (F5)");
            }
            
            ImGui.SameLine();
            var startingNodeCount = _graph.Nodes.Keys.Count(nodeId => 
                !_graph.Connections.Values.Any(c => c.TargetNodeId == nodeId));
            ImGui.Text($"Starting nodes: {startingNodeCount}");
            
            RenderCanvas();
        }
        ImGui.End();
        
        // Log window
        ImGui.SetNextWindowPos(new Vector2(250, ImGui.GetIO().DisplaySize.Y - 230), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(ImGui.GetIO().DisplaySize.X - 250, 230), ImGuiCond.FirstUseEver);
        
        if (ImGui.Begin("Log"))
        {
            RenderLogWindow();
        }
        ImGui.End();
        
        // Render plugin editor window
        _pluginEditor.Render();
    }
    
    private void RenderLogWindow()
    {
        if (ImGui.Button("Clear"))
        {
            Logger.Instance.Clear();
        }
        
        ImGui.SameLine();
        
        var logs = Logger.Instance.GetLogs().Reverse().ToArray();
        ImGui.Text($"Entries: {logs.Length}");
        
        ImGui.Separator();
        
        ImGui.BeginChild("LogScrolling", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar);
        
        foreach (var log in logs)
        {
            var color = log.Level switch
            {
                LogLevel.Debug => new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                LogLevel.Info => new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
                LogLevel.Warning => new Vector4(1.0f, 0.8f, 0.0f, 1.0f),
                LogLevel.Error => new Vector4(1.0f, 0.3f, 0.3f, 1.0f),
                _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
            };
            
            var timestamp = log.Timestamp.ToString("HH:mm:ss");
            var levelStr = log.Level.ToString().ToUpper().PadRight(7);
            var sourceStr = log.Source != null ? $"[{log.Source}] " : "";
            var text = $"{timestamp} {levelStr} {sourceStr}{log.Message}";
            
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
        }
        
        ImGui.EndChild();
    }

    private void RenderNodeConfig(Node node)
    {
        var name = node.Name;
        if (ImGui.InputText("Name", ref name, 100))
        {
            node.Name = name;
        }
        
        // Handle Input node
        if (node is InputNode inputNode)
        {
            ImGui.Separator();
            ImGui.Text("Input Data:");
            
            var dataType = inputNode.DataType;
            if (ImGui.BeginCombo("Data Type", dataType))
            {
                if (ImGui.Selectable("text", dataType == "text"))
                    inputNode.DataType = "text";
                if (ImGui.Selectable("json", dataType == "json"))
                    inputNode.DataType = "json";
                if (ImGui.Selectable("number", dataType == "number"))
                    inputNode.DataType = "number";
                if (ImGui.Selectable("boolean", dataType == "boolean"))
                    inputNode.DataType = "boolean";
                ImGui.EndCombo();
            }
            
            var inputData = inputNode.InputData;
            if (ImGui.InputTextMultiline("##inputdata", ref inputData, 10000, new Vector2(-1, 200)))
            {
                inputNode.InputData = inputData;
            }
        }
        // Handle Output node
        else if (node is OutputNode outputNode)
        {
            ImGui.Separator();
            ImGui.Text("Output Log:");
            
            if (outputNode.LastExecuted.HasValue)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), 
                    $"Last executed: {outputNode.LastExecuted.Value:yyyy-MM-dd HH:mm:ss}");
            }
            
            if (!string.IsNullOrEmpty(outputNode.LogMessage))
            {
                ImGui.BeginChild("OutputLog", new Vector2(-1, 200), true);
                ImGui.TextWrapped(outputNode.LogMessage);
                ImGui.EndChild();
            }
            else
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "(No output yet)");
            }
        }
        // Handle Plugin node
        else if (node is PluginNode pluginNode)
        {
            ImGui.Text($"Plugin: {pluginNode.Definition.Name}");
            ImGui.TextWrapped(pluginNode.Definition.Description);
            ImGui.Separator();
            
            // Show input pins
            if (pluginNode.Definition.InputPins.Any())
            {
                ImGui.Text("Inputs:");
                foreach (var input in pluginNode.Definition.InputPins)
                {
                    ImGui.BulletText($"{input.Name} ({input.DataType}){(input.Required ? " *" : "")}");
                }
                ImGui.Separator();
            }
            
            // Show output pins
            if (pluginNode.Definition.OutputPins.Any())
            {
                ImGui.Text("Outputs:");
                foreach (var output in pluginNode.Definition.OutputPins)
                {
                    var hasCode = !string.IsNullOrWhiteSpace(output.PythonCode);
                    ImGui.BulletText($"{output.Name} ({output.DataType}){(hasCode ? " 🐍" : "")}");
                }
                ImGui.Separator();
            }
            
            // Allow editing config properties
            if (pluginNode.Config.Any())
            {
                ImGui.Text("Properties:");
                ImGui.Separator();
                var configKeys = pluginNode.Config.Keys.ToList();
                foreach (var key in configKeys)
                {
                    var value = pluginNode.Config[key]?.ToString() ?? "";
                    
                    // Check if it's boolean
                    if (pluginNode.Config[key] is bool boolValue)
                    {
                        if (ImGui.Checkbox(key, ref boolValue))
                        {
                            pluginNode.Config[key] = boolValue;
                        }
                    }
                    else if (key.Contains("data") || key.Contains("payload") || key.Contains("input"))
                    {
                        // Multi-line for larger text fields
                        if (ImGui.InputTextMultiline(key, ref value, 5000, new Vector2(-1, 100)))
                        {
                            pluginNode.Config[key] = value;
                        }
                    }
                    else
                    {
                        if (ImGui.InputText(key, ref value, 500))
                        {
                            pluginNode.Config[key] = value;
                        }
                    }
                }
            }
        }
        
        if (ImGui.Button("Execute Node", new Vector2(-1, 30)))
        {
            Task.Run(async () => {
                Logger.Instance.Info($"Executing node: {node.Name}", "NodeEditor");
                await _graph.ExecuteNodeAsync(node.Id);
            });
        }
    }

    private void RenderCanvas()
    {
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();
        
        // Draw grid
        DrawGrid(drawList, canvasPos, canvasSize);
        
        // Draw connections
        DrawConnections(drawList, canvasPos);
        
        // Draw nodes
        foreach (var (id, node) in _graph.Nodes.ToList())
        {
            DrawNode(drawList, node, canvasPos);
        }
        
        // Handle temp connection line
        if (_isDraggingConnection && _connectionSourceNodeId.HasValue)
        {
            var sourceNode = _graph.Nodes[_connectionSourceNodeId.Value];
            var pinPos = GetOutputPinPos(sourceNode, _connectionSourcePin!, canvasPos);
            drawList.AddBezierCubic(pinPos, pinPos + new Vector2(50, 0), _connectionDragPos + new Vector2(-50, 0), _connectionDragPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 1)), 3.0f);
        }
        
        // Handle canvas panning
        if (ImGui.IsWindowHovered() && (ImGui.IsMouseDragging(ImGuiMouseButton.Right) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle)))
        {
            _scrolling += ImGui.GetIO().MouseDelta;
        }
        
        // NOTE: Don't reset drag state here! Let the input pin handlers process the release event first
        // Only reset if mouse is released and NOT over any input pin (handled in DrawNode)
        
        if (_isDraggingConnection)
        {
            _connectionDragPos = ImGui.GetMousePos();
            
            // If mouse was released but connection wasn't made, cancel it
            // This runs AFTER all nodes have been drawn and had a chance to capture the release
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                Logger.Instance.Debug($"Connection drag cancelled (released outside pin)", "Connection");
                _isDraggingConnection = false;
                _connectionSourceNodeId = null;
                _connectionSourcePin = null;
            }
        }
        
        // Invisible button for canvas interaction
        ImGui.SetCursorScreenPos(canvasPos);
        ImGui.InvisibleButton("canvas", canvasSize);
    }

    private void DrawGrid(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        var gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
        const float gridStep = 64.0f;
        
        for (float x = _scrolling.X % gridStep; x < canvasSize.X; x += gridStep)
        {
            drawList.AddLine(new Vector2(canvasPos.X + x, canvasPos.Y), new Vector2(canvasPos.X + x, canvasPos.Y + canvasSize.Y), gridColor);
        }
        
        for (float y = _scrolling.Y % gridStep; y < canvasSize.Y; y += gridStep)
        {
            drawList.AddLine(new Vector2(canvasPos.X, canvasPos.Y + y), new Vector2(canvasPos.X + canvasSize.X, canvasPos.Y + y), gridColor);
        }
    }

    private void DrawNode(ImDrawListPtr drawList, Node node, Vector2 canvasPos)
    {
        var nodePos = canvasPos + node.Position + _scrolling;
        
        // Debug log for Output node
        if (node.Name == "Output" && _isDraggingConnection)
        {
            Logger.Instance.Debug($"[START] DrawNode Output: canvasPos={canvasPos.X:F0},{canvasPos.Y:F0} node.Position={node.Position.X:F0},{node.Position.Y:F0} scrolling={_scrolling.X:F0},{_scrolling.Y:F0} → nodePos={nodePos.X:F0},{nodePos.Y:F0}", "Render");
        }
        
        // Get input pin count
        int inputPinCount = 0;
        if (node is PluginNode pluginNode)
        {
            inputPinCount = pluginNode.Definition.InputPins.Count;
        }
        
        // Calculate node size based on content
        var pinCount = Math.Max(inputPinCount, node.OutputPins.Count);
        pinCount = Math.Max(pinCount, 1); // At least space for 1 pin
        
        // Add extra height for Input/Output nodes to show content preview
        var baseHeight = 100f;
        if (node is InputNode || node is OutputNode)
        {
            baseHeight = 120f; // Extra space for preview text
        }
        else if (node is PluginNode pnCheck && (pnCheck.Definition.Name == "Input" || pnCheck.Definition.Name == "Output"))
        {
            baseHeight = 120f; // Extra space for preview text in Input/Output plugins
        }
        
        var nodeSize = new Vector2(NODE_WIDTH, baseHeight + pinCount * NODE_PIN_SPACING);
        
        var isSelected = _selectedNode?.Id == node.Id;
        var nodeBgColor = isSelected 
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.5f, 0.9f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 0.9f));
        var nodeBorderColor = isSelected
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.6f, 0.0f, 1.0f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
        
        drawList.AddRectFilled(nodePos, nodePos + nodeSize, nodeBgColor, 4.0f);
        drawList.AddRect(nodePos, nodePos + nodeSize, nodeBorderColor, 4.0f, ImDrawFlags.None, 2.0f);
        
        // Check if this is a starting node (no incoming connections)
        var isStartingNode = !_graph.Connections.Values.Any(c => c.TargetNodeId == node.Id);
        if (isStartingNode)
        {
            // Draw a small indicator for starting nodes
            drawList.AddCircleFilled(nodePos + new Vector2(nodeSize.X - 15, 15), 6.0f, 
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 1.0f, 0.3f, 1.0f)));
            drawList.AddText(nodePos + new Vector2(nodeSize.X - 20, 7), 
                ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), "▶");
        }
        
        drawList.AddText(nodePos + new Vector2(8, 8), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), node.Name);
        
        // Show content preview for Input and Output plugin nodes
        var contentYOffset = 28f;
        if (node is PluginNode pluginNode2)
        {
            // Input plugin node preview
            if (pluginNode2.Definition.Name == "Input" && pluginNode2.Config.ContainsKey("input_data"))
            {
                var data = pluginNode2.Config["input_data"]?.ToString() ?? "";
                var preview = data.Length > 30 
                    ? data.Substring(0, 27) + "..." 
                    : data;
                if (string.IsNullOrEmpty(preview)) preview = "(empty)";
                
                drawList.AddText(nodePos + new Vector2(8, contentYOffset), 
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.5f, 1.0f)), 
                    preview);
            }
            // Output plugin node preview
            else if (pluginNode2.Definition.Name == "Output" && pluginNode2.Outputs.ContainsKey("message"))
            {
                var message = pluginNode2.Outputs["message"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(message))
                {
                    var lines = message.Split('\n');
                    var preview = lines.Length > 0 ? lines[0] : "";
                    if (preview.Length > 30) preview = preview.Substring(0, 27) + "...";
                    
                    drawList.AddText(nodePos + new Vector2(8, contentYOffset), 
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 1.0f, 0.5f, 1.0f)), 
                        preview);
                }
                else
                {
                    drawList.AddText(nodePos + new Vector2(8, contentYOffset), 
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)), 
                        "(no output)");
                }
            }
        }
        // Handle legacy InputNode and OutputNode classes if they exist
        else if (node is InputNode inputNode)
        {
            var data = inputNode.InputData ?? "";
            var preview = data.Length > 30 
                ? data.Substring(0, 27) + "..." 
                : data;
            if (string.IsNullOrEmpty(preview)) preview = "(empty)";
            
            drawList.AddText(nodePos + new Vector2(8, contentYOffset), 
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.5f, 1.0f)), 
                preview);
        }
        else if (node is OutputNode outputNode)
        {
            if (!string.IsNullOrEmpty(outputNode.LogMessage))
            {
                var lines = outputNode.LogMessage.Split('\n');
                var preview = lines.Length > 0 ? lines[0] : "";
                if (preview.Length > 30) preview = preview.Substring(0, 27) + "...";
                
                drawList.AddText(nodePos + new Vector2(8, contentYOffset), 
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 1.0f, 0.5f, 1.0f)), 
                    preview);
            }
            else
            {
                drawList.AddText(nodePos + new Vector2(8, contentYOffset), 
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)), 
                    "(no output)");
            }
        }
        
        // Input pins
        float inputYOffset = 60;
        if (node is PluginNode pn)
        {
            foreach (var inputPin in pn.Definition.InputPins)
            {
                var pinPos = nodePos + new Vector2(0, inputYOffset);
                var pinColor = inputPin.Required 
                    ? new Vector4(1.0f, 0.3f, 0.3f, 1.0f) // Red for required
                    : new Vector4(0.2f, 0.6f, 1.0f, 1.0f); // Blue for optional
                
                // Check if we should highlight this pin (when dragging a connection over it)
                bool isHoverTarget = false;
                if (_isDraggingConnection)
                {
                    // Check if mouse is near this pin
                    var mousePos = ImGui.GetMousePos();
                    var distance = Vector2.Distance(mousePos, pinPos);
                    if (distance < NODE_SLOT_RADIUS * 3) // Larger hit area
                    {
                        isHoverTarget = true;
                        pinColor = new Vector4(1.0f, 1.0f, 0.0f, 1.0f); // Yellow highlight
                    }
                }
                
                drawList.AddCircleFilled(pinPos, NODE_SLOT_RADIUS, ImGui.ColorConvertFloat4ToU32(pinColor));
                
                // Draw outer ring when it's a hover target
                if (isHoverTarget)
                {
                    drawList.AddCircle(pinPos, NODE_SLOT_RADIUS + 3, 
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 1.0f, 0.0f, 1.0f)), 0, 2.0f);
                }
                
                drawList.AddText(nodePos + new Vector2(NODE_SLOT_RADIUS * 2 + 5, inputYOffset - 8), 
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f)), 
                    inputPin.Name + (inputPin.Required ? "*" : ""));
                inputYOffset += NODE_PIN_SPACING;
            }
        }
        
        // Output pins
        float yOffset = 60;
        foreach (var pin in node.OutputPins)
        {
            var pinPos = nodePos + new Vector2(NODE_WIDTH, yOffset);
            drawList.AddCircleFilled(pinPos, NODE_SLOT_RADIUS, ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 1.0f, 0.6f, 1.0f)));
            var textSize = ImGui.CalcTextSize(pin);
            drawList.AddText(nodePos + new Vector2(NODE_WIDTH - textSize.X - NODE_SLOT_RADIUS * 2 - 5, yOffset - 8), 
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f)), pin);
            yOffset += NODE_PIN_SPACING;
        }
        
        // IMPORTANT: Handle pin interactions BEFORE the main node button
        // This ensures pins can capture hover/click events before the node does
        
        // Input pin interactions
        inputYOffset = 60;
        if (node is PluginNode pn2)
        {
            // Re-check nodePos before creating buttons
            if (node.Name == "Output" && _isDraggingConnection)
            {
                var nodePosNow = canvasPos + node.Position + _scrolling;
                Logger.Instance.Debug($"[BUTTON] Output nodePos recalc: canvasPos={canvasPos.X:F0},{canvasPos.Y:F0} node.Position={node.Position.X:F0},{node.Position.Y:F0} scrolling={_scrolling.X:F0},{_scrolling.Y:F0} → nodePos={nodePosNow.X:F0},{nodePosNow.Y:F0} (original nodePos={nodePos.X:F0},{nodePos.Y:F0})", "Render");
            }
            
            foreach (var inputPin in pn2.Definition.InputPins)
            {
                var pinPos = nodePos + new Vector2(0, inputYOffset);
                var buttonPos = pinPos - new Vector2(NODE_SLOT_RADIUS, NODE_SLOT_RADIUS);
                var buttonSize = new Vector2(NODE_SLOT_RADIUS * 2, NODE_SLOT_RADIUS * 2);
                var mousePos = ImGui.GetMousePos();
                
                // Log the actual pin and button positions for this node
                if (_isDraggingConnection && node.Name == "Output")
                {
                    Logger.Instance.Debug($"[PIN] Output pin setup: nodePos={nodePos.X:F0},{nodePos.Y:F0} pinPos={pinPos.X:F0},{pinPos.Y:F0} buttonPos={buttonPos.X:F0},{buttonPos.Y:F0} buttonSize={buttonSize.X:F0}x{buttonSize.Y:F0} mousePos={mousePos.X:F0},{mousePos.Y:F0}", "Connection");
                }
                
                ImGui.SetCursorScreenPos(buttonPos);
                ImGui.InvisibleButton($"in_{node.Id}_{inputPin.Name}", buttonSize);
                
                // Manual rectangle intersection check (ImGui hover detection fails when canvas not hovered)
                var insideRect = mousePos.X >= buttonPos.X && mousePos.X <= (buttonPos.X + buttonSize.X) &&
                                 mousePos.Y >= buttonPos.Y && mousePos.Y <= (buttonPos.Y + buttonSize.Y);
                var isHovered = insideRect; // Use manual check instead of ImGui.IsItemHovered()
                
                if (isHovered)
                {
                    var buttonRect = $"[{buttonPos.X:F0},{buttonPos.Y:F0} to {buttonPos.X + buttonSize.X:F0},{buttonPos.Y + buttonSize.Y:F0}]";
                    Logger.Instance.Debug($"Hovering over {node.Name}.{inputPin.Name} @ {mousePos.X:F0},{mousePos.Y:F0} in {buttonRect} (dragging={_isDraggingConnection})", "Connection");
                }
                
                // Check if we're dropping a connection here
                if (isHovered && _isDraggingConnection)
                {
                    Logger.Instance.Debug($"HOVER DETECTED! {node.Name}.{inputPin.Name} while dragging", "Connection");
                    
                    // Show visual feedback
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        Logger.Instance.Debug($"Mouse released over {node.Name}.{inputPin.Name}", "Connection");
                        
                        var conn = new Connection
                        {
                            SourceNodeId = _connectionSourceNodeId!.Value,
                            SourceOutput = _connectionSourcePin!,
                            TargetNodeId = node.Id,
                            TargetInput = inputPin.Name
                        };
                        _graph.AddConnection(conn);
                        Logger.Instance.Info($"✓ Connected: {_connectionSourcePin} -> {node.Name}.{inputPin.Name}", "NodeEditor");
                        
                        // Reset dragging state
                        _isDraggingConnection = false;
                        _connectionSourceNodeId = null;
                        _connectionSourcePin = null;
                    }
                }
                else if (_isDraggingConnection)
                {
                    // Log when we're dragging but NOT hovering (to debug why hover isn't detected)
                    var distance = Vector2.Distance(mousePos, pinPos);
                    if (distance < 50) // Only log if mouse is nearby
                    {
                        var buttonRect = $"[{buttonPos.X:F0},{buttonPos.Y:F0} to {buttonPos.X + buttonSize.X:F0},{buttonPos.Y + buttonSize.Y:F0}]";
                        Logger.Instance.Debug($"Near {node.Name}.{inputPin.Name} @ {mousePos.X:F0},{mousePos.Y:F0} but not hovering. Button: {buttonRect}, Distance: {distance:F1}", "Connection");
                    }
                }
                
                inputYOffset += NODE_PIN_SPACING;
            }
        }
        
        // Output pin interactions
        yOffset = 60;
        foreach (var pin in node.OutputPins)
        {
            var pinPos = nodePos + new Vector2(NODE_WIDTH, yOffset);
            ImGui.SetCursorScreenPos(pinPos - new Vector2(NODE_SLOT_RADIUS, NODE_SLOT_RADIUS));
            ImGui.InvisibleButton($"out_{node.Id}_{pin}", new Vector2(NODE_SLOT_RADIUS * 2, NODE_SLOT_RADIUS * 2));
            
            if (ImGui.IsItemClicked())
            {
                _isDraggingConnection = true;
                _connectionSourceNodeId = node.Id;
                _connectionSourcePin = pin;
                Logger.Instance.Debug($"Started dragging from {node.Name}.{pin}", "Connection");
            }
            
            yOffset += NODE_PIN_SPACING;
        }
        
        // Handle node interaction LAST so pins take priority
        // Skip the main node button if we're dragging a connection to avoid blocking pin hover
        if (!_isDraggingConnection)
        {
            ImGui.SetCursorScreenPos(nodePos);
            ImGui.InvisibleButton($"node_{node.Id}", nodeSize);
            
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                node.Position += ImGui.GetIO().MouseDelta;
            }
            
            if (ImGui.IsItemClicked())
            {
                _selectedNode = node;
            }
        }
    }

    private void DrawConnections(ImDrawListPtr drawList, Vector2 canvasPos)
    {
        foreach (var conn in _graph.Connections.Values)
        {
            if (!_graph.Nodes.TryGetValue(conn.SourceNodeId, out var sourceNode)) continue;
            if (!_graph.Nodes.TryGetValue(conn.TargetNodeId, out var targetNode)) continue;
            
            var p1 = GetOutputPinPos(sourceNode, conn.SourceOutput, canvasPos);
            var p2 = GetInputPinPos(targetNode, conn.TargetInput, canvasPos);
            
            drawList.AddBezierCubic(p1, p1 + new Vector2(50, 0), p2 + new Vector2(-50, 0), p2, ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.8f, 1.0f, 1.0f)), 3.0f);
        }
    }

    private Vector2 GetOutputPinPos(Node node, string pinName, Vector2 canvasPos)
    {
        var nodePos = canvasPos + node.Position + _scrolling;
        var pinIndex = node.OutputPins.IndexOf(pinName);
        return nodePos + new Vector2(NODE_WIDTH, 60 + pinIndex * NODE_PIN_SPACING);
    }

    private Vector2 GetInputPinPos(Node node, string inputPinName, Vector2 canvasPos)
    {
        var nodePos = canvasPos + node.Position + _scrolling;
        
        if (node is PluginNode pluginNode)
        {
            var pinIndex = pluginNode.Definition.InputPins.FindIndex(p => p.Name == inputPinName);
            if (pinIndex >= 0)
            {
                return nodePos + new Vector2(0, 60 + pinIndex * NODE_PIN_SPACING);
            }
        }
        
        return nodePos + new Vector2(0, 60);
    }

    private void NewGraph()
    {
        if (_graph.Nodes.Any())
        {
            // Could add confirmation dialog here
            ClearGraph();
        }
        _currentFileName = "";
        Logger.Instance.Info("New graph created", "Editor");
    }
    
    private void SaveGraph()
    {
        if (string.IsNullOrEmpty(_currentFileName))
        {
            SaveGraphAs();
        }
        else
        {
            _graph.SaveToFile(_currentFileName);
            Logger.Instance.Info($"Graph saved: {_currentFileName}", "Editor");
        }
    }
    
    private void SaveGraphAs()
    {
        var filename = $"graphs/graph_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        Directory.CreateDirectory("graphs");
        _currentFileName = filename;
        _graph.SaveToFile(_currentFileName);
        Logger.Instance.Info($"Graph saved as: {_currentFileName}", "Editor");
    }
    
    private void AutoSave()
    {
        if (_graph.Nodes.Any())
        {
            var autosaveDir = "graphs/autosave";
            Directory.CreateDirectory(autosaveDir);
            var filename = $"{autosaveDir}/autosave_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            _graph.SaveToFile(filename);
            Logger.Instance.Debug($"Auto-saved to: {filename}", "Editor");
            
            // Keep only last 5 autosaves
            var autosaves = Directory.GetFiles(autosaveDir, "autosave_*.json")
                .OrderByDescending(f => f)
                .Skip(5)
                .ToList();
            foreach (var file in autosaves)
            {
                File.Delete(file);
            }
        }
    }
    
    private void LoadGraph()
    {
        try
        {
            var graphsDir = "graphs";
            if (!Directory.Exists(graphsDir))
            {
                Logger.Instance.Warning("No saved graphs found", "Editor");
                return;
            }
            
            var files = Directory.GetFiles(graphsDir, "graph_*.json")
                .OrderByDescending(f => f)
                .Take(10)
                .ToList();
            
            if (!files.Any())
            {
                Logger.Instance.Warning("No saved graphs found", "Editor");
                return;
            }
            
            // For now, load the most recent file
            // In a full implementation, you'd show a file picker dialog
            var latestFile = files.First();
            _graph.LoadFromFile(latestFile);
            _currentFileName = latestFile;
            _selectedNode = null;
            Logger.Instance.Info($"Graph loaded: {latestFile}", "Editor");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Failed to load graph: {ex.Message}", "Editor");
        }
    }

    private void ClearGraph()
    {
        _graph.Nodes.Clear();
        _graph.Connections.Clear();
        _selectedNode = null;
        _currentFileName = "";
        Logger.Instance.Info("Graph cleared", "Editor");
    }
}
