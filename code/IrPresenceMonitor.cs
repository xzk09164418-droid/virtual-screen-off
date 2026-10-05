using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

public sealed class IrPresenceMonitor : IDisposable
{
    readonly Action<string> log;
    readonly PresenceHistory history = new PresenceHistory();
    readonly object gate = new object();
    Process process;
    double retryAt, lastReceived, readyAt = -1;
    readonly int graceSeconds = ScreenConfig.Current.IrStartupGraceSeconds;
    public IrPresenceMonitor(Action<string> logger) { log=logger; }
    public double Absent { get { return history.Absent(PresenceHistory.Now); } }
    public double Present { get { return history.Present(PresenceHistory.Now); } }
    public bool Running { get {lock(gate)return process!=null&&!process.HasExited&&readyAt>=0;} }
    public string TofState {get {lock(gate)return tofState;} }
    string tofState="inactive";
    public bool AutoOffReady { get {lock(gate)return process!=null&&!process.HasExited&&PresencePolicy.IrWarmupReady(PresenceHistory.Now,readyAt,graceSeconds);} }
    public double WarmupRemaining { get {lock(gate)return readyAt<0?graceSeconds:Math.Max(0,graceSeconds-(PresenceHistory.Now-readyAt));} }
    public void Reset() { history.Reset(); }
    public void Ensure(bool needed)
    {
        if(!needed) { Stop(); return; }
        lock(gate) {
            if(process != null && !process.HasExited && PresenceHistory.Now-lastReceived <= 10) return;
        }
        Stop(false);
        lock(gate)
        {
            if(PresenceHistory.Now < retryAt) return;
            retryAt=PresenceHistory.Now+30;
            var c=ScreenConfig.Current;
            string arguments="0"; // ToF is now owned by the persistent coordinator.
            var p=new Process { StartInfo = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"IRPresence.exe"),arguments)
              { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true } };
            p.OutputDataReceived += delegate(object sender,DataReceivedEventArgs e) {
                if(String.IsNullOrEmpty(e.Data))return;
                lock(gate) {
                    if(process != p)return;
                    try {
                        var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(e.Data);
                        string type=Convert.ToString(data["type"]);
                        if(type=="tof") {lastReceived=PresenceHistory.Now;tofState=e.Data;if(Convert.ToBoolean(data["confirmed"]))log("TOF CONFIRMED "+e.Data);}
                        else if(type=="frame") {lastReceived=PresenceHistory.Now;history.Observe(lastReceived,Convert.ToInt32(data["faces"])>0);}
                        else if(type=="ready") {readyAt=PresenceHistory.Now;log("IR READY; observe_only_seconds="+graceSeconds+" "+e.Data);}
                        else if(type=="error") {history.Reset();log("IR ERROR "+e.Data);}
                    } catch(Exception ex) { history.Reset();log("IR parse error "+ex.Message); }
                }
            };
            p.ErrorDataReceived += delegate(object sender,DataReceivedEventArgs e) { if(!String.IsNullOrEmpty(e.Data))log("IR STDERR "+e.Data); };
            try {
                process=p;tofState="external-coordinator";history.Reset();readyAt=-1;lastReceived=PresenceHistory.Now;p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();
                log("IR HELPER START pid="+p.Id+"; controlled_by_coordinator=true");
            } catch {process=null;p.Dispose();throw;}
        }
    }
    public void Stop(bool normalStop = true)
    {
        Process old;
        lock(gate) {old=process;process=null;tofState="inactive";history.Reset();readyAt=-1;if(normalStop)retryAt=0;}
        if(old==null)return;
        try {
            if(!old.HasExited) {
                old.StandardInput.Close(); // EOF tells the helper to release the camera.
                if(!old.WaitForExit(1500)){old.Kill();old.WaitForExit(1500);}
            }
            log("IR STOP; camera released");
        } catch(Exception ex) {log("IR stop error "+ex.Message);}
        finally {old.Dispose();}
    }
    public void Dispose() {Stop();}
}
