using System.Numerics;
using Newtonsoft.Json;

namespace EtlNodeEditor;

/// <summary>
/// Defines an input pin for a node plugin
/// </summary>
public class PluginInputPin
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "any"; // any, string, number, boolean, json
    public bool Required { get; set; } = false;
    public object? DefaultValue { get; set; }
}

/// <summary>
/// Defines an output pin for a node plugin
/// </summary>
public class PluginOutputPin
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "any";
    public string? PythonCode { get; set; } // Optional Python code to process this output
}

/// <summary>
/// Represents a pluggable node definition
/// </summary>
public class NodePluginDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Plugin";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Custom";
    public string Icon { get; set; } = "🔌";
    
    public List<PluginInputPin> InputPins { get; set; } = new();
    public List<PluginOutputPin> OutputPins { get; set; } = new();
    
    /// <summary>
    /// Main Python processing code
    /// Receives 'inputs' dict and must return 'outputs' dict
    /// </summary>
    public string PythonProcessCode { get; set; } = @"# Available: inputs (dict)
# Return: outputs (dict)

outputs = {}
# Your processing code here
";
    
    public Dictionary<string, object?> ConfigProperties { get; set; } = new();
}

/// <summary>
/// A node instance created from a plugin definition
/// </summary>
public class PluginNode : Node
{
    public NodePluginDefinition Definition { get; set; }
    public Dictionary<string, object?> Config { get; set; } = new();
    
    public PluginNode(NodePluginDefinition definition)
    {
        Definition = definition;
        Name = definition.Name;
        Type = NodeType.Plugin;
        
        // Setup output pins
        OutputPins = definition.OutputPins.Select(p => p.Name).ToList();
        
        // Initialize config with defaults
        foreach (var prop in definition.ConfigProperties)
        {
            Config[prop.Key] = prop.Value;
        }
    }
    
    public override async Task<Dictionary<string, object?>> ExecuteAsync(Dictionary<string, object?> inputs)
    {
        var outputs = new Dictionary<string, object?>();
        
        try
        {
            // Validate required inputs
            foreach (var inputPin in Definition.InputPins.Where(p => p.Required))
            {
                if (!inputs.ContainsKey(inputPin.Name) || inputs[inputPin.Name] == null)
                {
                    throw new Exception($"Required input '{inputPin.Name}' is missing");
                }
            }
            
            // Apply default values for missing optional inputs
            foreach (var inputPin in Definition.InputPins)
            {
                if (!inputs.ContainsKey(inputPin.Name) && inputPin.DefaultValue != null)
                {
                    inputs[inputPin.Name] = inputPin.DefaultValue;
                }
            }
            
            // Execute main Python process code
            var pythonContext = new Dictionary<string, object?>
            {
                ["inputs"] = inputs,
                ["config"] = Config
            };
            
            var processResult = await PythonExecutor.ExecuteAsync(Definition.PythonProcessCode, pythonContext);
            
            if (processResult.ContainsKey("outputs") && processResult["outputs"] is Dictionary<string, object?> mainOutputs)
            {
                outputs = mainOutputs;
            }
            else
            {
                outputs = processResult;
            }
            
            // Process custom output pins with their own Python code
            foreach (var outputPin in Definition.OutputPins.Where(p => !string.IsNullOrWhiteSpace(p.PythonCode)))
            {
                var outputContext = new Dictionary<string, object?>
                {
                    ["inputs"] = inputs,
                    ["outputs"] = outputs,
                    ["config"] = Config
                };
                
                var customResult = await PythonExecutor.ExecuteAsync(outputPin.PythonCode!, outputContext);
                
                // The result should be assigned to this specific output
                if (customResult.ContainsKey("result"))
                {
                    outputs[outputPin.Name] = customResult["result"];
                }
                else if (customResult.ContainsKey(outputPin.Name))
                {
                    outputs[outputPin.Name] = customResult[outputPin.Name];
                }
            }
        }
        catch (Exception ex)
        {
            outputs["error"] = ex.Message;
            Logger.Instance.Error($"Error executing plugin node {Name}: {ex.Message}", "PluginNode");
        }
        
        Outputs = outputs;
        return outputs;
    }
}

