using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Avalonia.UpDock.Controls;

public class SplitFractions(params int[] fractions)
{
    public static SplitFractions Default => new(1);
    public int Count => fractions.Length;
    public int[] CalcFractionSizes(int totalSize)
    {
        int denominator = fractions.Sum();
        int[] sizes = new int[fractions.Length];
        int offset = 0;
        for (int i = 0; i < fractions.Length; i++)
        {
            int size = (int)Math.Round(fractions[i] * totalSize / (double)denominator);
            sizes[i] = size;
            offset += size;
        }

        sizes[^1] += totalSize - offset;

        return sizes;
    }

    public (int offset, int size)[] CalcFractionLayoutInfos(int totalSize)
    {
        int denominator = fractions.Sum();
        var layoutInfos = new (int offset, int size)[fractions.Length];
        int offset = 0;
        for (int i = 0; i < fractions.Length; i++)
        {
            int size = (int)Math.Round(fractions[i] * totalSize / (double)denominator);
            layoutInfos[i] = (offset, size);
            offset += size;
        }

        layoutInfos[^1].size += totalSize - offset;

        return layoutInfos;
    }
    public int this[int index] => fractions[index];

    public static SplitFractions Parse(string s)
    {
        var tokenizer = new StringTokenizer(s);

        List<int> fractions = [];
        while(tokenizer.TryReadString(out var fractionStr))
        {
            fractions.Add(int.Parse(fractionStr));
        }

        return new SplitFractions([.. fractions]);
    }
}

public class SplitPanel : Panel
{
    private (int index, Point lastPointerPosition)? _draggedSplitLine = null;

    private List<Line> _splitLines = [];
    private SplitFractions _fractions = SplitFractions.Default;

    public SplitFractions Fractions
    {
        get => _fractions;
        set
        {
            var oldCount = _fractions.Count;
            if (value == null || value.Count == 0)
                _fractions = SplitFractions.Default;
            else
                _fractions = value;

            InvalidateArrange();

            if (oldCount == _fractions.Count)
                return;

            VisualChildren.RemoveAll(_splitLines);

            for (int i = 0; i < _fractions.Count - 1; i++)
            {
                var line = new Line()
                {
                    Stroke = Brushes.Gray,
                    StrokeThickness = 4
                };
                VisualChildren.Add(line);
                _splitLines.Add(line);
            }
        }
    }

    public Orientation Orientation { get; set; }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layoutInfos = _fractions.CalcFractionLayoutInfos(
            (int)(Orientation == Orientation.Horizontal ? Bounds.Width : Bounds.Height));

        var slotCount = Fractions.Count;

        if(Orientation == Orientation.Horizontal)
        {
            for (int i = 0; i < slotCount - 1; i++)
            {
                var (offset, _) = layoutInfos[i + 1];
                _splitLines[i].StartPoint = new Point(offset, 0);
                _splitLines[i].EndPoint = new Point(offset, finalSize.Height);
            }

            for (int i = 0; i < Math.Min(Children.Count, slotCount); i++)
            {
                Control child = Children[i];

                var (offset, size) = layoutInfos[i];
                child.Arrange(new Rect(
                    offset, 0,
                    size, finalSize.Height));
            }
        }
        else
        {
            for (int i = 1; i < slotCount; i++)
            {
                var (offset, _) = layoutInfos[i];
                _splitLines[i].StartPoint = new Point(0, offset);
                _splitLines[i].EndPoint = new Point(finalSize.Width, offset);
            }

            for (int i = 0; i < Math.Min(Children.Count, slotCount); i++)
            {
                Control child = Children[i];

                var (offset, size) = layoutInfos[i];
                child.Arrange(new Rect(
                    0, offset,
                    finalSize.Width, size));
            }
        }


        return finalSize;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedSplitLine == null)
            return;

        var pointerPos = e.GetPosition(this);
        var (splitIndex, lastPointerPosition) = _draggedSplitLine.Value;
        var pointerDelta = new Point(
            Math.Round(pointerPos.X - lastPointerPosition.X),
            Math.Round(pointerPos.Y - lastPointerPosition.Y)
            );

        int[] fractionSizes = _fractions.CalcFractionSizes(
            (int)(Orientation == Orientation.Horizontal ? Bounds.Width : Bounds.Height));

        int delta = (int)(Orientation == Orientation.Horizontal ? pointerDelta.X : pointerDelta.Y);

        int minFractionSize = 20;

        if(fractionSizes[splitIndex] + delta < minFractionSize)
            delta = -(fractionSizes[splitIndex] - minFractionSize);
        if (fractionSizes[splitIndex + 1] - delta < minFractionSize)
            delta = (fractionSizes[splitIndex + 1] - minFractionSize);

        if (fractionSizes[splitIndex] + delta < minFractionSize) //we are trapped, abort
            return;

        fractionSizes[splitIndex] += delta;
        fractionSizes[splitIndex + 1] -= delta;

        if (Orientation == Orientation.Horizontal)
            pointerDelta = pointerDelta.WithX(delta);
        else
            pointerDelta = pointerDelta.WithY(delta);

        _draggedSplitLine = (splitIndex, lastPointerPosition + pointerDelta);

        Fractions = new SplitFractions(fractionSizes);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Source is not Line line)
            return;

        int splitIndex = _splitLines.IndexOf(line);
        if (splitIndex == -1)
            return;

        Debug.WriteLine($"Clicked on seperator between slot {splitIndex} and {splitIndex + 1}");

        _draggedSplitLine = (splitIndex, e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggedSplitLine = null;
    }
}
