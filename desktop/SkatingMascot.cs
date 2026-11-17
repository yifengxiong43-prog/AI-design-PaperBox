using System;
using System.Windows;
using System.Windows.Media;

namespace PaperStudio
{
    // The original character's face/cap is retained; limbs, tail and skateboard are articulated.
    public sealed class SkatingMascot : FrameworkElement
    {
        readonly ImageSource original;
        static readonly Brush Green=Paint("#7CC573"),Shirt=Paint("#242B29"),Blue=Paint("#267997"),Deck=Paint("#F0BC59"),Wheel=Paint("#5996AA");
        static readonly Pen Ink=Stroke("#202925",3),Detail=Stroke("#202925",1.6);
        static readonly Pen DeckAccent=Stroke("#CE634E",3);
        static readonly Geometry HeadClip=Shape("M55,8 L83,15 L88,3 Q110,-4 128,24 L120,40 Q126,47 121,58 Q119,73 98,75 Q71,75 62,62 L57,49 L59,30 L65,22 Z");
        static readonly Geometry Tail=Shape("M99,91 C132,113 153,76 142,64 C131,52 119,68 128,74 C134,77 138,71 133,68 C135,64 141,67 140,73 C137,89 120,99 104,84 Z");
        static readonly Geometry Torso=Shape("M66,71 Q86,78 105,72 L112,99 Q89,110 61,98 Z");
        static readonly Geometry Board=Shape("M15,137 Q10,134 9,129 Q17,132 27,134 L132,134 Q143,132 149,127 Q150,138 135,141 L27,141 Q19,141 15,137 Z");
        double phase,distance,velocity,landing;
        bool airborne,crashed,attack,guard,moving;
        internal double PushAmount { get { return moving&&!airborne&&!crashed?Math.Max(0,Math.Sin(phase*2*Math.PI)):0; } }
        // Negative locally: the existing horizontal flip makes wheels roll clockwise to the right.
        internal double WheelAngle { get { return moving?-distance*0.025:0; } }
        internal double BodyBob { get { return moving&&!airborne&&!crashed?Math.Sin(phase*4*Math.PI)*1.5:0; } }
        internal double BoardTilt { get { return crashed?-9:airborne?Math.Max(-13,Math.Min(13,-velocity/36)):0; } }
        public SkatingMascot(ImageSource source){original=source;IsHitTestVisible=false;}
        static Brush Paint(string color){var brush=(Brush)new BrushConverter().ConvertFromString(color);brush.Freeze();return brush;}
        static Pen Stroke(string color,double width){var pen=new Pen(Paint(color),width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};pen.Freeze();return pen;}
        static Geometry Shape(string path){var shape=Geometry.Parse(path);shape.Freeze();return shape;}
        public void Pose(double time,double travel,bool inAir,double verticalSpeed,double landingSeconds,bool firing,bool shielding,bool stopped,bool animate)
        {
            phase=time/1.15;distance=travel;airborne=inAir;velocity=verticalSpeed;landing=landingSeconds;
            attack=firing;guard=shielding;crashed=stopped;moving=animate;InvalidateVisual();
        }
        static void Limb(DrawingContext dc,Point a,Point b,Point c,Brush fill,double width)
        {
            var outline=new Pen(Ink.Brush,width+3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            var inside=new Pen(fill,width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            dc.DrawLine(outline,a,b);dc.DrawLine(outline,b,c);dc.DrawLine(inside,a,b);dc.DrawLine(inside,b,c);
        }
        void DrawWheel(DrawingContext dc,double x)
        {
            var center=new Point(x,151);dc.DrawEllipse(Wheel,Ink,center,7,7);
            double angle=WheelAngle;
            for(int i=0;i<3;i++){double a=angle+i*Math.PI*2/3;dc.DrawLine(Detail,center,new Point(x+Math.Cos(a)*5,151+Math.Sin(a)*5));}
            dc.DrawEllipse(Brushes.White,null,center,1.4,1.4);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.PushTransform(new ScaleTransform(ActualWidth/160,ActualHeight/160));
            double push=PushAmount,crouch=airborne?6:Math.Max(0,landing/0.16)*8;
            double bob=BodyBob+crouch;
            // Board pitch is shared by the rider, so feet stay planted during an ollie.
            dc.PushTransform(new RotateTransform(BoardTilt,80,137));
            dc.PushTransform(new RotateTransform(moving?Math.Sin(phase*2*Math.PI+0.5)*5:0,108,89));
            dc.PushTransform(new TranslateTransform(0,bob));dc.DrawGeometry(Green,Ink,Tail);dc.Pop();dc.Pop();
            // Far leg stays on deck; the nearer leg reaches the ground and sweeps backwards.
            Limb(dc,new Point(74,99+bob),new Point(59,114+bob/2),new Point(53,132),Blue,12);
            dc.DrawRoundedRectangle(Green,Ink,new Rect(42,128,22,7),3,3);
            double footX=91+push*48,footY=132+push*23;
            Limb(dc,new Point(95,98+bob),new Point(96+push*10,116+bob/2),new Point(footX,footY-3),Blue,12);
            dc.DrawRoundedRectangle(Green,Ink,new Rect(footX-9,footY-4,24,7),3,3);
            dc.PushTransform(new TranslateTransform(attack?3:0,bob));
            dc.PushTransform(new RotateTransform(crashed?12:push*-3,85,99));
            dc.DrawGeometry(Shirt,Ink,Torso);
            dc.DrawLine(new Pen(Brushes.WhiteSmoke,1.5),new Point(72,77),new Point(68,91));
            dc.DrawLine(new Pen(Brushes.WhiteSmoke,1.5),new Point(102,78),new Point(106,92));
            // Original eye, face and cap pixels, without the static board or surrounding speed doodles.
            dc.PushClip(HeadClip);dc.DrawImage(original,new Rect(0,0,160,160));dc.Pop();
            Point hand=guard?new Point(43,65):attack?new Point(34,84):new Point(45-push*5,82+push*7);
            Limb(dc,new Point(66,79),new Point(55,92),hand,Shirt,8);
            dc.DrawEllipse(Green,Detail,hand,8,5);
            Limb(dc,new Point(104,78),new Point(109+push*5,92),new Point(103+push*10,103),Shirt,8);
            dc.DrawEllipse(Green,Detail,new Point(103+push*10,103),5,7);
            dc.Pop();dc.Pop();
            dc.DrawLine(Ink,new Point(42,142),new Point(42,147));dc.DrawLine(Ink,new Point(120,142),new Point(120,147));
            dc.DrawGeometry(Deck,Ink,Board);
            dc.DrawLine(new Pen(Blue,3),new Point(24,137),new Point(60,137));
            dc.DrawLine(DeckAccent,new Point(83,137),new Point(126,137));
            DrawWheel(dc,42);DrawWheel(dc,120);
            dc.Pop();dc.Pop();
        }
    }
}
