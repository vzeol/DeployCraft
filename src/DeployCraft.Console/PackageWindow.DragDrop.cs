using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DeployCraft.Server.Data;

namespace DeployCraft.Console;

/// <summary>
/// Glisser-déposer des étapes dans l'arbre : déposée sur la moitié haute d'une étape, elle passe avant ; sur la moitié
/// basse, après. Échap pendant le glissement annule.
/// </summary>
public partial class PackageWindow
{
    private const string StepMoveFormat = "DeployCraft.StepMove";
    private Point? _dragStart;
    private PackageStep? _dragged;
    private (UIElement Element, bool After, Adorner Adorner)? _insertion;

    private void SetupStepDragDrop()
    {
        Tree.AllowDrop = true;
        Tree.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragged = StepItemAt(e.OriginalSource)?.Tag as PackageStep;
            _dragStart = _dragged == null ? null : e.GetPosition(Tree);
        };
        Tree.PreviewMouseMove += Tree_PreviewMouseMove;
        Tree.DragOver += Tree_DragOver;
        Tree.DragLeave += (_, _) => ClearInsertion();
        Tree.Drop += Tree_Drop;
    }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start || _dragged is not { } step) return;
        var p = e.GetPosition(Tree);
        if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragStart = null;
        try
        {
            DragDrop.DoDragDrop(Tree, new DataObject(StepMoveFormat, step), DragDropEffects.Move);
        }
        finally
        {
            ClearInsertion();
        }
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(StepMoveFormat) is PackageStep moving && DropTarget(e) is var (target, header, after) && target != moving)
        {
            e.Effects = DragDropEffects.Move;
            ShowInsertion(header, after);
        }
        else ClearInsertion();
        e.Handled = true;
    }

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        ClearInsertion();
        if (e.Data.GetData(StepMoveFormat) is not PackageStep moving || DropTarget(e) is not var (target, _, after) || target == moving) return;
        Package.Steps.Remove(moving);
        Package.Steps.Insert(Package.Steps.IndexOf(target) + (after ? 1 : 0), moving);
        RebuildSteps(moving);
        e.Handled = true;
    }

    /// <summary>Étape survolée, son en-tête et le côté (après = moitié basse).</summary>
    private static (PackageStep Step, FrameworkElement Header, bool After)? DropTarget(DragEventArgs e)
    {
        if (StepItemAt(e.OriginalSource) is not { Tag: PackageStep step, Header: FrameworkElement header }) return null;
        return (step, header, e.GetPosition(header).Y > header.ActualHeight / 2);
    }

    private static TreeViewItem? StepItemAt(object source)
    {
        for (var d = source as DependencyObject; d != null;
             d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is TreeViewItem { Tag: PackageStep } item) return item;
        return null;
    }

    private void ShowInsertion(UIElement element, bool after)
    {
        if (_insertion is { } current && current.Element == element && current.After == after) return;
        ClearInsertion();
        if (AdornerLayer.GetAdornerLayer(element) is not { } layer) return;
        var adorner = new InsertionLine(element, after);
        layer.Add(adorner);
        _insertion = (element, after, adorner);
    }

    private void ClearInsertion()
    {
        if (_insertion is { } current) AdornerLayer.GetAdornerLayer(current.Element)?.Remove(current.Adorner);
        _insertion = null;
    }

    /// <summary>Trait de la couleur d'accent au-dessus ou au-dessous de l'étape : là où l'étape glissée sera posée.</summary>
    private sealed class InsertionLine : Adorner
    {
        private readonly bool _after;

        public InsertionLine(UIElement element, bool after) : base(element)
        {
            _after = after;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            dc.DrawRectangle((Brush)Application.Current.Resources["Accent"], null, new Rect(0, (_after ? size.Height : 0) - 1, size.Width, 2));
        }
    }
}
