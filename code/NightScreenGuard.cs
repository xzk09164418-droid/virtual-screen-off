using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public sealed class NightScreenGuard : IDisposable
{
    delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,Hook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    readonly Hook keyboard,mouse;
    IntPtr keyboardHandle,mouseHandle;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly Action<string> log;
    readonly ScreenConfig config=ScreenConfig.Current;
    readonly IrPresenceMonitor ir;
    readonly SensorCoordinator sensors;
    int internalOff;
    readonly BatteryDisplayPower battery;
    readonly Control dispatch=new Control();
    long lastInput=Stopwatch.GetTimestamp(),lastPoll=Stopwatch.GetTimestamp(),inputVersion,observedInput,ignoreInputUntil;
    long lastHooks=Stopwatch.GetTimestamp();
    int busy,disposed;
    volatile bool healthy;
    double lastWork=-100;
    bool? previousPower;
    bool powerKnown;
    string batteryOffReason="";
    volatile bool priorityWindow;
    bool priorityManualOverride;
    readonly string morningFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"morning-restored.txt");
    string morningDone="";
    DateTime inputRetry=DateTime.MinValue,morningRetry=DateTime.MinValue,priorityRetry=DateTime.MinValue;
    public static bool RealInput(bool keyboard,int flags) {return (flags&(keyboard?0x12:0x03))==0;}
    public static bool Due(DateTime utc,double idleSeconds){return ScreenConfig.Current.OffDue(utc,idleSeconds);}
    public static bool MorningDue(DateTime utc,DateTime completedDay){return ScreenConfig.Current.OnDue(utc,completedDay);}
    public void ResetIdle(){Interlocked.Exchange(ref lastInput,Stopwatch.GetTimestamp());}
    public bool ManualAction()
    {
        if(priorityWindow&&!priorityManualOverride){priorityManualOverride=true;log("PRIORITY manual override; detection remains disabled until window ends");}
        bool wasAway=battery.Active;
        if(wasAway){battery.Wake("manual-command");Install();if(!priorityWindow)sensors.Update(true,false,BatteryDisplayPower.OnBattery==true);}
        ResetIdle();ir.Reset();
        Interlocked.Exchange(ref ignoreInputUntil,Stopwatch.GetTimestamp()+3*Stopwatch.Frequency);
        Interlocked.Exchange(ref observedInput,Interlocked.Read(ref inputVersion));
        return wasAway;
    }
    void RealActivity()
    {
        ResetIdle();
        if(Stopwatch.GetTimestamp()>=Interlocked.Read(ref ignoreInputUntil))Interlocked.Increment(ref inputVersion);
    }
    double Idle {get{return (Stopwatch.GetTimestamp()-Interlocked.Read(ref lastInput))/(double)Stopwatch.Frequency;}}
    public NightScreenGuard(Action<string> logger)
    {
        log=logger;dispatch.CreateControl();var unused=dispatch.Handle;
        ir=new IrPresenceMonitor(log);sensors=new SensorCoordinator(config,ir,log);battery=new BatteryDisplayPower(log);
        priorityWindow=config.PriorityOffDue(DateTime.UtcNow);
        try{if(File.Exists(morningFile))morningDone=File.ReadAllText(morningFile).Trim();}
        catch(Exception ex){log("MORNING state read failed: "+ex.Message);}
        keyboard=delegate(int code,IntPtr message,IntPtr data){
            if(code>=0&&RealInput(true,Marshal.ReadInt32(data,8))){
                int kind=message.ToInt32();
                if(!priorityWindow){
                    if(battery.Active) {if(PresencePolicy.BatteryWake(true,true,kind==0x100||kind==0x104))battery.MarkKeyboard();}
                    else RealActivity();
                }
            }
            return CallNextHookEx(IntPtr.Zero,code,message,data);
        };
        mouse=delegate(int code,IntPtr message,IntPtr data){
            if(code>=0&&!priorityWindow&&!battery.Active&&RealInput(false,Marshal.ReadInt32(data,12)))RealActivity();
            return CallNextHookEx(IntPtr.Zero,code,message,data);
        };
        Install();timer.Interval=250;timer.Tick+=delegate{try{Tick();}catch(Exception ex){ResetIdle();log("TICK ERROR "+ex.Message);}};timer.Start();
        log("GUARD READY night="+config.OffStart+"-"+config.OffEnd+"; idle="+config.IdleSeconds+"; ir_absence="+config.IrAbsenceSeconds+"; battery_absence="+config.BatteryAwaySeconds);
        if(priorityWindow)log("PRIORITY OFF active; input and presence detection disabled");
    }
    void Install()
    {
        healthy=false;
        if(keyboardHandle!=IntPtr.Zero)UnhookWindowsHookEx(keyboardHandle);
        if(mouseHandle!=IntPtr.Zero)UnhookWindowsHookEx(mouseHandle);
        keyboardHandle=mouseHandle=IntPtr.Zero;
        if(priorityWindow){lastHooks=Stopwatch.GetTimestamp();return;}
        keyboardHandle=SetWindowsHookEx(13,keyboard,GetModuleHandle(null),0);
        mouseHandle=battery.Active?IntPtr.Zero:SetWindowsHookEx(14,mouse,GetModuleHandle(null),0);
        healthy=keyboardHandle!=IntPtr.Zero&&(battery.Active||mouseHandle!=IntPtr.Zero);
        if(!healthy){ResetIdle();log("INPUT ERROR hooks unavailable; auto-off suspended");}
        lastHooks=Stopwatch.GetTimestamp();
    }
    void Tick()
    {
        if(Volatile.Read(ref disposed)!=0)return;
        long now=Stopwatch.GetTimestamp();
        DateTime utc=DateTime.UtcNow;
        bool priority=config.PriorityOffDue(utc);
        if(priority) {
            if(!priorityWindow)EnterPriorityWindow();
            sensors.Update(false,true,BatteryDisplayPower.OnBattery==true);
            if(PresenceHistory.Now-lastWork<config.PollSeconds)return;
            lastWork=PresenceHistory.Now;
            if(Interlocked.CompareExchange(ref busy,1,0)!=0)return;
            ThreadPool.QueueUserWorkItem(delegate{
                try {if(Volatile.Read(ref disposed)==0)PollPriority();}
                catch(Exception ex){log("PRIORITY ERROR "+ex.Message);}
                finally {Interlocked.Exchange(ref busy,0);}
            });
            return;
        }
        if(priorityWindow)LeavePriorityWindow();
        if((now-lastPoll)/(double)Stopwatch.Frequency>config.GapSeconds){ResetIdle();ir.Reset();sensors.Reset();}
        lastPoll=now;
        if((now-lastHooks)/(double)Stopwatch.Frequency>=60||!healthy)Install();
        if(battery.Active) {
            sensors.Update(false,true,true);
            if(battery.KeyboardPending||battery.SystemWakePending) {
                battery.Wake(battery.KeyboardPending?"real-keyboard":"system-display-on",!battery.SystemWakePending);
                ResetIdle();ir.Reset();Install();
                Interlocked.Exchange(ref observedInput,Interlocked.Read(ref inputVersion));
                // Resume detection immediately, even if AC was attached while away.
                sensors.Update(true,false,BatteryDisplayPower.OnBattery==true);lastWork=PresenceHistory.Now;SaveState();
            }
            if(PresenceHistory.Now-lastWork>=1){lastWork=PresenceHistory.Now;SaveState();}
            return; // Both sensors stay stopped; Windows input wakes the display.
        }
        bool dark=battery.DisplayOff||Volatile.Read(ref internalOff)!=0;
        bool? livePower=BatteryDisplayPower.OnBattery;
        bool detect=PresencePolicy.NeedDetection(config,DateTime.UtcNow,livePower,dark);
        sensors.Update(detect,dark,livePower==true);
        if(TrySensorOff()) {lastWork=PresenceHistory.Now;SaveState();return;}
        if(dark&&livePower==false&&Volatile.Read(ref internalOff)==0&&sensors.WakePending&&!config.SuppressRestore(DateTime.UtcNow,false)){
            battery.Wake("TOF FACE");ResetIdle();sensors.Update(true,false,false);SaveState();
        }
        if(PresenceHistory.Now-lastWork<config.PollSeconds)return;
        lastWork=PresenceHistory.Now;
        if(Interlocked.CompareExchange(ref busy,1,0)!=0)return;
        bool? power=BatteryDisplayPower.OnBattery;
        if(!powerKnown||power!=previousPower){previousPower=power;powerKnown=true;ResetIdle();ir.Reset();log("POWER "+(power.HasValue?(power.Value?"battery":"AC"):"unknown"));}
        ThreadPool.QueueUserWorkItem(delegate{
            try {if(Volatile.Read(ref disposed)==0)Poll(power);}
            catch(Exception ex){ResetIdle();log("GUARD ERROR "+ex.Message);}
            finally {Interlocked.Exchange(ref busy,0);}
        });
    }
    void EnterPriorityWindow()
    {
        priorityWindow=true;priorityManualOverride=false;priorityRetry=DateTime.MinValue;
        if(battery.Active)battery.Wake("priority-virtual-mode");
        sensors.Update(false,true,BatteryDisplayPower.OnBattery==true);ir.Reset();Install();
        log("PRIORITY OFF entered; keyboard, mouse, IR and ToF detection disabled");
    }
    void LeavePriorityWindow()
    {
        priorityWindow=false;priorityManualOverride=false;priorityRetry=DateTime.MinValue;
        ResetIdle();ir.Reset();Install();lastWork=PresenceHistory.Now;
        log("PRIORITY OFF ended; normal detection restored");
    }
    void PollPriority()
    {
        SaveState();
        DateTime utc=DateTime.UtcNow;
        if(!priorityWindow||priorityManualOverride||!config.PriorityOffDue(utc)||utc<priorityRetry)return;
        var paths=DisplayNative.Query(false).Paths;
        bool on=paths.Any(p=>DisplayNative.Internal(p));
        Interlocked.Exchange(ref internalOff,on?0:1);
        if(!on)return;
        priorityRetry=utc.AddSeconds(config.RetrySeconds);
        if(!priorityWindow||priorityManualOverride||!config.PriorityOffDue(DateTime.UtcNow))return;
        using(Process p=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"VirtualScreenTest.exe"),"--off")
          {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))
            log("PRIORITY requested --off; detection bypassed; pid="+p.Id);
    }
    void SaveState()
    {
        try {
            var state=new {running=Volatile.Read(ref disposed)==0,utc=DateTime.UtcNow.ToString("O"),on_battery=BatteryDisplayPower.OnBattery,battery_away=battery.Active,
                keyboard_hook=keyboardHandle!=IntPtr.Zero,mouse_hook=mouseHandle!=IntPtr.Zero,ir_running=ir.Running,tof_state=sensors.State,display_power_off=battery.DisplayOff,battery_off_reason=batteryOffReason,ir_auto_off_ready=ir.AutoOffReady,ir_warmup_remaining_seconds=ir.WarmupRemaining,
                priority_off_window=priorityWindow,priority_manual_override=priorityManualOverride,face_absent_seconds=ir.Absent,face_present_seconds=ir.Present,input_idle_seconds=Idle};
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"guard-state.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(state));
        } catch(Exception ex) {log("STATE ERROR "+ex.Message);}
    }
    bool TrySensorOff()
    {
        string reason=sensors.PendingOffReason;
        if(Volatile.Read(ref disposed)!=0||battery.Active||!PresencePolicy.BatterySensorOff(config,BatteryDisplayPower.OnBattery,healthy,battery.DisplayOff||Volatile.Read(ref internalOff)!=0,reason.Length>0))return false;
        try {
            var paths=DisplayNative.Query(false).Paths;
            if(paths.Length!=1||!DisplayNative.Internal(paths[0])){
                log("BATTERY "+reason+" skipped: requires internal-only topology");sensors.Reset();return false;
            }
            // Recheck after the topology query; AC may have been attached meanwhile.
            if(BatteryDisplayPower.OnBattery!=true)return false;
            log(reason=="tof-initial-timeout"?"BATTERY initial ToF timeout after "+config.BatteryTofInitialWaitSeconds+"s; near confirmation incomplete; skip IR":"BATTERY stable-distance final check: "+config.BatteryFinalNoFaceSeconds+"s without face; IR startup grace completed");
            sensors.Update(false,true,true);
            if(BatteryDisplayPower.OnBattery!=true)return false;
            batteryOffReason=reason;
            battery.Enter();Install();
            if(!healthy){battery.Wake("keyboard-hook-failed");Install();}
            return true;
        } catch(Exception ex){if(battery.Active)battery.Wake("sensor-decision-error");sensors.Reset();Install();log("BATTERY sensor decision error "+ex.Message);return false;}
    }
    void Poll(bool? power)
    {
        SaveState();
        if(config.PriorityOffDue(DateTime.UtcNow))return;
        if(!power.HasValue)return;
        DateTime utc=DateTime.UtcNow;
        // Battery absence takes precedence over idle, time windows and morning wake.
        if(!battery.DisplayOff&&Volatile.Read(ref internalOff)==0&&sensors.AutoOffAllowed&&ir.AutoOffReady&&PresencePolicy.BatteryOff(config,power.Value,healthy,ir.Absent)) {
            if(!DisplayNative.Query(false).Paths.Any(p=>DisplayNative.Internal(p))) {
                RunRestore("--restore","BATTERY prepare physical-only mode");
            }
            dispatch.BeginInvoke((Action)delegate{
                try {
                if(Volatile.Read(ref disposed)!=0||BatteryDisplayPower.OnBattery!=true||!sensors.AutoOffAllowed||!ir.AutoOffReady||!PresencePolicy.BatteryOff(config,true,healthy,ir.Absent))return;
                var paths=DisplayNative.Query(false).Paths;
                if(paths.Length!=1||!DisplayNative.Internal(paths[0])){log("BATTERY skipped: physical off requires internal-only topology; external displays untouched");ir.Reset();return;}
                log("BATTERY criteria face_absence="+ir.Absent.ToString("F1")+"; input_idle="+Idle.ToString("F1"));
                sensors.Update(false,true,true); // Release ToF and IR before posting physical off.
                batteryOffReason="ir-absence";
                battery.Enter();
                Install(); // Drops the mouse hook; only the keyboard hook remains.
                SaveState();
                if(!healthy){battery.Wake("keyboard-hook-failed");Install();}
                } catch(Exception ex) { if(battery.Active)battery.Wake("entry-error");Install();log("BATTERY ERROR "+ex.Message); }
            });
            return;
        }
        bool on=DisplayNative.Query(false).Paths.Any(p=>DisplayNative.Internal(p));
        Interlocked.Exchange(ref internalOff,on?0:1);
        if(!power.Value&&config.ScheduledDue(utc,morningDone)&&utc>=morningRetry) {
            morningRetry=utc.AddSeconds(config.RetrySeconds);
            if(on||RestoreAutomatically("MORNING")) {
                string key=config.MorningKey(utc);File.WriteAllText(morningFile,key);morningDone=key;
                log("MORNING completed "+key);
            }
            return;
        }
        long input=Interlocked.Read(ref inputVersion);
        bool faceWake=!power.Value&&config.IrEnabled&&(config.TofEnabled?sensors.WakePending:ir.Present>=1.5);
        if(!on&&healthy&&!config.SuppressRestore(utc,false)&&utc>=inputRetry&&
           (config.InputWakeDue(utc,false,input,Interlocked.Read(ref observedInput))||faceWake)) {
            inputRetry=utc.AddSeconds(config.RetrySeconds);
            if(RestoreAutomatically(faceWake?"IR FACE":"INPUT")) {
                inputRetry=DateTime.MinValue;Interlocked.Exchange(ref observedInput,input);ResetIdle();ir.Reset();
            }
            return;
        }
        if(on||config.SuppressRestore(utc,false)||!config.WakeOnInput)Interlocked.Exchange(ref observedInput,input);
        if(!on||battery.DisplayOff||BatteryDisplayPower.OnBattery!=false||!sensors.AutoOffAllowed||Volatile.Read(ref disposed)!=0||(config.IrEnabled&&!ir.AutoOffReady)||!PresencePolicy.NightOff(config,utc,power.Value,healthy,Idle,ir.Absent))return;
        using(Process p=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"VirtualScreenTest.exe"),"--off")
          {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))
            log("NIGHT requested --off; real_input_idle="+Idle.ToString("F1")+"; face_absence="+ir.Absent.ToString("F1")+"; pid="+p.Id);
        ResetIdle();
    }
    void RunRestore(string argument,string reason)
    {
        using(Process p=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"VirtualScreenTest.exe"),argument)
          {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
            if(!p.WaitForExit(20000)||p.ExitCode!=0)throw new IOException("Restore failed: "+reason);
        }
        if(!DisplayNative.Query(false).Paths.Any(p=>DisplayNative.Internal(p)))throw new IOException("Restore verification failed: "+reason);
        Interlocked.Exchange(ref internalOff,0);
        log(reason+" restore verified");
    }
    bool RestoreAutomatically(string reason)
    {
        if(Volatile.Read(ref disposed)!=0||config.SuppressRestore(DateTime.UtcNow,false))return false;
        RunRestore("--auto-restore",reason);return true;
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref disposed,1);healthy=false;timer.Stop();timer.Dispose();
        sensors.Dispose();ir.Dispose();battery.Dispose();
        if(keyboardHandle!=IntPtr.Zero)UnhookWindowsHookEx(keyboardHandle);
        if(mouseHandle!=IntPtr.Zero)UnhookWindowsHookEx(mouseHandle);
        keyboardHandle=mouseHandle=IntPtr.Zero;SaveState();
        dispatch.Dispose();
    }
}
