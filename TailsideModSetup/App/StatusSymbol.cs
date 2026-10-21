using System.Windows;
using System.Windows.Media;

namespace UnifiedTools;

public enum StatusSymbolKind { Package, Information, Check, Update, Paused, Warning }

// All symbols share a 24-unit canvas and two-unit rounded strokes; no font glyphs.
public sealed class StatusSymbol : FrameworkElement
{
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(StatusSymbolKind),typeof(StatusSymbol),new FrameworkPropertyMetadata(StatusSymbolKind.Package,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty=DependencyProperty.Register(nameof(Stroke),typeof(Brush),typeof(StatusSymbol),new FrameworkPropertyMetadata(Brushes.SaddleBrown,FrameworkPropertyMetadataOptions.AffectsRender));
    public StatusSymbolKind Kind {get=>(StatusSymbolKind)GetValue(KindProperty);set=>SetValue(KindProperty,value);}
    public Brush Stroke {get=>(Brush)GetValue(StrokeProperty);set=>SetValue(StrokeProperty,value);}

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var scale=Math.Min(ActualWidth,ActualHeight)/24;
        if(scale<=0)return;
        drawing.PushTransform(new TranslateTransform((ActualWidth-24*scale)/2,(ActualHeight-24*scale)/2));
        drawing.PushTransform(new ScaleTransform(scale,scale));
        var pen=new Pen(Stroke,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        void Line(double x1,double y1,double x2,double y2)=>drawing.DrawLine(pen,new Point(x1,y1),new Point(x2,y2));
        void Shape(bool closed,params Point[] points){var geometry=new StreamGeometry();using(var context=geometry.Open()){context.BeginFigure(points[0],false,closed);context.PolyLineTo(points.Skip(1).ToArray(),true,false);}drawing.DrawGeometry(null,pen,geometry);}
        if(Kind is not (StatusSymbolKind.Package or StatusSymbolKind.Warning))drawing.DrawEllipse(null,pen,new Point(12,12),9,9);
        switch(Kind){
            case StatusSymbolKind.Package:
                Shape(true,new(12,2),new(21,7),new(21,17),new(12,22),new(3,17),new(3,7));
                Shape(false,new(3,7),new(12,12),new(21,7));Line(12,12,12,22);break;
            case StatusSymbolKind.Information:
                drawing.DrawEllipse(Stroke,null,new Point(12,7.5),1,1);Line(12,11,12,17);break;
            case StatusSymbolKind.Check:
                Shape(false,new(7,12),new(10.5,15.5),new(17,8.5));break;
            case StatusSymbolKind.Update:
                Line(12,17,12,7);Shape(false,new(8,11),new(12,7),new(16,11));break;
            case StatusSymbolKind.Paused:
                Line(8.5,8,8.5,16);Line(15.5,8,15.5,16);break;
            case StatusSymbolKind.Warning:
                Shape(true,new(12,2.5),new(22,21),new(2,21));Line(12,9,12,13.5);drawing.DrawEllipse(Stroke,null,new Point(12,17),1,1);break;
        }
        drawing.Pop();drawing.Pop();
    }
}
