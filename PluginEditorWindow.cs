using System.Numerics;
using ImGuiNET;

namespace EtlNodeEditor;

public class PluginEditorWindow
{
    private bool _isOpen = false;
    private NodePluginDefinition? _currentPlugin = null;
    private bool _isNewPlugin = false;
    
    // Temporary editing variables
    private string _pluginName = "";
    private string _pluginDescription = "";
    private string _pluginCategory = "";
    private string _pluginIcon = "";
    private string _pythonCode = "";
    
    // Input pin editing
    private string _newInputName = "";
    private string _newInputType = "any";
    private bool _newInputRequired = false;
    private string _newInputDefault = "";
    
    // Output pin editing
    private string _newOutputName = "";
    private string _newOutputType = "any";
    private string _newOutputPythonCode = "";
    
    private int _selectedInputIndex = -1;
    private int _selectedOutputIndex = -1;
    
    public void Show(NodePluginDefinition? plugin = null)
    {
        _isOpen = true;
        _isNewPlugin = plugin == null;
        
        if (plugin != null)
        {
            _currentPlugin = plugin;
            LoadPluginForEditing(plugin);
        }
        else
        {
            _currentPlugin = new NodePluginDefinition();
            _pluginName = "New Plugin";
            _pluginDescription = "";
            _pluginCategory = "Custom";
            _pluginIcon = "🔌";
            _pythonCode = @"# Available: inputs (dict), config (dict)
# Return: outputs (dict)

outputs = {}
# Your processing code here
";
        }
    }
    
    public void Hide()
    {
        _isOpen = false;
    }
    
    public void Render()
    {
        if (!_isOpen || _currentPlugin == null)
            return;
        
        ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
        
        var title = _isNewPlugin ? "Create New Plugin" : $"Edit Plugin: {_currentPlugin.Name}";
        
        if (ImGui.Begin(title, ref _isOpen, ImGuiWindowFlags.None))
        {
            // Tabs
            if (ImGui.BeginTabBar("PluginTabs"))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    RenderGeneralTab();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Input Pins"))
                {
                    RenderInputPinsTab();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Output Pins"))
                {
                    RenderOutputPinsTab();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Python Code"))
                {
                    RenderPythonCodeTab();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Test"))
                {
                    RenderTestTab();
                    ImGui.EndTabItem();
                }
                
                ImGui.EndTabBar();
            }
            
            ImGui.Separator();
            
            // Bottom buttons
            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - 40);
            
            if (ImGui.Button("Save Plugin", new Vector2(120, 30)))
            {
                SavePlugin();
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel", new Vector2(120, 30)))
            {
                _isOpen = false;
            }
            
            ImGui.SameLine();
            ImGui.TextDisabled(_isNewPlugin ? "Creating new plugin" : "Editing existing plugin");
        }
        
        ImGui.End();
    }
    
    private void RenderGeneralTab()
    {
        ImGui.Text("Plugin Information");
        ImGui.Separator();
        
        ImGui.PushItemWidth(400);
        
        ImGui.InputText("Name", ref _pluginName, 256);
        ImGui.InputText("Icon", ref _pluginIcon, 32);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Use emoji or single character");
        }
        
        ImGui.InputText("Category", ref _pluginCategory, 128);
        ImGui.InputTextMultiline("Description", ref _pluginDescription, 1024, new Vector2(400, 100));
        
        ImGui.PopItemWidth();
    }
    
    private void RenderInputPinsTab()
    {
        ImGui.Text("Input Pins");
        ImGui.Separator();
        
        // List existing input pins
        ImGui.BeginChild("InputPinsList", new Vector2(400, 300), true);
        
        for (int i = 0; i < _currentPlugin!.InputPins.Count; i++)
        {
            var pin = _currentPlugin.InputPins[i];
            var isSelected = _selectedInputIndex == i;
            
            if (ImGui.Selectable($"{pin.Name} ({pin.DataType}){(pin.Required ? " *" : "")}", isSelected))
            {
                _selectedInputIndex = i;
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Type: {pin.DataType}\nRequired: {pin.Required}\nDefault: {pin.DefaultValue?.ToString() ?? "none"}");
            }
        }
        
        ImGui.EndChild();
        
        ImGui.SameLine();
        
        ImGui.BeginGroup();
        
        if (ImGui.Button("Remove Selected", new Vector2(150, 0)) && _selectedInputIndex >= 0)
        {
            _currentPlugin.InputPins.RemoveAt(_selectedInputIndex);
            _selectedInputIndex = -1;
        }
        
        ImGui.EndGroup();
        
        ImGui.Separator();
        ImGui.Text("Add New Input Pin");
        
        ImGui.PushItemWidth(200);
        ImGui.InputText("Pin Name", ref _newInputName, 128);
        
        if (ImGui.BeginCombo("Data Type", _newInputType))
        {
            foreach (var type in new[] { "any", "string", "number", "boolean", "json" })
            {
                if (ImGui.Selectable(type, _newInputType == type))
                {
                    _newInputType = type;
                }
            }
            ImGui.EndCombo();
        }
        
        ImGui.Checkbox("Required", ref _newInputRequired);
        ImGui.InputText("Default Value", ref _newInputDefault, 256);
        ImGui.PopItemWidth();
        
        if (ImGui.Button("Add Input Pin", new Vector2(150, 0)) && !string.IsNullOrWhiteSpace(_newInputName))
        {
            _currentPlugin.InputPins.Add(new PluginInputPin
            {
                Name = _newInputName,
                DataType = _newInputType,
                Required = _newInputRequired,
                DefaultValue = string.IsNullOrWhiteSpace(_newInputDefault) ? null : _newInputDefault
            });
            
            _newInputName = "";
            _newInputDefault = "";
            _newInputRequired = false;
        }
    }
    
    private void RenderOutputPinsTab()
    {
        ImGui.Text("Output Pins");
        ImGui.Separator();
        
        // List existing output pins
        ImGui.BeginChild("OutputPinsList", new Vector2(400, 300), true);
        
        for (int i = 0; i < _currentPlugin!.OutputPins.Count; i++)
        {
            var pin = _currentPlugin.OutputPins[i];
            var isSelected = _selectedOutputIndex == i;
            var hasCustomCode = !string.IsNullOrWhiteSpace(pin.PythonCode);
            
            if (ImGui.Selectable($"{pin.Name} ({pin.DataType}){(hasCustomCode ? " 🐍" : "")}", isSelected))
            {
                _selectedOutputIndex = i;
            }
            
            if (ImGui.IsItemHovered())
            {
                var tooltip = $"Type: {pin.DataType}";
                if (hasCustomCode)
                {
                    tooltip += "\nHas custom Python code";
                }
                ImGui.SetTooltip(tooltip);
            }
        }
        
        ImGui.EndChild();
        
        ImGui.SameLine();
        
        ImGui.BeginGroup();
        
        if (ImGui.Button("Remove Selected", new Vector2(150, 0)) && _selectedOutputIndex >= 0)
        {
            _currentPlugin.OutputPins.RemoveAt(_selectedOutputIndex);
            _selectedOutputIndex = -1;
        }
        
        ImGui.EndGroup();
        
        ImGui.Separator();
        ImGui.Text("Add New Output Pin");
        
        ImGui.PushItemWidth(200);
        ImGui.InputText("Pin Name##output", ref _newOutputName, 128);
        
        if (ImGui.BeginCombo("Data Type##output", _newOutputType))
        {
            foreach (var type in new[] { "any", "string", "number", "boolean", "json" })
            {
                if (ImGui.Selectable(type, _newOutputType == type))
                {
                    _newOutputType = type;
                }
            }
            ImGui.EndCombo();
        }
        
        ImGui.PopItemWidth();
        
        ImGui.Text("Custom Python Code (optional):");
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Python code to compute this specific output.\nSet 'result' variable with the output value.");
        }
        
        ImGui.PushItemWidth(400);
        ImGui.InputTextMultiline("##outputPython", ref _newOutputPythonCode, 2048, new Vector2(400, 100));
        ImGui.PopItemWidth();
        
        if (ImGui.Button("Add Output Pin", new Vector2(150, 0)) && !string.IsNullOrWhiteSpace(_newOutputName))
        {
            _currentPlugin.OutputPins.Add(new PluginOutputPin
            {
                Name = _newOutputName,
                DataType = _newOutputType,
                PythonCode = string.IsNullOrWhiteSpace(_newOutputPythonCode) ? null : _newOutputPythonCode
            });
            
            _newOutputName = "";
            _newOutputPythonCode = "";
        }
    }
    
