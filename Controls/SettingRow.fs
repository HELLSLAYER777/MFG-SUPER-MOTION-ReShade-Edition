namespace ARMO.Controls

open Avalonia
open Avalonia.Controls

/// Reflows one label/control pair using the space available to the row, in logical pixels.
type SettingRow() as this =
    inherit Grid()
    let mutable stacked = None
    do
        this.RowDefinitions <- RowDefinitions("Auto,Auto")
        this.MinHeight <- 44.

    override _.MeasureOverride(availableSize: Size) =
        let shouldStack = availableSize.Width < 330.
        if stacked <> Some shouldStack then
            stacked <- Some shouldStack
            this.ColumnDefinitions <- ColumnDefinitions(if shouldStack then "*" else "*,Auto")
            // Side by side the pair shares one row as tall as the row itself: a
            // switch then sits in the middle of it like a taller box does (in two
            // rows sized to their content it sat at the top, eight pixels higher
            // than its neighbours).
            this.RowDefinitions <- RowDefinitions(if shouldStack then "Auto,Auto" else "*")
            this.Margin <- if shouldStack then Thickness(0., 4., 0., 8.) else Thickness(0.)
            for child in this.Children do
                if child.Classes.Contains("row-control") then
                    Grid.SetColumn(child, if shouldStack then 0 else 1)
                    Grid.SetRow(child, if shouldStack then 1 else 0)
                    child.Margin <- if shouldStack then Thickness(0., 6., 0., 0.) else Thickness(0.)
        base.MeasureOverride(availableSize)
