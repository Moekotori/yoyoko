using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Chat.Core.Messaging;

namespace Chat.UI.Markup;

internal static class MathView
{
    private static readonly FontFamily MathFont = new("Georgia, Times New Roman, Songti SC, STIX Two Math, serif");
    private static readonly SolidColorBrush Ink = new(Color.Parse("#CCCCCC"));
    private static readonly SolidColorBrush Rule = new(Color.Parse("#8A8A8A"));

    public static Control Create(MathNode node, double em) =>
        new Border
        {
            Child = Build(node, Math.Clamp(em, 11, 22)),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent
        };

    private static Control Build(MathNode node, double em) => node switch
    {
        MathList list => Row(list.Items, em),
        MathFrac frac => Fraction(frac, em),
        MathSqrt sqrt => Root(sqrt, em),
        MathScripts scripts => Scripts(scripts, em),
        MathAccent accent => Accent(accent, em),
        MathMatrix matrix => Matrix(matrix, em),
        MathText text => Glyph(text, em),
        _ => Glyph(new MathText(""), em)
    };

    private static Control Row(MathNode[] items, double em)
    {
        if (items.Length == 0) return Glyph(new MathText(""), em);
        if (items.Length == 1) return Build(items[0], em);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = Math.Max(0, em * 0.04)
        };
        foreach (var item in items)
            row.Children.Add(Build(item, em));
        return row;
    }

    private static Control Fraction(MathFrac frac, double em)
    {
        var inner = em * 0.82;
        var column = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var num = Build(frac.Num, inner);
        var den = Build(frac.Den, inner);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        den.HorizontalAlignment = HorizontalAlignment.Center;
        column.Children.Add(num);
        column.Children.Add(new Border { Height = 1, Background = Rule, Margin = new Thickness(0, 1) });
        column.Children.Add(den);
        return column;
    }

    private static Control Root(MathSqrt sqrt, double em)
    {
        var body = Build(sqrt.Body, em);
        var inner = new StackPanel { Spacing = 1 };
        inner.Children.Add(new Border { Height = 1, Background = Rule });
        inner.Children.Add(body);
        var radical = new TextBlock
        {
            Text = "√",
            FontSize = em * 1.15,
            FontFamily = MathFont,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 0
        };
        if (sqrt.Index is not null)
        {
            var index = Build(sqrt.Index, em * 0.62);
            index.VerticalAlignment = VerticalAlignment.Top;
            index.Margin = new Thickness(0, 0, -2, 0);
            row.Children.Add(index);
        }
        row.Children.Add(radical);
        row.Children.Add(inner);
        return row;
    }

    private static Control Scripts(MathScripts scripts, double em)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(Build(scripts.Core, em));
        if (scripts.Super is null && scripts.Sub is null) return row;
        var column = new StackPanel { Spacing = em * 0.08, Margin = new Thickness(1, 0, 0, 0) };
        if (scripts.Super is not null) column.Children.Add(Build(scripts.Super, em * 0.68));
        if (scripts.Sub is not null) column.Children.Add(Build(scripts.Sub, em * 0.68));
        row.Children.Add(column);
        return row;
    }

    private static Control Accent(MathAccent accent, double em)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = -em * 0.15 };
        column.Children.Add(new TextBlock
        {
            Text = accent.Mark,
            FontSize = em * 0.7,
            FontFamily = MathFont,
            Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        column.Children.Add(Build(accent.Body, em));
        return column;
    }

    private static Control Matrix(MathMatrix matrix, double em)
    {
        var inner = em * 0.88;
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var rowCount = Math.Min(matrix.Rows.Length, 6);
        for (var r = 0; r < rowCount; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var cols = 1;
        for (var r = 0; r < rowCount; r++)
            cols = Math.Max(cols, Math.Min(matrix.Rows[r].Length, 6));
        for (var c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < rowCount; r++)
        {
            var cells = matrix.Rows[r];
            for (var c = 0; c < Math.Min(cells.Length, cols); c++)
            {
                var child = Build(cells[c], inner);
                child.Margin = new Thickness(em * 0.25, 1);
                Grid.SetRow(child, r);
                Grid.SetColumn(child, c);
                grid.Children.Add(child);
            }
        }
        if (matrix.Left.Length == 0 && matrix.Right.Length == 0) return grid;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (matrix.Left.Length > 0)
            row.Children.Add(new TextBlock
            {
                Text = matrix.Left,
                FontSize = em * 1.2,
                FontFamily = MathFont,
                Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center
            });
        row.Children.Add(grid);
        if (matrix.Right.Length > 0)
            row.Children.Add(new TextBlock
            {
                Text = matrix.Right,
                FontSize = em * 1.2,
                FontFamily = MathFont,
                Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center
            });
        return row;
    }

    private static Control Glyph(MathText text, double em)
    {
        var value = text.Text.Replace('\n', ' ');
        return new TextBlock
        {
            Text = value.Length == 0 ? " " : value,
            FontSize = em,
            FontFamily = MathFont,
            FontStyle = text.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
