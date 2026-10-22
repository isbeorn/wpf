extern alias WpfFork;
using System.Diagnostics;
using System.Text.Json;
using Wpf.Portable.NativeHost;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Threading;

internal static class SplashChecks
{
 internal static int Unregistered()
 {
  bool passed=false;string detail="";
  try { new SplashScreen("splash.png").Show(false); }
  catch(PlatformNotSupportedException error) { passed=error.Message.Contains("registered",StringComparison.Ordinal);detail=error.Message; }
  catch(Exception error) { detail=error.ToString(); }
  finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
  Console.WriteLine(JsonSerializer.Serialize(new{probe="unregistered-native-splash",checks=new[]{new{name="unregistered-splash-backend-is-explicit",status=passed?"pass":"fail",detail}}}));
  return passed?0:1;
 }
 internal static int Run(string[] args)
 {
  var checks=new List<object>();bool failed=false;var observations=new List<object>();
  using var native=new X11Fixture("little");
  int dpi=int.Parse(args.FirstOrDefault(value=>value.StartsWith("--dpi=",StringComparison.Ordinal))?[6..]??"96",System.Globalization.CultureInfo.InvariantCulture);
  native.SetDpi(dpi);
  using var sentinel=native.CreateFocusSentinel(out IntPtr focusSentinel);
  NativeWindowHost.InitializeForCurrentThread();
  Check("splash-before-Application-resource-pixels-and-duplicate",()=>
  {
   Require(WpfFork::System.Windows.Application.Current==null,"Application already exists.");
   var splash=new SplashScreen("SPLASH.PNG");
   try
   {
    IntPtr focusBeforeShow=native.FocusedWindow;Require(focusBeforeShow==focusSentinel,"Owned sentinel was not focused before Show.");
    splash.Show(false);IntPtr window=Only();PumpUntil(()=>native.IsMapped(window));var bounds=native.Bounds(window);
    Require(native.FocusedWindow==focusBeforeShow,"Initial Show activated the splash.");
    Require(native.HasZeroUserTime(window),"Splash omitted its native nonactivation hint.");
    Require(bounds.Width==8&&bounds.Height==6,"Splash changed source pixel dimensions.");
    Require(native.Pixel(window,0,0,true)==0xffff0000,"Splash opaque pixel mismatch.");
    Require(native.Pixel(window,3,2,true)==0,"Splash transparent pixel mismatch.");
    Require(native.Pixel(window,7,5,true)==0x80000080,"Splash premultiplied alpha mismatch.");
    Require(native.SkipsTaskbar(window)&&!native.IsAbove(window),"Default native window hints mismatch.");
    splash.Show(true,true);Require(Only()==window&&!native.IsAbove(window),"Duplicate Show changed identity or topmost.");
    var automatic=new SplashScreen("splash.png");
    try
    {
     automatic.Show(true);IntPtr automaticWindow=native.SplashWindows.Single(candidate=>candidate!=window);
     PumpUntil(()=>!native.SplashWindows.Contains(automaticWindow));
     Require(Only()==window&&native.IsMapped(window)&&!native.IsAbove(window),"Duplicate Show enabled autoclose or changed the manual splash.");
     Require(native.Pixel(window,0,0,true)==0xffff0000,"Duplicate Show started fading the manual splash.");
     Require(native.FocusedWindow==focusBeforeShow,"Show or dispatcher painting changed the owned sentinel focus.");
     observations.Add(new{kind="duplicate-autoclose-control-disappeared",manualWindow=window.ToInt64(),automaticWindow=automaticWindow.ToInt64(),focusBefore=focusBeforeShow.ToInt64(),focusAfter=native.FocusedWindow.ToInt64()});
    }
    finally{automatic.Close(TimeSpan.Zero);}
    var screen=native.ScreenSize;
    Require(bounds.X==(screen.Width-8)/2&&bounds.Y==(screen.Height-6)/2,"Splash is not centered in actual physical primary bounds.");
    observations.Add(new{kind="physical-splash",bounds.X,bounds.Y,bounds.Width,bounds.Height,configuredDpi=dpi,rootWidth=screen.Width,rootHeight=screen.Height,zeroUserTime=native.HasZeroUserTime(window),focus=native.FocusedWindow.ToInt64()});
   }
   finally{splash.Close(TimeSpan.Zero);}PumpUntil(()=>native.SplashWindows.Count==0);
  });
  Check("splash-resource-errors-escaped-name-and-retry",()=>
  {
   Expect<IOException>(()=>new SplashScreen(typeof(SplashChecks).Assembly,"missing.png").Show(false));
   bool rejected=false;try{new SplashScreen(typeof(SplashChecks).Assembly,"corrupt.png").Show(false);}catch(IOException){rejected=true;}
   Require(rejected&&native.SplashWindows.Count==0,"Invalid encoded resource leaked or was accepted.");
   var splash=new SplashScreen(typeof(SplashChecks).Assembly,"spaced name.png");
   try{splash.Show(false,true);Require(native.IsAbove(Only()),"Requested topmost hint missing.");splash.Close(TimeSpan.FromMilliseconds(-1));PumpUntil(()=>native.SplashWindows.Count==0);splash.Show(false);Require(!native.IsAbove(Only()),"Reopened default splash kept topmost.");}
   finally{splash.Close(TimeSpan.Zero);splash.Close(TimeSpan.Zero);}
  });
  Check("splash-stock-queued-Loaded-autoclose-after-reopen",()=>
  {
   var splash=new SplashScreen("splash.png");try{splash.Show(true);splash.Close(TimeSpan.Zero);splash.Show(false);Require(native.SplashWindows.Count==1,"Splash did not reopen.");PumpUntil(()=>native.SplashWindows.Count==0);}
   finally{splash.Close(TimeSpan.Zero);}
  });
  Check("splash-positive-fade-reclose-and-interrupt",()=>
  {
   var splash=new SplashScreen("splash.png");try
   {
    splash.Show(false);IntPtr window=Only();IntPtr focus=native.FocusedWindow;splash.Close(TimeSpan.FromMilliseconds(450));splash.Close(TimeSpan.FromSeconds(10));
    PumpUntil(()=>native.SplashWindows.Count==0||native.Pixel(window,0,0,true)!=0xffff0000);
    Require(native.SplashWindows.Count==1,"Fade skipped every intermediate frame.");
    uint pixel=native.Pixel(window,0,0,true);uint alpha=pixel>>24;Require(alpha>0&&alpha<255&&(pixel&0xff0000)==alpha<<16,"Fade did not preserve premultiplied pixels.");
    Require(native.FocusedWindow==focus,"Fade activated the splash.");
    observations.Add(new{kind="intermediate-native-alpha",pixel,alpha});splash.Close(TimeSpan.Zero);PumpUntil(()=>native.SplashWindows.Count==0);
    splash.Show(false);splash.Close(TimeSpan.FromMilliseconds(100));splash.Close(TimeSpan.FromSeconds(10));PumpUntil(()=>native.SplashWindows.Count==0);
   }
   finally{splash.Close(TimeSpan.Zero);}
  });
  Check("splash-worker-Close-owner-dispatcher",()=>
  {
   var splash=new SplashScreen("splash.png");splash.Show(false);Task close=Task.Run(()=>splash.Close(TimeSpan.Zero));PumpUntil(()=>close.IsCompleted);close.GetAwaiter().GetResult();PumpUntil(()=>native.SplashWindows.Count==0);
  });
  Check("splash-finalizer-and-dispatcher-shutdown-cleanup",()=>
  {
   WeakReference forgotten=Forget();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();PumpUntil(()=>native.SplashWindows.Count==0);Require(!forgotten.IsAlive,"Forgotten SplashScreen was retained.");
   var splash=new SplashScreen("splash.png");splash.Show(false);Dispatcher.CurrentDispatcher.InvokeShutdown();Require(native.SplashWindows.Count==0,"Dispatcher shutdown leaked native splash.");GC.KeepAlive(splash);
  });
  Check("splash-post-shutdown-creation-is-rejected",()=>
  {
   var splash=new SplashScreen("splash.png");bool rejected=false;
   try
   {
    try{splash.Show(false);}
    catch(InvalidOperationException error){rejected=error.Message.Contains("shutdown",StringComparison.OrdinalIgnoreCase);}
   }
   finally{splash.Close(TimeSpan.Zero);}
   Require(rejected&&native.SplashWindows.Count==0,"A native splash was accepted after owner dispatcher shutdown.");
  });
  Console.WriteLine(JsonSerializer.Serialize(new{probe="portable-native-splash",scope="Owned Xvfb raw ARGB storage and native window lifecycle, no compositor appearance or desktop activation claim",observations,checks},new JsonSerializerOptions{WriteIndented=true}));return failed?1:0;
  IntPtr Only(){var windows=native.SplashWindows;Require(windows.Count==1,"Expected exactly one actual splash window.");return windows[0];}
  void Check(string name,Action action){try{action();checks.Add(new{name,status="pass",detail="Actual public SplashScreen and native observables passed."});}catch(Exception error){failed=true;checks.Add(new{name,status="fail",detail=error.ToString()});}}
 }
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static WeakReference Forget(){var splash=new SplashScreen("splash.png");splash.Show(false);return new WeakReference(splash);}
 static void PumpUntil(Func<bool> condition)
 {
  var frame=new DispatcherFrame();var watch=Stopwatch.StartNew();Exception? failure=null;var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(5)};
  timer.Tick+=(_,_)=>{try{if(condition()){frame.Continue=false;return;}if(watch.Elapsed>TimeSpan.FromSeconds(4))throw new TimeoutException("Native splash observable did not arrive.");}catch(Exception error){failure=error;frame.Continue=false;}};
  timer.Start();try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}if(failure!=null)throw failure;
 }
 static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 static void Expect<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
}
