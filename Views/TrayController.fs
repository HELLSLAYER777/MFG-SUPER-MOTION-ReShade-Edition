namespace ARMO.Views

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Threading
open ARMO.ViewModels

/// Owns the tray icon for this window. No polling or work is scheduled while idle.
type TrayController(window: MainWindow, model: MainViewModel) as this =
    let tray = new TrayIcon(Icon = window.Icon, ToolTipText = "MFG SUPER MOTION", IsVisible = false)
    let restoreItem = NativeMenuItem(model.Text.Restore)
    let exitItem = NativeMenuItem(model.Text.Exit)
    let icons =
        match TrayIcon.GetIcons(Application.Current) with
        | null ->
            let icons = TrayIcons()
            TrayIcon.SetIcons(Application.Current, icons)
            icons
        | icons -> icons
    let mutable previousState = WindowState.Normal
    let mutable disposed = false
    let mutable firstOpen = not window.IsVisible

    let hideMinimized () =
        if not disposed && model.MinimizeToTray && window.WindowState = WindowState.Minimized then
            tray.IsVisible <- true
            window.Hide()

    let windowChanged = EventHandler<AvaloniaPropertyChangedEventArgs>(fun _ args ->
        if args.Property = Window.WindowStateProperty then
            if window.WindowState = WindowState.Minimized then
                Dispatcher.UIThread.Post(hideMinimized)
            else previousState <- window.WindowState)

    let modelChanged = ComponentModel.PropertyChangedEventHandler(fun _ args ->
        match args.PropertyName with
        | "Text" -> restoreItem.Header <- model.Text.Restore; exitItem.Header <- model.Text.Exit
        | "MinimizeToTray" ->
            if model.MinimizeToTray then hideMinimized ()
            elif not window.IsVisible then this.Restore()
        | _ -> ())

    do
        let menu = NativeMenu()
        menu.Items.Add restoreItem
        menu.Items.Add(NativeMenuItemSeparator())
        menu.Items.Add exitItem
        tray.Menu <- menu
        icons.Add tray
        restoreItem.Click.Add(fun _ -> this.Restore())
        exitItem.Click.Add(fun _ -> window.Close())
        tray.Clicked.Add(fun _ -> this.Restore())
        window.PropertyChanged.AddHandler windowChanged
        model.PropertyChanged.AddHandler modelChanged
        window.Opened.Add(fun _ ->
            let startMinimized = firstOpen && model.StartMinimized
            firstOpen <- false
            if startMinimized then
                // Only the initial open applies the saved startup mode.
                Dispatcher.UIThread.Post(fun () -> window.WindowState <- WindowState.Minimized))

    member _.Restore() =
        if not disposed then
            // Hide the icon after showing the window so a restore path always exists.
            let state = previousState
            window.Show()
            window.WindowState <- state
            window.Activate()
            tray.IsVisible <- false

    member _.IsTrayVisible = tray.IsVisible
    member _.HasNativeMenu = not (isNull tray.NativeMenuExporter)

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                window.PropertyChanged.RemoveHandler windowChanged
                model.PropertyChanged.RemoveHandler modelChanged
                icons.Remove tray |> ignore
                tray.Dispose()
