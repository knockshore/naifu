using Python.Runtime;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace EtlNodeEditor;

/// <summary>
/// Handles execution of Python code using Python.NET
/// </summary>
public static class PythonExecutor
{
    private static bool _isInitialized = false;
    private static readonly object _lock = new();
    
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;
            
            try
            {
                // IMPORTANT: Set Python DLL path BEFORE any PythonEngine calls
                var pythonPath = FindPythonPath();
                if (!string.IsNullOrEmpty(pythonPath))
                {
                    // Must set this before PythonEngine.Initialize()
                    if (!PythonEngine.IsInitialized)
                    {
                        Runtime.PythonDLL = pythonPath;
                        Logger.Instance.Info($"Using Python DLL: {pythonPath}", "PythonExecutor");
                    }
                }
                
                PythonEngine.Initialize();
                PythonEngine.BeginAllowThreads();
                _isInitialized = true;
                
                Logger.Instance.Info("Python.NET initialized successfully", "PythonExecutor");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Failed to initialize Python.NET: {ex.Message}", "PythonExecutor");
                Logger.Instance.Warning("Python features will be disabled. Please ensure Python 3.x is installed.", "PythonExecutor");
            }
        }
    }
    
    public static async Task<Dictionary<string, object?>> ExecuteAsync(string pythonCode, Dictionary<string, object?> context)
    {
        return await Task.Run(() => Execute(pythonCode, context));
    }
    
    public static Dictionary<string, object?> Execute(string pythonCode, Dictionary<string, object?> context)
    {
        if (!_isInitialized)
        {
            Initialize();
            if (!_isInitialized)
            {
                throw new Exception("Python is not initialized. Please install Python 3.x and restart the application.");
            }
        }
        
        var results = new Dictionary<string, object?>();
        
        try
        {
            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    // Set up context variables
                    foreach (var kvp in context)
                    {
                        scope.Set(kvp.Key, ConvertToPython(kvp.Value));
                    }
                    
                    // Execute the Python code
                    scope.Exec(pythonCode);
                    
                    // Extract all variables from scope by executing Python's locals() in the scope
                    var localsCode = "dict(locals())";
                    using (var localsDict = scope.Eval(localsCode))
                    {
                        if (PyDict.IsDictType(localsDict))
                        {
                            var pyDict = new PyDict(localsDict);
                            foreach (PyObject key in pyDict.Keys())
                            {
                                try
                                {
                                    var varName = key.ToString();
                                    if (varName != null && !varName.StartsWith("__") && !varName.StartsWith("_"))
                                    {
                                        var pyObj = pyDict.GetItem(key);
                                        results[varName] = ConvertFromPython(pyObj);
                                    }
                                }
                                catch
                                {
                                    // Skip variables that can't be converted
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (PythonException ex)
        {
            results["error"] = $"Python Error: {ex.Message}";
            Logger.Instance.Error($"Python execution error: {ex.Message}", "PythonExecutor");
        }
        catch (Exception ex)
        {
            results["error"] = $"Execution Error: {ex.Message}";
            Logger.Instance.Error($"Error executing Python code: {ex.Message}", "PythonExecutor");
        }
        
        return results;
    }
    
    private static PyObject ConvertToPython(object? value)
    {
        if (value == null)
            return Runtime.None;
        
        return value switch
        {
            string s => new PyString(s),
            int i => new PyInt(i),
            long l => new PyInt(l),
            float f => new PyFloat(f),
            double d => new PyFloat(d),
            bool b => b.ToPython(),
            Dictionary<string, object?> dict => ConvertDictToPython(dict),
            List<object> list => ConvertListToPython(list),
            _ => value.ToPython()
        };
    }
    
    private static PyObject ConvertDictToPython(Dictionary<string, object?> dict)
    {
        using (Py.GIL())
        {
            var pyDict = new PyDict();
            foreach (var kvp in dict)
            {
                pyDict.SetItem(kvp.Key, ConvertToPython(kvp.Value));
            }
            return pyDict;
        }
    }
    
    private static PyObject ConvertListToPython(List<object> list)
    {
        using (Py.GIL())
        {
            var pyList = new PyList();
            foreach (var item in list)
            {
                pyList.Append(ConvertToPython(item));
            }
            return pyList;
        }
    }
    
    private static object? ConvertFromPython(PyObject pyObj)
    {
        if (pyObj.IsNone())
            return null;
        
        // Try to determine the type and convert appropriately
        try
        {
            if (PyInt.IsIntType(pyObj))
                return pyObj.As<long>();
            
            if (PyFloat.IsFloatType(pyObj))
                return pyObj.As<double>();
            
            if (PyString.IsStringType(pyObj))
                return pyObj.As<string>();
            
            // Check for boolean before dict/list as bools are also ints in Python
            var pyType = pyObj.GetPythonType().ToString();
            if (pyType.Contains("bool"))
                return pyObj.As<bool>();
            
            if (PyDict.IsDictType(pyObj))
            {
                var dict = new Dictionary<string, object?>();
                var pyDict = new PyDict(pyObj);
                foreach (PyObject key in pyDict.Keys())
                {
                    var keyStr = key.As<string>();
                    var value = pyDict.GetItem(key);
                    dict[keyStr] = ConvertFromPython(value);
                }
                return dict;
            }
            
            if (PyList.IsListType(pyObj))
            {
                var list = new List<object?>();
                var pyList = new PyList(pyObj);
                for (int i = 0; i < pyList.Length(); i++)
                {
                    list.Add(ConvertFromPython(pyList[i]));
                }
                return list;
            }
            
            // Fallback to string representation
            return pyObj.ToString();
        }
        catch
        {
            return pyObj.ToString();
        }
    }
    
    private static string? FindPythonPath()
    {
        // First, try to read from appsettings.json
        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();
            
            // Check for direct DLL path
            var pythonDll = configuration["Python:PythonDll"];
            if (!string.IsNullOrEmpty(pythonDll) && File.Exists(pythonDll))
            {
                Logger.Instance.Info($"Using Python DLL from appsettings: {pythonDll}", "PythonExecutor");
                return pythonDll;
            }
            
            // Check for Python directory path
            var pythonPath = configuration["Python:PythonPath"];
            if (!string.IsNullOrEmpty(pythonPath) && Directory.Exists(pythonPath))
            {
                // Look for pythonXY.dll (e.g., python311.dll) - the version-specific DLL
                // Directory.GetFiles uses wildcards, not regex
                var allDlls = Directory.GetFiles(pythonPath, "python*.dll");
                
                // Filter for version-specific DLLs (pythonXY.dll where X and Y are digits)
                var versionedDlls = allDlls
                    .Where(path => {
                        var fileName = Path.GetFileName(path);
                        // Match pattern: python followed by 2-3 digits, then .dll
                        // e.g., python311.dll, python39.dll
                        return System.Text.RegularExpressions.Regex.IsMatch(
                            fileName, 
                            @"^python3\d{1,2}\.dll$", 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    })
                    .OrderByDescending(x => x) // Get the highest version
                    .ToList();
                
                if (versionedDlls.Count > 0)
                {
                    Logger.Instance.Info($"Found Python DLL from appsettings path: {versionedDlls[0]}", "PythonExecutor");
                    return versionedDlls[0];
                }
                else
                {
                    Logger.Instance.Warning($"Python path configured ({pythonPath}) but no pythonXY.dll found", "PythonExecutor");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Warning($"Error reading appsettings.json: {ex.Message}", "PythonExecutor");
        }
        
        // Fall back to common Python DLL locations on Windows
        Logger.Instance.Info("Searching for Python in common locations...", "PythonExecutor");
        var possiblePaths = new[]
        {
            @"C:\Python312\python312.dll",
            @"C:\Python311\python311.dll",
            @"C:\Python310\python310.dll",
            @"C:\Python39\python39.dll",
            @"C:\Python38\python38.dll",
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Python\Python312\python312.dll"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Python\Python311\python311.dll"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Python\Python310\python310.dll"),
        };
        
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                Logger.Instance.Info($"Found Python at: {path}", "PythonExecutor");
                return path;
            }
        }
        
        // Try to get from PATH
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "-c \"import sys; print(sys.executable)\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var pythonExe = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            
            if (!string.IsNullOrEmpty(pythonExe) && File.Exists(pythonExe))
            {
                var pythonDir = Path.GetDirectoryName(pythonExe);
                if (pythonDir != null)
                {
                    // Look for python3x.dll in the same directory
                    var dlls = Directory.GetFiles(pythonDir, "python3*.dll");
                    if (dlls.Length > 0)
                    {
                        Logger.Instance.Info($"Found Python at: {dlls[0]}", "PythonExecutor");
                        return dlls[0];
                    }
                }
            }
        }
        catch
        {
            // Ignore errors when trying to find Python
        }
        
        return null;
    }
    
    public static void Shutdown()
    {
        if (_isInitialized)
        {
            try
            {
                PythonEngine.Shutdown();
                _isInitialized = false;
                Logger.Instance.Info("Python.NET shutdown complete", "PythonExecutor");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Error shutting down Python: {ex.Message}", "PythonExecutor");
            }
        }
    }
}