/// <summary>
/// Manages plugin definitions and provides access to them
/// </summary>
public class PluginManager
{
    private static PluginManager? _instance;
    public static PluginManager Instance => _instance ??= new PluginManager();
    
    private Dictionary<Guid, NodePluginDefinition> _plugins = new();
    private string _pluginsDirectory = "plugins";
    
    private PluginManager()
    {
        LoadPlugins();
    }
    
    public IEnumerable<NodePluginDefinition> GetAllPlugins() => _plugins.Values;
    
    public NodePluginDefinition? GetPlugin(Guid id) => _plugins.GetValueOrDefault(id);
    
    public void AddOrUpdatePlugin(NodePluginDefinition plugin)
    {
        _plugins[plugin.Id] = plugin;
        SavePlugin(plugin);
        Logger.Instance.Info($"Saved plugin: {plugin.Name}", "PluginManager");
    }
    
    public void RemovePlugin(Guid id)
    {
        if (_plugins.Remove(id))
        {
            var filePath = Path.Combine(_pluginsDirectory, $"{id}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
    
    public PluginNode CreateNodeFromPlugin(Guid pluginId)
    {
        var plugin = GetPlugin(pluginId);
        if (plugin == null)
        {
            throw new Exception($"Plugin {pluginId} not found");
        }
        
        return new PluginNode(plugin);
    }
    
    private void LoadPlugins()
    {
        if (!Directory.Exists(_pluginsDirectory))
        {
            Directory.CreateDirectory(_pluginsDirectory);
            Logger.Instance.Info($"Created plugins directory: {_pluginsDirectory}", "PluginManager");
            CreateDefaultPlugins();
            return;
        }
        
        Logger.Instance.Info($"Loading plugins from: {_pluginsDirectory}", "PluginManager");
        var files = Directory.GetFiles(_pluginsDirectory, "*.json");
        int successCount = 0;
        int errorCount = 0;
        
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var plugin = JsonConvert.DeserializeObject<NodePluginDefinition>(json);
                if (plugin != null)
                {
                    _plugins[plugin.Id] = plugin;
                    Logger.Instance.Debug($"Loaded plugin: {plugin.Name} ({plugin.Category})", "PluginManager");
                    successCount++;
                }
                else
                {
                    Logger.Instance.Error($"Failed to deserialize plugin from {Path.GetFileName(file)}", "PluginManager");
                    errorCount++;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Error loading plugin from {Path.GetFileName(file)}: {ex.Message}", "PluginManager");
                errorCount++;
            }
        }
        
        Logger.Instance.Info($"Loaded {successCount} plugins successfully, {errorCount} errors", "PluginManager");
        
        // Create default plugins if none exist
        if (_plugins.Count == 0)
        {
            Logger.Instance.Info("No plugins found, creating defaults", "PluginManager");
            CreateDefaultPlugins();
        }
    }
    
    private void SavePlugin(NodePluginDefinition plugin)
    {
        try
        {
            if (!Directory.Exists(_pluginsDirectory))
            {
                Directory.CreateDirectory(_pluginsDirectory);
            }
            
            var json = JsonConvert.SerializeObject(plugin, Formatting.Indented);
            var filePath = Path.Combine(_pluginsDirectory, $"{plugin.Id}.json");
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving plugin {plugin.Name}: {ex.Message}");
        }
    }
    
    private void CreateDefaultPlugins()
    {
        Logger.Instance.Info("Creating default plugins", "PluginManager");
        
        // Input Node Plugin
        var inputNode = new NodePluginDefinition
        {
            Name = "Input",
            Description = "Provides input data to the graph",
            Category = "Core",
            Icon = "📥",
            InputPins = new List<PluginInputPin>(),
            OutputPins = new List<PluginOutputPin>
            {
                new() { Name = "data", DataType = "any" }
            },
            PythonProcessCode = @"# Input node - provides data
# Configure the data in the node properties
data = config.get('input_data', '')
data_type = config.get('data_type', 'text')

# Parse based on data type
if data_type == 'json':
    import json
    try:
        parsed_data = json.loads(data)
    except:
        parsed_data = data
elif data_type == 'number':
    try:
        parsed_data = float(data)
    except:
        parsed_data = data
elif data_type == 'boolean':
    parsed_data = data.lower() in ('true', '1', 'yes')
else:
    parsed_data = data

outputs = {'data': parsed_data}
"
        };
        inputNode.ConfigProperties["input_data"] = "";
        inputNode.ConfigProperties["data_type"] = "text";
        AddOrUpdatePlugin(inputNode);
        
        // Output/Log Node Plugin
        var outputNode = new NodePluginDefinition
        {
            Name = "Output",
            Description = "Logs all inputs to console",
            Category = "Core",
            Icon = "📤",
            InputPins = new List<PluginInputPin>
            {
                new() { Name = "data", DataType = "any", Required = false }
            },
            OutputPins = new List<PluginOutputPin>(),
            PythonProcessCode = @"# Output node - logs inputs
from datetime import datetime

timestamp = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
log_lines = [f'[{timestamp}] Output Node:']

for key, value in inputs.items():
    log_lines.append(f'  {key}: {value}')

log_message = '\n'.join(log_lines)
print(log_message)

outputs = {'logged': True, 'message': log_message}
"
        };
        AddOrUpdatePlugin(outputNode);
        
        // REST API Node Plugin
        var restApiNode = new NodePluginDefinition
        {
            Name = "REST API",
            Description = "Make HTTP requests to REST APIs",
            Category = "Network",
            Icon = "🌐",
            InputPins = new List<PluginInputPin>
            {
                new() { Name = "url_override", DataType = "string", Required = false },
                new() { Name = "payload_data", DataType = "any", Required = false }
            },
            OutputPins = new List<PluginOutputPin>
            {
                new() { Name = "response_text", DataType = "string" },
                new() { Name = "status_code", DataType = "number" },
                new() { Name = "success", DataType = "boolean" }
            },
            PythonProcessCode = @"# REST API request
import json
try:
    import urllib.request
    
    # Get configuration
    url = inputs.get('url_override', config.get('url', ''))
    method = config.get('method', 'GET')
    payload_data = inputs.get('payload_data', config.get('payload', ''))
    
    # Prepare headers
    headers = {}
    headers_str = config.get('headers', '')
    if headers_str:
        try:
            headers = json.loads(headers_str) if isinstance(headers_str, str) else headers_str
        except:
            pass
    
    # Prepare request
    data = None
    if payload_data:
        if isinstance(payload_data, str):
            data = payload_data.encode('utf-8')
        else:
            data = json.dumps(payload_data).encode('utf-8')
            headers['Content-Type'] = 'application/json'
    
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    
    # Make request
    with urllib.request.urlopen(req) as response:
        response_text = response.read().decode('utf-8')
        status_code = response.status
        
    outputs = {
        'response_text': response_text,
        'status_code': status_code,
        'success': True
    }
except Exception as e:
    outputs = {
        'response_text': '',
        'status_code': 0,
        'success': False,
        'error': str(e)
    }
"
        };
        restApiNode.ConfigProperties["url"] = "https://api.example.com";
        restApiNode.ConfigProperties["method"] = "GET";
        restApiNode.ConfigProperties["headers"] = "{}";
        restApiNode.ConfigProperties["payload"] = "";
        AddOrUpdatePlugin(restApiNode);
        
        // CLI Command Node Plugin
        var cliNode = new NodePluginDefinition
        {
            Name = "CLI Command",
            Description = "Execute command-line programs",
            Category = "System",
            Icon = "⚙️",
            InputPins = new List<PluginInputPin>
            {
                new() { Name = "stdin_data", DataType = "string", Required = false }
            },
            OutputPins = new List<PluginOutputPin>
            {
                new() { Name = "stdout", DataType = "string" },
                new() { Name = "stderr", DataType = "string" },
                new() { Name = "return_code", DataType = "number" }
            },
            PythonProcessCode = @"# CLI Command execution
import subprocess

command = config.get('command', '')
args = config.get('arguments', '')
stdin_data = inputs.get('stdin_data', config.get('stdin_input', ''))
use_stdin = config.get('use_stdin', False)

if not command:
    outputs = {
        'stdout': '',
        'stderr': 'No command specified',
        'return_code': -1
    }
else:
    try:
        # Build command list
        cmd_list = [command]
        if args:
            if isinstance(args, str):
                cmd_list.extend(args.split())
            else:
                cmd_list.extend(args)
        
        # Execute
        result = subprocess.run(
            cmd_list,
            input=stdin_data if use_stdin else None,
            capture_output=True,
            text=True,
            timeout=30
        )
        
        outputs = {
            'stdout': result.stdout,
            'stderr': result.stderr,
            'return_code': result.returncode
        }
    except Exception as e:
        outputs = {
            'stdout': '',
            'stderr': str(e),
            'return_code': -1
        }
"
        };
        cliNode.ConfigProperties["command"] = "";
        cliNode.ConfigProperties["arguments"] = "";
        cliNode.ConfigProperties["use_stdin"] = false;
        cliNode.ConfigProperties["stdin_input"] = "";
        AddOrUpdatePlugin(cliNode);
        
        // Example: String Transform Plugin
        var stringTransform = new NodePluginDefinition
        {
            Name = "String Transform",
            Description = "Transform string using Python",
            Category = "Text",
            Icon = "📝",
            InputPins = new List<PluginInputPin>
            {
                new() { Name = "text", DataType = "string", Required = true }
            },
            OutputPins = new List<PluginOutputPin>
            {
                new() { Name = "result", DataType = "string" },
                new() { Name = "length", DataType = "number", PythonCode = "result = len(str(inputs.get('text', '')))" }
            },
            PythonProcessCode = @"# Transform the input text
text = str(inputs.get('text', ''))
outputs = {
    'result': text.upper(),
    'original_length': len(text)
}
"
        };
        AddOrUpdatePlugin(stringTransform);
        
        // Example: Math Calculator Plugin
        var mathCalc = new NodePluginDefinition
        {
            Name = "Math Calculator",
            Description = "Perform mathematical operations",
            Category = "Math",
            Icon = "🔢",
            InputPins = new List<PluginInputPin>
            {
                new() { Name = "a", DataType = "number", Required = true, DefaultValue = 0 },
                new() { Name = "b", DataType = "number", Required = true, DefaultValue = 0 }
            },
            OutputPins = new List<PluginOutputPin>
            {
                new() { Name = "sum", DataType = "number" },
                new() { Name = "difference", DataType = "number" },
                new() { Name = "product", DataType = "number" },
                new() { Name = "quotient", DataType = "number" }
            },
            PythonProcessCode = @"# Perform calculations
a = float(inputs.get('a', 0))
b = float(inputs.get('b', 0))

outputs = {
    'sum': a + b,
    'difference': a - b,
    'product': a * b,
    'quotient': a / b if b != 0 else None
}
"
        };
        AddOrUpdatePlugin(mathCalc);
        
        // Example: JSON Parser Plugin
        var jsonParser = new NodePluginDefinition
        {
            Name = "JSON Parser",
            Description = "Parse and extract data from JSON",
            Category = "Data",
            Icon = "📋",
            InputPins = new List<PluginInputPin>
            {
                new() { Name = "json_string", DataType = "string", Required = true },
                new() { Name = "path", DataType = "string", DefaultValue = "" }
            },
            OutputPins = new List<PluginOutputPin>
            {
                new() { Name = "data", DataType = "any" },
                new() { Name = "is_valid", DataType = "boolean" }
            },
            PythonProcessCode = @"import json

json_string = inputs.get('json_string', '{}')
path = inputs.get('path', '')

try:
    data = json.loads(json_string)
    
    # Navigate path if provided
    if path:
        for key in path.split('.'):
            if key:
                data = data.get(key, None) if isinstance(data, dict) else None
    
    outputs = {
        'data': data,
        'is_valid': True
    }
except Exception as e:
    outputs = {
        'data': None,
        'is_valid': False,
        'error': str(e)
    }
"
        };
        AddOrUpdatePlugin(jsonParser);
        
        Logger.Instance.Info($"Created {_plugins.Count} default plugins", "PluginManager");
    }
}
