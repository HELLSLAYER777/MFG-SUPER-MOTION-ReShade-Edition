namespace ARMO

open System
open Avalonia

module Program =

    [<CompiledName "BuildAvaloniaApp">] 
    let buildAvaloniaApp () = 
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .With(Win32PlatformOptions(
                DpiAwareness = Win32DpiAwareness.PerMonitorDpiAware,
                RenderingMode = [| Win32RenderingMode.Software |]))
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace(areas = Array.empty)

    [<EntryPoint; STAThread>]
    let main argv =
        buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)
