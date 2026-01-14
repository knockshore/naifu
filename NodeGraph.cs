using System.Numerics;
using Newtonsoft.Json;

namespace EtlNodeEditor;

public class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceNodeId { get; set; }
    public string SourceOutput { get; set; } = "";
    public Guid TargetNodeId { get; set; }
    public string TargetInput { get; set; } = "";
}

public class NodeGraph
{
    public Dictionary<Guid, Node> Nodes { get; set; } = new();
    public Dictionary<Guid, Connection> Connections { get; set; } = new();
    
    public void AddNode(Node node)
    {
        Nodes[node.Id] = node;
    }
    
    public void RemoveNode(Guid nodeId)
    {
        Nodes.Remove(nodeId);
        
        var connectionsToRemove = Connections
            .Where(c => c.Value.SourceNodeId == nodeId || c.Value.TargetNodeId == nodeId)
            .Select(c => c.Key)
            .ToList();
        
        foreach (var connId in connectionsToRemove)
        {
            Connections.Remove(connId);
        }
    }
    
    public void AddConnection(Connection connection)
    {
        Connections[connection.Id] = connection;
    }
    
    public void RemoveConnection(Guid connectionId)
    {
        Connections.Remove(connectionId);
    }
    
    public Dictionary<string, object?> GetNodeInputs(Guid nodeId)
    {
        var inputs = new Dictionary<string, object?>();
        
        foreach (var conn in Connections.Values)
        {
            if (conn.TargetNodeId == nodeId)
            {
                if (Nodes.TryGetValue(conn.SourceNodeId, out var sourceNode))
                {
                    if (sourceNode.Outputs.TryGetValue(conn.SourceOutput, out var value))
                    {
                        inputs[conn.TargetInput] = value;
                    }
                }
            }
        }
        
        return inputs;
    }
    
    public async Task<Dictionary<string, object?>> ExecuteNodeAsync(Guid nodeId)
    {
        if (!Nodes.TryGetValue(nodeId, out var node))
        {
            Logger.Instance.Error($"Node {nodeId} not found", "GraphExecutor");
            return new Dictionary<string, object?> { ["error"] = "Node not found" };
        }
        
        var inputs = GetNodeInputs(nodeId);
        
        Logger.Instance.Info($"Executing: {node.Name}", "GraphExecutor");
        
        // Log inputs
        if (inputs.Any())
        {
            Logger.Instance.Debug($"  Inputs: {string.Join(", ", inputs.Select(kv => $"{kv.Key}={kv.Value}"))}", "GraphExecutor");
        }
        else
        {
            Logger.Instance.Debug($"  No inputs", "GraphExecutor");
        }
        
        var outputs = await node.ExecuteAsync(inputs);
        
        // Log outputs
        if (outputs.Any())
        {
            Logger.Instance.Debug($"  Outputs: {string.Join(", ", outputs.Select(kv => $"{kv.Key}={kv.Value}"))}", "GraphExecutor");
        }
        else
        {
            Logger.Instance.Debug($"  No outputs", "GraphExecutor");
        }
        
        return outputs;
    }
    
    public async Task ExecuteAllAsync()
    {
        Logger.Instance.Info($"Starting graph execution with {Nodes.Count} nodes", "GraphExecutor");
        
        var executed = new HashSet<Guid>();
        var maxIterations = Nodes.Count * 2;
        var iteration = 0;
        
        // Find starting nodes (nodes with no inputs)
        var startingNodes = Nodes.Keys.Where(nodeId => 
            !Connections.Values.Any(c => c.TargetNodeId == nodeId)
        ).ToList();
        
        if (!startingNodes.Any())
        {
            Logger.Instance.Warning("No starting nodes found (nodes without inputs). Executing all nodes.", "GraphExecutor");
        }
        else
        {
            Logger.Instance.Info($"Found {startingNodes.Count} starting node(s)", "GraphExecutor");
        }
        
        while (executed.Count < Nodes.Count && iteration < maxIterations)
        {
            iteration++;
            var executedThisIteration = 0;
            
            foreach (var (nodeId, node) in Nodes)
            {
                if (executed.Contains(nodeId))
                    continue;
                
                // Check if all input nodes have been executed
                var inputNodesReady = true;
                foreach (var conn in Connections.Values)
                {
                    if (conn.TargetNodeId == nodeId)
                    {
                        if (!executed.Contains(conn.SourceNodeId))
                        {
                            inputNodesReady = false;
                            break;
                        }
                    }
                }
                
                if (inputNodesReady)
                {
                    await ExecuteNodeAsync(nodeId);
                    executed.Add(nodeId);
                    executedThisIteration++;
                }
            }
            
            if (executedThisIteration == 0 && executed.Count < Nodes.Count)
            {
                Logger.Instance.Warning($"Circular dependency or disconnected nodes detected. {Nodes.Count - executed.Count} nodes not executed.", "GraphExecutor");
                break;
            }
        }
        
        Logger.Instance.Info($"Graph execution completed. Executed {executed.Count}/{Nodes.Count} nodes", "GraphExecutor");
    }
    
