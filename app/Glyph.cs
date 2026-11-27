using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace TwinShift
{
    public sealed class Glyph : FrameworkElement
    {
        static readonly Dictionary<string, Geometry> Shapes = new Dictionary<string, Geometry>();
        static Glyph()
        {
            Add("radio", "M3,8 L5,6 20,6 21,8 21,18 19,20 4,20 3,18 Z M6,6 L17,2 M6,10 H12 M6,13 H10 M17,11 A2.5,2.5 0 1 1 16.99,11");
            Add("heart", "M12,20 C10,18 3,13 3,8 C3,3 9,2 12,7 C15,2 21,3 21,8 C21,13 15,18 12,20 Z");
            Add("broken", "M12,20 C10,18 3,13 3,8 C3,3 9,2 12,7 C15,2 21,3 21,8 C21,13 15,18 12,20 Z M13,6 L10,11 14,12 11,16");
            Add("pipes", "M2,19 H11 V9 H21 M2,15 H7 V5 H21 M15,3 V11 M4,13 V21");
            Add("lamp", "M9,3 H15 L14,6 17,15 14,18 16,21 H8 L10,18 7,15 10,6 Z M10,6 H14 M10,18 H14 M11,12 C10,10 13,9 14,12 C15,15 11,15 11,12");
            Add("crt", "M3,5 H20 V15 H3 Z M5,7 H12 V12 H5 Z M15,8 H18 M15,11 H18 M4,15 L2,19 H22 L20,15 M6,17 H15");
            Add("shield", "M12,2 L20,5 V11 C20,15 17,19 12,22 C7,19 4,15 4,11 V5 Z M12,7 V13 M12,16 V17");
            Add("audio", "M3,10 H7 L12,5 V19 L7,14 H3 Z M15,8 C18,10 18,14 15,16 M18,5 C23,8 23,16 18,19");
            Add("activity", "M2,13 H7 L10,5 14,20 17,11 H22");
            Add("plug", "M8,2 V8 M16,2 V8 M5,8 H19 V11 C19,15 16,17 12,17 C8,17 5,15 5,11 Z M12,17 V22");
            Add("screen", "M3,4 H21 V17 H3 Z M12,17 V21 M8,21 H16");
            Add("star", "M12,2 L14,9 21,11 14,14 12,21 10,14 3,11 10,9 Z");
            Add("headphones", "M4,14 V11 A8,8 0 0 1 20,11 V15 M3,13 H7 V20 H3 Z M17,13 H21 V20 H17 Z");
        }

        static void Add(string name, string path)
        {
            var g = Geometry.Parse(path);
            g.Freeze();
            Shapes[name] = g;
        }

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Glyph), new FrameworkPropertyMetadata("heart", FrameworkPropertyMetadataOptions.AffectsRender));
        public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Glyph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        protected override void OnRender(DrawingContext dc)
        {
            if (!Shapes.TryGetValue(Kind ?? "", out var geometry))
                return;
            var pen = new Pen(Stroke ?? TryFindResource("Accent") as Brush ?? Brushes.Gray, 1.6)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            double scale = Math.Min(ActualWidth, ActualHeight) / 24;
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(null, pen, geometry);
            dc.Pop();
        }
    }
}