    private void RenderPythonCodeTab()
    {
        ImGui.Text("Main Processing Code (Python)");
        ImGui.Separator();
        
        ImGui.TextWrapped("This Python code will be executed when the node processes data.");
        ImGui.TextWrapped("Available variables: inputs (dict), config (dict)");
        ImGui.TextWrapped("Set outputs: outputs = {'key': value, ...}");
        
        ImGui.Spacing();
        
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - 40);
        ImGui.InputTextMultiline("##pythonCode", ref _pythonCode, 10000, size);
        
        if (ImGui.Button("Insert Example Code"))
        {
            _pythonCode = @"# Example: Process inputs and create outputs
import json

# Access inputs
input_value = inputs.get('input_name', 'default_value')

# Your processing logic here
result = input_value.upper() if isinstance(input_value, str) else str(input_value)

# Set outputs
outputs = {
    'result': result,
    'length': len(result),
    'processed': True
}
";
        }
    }
    
    private void RenderTestTab()
    {
        ImGui.Text("Test Plugin");
        ImGui.Separator();
        ImGui.TextWrapped("Testing functionality coming soon...");
        ImGui.TextWrapped("You can test the plugin by creating a node instance in the graph.");
    }
    
    private void LoadPluginForEditing(NodePluginDefinition plugin)
    {
        _pluginName = plugin.Name;
        _pluginDescription = plugin.Description;
        _pluginCategory = plugin.Category;
        _pluginIcon = plugin.Icon;
        _pythonCode = plugin.PythonProcessCode;
    }
    
    private void SavePlugin()
    {
        if (_currentPlugin == null) return;
        
        // Update plugin with edited values
        _currentPlugin.Name = _pluginName;
        _currentPlugin.Description = _pluginDescription;
        _currentPlugin.Category = _pluginCategory;
        _currentPlugin.Icon = _pluginIcon;
        _currentPlugin.PythonProcessCode = _pythonCode;
        
        // Save to plugin manager
        PluginManager.Instance.AddOrUpdatePlugin(_currentPlugin);
        
        Console.WriteLine($"Plugin saved: {_currentPlugin.Name}");
        _isOpen = false;
    }
}