    public void SaveToFile(string filename)
    {
        var data = new
        {
            nodes = Nodes.Values.Select(n => new
            {
                id = n.Id,
                type = n.Type.ToString(),
                name = n.Name,
                position = new { x = n.Position.X, y = n.Position.Y },
                pluginId = (n is PluginNode pn) ? pn.Definition.Id : (Guid?)null,
                config = SerializeNodeConfig(n)
            }),
            connections = Connections.Values.Select(c => new
            {
                id = c.Id,
                source_node_id = c.SourceNodeId,
                source_output = c.SourceOutput,
                target_node_id = c.TargetNodeId,
                target_input = c.TargetInput
            })
        };
        
        var json = JsonConvert.SerializeObject(data, Formatting.Indented);
        File.WriteAllText(filename, json);
        Logger.Instance.Info($"Saved {Nodes.Count} nodes and {Connections.Count} connections", "NodeGraph");
    }
    
    private object SerializeNodeConfig(Node node)
    {
        return node switch
        {
            PluginNode plugin => plugin.Config,
            RestApiNode rest => new
            {
                url = rest.Url,
                method = rest.Method,
                headers = rest.Headers,
                payload = rest.Payload,
                python_script = rest.PythonScript,
                custom_outputs = rest.CustomOutputs
            },
            CliNode cli => new
            {
                command = cli.Command,
                arguments = cli.Arguments,
                env_vars = cli.EnvVars,
                use_stdin = cli.UseStdin,
                stdin_input = cli.StdinInput,
                csharp_script = cli.CSharpScript,
                custom_outputs = cli.CustomOutputs
            },
            _ => new { }
        };
    }
    
    public void LoadFromFile(string filename)
    {
        var json = File.ReadAllText(filename);
        var data = JsonConvert.DeserializeObject<dynamic>(json);
        
        Nodes.Clear();
        Connections.Clear();
        
        // Load nodes
        foreach (var nodeData in data.nodes)
        {
            try
            {
                var id = Guid.Parse(nodeData.id.ToString());
                var name = nodeData.name.ToString();
                var posX = (float)nodeData.position.x;
                var posY = (float)nodeData.position.y;
                
                Node? node = null;
                
                // Check if it's a plugin node
                if (nodeData.pluginId != null)
                {
                    var pluginId = Guid.Parse(nodeData.pluginId.ToString());
                    node = PluginManager.Instance.CreateNodeFromPlugin(pluginId);
                }
                    
                if (node != null)
                {
                    node.Id = id;
                    node.Name = name;
                    node.Position = new Vector2(posX, posY);
                    
                    // Restore config for plugin nodes
                    if (nodeData.config != null && node is PluginNode)
                    {
                        var pluginNode = (PluginNode)node;
                        var configDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(
                            nodeData.config.ToString());
                        if (configDict != null)
                        {
                            pluginNode.Config = configDict;
                        }
                    }
                    
                    Nodes[id] = node;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Failed to load node: {ex.Message}", "NodeGraph");
            }
        }
        
        // Load connections
        foreach (var connData in data.connections)
        {
            try
            {
                var conn = new Connection
                {
                    Id = Guid.Parse(connData.id.ToString()),
                    SourceNodeId = Guid.Parse(connData.source_node_id.ToString()),
                    SourceOutput = connData.source_output.ToString(),
                    TargetNodeId = Guid.Parse(connData.target_node_id.ToString()),
                    TargetInput = connData.target_input.ToString()
                };
                Connections[conn.Id] = conn;
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Failed to load connection: {ex.Message}", "NodeGraph");
            }
        }
        
        Logger.Instance.Info($"Loaded {Nodes.Count} nodes and {Connections.Count} connections", "NodeGraph");
    }
}
