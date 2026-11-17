using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PaperStudio
{
    // A loading companion only. Game state never controls the generation task.
    public sealed class LoadingRunner : IDisposable
    {
        readonly Window owner;
        readonly Canvas track;
        readonly TextBlock scoreLabel,hint;
        readonly SkatingMascot mascot;
        readonly Image obstacle;
        readonly Image marker;
        readonly Image rocket,shield;
        readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
        readonly Random random=new Random();
        readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
        bool active,crashed,shooting,rocketActive,reflected,enemyFired;
        double y,velocity,x,distance,shotX,shotY,cooldown,rocketX,rocketY,shieldTime,shieldCooldown;
        int score,best,cleared,hits,kind=-1;
        double obstacleWidth=40,obstacleHeight=40;
        double animationTime,landingTime;
        public LoadingRunner(Window owner,Canvas track,TextBlock scoreLabel,TextBlock hint,Stream sprite)
        {
            this.owner=owner;this.track=track;this.scoreLabel=scoreLabel;this.hint=hint;
            var bitmap=new BitmapImage();using(sprite){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=sprite;bitmap.EndInit();bitmap.Freeze();}
            var ground=new Rectangle{Height=1,Fill=new SolidColorBrush(Color.FromRgb(234,234,234))};
            Canvas.SetTop(ground,172);track.Children.Add(ground);track.SizeChanged+=delegate{ground.Width=track.ActualWidth;};
            mascot=new SkatingMascot(bitmap){Width=74,Height=76,RenderTransformOrigin=new Point(0.5,0.5),RenderTransform=new ScaleTransform(-1,1)};track.Children.Add(mascot);Canvas.SetLeft(mascot,32);
            obstacle=new Image{IsHitTestVisible=false,Stretch=Stretch.Fill};track.Children.Add(obstacle);ChooseObstacle();
            marker=new Image{Source=RunnerArt.Marker(),Width=76,Height=18,IsHitTestVisible=false,Visibility=Visibility.Collapsed};track.Children.Add(marker);
            rocket=new Image{Source=RunnerArt.Rocket(),Width=40,Height=18,IsHitTestVisible=false,Visibility=Visibility.Collapsed,RenderTransformOrigin=new Point(0.5,0.5)};track.Children.Add(rocket);
            shield=new Image{Source=RunnerArt.Shield(),Width=28,Height=54,IsHitTestVisible=false,Visibility=Visibility.Collapsed};track.Children.Add(shield);Canvas.SetLeft(shield,87);
            track.MouseLeftButtonDown+=Click;track.KeyDown+=Key;
            owner.Activated+=Activated;owner.Deactivated+=Deactivated;timer.Tick+=Tick;Draw();
        }
        public void SetActive(bool value)
        {
            active=value;
            if(value){Reset();if(owner.IsActive){clock.Restart();timer.Start();}}
            else{timer.Stop();clock.Stop();shooting=rocketActive=false;shieldTime=0;marker.Visibility=rocket.Visibility=shield.Visibility=Visibility.Collapsed;if(track.IsKeyboardFocused)Keyboard.ClearFocus();Draw();}
        }
        void ChooseObstacle()
        {
            int next=random.Next(3)==0?8+random.Next(2):random.Next(8);
            if(next==kind)next=kind>=8?8+(kind-7)%2:(kind+1)%8;
            SetObstacle(next);
        }
        bool LargeObstacle {get{return kind>=8;}}
        void SetObstacle(int value)
        {
            enemyFired=rocketActive=reflected=false;
            kind=value;obstacle.Source=RunnerArt.Create(kind==8?6:kind==9?7:kind);
            obstacleWidth=kind==8?88:kind==9?128:kind==7?64:kind==2?38:kind==3?50:44;obstacleHeight=LargeObstacle?140:kind==7?70:kind==6?62:kind==2?58:kind==3?50:44;
            obstacle.Width=obstacleWidth;obstacle.Height=obstacleHeight;Canvas.SetTop(obstacle,172-obstacleHeight);
        }
        double Speed {get{return Math.Min(880,480+cleared*32);}}
        void PositionObstacle()
        {
            x=Math.Max(360,track.ActualWidth)+random.Next(0,80);
            // A mandatory shot must be available before impact, even in a narrow, fast game.
            if(LargeObstacle)x=Math.Max(x,94+Speed*(cooldown+0.9));
        }
        void Reset(){y=velocity=distance=cooldown=shieldTime=shieldCooldown=animationTime=landingTime=0;score=cleared=hits=0;crashed=shooting=false;ChooseObstacle();PositionObstacle();Draw();}
        void Click(object sender,MouseButtonEventArgs e){track.Focus();Draw();}
        void Fire()
        {
            if(!active||crashed||cooldown>0||shooting)return;
            shooting=true;shotX=94;shotY=138-y;cooldown=2.8;Draw();
        }
        void RaiseShield()
        {
            if(!active||crashed||shieldCooldown>0)return;
            shieldTime=0.45;shieldCooldown=1.4;Draw();
        }
        void Key(object sender,KeyEventArgs e)
        {
            if(!active)return;
            if(e.Key==System.Windows.Input.Key.Escape){Keyboard.ClearFocus();e.Handled=true;return;}
            if(e.Key==System.Windows.Input.Key.Right){if(!e.IsRepeat)Fire();e.Handled=true;return;}
            if(e.Key==System.Windows.Input.Key.Down){if(!e.IsRepeat)RaiseShield();e.Handled=true;return;}
            if(e.Key!=System.Windows.Input.Key.Space||e.IsRepeat)return;
            if(crashed)Reset();
            if(y==0)velocity=490;
            e.Handled=true;
        }
        void Activated(object sender,EventArgs e){if(active){clock.Restart();timer.Start();}}
        void Deactivated(object sender,EventArgs e){timer.Stop();clock.Stop();}
        void Tick(object sender,EventArgs e)
        {
            double dt=Math.Min(0.04,clock.Elapsed.TotalSeconds);clock.Restart();Advance(dt);
        }
        void Advance(double dt)
        {
            if(crashed)return;
            animationTime+=dt*Math.Min(1.6,Speed/480);landingTime=Math.Max(0,landingTime-dt);
            cooldown=Math.Max(0,cooldown-dt);
            shieldTime=Math.Max(0,shieldTime-dt);shieldCooldown=Math.Max(0,shieldCooldown-dt);
            double oldY=y;y=Math.Max(0,y+velocity*dt);velocity-=1400*dt;if(y==0){velocity=0;if(oldY>0)landingTime=0.16;}
            double oldX=x,oldShotX=shotX;
            x-=Speed*dt;distance+=Speed*dt;
            bool hit=false;
            if(shooting){
                shotX+=1040*dt;
                // Swept horizontal collision prevents a fast pen from passing through an obstacle between frames.
                hit=oldShotX<=oldX+obstacleWidth&&shotX+76>=x&&shotY+18>172-obstacleHeight&&shotY<172;
                if(hit){shooting=false;hits++;}
                else if(shotX>Math.Max(360,track.ActualWidth)+80)shooting=false;
            }
            if(!hit&&rocketActive){
                double oldRocket=rocketX;rocketX+=(reflected?1550:-1300)*dt;
                if(reflected){
                    if(oldRocket<=oldX+obstacleWidth&&rocketX+40>=x&&rocketY+18>172-obstacleHeight&&rocketY<172){hit=true;hits++;rocketActive=false;}
                }else{
                    bool shieldOverlap=rocketY+18>110-y&&rocketY<164-y;
                    if(shieldTime>0&&shieldOverlap&&rocketX<=108&&oldRocket+40>=87){reflected=true;rocketX=Math.Max(104,rocketX);shieldTime=0;}
                    else if(rocketX<94&&oldRocket+40>45&&rocketY+18>104-y&&rocketY<172-y){crashed=true;}
                }
                if(rocketX+40<0||rocketX>Math.Max(360,track.ActualWidth)+160)rocketActive=false;
            }
            if(hit||x+obstacleWidth<0){cleared++;ChooseObstacle();PositionObstacle();}
            // Fire only after entering view, with enough distance to see and react to the missile.
            if(!crashed&&!hit&&LargeObstacle&&!enemyFired&&x<Math.Max(360,track.ActualWidth)-20&&x>300){enemyFired=rocketActive=true;reflected=false;rocketX=x;rocketY=138;}
            score=(int)(distance/35)+cleared*25+hits*50;best=Math.Max(best,score);
            // The transparent border of the original sprite is excluded from its collision box.
            if(x+5<94&&x+obstacleWidth-5>45&&y<obstacleHeight-6){crashed=true;shooting=false;}
            if(crashed){shooting=rocketActive=false;shieldTime=0;}
            Draw();
        }
        void Draw()
        {
            Canvas.SetTop(mascot,96-y);Canvas.SetLeft(obstacle,x);
            mascot.Pose(animationTime,distance,y>0,velocity,landingTime,cooldown>2.6,shieldTime>0,crashed,active);
            if(marker!=null){marker.Visibility=shooting?Visibility.Visible:Visibility.Collapsed;Canvas.SetLeft(marker,shotX);Canvas.SetTop(marker,shotY);}
            if(rocket!=null){rocket.Visibility=rocketActive?Visibility.Visible:Visibility.Collapsed;Canvas.SetLeft(rocket,rocketX);Canvas.SetTop(rocket,rocketY);rocket.RenderTransform=new ScaleTransform(reflected?-1:1,1);}
            if(shield!=null){shield.Visibility=shieldTime>0?Visibility.Visible:Visibility.Collapsed;Canvas.SetTop(shield,110-y);}
            scoreLabel.Text="得分 "+score.ToString("D4")+"  ·  最佳 "+best.ToString("D4");
            hint.Text=crashed?"碰到了！空格重来 · 本次 "+score+" 分，设计仍在生成":(track.IsKeyboardFocused?"":"点击跑道 · ")+"空格跳 · → 马克笔"+(cooldown>0?"（"+cooldown.ToString("0.0")+"s）":"（就绪）")+" · ↓ 弹反"+(shieldCooldown>0?"（"+shieldCooldown.ToString("0.0")+"s）":"（就绪）")+(LargeObstacle?" · 巨型障碍须击破":"")+" · Esc 退出";
        }
#if QA
        public void CheckGame()
        {
            SetActive(true);timer.Stop();SetObstacle(0);x=100000;
            Advance(0.1);double push=mascot.PushAmount,wheels=mascot.WheelAngle;Advance(0.1);
            if(mascot.PushAmount==push||mascot.WheelAngle==wheels||y!=0)throw new Exception("Skating must articulate limbs and wheels without changing physics height");
            velocity=490;Advance(0.02);if(mascot.PushAmount!=0||mascot.BoardTilt==0)throw new Exception("Jump pose must stop pushing and pitch the board");
            y=0.1;velocity=-200;Advance(0.02);if(landingTime<=0||y!=0)throw new Exception("Landing compression not triggered");
            y=0;velocity=0;x=60;Advance(0);double stopped=animationTime;Advance(0.2);if(animationTime!=stopped||!crashed)throw new Exception("Crash must freeze animation");
            Reset();Deactivated(owner,EventArgs.Empty);if(timer.IsEnabled||clock.IsRunning)throw new Exception("Unfocused animation must pause");
            Activated(owner,EventArgs.Empty);if(!timer.IsEnabled)throw new Exception("Active loading animation must resume");timer.Stop();SetActive(false);
            SetActive(true);timer.Stop();x=200;velocity=510;Advance(0.02);if(y<=0)throw new Exception("Runner jump failed");
            y=0;velocity=0;x=60;Advance(0);if(!crashed)throw new Exception("Runner collision failed");
            Reset();if(crashed||score!=0)throw new Exception("Runner restart failed");SetActive(false);if(timer.IsEnabled)throw new Exception("Runner timer leaked after stop");
            int old=kind;ChooseObstacle();if(kind==old||Speed<480)throw new Exception("Obstacle variety or initial speed failed");cleared=12;if(Speed<850)throw new Exception("Runner difficulty ramp failed");Reset();
            Fire();if(shooting)throw new Exception("Inactive runner fired");
            SetActive(true);timer.Stop();SetObstacle(7);x=210;Fire();Advance(0.04);
            if(hits!=1||cleared!=1||score<75||shooting||crashed)throw new Exception("Marker hit, reward or swept collision failed");
            Fire();if(shooting)throw new Exception("Attack cooldown failed");
            x=1000;Advance(2.8);if(cooldown!=0)throw new Exception("Attack cooldown did not recover");
            Reset();SetObstacle(6);x=210;y=85;Fire();Advance(0.04);if(hits!=0||!shooting)throw new Exception("Airborne marker must miss lower obstacles");
            shotX=Math.Max(360,track.ActualWidth)+90;Advance(0);if(shooting)throw new Exception("Offscreen marker was not removed");
            Reset();if(shooting||cooldown!=0||hits!=0)throw new Exception("Restart retained attack state");
            Fire();SetActive(false);if(shooting||marker.Visibility!=Visibility.Collapsed)throw new Exception("Stopped runner retained projectile");
            SetActive(true);timer.Stop();
            var right=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(owner),0,System.Windows.Input.Key.Right){RoutedEvent=Keyboard.KeyDownEvent};Key(track,right);
            if(!right.Handled||!shooting)throw new Exception("Right arrow does not fire");
            Reset();var oldKey=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(owner),0,System.Windows.Input.Key.J){RoutedEvent=Keyboard.KeyDownEvent};Key(track,oldKey);
            if(shooting)throw new Exception("Old J attack shortcut retained");
            foreach(double step in new[]{0.016,0.04}){
                Reset();SetObstacle(8);x=100000;velocity=490;double peak=0;
                for(int i=0;i<60;i++){Advance(step);peak=Math.Max(peak,y);}
                if(peak>=obstacleHeight-6)throw new Exception("Giant obstacle is jumpable");
                y=peak;x=60;Advance(0);if(!crashed)throw new Exception("Jump bypassed giant obstacle");
            }
            foreach(int giant in new[]{8,9}){
                Reset();SetObstacle(giant);x=210;Fire();Advance(0.04);
                if(hits!=1||crashed)throw new Exception("Giant cannot be destroyed by marker");
                cleared=20;cooldown=2.8;SetObstacle(giant);PositionObstacle();
                if((x-94)/Speed<cooldown+0.89)throw new Exception("Giant arrives before attack cooldown and reaction time");
            }
            if(((ScaleTransform)mascot.RenderTransform).ScaleX!=-1)throw new Exception("Mascot must face incoming obstacles");
            Reset();SetObstacle(9);x=330;Advance(0);if(!rocketActive||!enemyFired)throw new Exception("Giant did not fire visible missile");
            Reset();SetObstacle(9);x=500;enemyFired=rocketActive=true;rocketX=120;rocketY=138;
            Advance(0.04);if(!crashed)throw new Exception("Unblocked missile did not collide");
            Reset();SetObstacle(9);x=500;enemyFired=rocketActive=true;rocketX=120;rocketY=138;
            var down=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(owner),0,System.Windows.Input.Key.Down){RoutedEvent=Keyboard.KeyDownEvent};Key(track,down);Advance(0.04);
            if(!down.Handled||!reflected||!rocketActive||crashed)throw new Exception("Down arrow failed to parry");
            RaiseShield();if(shieldTime>0)throw new Exception("Shield cooldown bypassed");
            for(int i=0;i<10&&hits==0;i++)Advance(0.02);
            if(hits!=1||crashed)throw new Exception("Reflected missile did not destroy its source");
            Reset();SetObstacle(0);x=10000;RaiseShield();for(int i=0;i<13;i++)Advance(0.04);
            if(shieldTime!=0||shieldCooldown<=0)throw new Exception("Shield duration or cooldown failed");
            enemyFired=rocketActive=true;rocketX=120;rocketY=138;Advance(0.04);if(!crashed)throw new Exception("Expired shield still protected player");
            Reset();SetObstacle(9);x=500;y=85;enemyFired=rocketActive=true;rocketX=120;rocketY=138;Advance(0.04);
            if(crashed||reflected)throw new Exception("Jumping cannot dodge a low missile");
            RaiseShield();SetActive(false);if(rocketActive||shieldTime!=0||rocket.Visibility!=Visibility.Collapsed||shield.Visibility!=Visibility.Collapsed)throw new Exception("Combat visuals leaked after loading finished");
            Reset();SetActive(false);
        }
        public void PreviewAttack(int obstacleKind)
        {
            timer.Stop();SetObstacle(obstacleKind);x=track.ActualWidth*0.65;Fire();shotX=track.ActualWidth*0.4;Draw();
        }
        public void PreviewShield()
        {
            Reset();timer.Stop();SetObstacle(9);x=track.ActualWidth*0.7;enemyFired=rocketActive=true;rocketX=150;rocketY=138;RaiseShield();Draw();
        }
#endif
        public void Dispose(){timer.Stop();clock.Stop();timer.Tick-=Tick;owner.Activated-=Activated;owner.Deactivated-=Deactivated;track.MouseLeftButtonDown-=Click;track.KeyDown-=Key;}
    }
}
