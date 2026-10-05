using System;
using System.Threading;
using System.Diagnostics;
using Windows.Devices.Sensors.Custom;
using Windows.Devices.Enumeration;
public static class ToFGate {
 static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op,Func<bool> stop){var clock=Stopwatch.StartNew();while(op.Status==Windows.Foundation.AsyncStatus.Started){if(stop()||clock.Elapsed.TotalSeconds>10){op.Cancel();throw new OperationCanceledException();}Thread.Sleep(20);}return op.GetResults();}
 public static bool Confirm(int mm,int count,int interval,Func<bool> stop,Action<object> emit){
  CustomSensor sensor=null;uint previous=0;bool captured=false;
  try {
   var devices=Wait(DeviceInformation.FindAllAsync(CustomSensor.GetDeviceSelector(new Guid("e83af229-8640-4d18-a213-e22675ebb2c3"))),stop);
   foreach(var d in devices)if(d.Id.IndexOf("99E646A6-11C7-4ACF-983B-4C63E0EB1A56",StringComparison.OrdinalIgnoreCase)>=0){sensor=Wait(CustomSensor.FromIdAsync(d.Id),stop);break;}
   if(sensor==null)throw new Exception("AMS_TMF882X HOD unavailable; IR remains gated");
   previous=sensor.ReportInterval;captured=true;sensor.ReportInterval=Math.Max(sensor.MinimumReportInterval,(uint)interval);
   var rule=new ToFConfirmation(mm,count,interval);var clock=Stopwatch.StartNew();
   while(!stop()){
    var r=sensor.GetCurrentReading();int distance=0,state=0,score=0;bool fresh=false;long stamp=0;
    if(r!=null){stamp=r.Timestamp.UtcDateTime.Ticks;double age=(DateTimeOffset.UtcNow-r.Timestamp).TotalSeconds;fresh=age>=-1&&age<=3;
     object value;string prefix="{C458F8A7-4AE8-4777-9607-2E9BDD65110A} ";
     if(r.Properties.TryGetValue(prefix+"162",out value))distance=Convert.ToInt32(value);
     if(r.Properties.TryGetValue(prefix+"163",out value))state=Convert.ToInt32(value);
     if(r.Properties.TryGetValue(prefix+"164",out value))score=Convert.ToInt32(value);
    }
    bool confirmed=rule.Observe(clock.Elapsed.TotalSeconds,stamp,fresh,distance,state,score);
    emit(new{type="tof",distance_mm=distance,status=state,score=score,fresh=fresh,consecutive=rule.Count,confirmed=confirmed});
    if(confirmed)return true;
    for(int waited=0;waited<interval&&!stop();waited+=50)Thread.Sleep(Math.Min(50,interval-waited));
   }
   return false;
  }finally{if(sensor!=null&&captured)sensor.ReportInterval=previous;}
 }
}
