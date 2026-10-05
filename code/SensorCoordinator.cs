using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public sealed class SensorCoordinator : IDisposable {
 readonly object gate=new object();readonly ScreenConfig c;readonly IrPresenceMonitor ir;readonly Action<string> log;readonly ToFControlPolicy policy;
 Process process;double received,retryAt;bool enabled,dark,onBattery;int sent=-1;string lastSample="inactive";
 public SensorCoordinator(ScreenConfig config,IrPresenceMonitor camera,Action<string> logger){c=config;ir=camera;log=logger;policy=new ToFControlPolicy(c);}
 public bool AutoOffAllowed {get{lock(gate)return !c.IrEnabled||(enabled&&!dark&&(!c.TofEnabled||policy.AutoOffAllowed));}}
 public bool WakePending {get{lock(gate)return enabled&&dark&&!onBattery&&policy.WakePending;}}
 public bool InitialGateTimedOut {get{lock(gate)return c.TofEnabled&&enabled&&!dark&&onBattery&&policy.InitialGateTimedOut;}}
 public string PendingOffReason {get{lock(gate)return c.TofEnabled&&enabled&&!dark&&onBattery?(policy.InitialGateTimedOut?"tof-initial-timeout":policy.FinalAbsencePending?"tof-stable-no-face":""):"";}}
 public object State {get{lock(gate)return new{enabled=enabled,dark=dark,battery=onBattery,mode=policy.Mode,initial_gate_timed_out=policy.InitialGateTimedOut,interval_ms=policy.Interval,confirm_count=policy.Count,baseline_mm=policy.Baseline,tof_running=process!=null&&!process.HasExited,sample=lastSample};}}
 void SendInterval(){if(process!=null&&!process.HasExited&&sent!=policy.Interval){process.StandardInput.WriteLine(policy.Interval);process.StandardInput.Flush();sent=policy.Interval;}}
 void Transition(string before){if(before!=policy.Mode)log("SENSORS "+before+" -> "+policy.Mode+"; baseline_mm="+policy.Baseline);}
 public void Update(bool needed,bool isDark,bool battery=false){
  bool reset;
  lock(gate){reset=enabled!=needed||dark!=isDark||onBattery!=battery;if(reset){enabled=needed;dark=isDark;onBattery=battery;policy.Reset(dark,PresenceHistory.Now,battery);}}
  if(reset){StopProcess();ir.Stop();lock(gate)retryAt=0;}
  if(!needed){StopProcess();ir.Stop();return;}
  if(!c.TofEnabled){StopProcess();ir.Ensure(true);return;}
  bool wantTof,camera;
  lock(gate){
   string before=policy.Mode;
   policy.Advance(PresenceHistory.Now);policy.Face(PresenceHistory.Now,ir.Present,ir.Absent,ir.AutoOffReady);Transition(before);
   wantTof=policy.Tof;camera=policy.Camera;
  }
  if(!wantTof)StopProcess();
  if(!camera)ir.Stop();
  if(wantTof){
   bool failed;lock(gate)failed=process!=null&&(process.HasExited||PresenceHistory.Now-received>15);
   if(failed){StopProcess();lock(gate){string before=policy.Mode;policy.Fault(PresenceHistory.Now);retryAt=PresenceHistory.Now+30;Transition(before);}log("TOF unavailable; retry in 30s");}
   lock(gate){if(policy.Tof&&process==null&&PresenceHistory.Now>=retryAt)Start();SendInterval();camera=policy.Camera;}
  }
  ir.Ensure(camera);
 }
 void Start(){
  var p=new Process{StartInfo=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ToFStream.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
  p.OutputDataReceived+=(sender,e)=>{if(String.IsNullOrEmpty(e.Data))return;lock(gate){if(process!=p)return;try{
   var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(e.Data);received=PresenceHistory.Now;string type=Convert.ToString(d["type"]);
   if(type=="tof"){
    string before=policy.Mode;lastSample=e.Data;
    // A face must still be present when the last distance confirmation arrives.
    if(policy.Mode=="battery-check")policy.Face(received,ir.Present);
    policy.Observe(received,Convert.ToInt64(d["timestamp"]),Convert.ToBoolean(d["fresh"]),Convert.ToInt32(d["distance_mm"]),Convert.ToInt32(d["status"]),Convert.ToInt32(d["score"]));
    Transition(before);SendInterval();
   }else if(type=="error"){string before=policy.Mode;policy.Fault(received);retryAt=received+30;Transition(before);log("TOF ERROR "+e.Data);}
  }catch(Exception ex){policy.Fault(PresenceHistory.Now);retryAt=PresenceHistory.Now+30;log("TOF parse error "+ex.Message);}}};
  p.ErrorDataReceived+=(sender,e)=>{if(!String.IsNullOrEmpty(e.Data))log("TOF STDERR "+e.Data);};
  try{process=p;sent=-1;received=PresenceHistory.Now;p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();SendInterval();log("TOF START pid="+p.Id);}catch{process=null;p.Dispose();retryAt=PresenceHistory.Now+30;policy.Fault(PresenceHistory.Now);throw;}
 }
 void StopProcess(){Process p;lock(gate){p=process;process=null;sent=-1;lastSample="inactive";}if(p==null)return;try{if(!p.HasExited){p.StandardInput.Close();if(!p.WaitForExit(1500)){p.Kill();p.WaitForExit(1500);}}log("TOF STOP; client released");}catch(Exception e){log("TOF stop error "+e.Message);}finally{p.Dispose();}}
 public void Stop(){StopProcess();ir.Stop();lock(gate){enabled=false;retryAt=0;policy.Reset(dark,PresenceHistory.Now,onBattery);}}
 public void Reset(){StopProcess();ir.Stop();lock(gate){retryAt=0;policy.Reset(dark,PresenceHistory.Now,onBattery);}}
 public void Dispose(){Stop();}
}
