using System.Numerics;

namespace EtlNodeEditor;

public enum NodeType
{
    RestApi,
    Cli,
    Input,
    Output,
    Plugin
}

public abstract class Node
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Vector2 Position { get; set; }
    public NodeType Type { get; set; }
    public Dictionary<string, object?> Outputs { get; set; } = new();
    public List<string> OutputPins { get; protected set; } = new();
    
    public abstract Task<Dictionary<string, object?>> ExecuteAsync(Dictionary<string, object?> inputs);
}

public class RestApiNode : Node
{
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Payload { get; set; } = "";
    public string PythonScript { get; set; } = "";
    public List<string> CustomOutputs { get; set; } = new() { "output_1", "output_2" };
    
    public RestApiNode()
    {
        Type = NodeType.RestApi;
        Name = "REST API";
        OutputPins = new() { "ResponseText", "StatusCode", "output_1", "output_2" };
    }
    
    public override async Task<Dictionary<string, object?>> ExecuteAsync(Dictionary<string, object?> inputs)
    {
        var outputs = new Dictionary<string, object?>();
        
        try
        {
            using var client = new HttpClient();
            
            // Add headers
            foreach (var header in Headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
            
            HttpResponseMessage response;
            HttpContent? content = null;
            
            if (!string.IsNullOrEmpty(Payload))
            {
                content = new StringContent(Payload, System.Text.Encoding.UTF8, "application/json");
            }
            
            response = Method.ToUpper() switch
            {
                "POST" => await client.PostAsync(Url, content),
                "PUT" => await client.PutAsync(Url, content),
                "DELETE" => await client.DeleteAsync(Url),
                "PATCH" => await client.PatchAsync(Url, content),
                _ => await client.GetAsync(Url)
            };
            
            var responseText = await response.Content.ReadAsStringAsync();
            outputs["ResponseText"] = responseText;
            outputs["StatusCode"] = (int)response.StatusCode;
            
            // Execute C# script for custom outputs (simplified - would need Roslyn for full scripting)
            if (!string.IsNullOrEmpty(PythonScript))
            {
                // For now, just set defaults
                foreach (var output in CustomOutputs)
                {
                    outputs[output] = null;
                }
            }
        }
        catch (Exception ex)
        {
            outputs["ResponseText"] = "";
            outputs["StatusCode"] = 0;
            outputs["error"] = ex.Message;
        }
        
        Outputs = outputs;
        return outputs;
    }
}

public class CliNode : Node
{
    public string Command { get; set; } = "";
    public List<string> Arguments { get; set; } = new();
    public Dictionary<string, string> EnvVars { get; set; } = new();
    public bool UseStdin { get; set; } = false;
    public string StdinInput { get; set; } = "";
    public string CSharpScript { get; set; } = "";
    public List<string> CustomOutputs { get; set; } = new() { "output_1", "output_2" };
    
    public CliNode()
    {
        Type = NodeType.Cli;
        Name = "CLI Command";
        OutputPins = new() { "stdout", "stderr", "return_code", "output_1", "output_2" };
    }
    
    public override async Task<Dictionary<string, object?>> ExecuteAsync(Dictionary<string, object?> inputs)
    {
        var outputs = new Dictionary<string, object?>();
        
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = UseStdin,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            foreach (var arg in Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }
            
            foreach (var env in EnvVars)
            {
                startInfo.Environment[env.Key] = env.Value;
            }
            
            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            process.Start();
            
            if (UseStdin && !string.IsNullOrEmpty(StdinInput))
            {
                await process.StandardInput.WriteAsync(StdinInput);
                process.StandardInput.Close();
            }
            
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            outputs["stdout"] = stdout;
            outputs["stderr"] = stderr;
            outputs["return_code"] = process.ExitCode;
            
            // Execute C# script for custom outputs
            if (!string.IsNullOrEmpty(CSharpScript))
            {
                foreach (var output in CustomOutputs)
                {
                    outputs[output] = null;
                }
            }
        }
        catch (Exception ex)
        {
            outputs["stdout"] = "";
            outputs["stderr"] = ex.Message;
            outputs["return_code"] = -1;
            outputs["error"] = ex.Message;
        }
        
        Outputs = outputs;
        return outputs;
    }
}

public class InputNode : Node
{
    public string InputData { get; set; } = "";
    public string DataType { get; set; } = "text";
    
    public InputNode()
    {
        Type = NodeType.Input;
        Name = "Input";
        OutputPins = new() { "data" };
    }
    
    public override Task<Dictionary<string, object?>> ExecuteAsync(Dictionary<string, object?> inputs)
    {
        var outputs = new Dictionary<string, object?>();
        
        // Parse based on data type
        object? data = DataType.ToLower() switch
        {
            "json" => InputData,
            "number" => double.TryParse(InputData, out var num) ? num : InputData,
            "boolean" => bool.TryParse(InputData, out var b) ? b : InputData,
            _ => InputData
        };
        
        outputs["data"] = data;
        Outputs = outputs;
        return Task.FromResult(outputs);
    }
}

public class OutputNode : Node
{
    public string LogMessage { get; set; } = "";
    public DateTime? LastExecuted { get; set; }
    
    public OutputNode()
    {
        Type = NodeType.Output;
        Name = "Output/Log";
        OutputPins = new(); // No output pins - this is a terminal node
    }
    
    public override Task<Dictionary<string, object?>> ExecuteAsync(Dictionary<string, object?> inputs)
    {
        var outputs = new Dictionary<string, object?>();
        LastExecuted = DateTime.Now;
        
        // Log all inputs
        var logLines = new List<string>();
        logLines.Add($"[{LastExecuted:yyyy-MM-dd HH:mm:ss}] Output Node Executed:");
        
        foreach (var input in inputs)
        {
            var value = input.Value?.ToString() ?? "(null)";
            logLines.Add($"  {input.Key}: {value}");
        }
        
        LogMessage = string.Join("\n", logLines);
        Console.WriteLine(LogMessage);
        
        Outputs = outputs;
        return Task.FromResult(outputs);
    }
}
