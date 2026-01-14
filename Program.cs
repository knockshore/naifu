using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace EtlNodeEditor;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            Logger.Instance.Info("Naifu", "Program");
            
            WindowCreateInfo windowCI = new WindowCreateInfo
            {
                X = 100,
                Y = 100,
                WindowWidth = 1600,
                WindowHeight = 900,
                WindowTitle = "Naifu",
                WindowInitialState = WindowState.Maximized
            };

            Logger.Instance.Info("Creating window...", "Program");
            Sdl2Window window = VeldridStartup.CreateWindow(ref windowCI);
            window.Visible = true;

            Logger.Instance.Info("Creating graphics device...", "Program");
            GraphicsDevice gd = VeldridStartup.CreateGraphicsDevice(window, new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true
            });

            Logger.Instance.Info("Creating ImGui context...", "Program");
            ImGui.CreateContext();
            ImGuiIOPtr io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            
            Logger.Instance.Info("Creating resources...", "Program");
            var commandList = gd.ResourceFactory.CreateCommandList();
            
            Logger.Instance.Info("Creating ImGui renderer...", "Program");
            var controller = new ImGuiRenderer(gd, gd.MainSwapchain.Framebuffer.OutputDescription, window.Width, window.Height);

            Logger.Instance.Info("Creating node editor...", "Program");
            var editor = new SimpleNodeEditor();
            
            Logger.Instance.Info("Initializing Python...", "Program");
            PythonExecutor.Initialize();
            
            Logger.Instance.Info("Loading plugins...", "Program");
            var pluginCount = PluginManager.Instance.GetAllPlugins().Count();
            Logger.Instance.Info($"Loaded {pluginCount} plugins", "Program");

            window.Resized += () =>
            {
                gd.MainSwapchain.Resize((uint)window.Width, (uint)window.Height);
                controller.WindowResized(window.Width, window.Height);
            };

            Logger.Instance.Info("Starting main loop...", "Program");
            var frameWatch = System.Diagnostics.Stopwatch.StartNew();
            bool logKeyEvents = false; // Toggle with F12

            while (window.Exists)
            {
                float deltaTime = frameWatch.ElapsedTicks / (float)System.Diagnostics.Stopwatch.Frequency;
                frameWatch.Restart();

                InputSnapshot snapshot = window.PumpEvents();
                if (!window.Exists) break;

                // Log input events for debugging
                if (snapshot.KeyEvents.Count > 0 || snapshot.KeyCharPresses.Count > 0)
                {
                    foreach (var keyEvent in snapshot.KeyEvents)
                    {
                        Logger.Instance.Debug($"KeyEvent: Key={keyEvent.Key} Down={keyEvent.Down} Modifiers={keyEvent.Modifiers}", "Input");
                    }
                    foreach (var ch in snapshot.KeyCharPresses)
                    {
                        Logger.Instance.Debug($"KeyChar: '{ch}' (0x{((int)ch):X4})", "Input");
                    }
                }

                controller.Update(deltaTime, snapshot);
                
                // Workaround: Manually send navigation key events to ImGui
                // Veldrid.ImGui 5.72.0 may not properly map these keys
                var imguiIO = ImGui.GetIO();
                foreach (var keyEvent in snapshot.KeyEvents)
                {
                    ImGuiKey? imguiKey = keyEvent.Key switch
                    {
                        Key.BackSpace => ImGuiKey.Backspace,
                        Key.Delete => ImGuiKey.Delete,
                        Key.Left => ImGuiKey.LeftArrow,
                        Key.Right => ImGuiKey.RightArrow,
                        Key.Up => ImGuiKey.UpArrow,
                        Key.Down => ImGuiKey.DownArrow,
                        Key.Home => ImGuiKey.Home,
                        Key.End => ImGuiKey.End,
                        Key.PageUp => ImGuiKey.PageUp,
                        Key.PageDown => ImGuiKey.PageDown,
                        _ => null
                    };
                    
                    if (imguiKey.HasValue)
                    {
                        imguiIO.AddKeyEvent(imguiKey.Value, keyEvent.Down);
                        Logger.Instance.Debug($"Manually sent {imguiKey.Value} event: {keyEvent.Down}", "Input");
                    }
                }

                // Check if F12 is pressed to toggle logging
                if (ImGui.IsKeyPressed(ImGuiKey.F12))
                {
                    logKeyEvents = !logKeyEvents;
                    Logger.Instance.Info($"Key event logging: {logKeyEvents}", "Input");
                }

                // Log ImGui key state if enabled
                if (logKeyEvents)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.Backspace))
                        Logger.Instance.Debug("ImGui detected: Backspace pressed", "Input");
                    if (ImGui.IsKeyPressed(ImGuiKey.Delete))
                        Logger.Instance.Debug("ImGui detected: Delete pressed", "Input");
                    if (ImGui.IsKeyDown(ImGuiKey.Backspace))
                        Logger.Instance.Debug("ImGui state: Backspace is down", "Input");
                    if (ImGui.IsKeyDown(ImGuiKey.Delete))
                        Logger.Instance.Debug("ImGui state: Delete is down", "Input");
                }

                editor.Render();

                commandList.Begin();
                commandList.SetFramebuffer(gd.MainSwapchain.Framebuffer);
                commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1f));
                controller.Render(gd, commandList);
                commandList.End();
                gd.SubmitCommands(commandList);
                gd.SwapBuffers(gd.MainSwapchain);
            }

            Logger.Instance.Info("Cleaning up...", "Program");
            gd.WaitForIdle();
            controller.Dispose();
            commandList.Dispose();
            gd.Dispose();
            
            Logger.Instance.Info("Shutting down Python...", "Program");
            PythonExecutor.Shutdown();
            
            Logger.Instance.Info("Exited successfully.", "Program");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"{ex.GetType().Name}: {ex.Message}", "Program");
            Logger.Instance.Error($"StackTrace: {ex.StackTrace}", "Program");
            Console.ReadLine();
        }
    }
}
