using System;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;
using Windows.Devices.Sensors.Custom;
using Windows.Devices.Enumeration;
class ToFStream {
 static volatile bool stop;static int interval=1000,revision;
 static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op){var w=Stopwatch.StartNew();while(op.Status==Windows.Foundation.AsyncStatus.Started){if(stop||w.Elapsed.TotalSeconds>10){op.Cancel();throw new OperationCanceledException();}Thread.Sleep(20);}return op.GetResults();}
 static void Emit(object o){Console.WriteLine(new JavaScriptSerializer().Serialize(o));}
 static int Main(){return Run();}
 static int Run(){Windows.Foundation.TypedEventHandler<CustomSensor,CustomSensorReadingChangedEventArgs> listener=(s,e)=>{};CustomSensor sensor=null;uint previous=0;bool captured=false;string deviceId=null;
  new Thread(()=>{string line;while((line=Console.ReadLine())!=null){int n;if(Int32.TryParse(line,out n)&&n>=100&&n<=30000){Interlocked.Exchange(ref interval,n);Interlocked.Increment(ref revision);}}stop=true;}){IsBackground=true}.Start();
  try {
   var list=Wait(DeviceInformation.FindAllAsync(CustomSensor.GetDeviceSelector(new Guid("e83af229-8640-4d18-a213-e22675ebb2c3"))));
   foreach(var d in list)if(d.Id.IndexOf("99E646A6-11C7-4ACF-983B-4C63E0EB1A56",StringComparison.OrdinalIgnoreCase)>=0){deviceId=d.Id;sensor=Wait(CustomSensor.FromIdAsync(d.Id));break;}
   if(sensor==null)throw new Exception("AMS_TMF882X HOD unavailable");previous=sensor.ReportInterval;captured=true;sensor.ReportInterval=(uint)Volatile.Read(ref interval);sensor.ReadingChanged+=listener;
   var clock=Stopwatch.StartNew();double readAt=0,beatAt=0,lastRead=0;int seen=-1;
   while(!stop){
    double now=clock.Elapsed.TotalSeconds;int rev=Volatile.Read(ref revision),ms=Volatile.Read(ref interval);
    if(rev!=seen){if(seen>=0){sensor.ReadingChanged-=listener;sensor.ReportInterval=0;sensor=null;GC.Collect();GC.WaitForPendingFinalizers();sensor=Wait(CustomSensor.FromIdAsync(deviceId));if(sensor==null)throw new Exception("ToF reopen failed");}sensor.ReportInterval=Math.Max(sensor.MinimumReportInterval,(uint)ms);if(seen>=0)sensor.ReadingChanged+=listener;seen=rev;readAt=lastRead+ms/1000.0;}
    if(now>=readAt){
     var r=sensor.GetCurrentReading();int distance=0,status=0,score=0;long stamp=0;bool fresh=false;
     if(r!=null){stamp=r.Timestamp.UtcDateTime.Ticks;double age=(DateTimeOffset.UtcNow-r.Timestamp).TotalSeconds;fresh=age>=-1&&age<=Math.Max(3,ms/1000.0+1);object v;string k="{C458F8A7-4AE8-4777-9607-2E9BDD65110A} ";
      if(r.Properties.TryGetValue(k+"162",out v))distance=Convert.ToInt32(v);
      if(r.Properties.TryGetValue(k+"163",out v))status=Convert.ToInt32(v);
      if(r.Properties.TryGetValue(k+"164",out v))score=Convert.ToInt32(v);
     }
     lastRead=clock.Elapsed.TotalSeconds;readAt=lastRead+ms/1000.0;
     Emit(new{type="tof",timestamp=stamp,fresh=fresh,distance_mm=distance,status=status,score=score,interval_ms=ms});
    }
    if(now>=beatAt){Emit(new{type="heartbeat"});beatAt=now+2;}
    Thread.Sleep(20);
   }
   return 0;
  }catch(Exception e){if(!stop)Emit(new{type="error",message=e.ToString()});return stop?0:1;}
  finally{if(sensor!=null&&captured)try{sensor.ReadingChanged-=listener;sensor.ReportInterval=previous;}catch{}}
 }
}
