using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Graphics.Imaging;
class Presence {
 static volatile bool stopping;
 static JavaScriptSerializer json=new JavaScriptSerializer();
 static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op) { var watch=Stopwatch.StartNew(); while(op.Status==Windows.Foundation.AsyncStatus.Started) { if(watch.Elapsed.TotalSeconds>20){op.Cancel();throw new TimeoutException();} Thread.Sleep(20); } return op.GetResults(); }
 static void Wait(Windows.Foundation.IAsyncAction op) { var watch=Stopwatch.StartNew(); while(op.Status==Windows.Foundation.AsyncStatus.Started) { if(watch.Elapsed.TotalSeconds>20){op.Cancel();throw new TimeoutException();} Thread.Sleep(20); } op.GetResults(); }
 static void Emit(object value){Console.WriteLine(json.Serialize(value));}
 static int Main(string[] args) { try { Run(args);return 0; }catch(Exception e){Emit(new {type="error",message=e.ToString()});return 1;} }
 static void Run(string[] args) {
  int seconds=args.Length==0?30:Int32.Parse(args[0]);
  new Thread(delegate(){Console.ReadLine();stopping=true;}) {IsBackground=true}.Start();
  if(args.Length>=4 && !ToFGate.Confirm(Int32.Parse(args[1]),Int32.Parse(args[2]),Int32.Parse(args[3]),()=>stopping,Emit))return;
  if(stopping)return;
  // Drop the completed WinRT ToF client before acquiring the camera.
  if(args.Length>=4){GC.Collect();GC.WaitForPendingFinalizers();}
  var groups=Wait(MediaFrameSourceGroup.FindAllAsync());
  var group=groups.FirstOrDefault(g=>g.SourceInfos.Count==1 && g.SourceInfos.Any(s=>s.SourceKind==MediaFrameSourceKind.Infrared));
  if(group==null)throw new Exception("No dedicated infrared group.");
  if(!FaceDetector.IsSupported)throw new Exception("FaceDetector unavailable.");
  var detector=Wait(FaceDetector.CreateAsync());
  using(var cap=new MediaCapture()){
   Wait(cap.InitializeAsync(new MediaCaptureInitializationSettings {SourceGroup=group,SharingMode=MediaCaptureSharingMode.SharedReadOnly,MemoryPreference=MediaCaptureMemoryPreference.Cpu,StreamingCaptureMode=StreamingCaptureMode.Video}));
   var source=cap.FrameSources.Values.First(s=>s.Info.SourceKind==MediaFrameSourceKind.Infrared);
   using(var reader=Wait(cap.CreateFrameReaderAsync(source))){
    var start=Wait(reader.StartAsync());if(start!=MediaFrameReaderStartStatus.Success)throw new Exception("Reader: "+start);
    Emit(new {type="ready",camera=group.DisplayName,sharing="SharedReadOnly",width=source.CurrentFormat.VideoFormat.Width,height=source.CurrentFormat.VideoFormat.Height,subtype=source.CurrentFormat.Subtype});
    var clock=Stopwatch.StartNew(); TimeSpan? previous=null;
    while(!stopping && (seconds==0 || clock.Elapsed.TotalSeconds<seconds)){
     using(var fr=reader.TryAcquireLatestFrame()){
      if(fr!=null && fr.SystemRelativeTime!=previous && fr.VideoMediaFrame!=null && fr.VideoMediaFrame.SoftwareBitmap!=null){
       previous=fr.SystemRelativeTime;
       using(var gray=SoftwareBitmap.Convert(fr.VideoMediaFrame.SoftwareBitmap,BitmapPixelFormat.Gray8)){
        // No black-frame filter: every bitmap reaches face detection.
        int faces=Wait(detector.DetectFacesAsync(gray)).Count;
        Emit(new {type="frame",faces=faces});
       }
      }
     }
     Thread.Sleep(250);
    }
    Wait(reader.StopAsync());
   }
  }
 }
}
