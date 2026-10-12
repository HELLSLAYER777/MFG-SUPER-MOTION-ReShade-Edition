namespace ARMO.Controls

open System
open Avalonia
open Avalonia.Controls

/// Lays groups of settings out side by side where the page is wide enough for two columns - each group under
/// the shorter column so far - and under each other where it is not. Sizes in logical pixels.
type GroupFlow() =
    inherit Panel()
    let gap = 12.
    let columnsFor (width: float) = if Double.IsFinite width && width >= 760. then 2 else 1

    override this.MeasureOverride(available: Size) =
        let columns = columnsFor available.Width
        let width = if Double.IsFinite available.Width then (available.Width - gap * float (columns - 1)) / float columns else 420.
        let heights = Array.zeroCreate<float> columns
        for child in this.Children do
            if child.IsVisible then
                child.Measure(Size(width, Double.PositiveInfinity))
                let column = if columns = 2 && heights[1] < heights[0] then 1 else 0
                heights[column] <- heights[column] + (if heights[column] > 0. then gap else 0.) + child.DesiredSize.Height
        Size((if Double.IsFinite available.Width then available.Width else width), Array.max heights)

    override this.ArrangeOverride(final: Size) =
        let columns = columnsFor final.Width
        let width = (final.Width - gap * float (columns - 1)) / float columns
        let heights = Array.zeroCreate<float> columns
        for child in this.Children do
            if child.IsVisible then
                let column = if columns = 2 && heights[1] < heights[0] then 1 else 0
                let top = heights[column] + (if heights[column] > 0. then gap else 0.)
                child.Arrange(Rect(float column * (width + gap), top, width, child.DesiredSize.Height))
                heights[column] <- top + child.DesiredSize.Height
        final
