using System;
using System.Windows;
using System.Windows.Media;

namespace PaperStudio
{
    public static class RunnerArt
    {
        static Brush Color(string value){return (Brush)new BrushConverter().ConvertFromString(value);}
        public static ImageSource Create(int kind)
        {
            var drawing=new DrawingGroup();var ink=new Pen(Color("#435A47"),1.5);
            using(var dc=drawing.Open())
            {
                // A common 64px artboard gives each obstacle a predictable hitbox.
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,64,64));
                if(kind==0||kind==1)
                {
                    for(int i=0;i<5;i++){
                        dc.PushTransform(new RotateTransform(i*72,32,32));
                        dc.DrawEllipse(Color(kind==0?"#F2A2AC":"#FFF9DC"),new Pen(Color(kind==0?"#BD6D78":"#B7A66B"),1),new Point(32,19),12,17);dc.Pop();
                    }
                    dc.DrawEllipse(Color("#E9BE54"),null,new Point(32,32),7,7);
                    if(kind==0){dc.DrawLine(new Pen(Color("#D1A34A"),2),new Point(32,32),new Point(48,13));for(int i=0;i<3;i++)dc.DrawEllipse(Color("#EDBB48"),null,new Point(45+i*3,13+i*2),2,2);}
                }
                else if(kind==2)
                {
                    for(int i=0;i<3;i++){
                        double x=15+i*14,y=8+(i%2)*9;
                        dc.DrawRoundedRectangle(Color("#82AA79"),ink,new Rect(x,y,8,56-y),3,3);
                        for(int j=0;j<3;j++)dc.DrawLine(ink,new Point(x,20+j*15),new Point(x+8,20+j*15));
                        dc.PushTransform(new RotateTransform(i==1?-40:35,x+4,26));dc.DrawEllipse(Color("#6A9768"),null,new Point(x+13,26),15,4);dc.Pop();
                    }
                }
                else if(kind==3)
                {
                    var body=Geometry.Parse("M5,42 Q17,43 24,30 L27,14 Q29,5 42,8 Q56,10 56,21 L45,23 L44,33 L53,35 L51,40 L42,37 L40,48 L44,57 L35,57 L31,48 L27,57 L18,57 L21,46 Q8,51 5,42 Z");
                    dc.DrawGeometry(Color("#9AB884"),ink,body);dc.DrawEllipse(Brushes.White,ink,new Point(42,15),4,4);dc.DrawEllipse(Color("#293D31"),null,new Point(43,15),1.6,2);
                    dc.DrawLine(ink,new Point(46,24),new Point(54,24));
                }
                else if(kind==4)
                {
                    dc.PushTransform(new RotateTransform(-30,32,32));
                    dc.DrawEllipse(Color("#93B786"),ink,new Point(32,32),20,28);dc.DrawLine(ink,new Point(32,9),new Point(32,60));
                    for(int i=0;i<3;i++){dc.DrawLine(new Pen(Brushes.White,3),new Point(14,20+i*12),new Point(26,25+i*10));dc.DrawLine(new Pen(Brushes.White,3),new Point(50,20+i*12),new Point(38,25+i*10));}dc.Pop();
                }
                else if(kind==5)
                {
                    dc.DrawRoundedRectangle(Color("#CFBC8E"),ink,new Rect(20,21,25,38),9,9);
                    for(int i=0;i<4;i++)dc.DrawLine(new Pen(Color("#AD9B72"),1),new Point(22,28+i*8),new Point(43,28+i*8));
                    dc.DrawGeometry(Color("#6E9867"),ink,Geometry.Parse("M32,25 L16,8 L28,15 L31,2 L36,16 L49,8 L40,26 Z"));
                }
                else if(kind==6)
                {
                    dc.PushTransform(new RotateTransform(18,32,32));
                    dc.DrawGeometry(Color("#F2D7AB"),ink,Geometry.Parse("M32,3 Q12,20 23,60 L41,60 Q52,20 32,3 Z"));
                    dc.DrawGeometry(Color("#79B8B1"),null,Geometry.Parse("M30,7 L27,60 L35,60 L34,7 Z"));
                    dc.DrawLine(new Pen(Color("#D17D66"),3),new Point(21,40),new Point(43,40));dc.Pop();
                }
                else if(kind==7)
                {
                    dc.DrawGeometry(Color("#B89669"),ink,Geometry.Parse("M27,61 Q38,40 31,22 L37,22 Q45,44 36,61 Z"));
                    for(int i=0;i<4;i++)dc.DrawLine(ink,new Point(32,32+i*7),new Point(39,34+i*7));
                    dc.DrawGeometry(Color("#73A283"),ink,Geometry.Parse("M34,23 Q10,1 3,28 Q19,17 34,23 Q15,12 16,5 Q31,6 34,23 Q38,0 53,5 Q40,12 34,23 Q60,3 62,29 Q48,17 34,23 Q51,25 53,41 Q42,28 34,23 Z"));
                    dc.DrawEllipse(Color("#987654"),ink,new Point(30,25),4,5);dc.DrawEllipse(Color("#B28C5B"),ink,new Point(38,26),4,5);
                }
            }
            drawing.Freeze();return new DrawingImage(drawing);
        }
        public static ImageSource Marker()
        {
            var drawing=new DrawingGroup();var ink=new Pen(Color("#3F4440"),0.8);
            using(var dc=drawing.Open())
            {
                dc.DrawRoundedRectangle(Color("#FAFAF5"),ink,new Rect(3,3,64,12),3,3);
                dc.DrawRoundedRectangle(Color("#88B7A4"),ink,new Rect(0,2,14,14),2,2);
                dc.DrawRoundedRectangle(Color("#88B7A4"),ink,new Rect(61,2,8,14),2,2);
                dc.DrawGeometry(Color("#3F4440"),null,Geometry.Parse("M69,5 L76,8 L76,11 L69,14 Z"));
                dc.DrawText(new FormattedText("Ohuhu",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),9,Color("#303A34"),1.0),new Point(22,2));
            }
            drawing.Freeze();return new DrawingImage(drawing);
        }
        public static ImageSource Rocket()
        {
            var drawing=new DrawingGroup();var ink=new Pen(Color("#805C50"),1);
            using(var dc=drawing.Open())
            {
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,40,18));
                dc.DrawGeometry(Color("#E6B763"),null,Geometry.Parse("M30,6 L39,9 L30,12 L33,9 Z"));
                dc.DrawGeometry(Color("#D49A83"),ink,Geometry.Parse("M22,5 L28,1 L29,7 L29,11 L28,17 L22,13 Z"));
                dc.DrawRoundedRectangle(Color("#F0D6B3"),ink,new Rect(9,5,22,8),3,3);
                dc.DrawGeometry(Color("#C97D6D"),ink,Geometry.Parse("M1,9 Q6,4 12,5 L12,13 Q6,14 1,9 Z"));
                dc.DrawLine(new Pen(Color("#B88765"),1.5),new Point(23,6),new Point(23,12));
            }
            drawing.Freeze();return new DrawingImage(drawing);
        }
        public static ImageSource Shield()
        {
            var drawing=new DrawingGroup();var edge=new Pen(Color("#578F7D"),2);
            using(var dc=drawing.Open())
            {
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,28,54));
                dc.DrawEllipse(Color("#668FC7B4"),edge,new Point(14,27),12,25);
                dc.DrawEllipse(null,new Pen(Color("#B5DDD0"),1),new Point(14,27),8,20);
                dc.DrawGeometry(null,new Pen(Color("#477E6D"),2),Geometry.Parse("M10,18 L18,27 L10,36"));
            }
            drawing.Freeze();return new DrawingImage(drawing);
        }
    }
}
