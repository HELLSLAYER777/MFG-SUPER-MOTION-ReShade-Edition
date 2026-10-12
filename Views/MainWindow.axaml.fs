namespace ARMO.Views

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Markup.Xaml
open Avalonia.Media
open Avalonia.Styling
open Avalonia.Threading
open ARMO.Infrastructure
open ARMO.ViewModels
open ARMO.Models

type MainWindow() as this =
    inherit Window()
    let mutable transparencyEnabled = false
    let mutable nativeBackdrop = false
    let mutable firstOpen = true

    do
        this.InitializeComponent()
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias)
        this.SizeChanged.Add(fun _ -> this.Classes.Set("compact", this.ClientSize.Width < UiMetrics.compactWidth))
        this.Opened.Add(fun _ ->
            if firstOpen then
                firstOpen <- false
                this.FitToScreen()
            this.SetTransparency(transparencyEnabled))
        this.PropertyChanged.Add(fun args ->
            if args.Property = TopLevel.ActualTransparencyLevelProperty then this.UpdateMaterial())
        this.ActualThemeVariantChanged.Add(fun _ ->
            WindowsBackdrop.setTheme (this.NativeHandle()) (this.ActualThemeVariant = ThemeVariant.Dark)
            this.InvalidateVisual())
        // While the shortcut is being picked every key is its: Esc leaves it as
        // it was, Backspace or Delete takes it away, anything else with a
        // modifier (or a function key alone) becomes the shortcut.
        this.AddHandler(InputElement.KeyDownEvent, (fun _ (args: KeyEventArgs) ->
            match this.DataContext with
            | :? MainViewModel as model when model.IsCapturingShortcut ->
                args.Handled <- true
                match args.Key with
                | Key.Escape -> model.EndShortcutCapture 0
                | Key.Back | Key.Delete -> model.EndShortcutCapture -1
                | Key.LeftCtrl | Key.RightCtrl | Key.LeftAlt | Key.RightAlt | Key.LeftShift | Key.RightShift
                | Key.LWin | Key.RWin | Key.System | Key.None -> ()
                | key ->
                    let has modifier = args.KeyModifiers.HasFlag modifier
                    let modifiers =
                        (if has KeyModifiers.Alt then 1 else 0) ||| (if has KeyModifiers.Control then 2 else 0)
                        ||| (if has KeyModifiers.Shift then 4 else 0) ||| (if has KeyModifiers.Meta then 8 else 0)
                    let name = key.ToString()
                    let vk = Shortcut.virtualKey name
                    let functionKey = vk >= 0x70 && vk <= 0x87
                    if vk <> 0 && (modifiers <> 0 || functionKey) then model.EndShortcutCapture((modifiers <<< 16) ||| vk)
            | _ -> ()), Avalonia.Interactivity.RoutingStrategies.Tunnel)
        this.Deactivated.Add(fun _ ->
            match this.DataContext with
            | :? MainViewModel as model when model.IsCapturingShortcut -> model.EndShortcutCapture 0
            | _ -> ())
        this.KeyDown.Add(fun args ->
            match this.DataContext with
            | :? MainViewModel as model when args.Key = Key.Escape && model.IsDialogOpen ->
                model.CancelDialog()
                args.Handled <- true
            | _ -> ())

    member private this.InitializeComponent() =
        AvaloniaXamlLoader.Load(this)

    member private _.UpdateMaterial() =
        let level = this.ActualTransparencyLevel
        let supported = level = WindowTransparencyLevel.Mica || level = WindowTransparencyLevel.AcrylicBlur || level = WindowTransparencyLevel.Blur
        this.Classes.Set("material", transparencyEnabled && (supported || nativeBackdrop))

    member private _.NativeHandle() =
        let handle = this.TryGetPlatformHandle()
        if isNull handle then 0n else handle.Handle

    member _.SetTransparency(enabled: bool) =
        transparencyEnabled <- enabled
        if nativeBackdrop then
            WindowsBackdrop.trySet (this.NativeHandle()) false (this.ActualThemeVariant = ThemeVariant.Dark) |> ignore
            nativeBackdrop <- false
        this.TransparencyLevelHint <-
            if enabled then [| WindowTransparencyLevel.AcrylicBlur; WindowTransparencyLevel.Transparent |]
            else [| WindowTransparencyLevel.None |]
        if enabled && this.ActualTransparencyLevel = WindowTransparencyLevel.Transparent then
            nativeBackdrop <- WindowsBackdrop.trySet (this.NativeHandle()) true (this.ActualThemeVariant = ThemeVariant.Dark)
            if not nativeBackdrop then this.TransparencyLevelHint <- [| WindowTransparencyLevel.None |]
        WindowsBackdrop.setTheme (this.NativeHandle()) (this.ActualThemeVariant = ThemeVariant.Dark)
        this.UpdateMaterial()
        this.InvalidateVisual()

    member private _.FitToScreen() =
        let screen = this.Screens.ScreenFromWindow(this)
        if not (isNull screen) then
            let availableWidth = float screen.WorkingArea.Width / screen.Scaling
            let availableHeight = float screen.WorkingArea.Height / screen.Scaling
            let outerWidth, outerHeight =
                WindowsBackdrop.visibleFrameSize (this.NativeHandle()) this.RenderScaling
                |> Option.defaultValue (this.ClientSize.Width, this.ClientSize.Height)
            let decorationWidth = max 0. (outerWidth - this.ClientSize.Width)
            let decorationHeight = max 0. (outerHeight - this.ClientSize.Height)
            let targetWidth = min UiMetrics.defaultWidth (availableWidth * 0.94)
            let targetHeight = min UiMetrics.defaultHeight (availableHeight * 0.94)
            this.MinWidth <- min UiMetrics.minimumWidth (max 1. (targetWidth - decorationWidth))
            this.MinHeight <- min UiMetrics.minimumHeight (max 1. (targetHeight - decorationHeight))
            // Width/Height describe the client area; the reference includes Windows' frame.
            this.Width <- targetWidth - decorationWidth
            this.Height <- targetHeight - decorationHeight
            this.Position <- PixelPoint(
                screen.WorkingArea.X + int ((availableWidth - targetWidth) * screen.Scaling / 2.),
                screen.WorkingArea.Y + int ((availableHeight - targetHeight) * screen.Scaling / 2.))

    member _.FocusProfileName() =
        Dispatcher.UIThread.Post(fun () ->
            let input = this.FindControl<TextBox>("ProfileNameBox")
            if not (isNull input) then
                input.Focus() |> ignore
                input.SelectAll())

