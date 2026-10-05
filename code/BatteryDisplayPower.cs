using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// A system display-on event exits away mode; never fight Windows with another off request.
public sealed class BatteryDisplayPower : NativeWindow, IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct PowerStatus { public byte AC,BatteryFlag,Percent,Reserved; public uint Life,FullLife; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus value);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient,ref Guid setting,uint flags);
    [DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
    readonly Action<string> log;


    IntPtr notification;
    int active, keyboardPending, systemWakePending, offIssued, generation, displayOff;
    public bool DisplayOff {get{return Volatile.Read(ref displayOff)!=0;}}
    public bool Active {get{return Volatile.Read(ref active)!=0;}}
    public bool KeyboardPending {get{return Volatile.Read(ref keyboardPending)!=0;}}
    public bool SystemWakePending {get{return Volatile.Read(ref systemWakePending)!=0;}}
    public static bool? OnBattery {
        get {PowerStatus p;if(!GetSystemPowerStatus(out p)||p.AC==255)return null;return p.AC==0;}
    }
    public BatteryDisplayPower(Action<string> logger)
    {
        log=logger;CreateHandle(new CreateParams {Parent=new IntPtr(-3),Caption="ScreenOffHotkey battery power"});
        Guid guid=new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        notification=RegisterPowerSettingNotification(Handle,ref guid,0);
        if(notification==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Display power notifications unavailable");

    }
    public void MarkKeyboard() {if(Active)Interlocked.Exchange(ref keyboardPending,1);}
    public void Enter()
    {
        Interlocked.Exchange(ref keyboardPending,0);Interlocked.Exchange(ref systemWakePending,0);Interlocked.Exchange(ref offIssued,0);Interlocked.Exchange(ref active,1);Interlocked.Increment(ref generation);
        QueuePower(false);
        log("BATTERY AWAY entered; no virtual display; IR and ToF stopped; proximity wake disabled; mouse hook removed; system display-on or keyboard exits");
    }
    public void Wake(string reason, bool requestDisplayOn = true)
    {
        Interlocked.Exchange(ref active,0);Interlocked.Increment(ref generation);
        Interlocked.Exchange(ref displayOff,0);
        if(requestDisplayOn)QueuePower(true);log("BATTERY AWAKE reason="+reason);
    }
    void QueuePower(bool on)
    {
        // One private window, one serialized command. Broadcasting SC_MONITORPOWER
        // makes every application repeat the command and can leave late off requests.
        PostMessage(Handle,0x8001,new IntPtr(Volatile.Read(ref generation)),new IntPtr(on?1:0));
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x8001) {
            bool on=m.LParam.ToInt32()==1;
            if(PresencePolicy.PowerCommandAllowed(m.WParam.ToInt32(),Volatile.Read(ref generation),on,Active,KeyboardPending||SystemWakePending)) {
                if(!on)Interlocked.Exchange(ref offIssued,1);
                DefWindowProc(Handle,0x112,new IntPtr(0xf170),new IntPtr(on?-1:2));
            }
            return;
        }
        if(m.Msg==0x218 && m.WParam.ToInt32()==0x8013 && m.LParam!=IntPtr.Zero) {
            int length=Marshal.ReadInt32(m.LParam,16);
            if(length>=4) {
                int state=Marshal.ReadInt32(m.LParam,20);
                if(state==0||state==1)Interlocked.Exchange(ref displayOff,state==0?1:0);
                log("DISPLAY POWER state="+state+"; battery_away="+Active);
                if(PresencePolicy.SystemDisplayWake(Active,Volatile.Read(ref offIssued)!=0,state)) {
                    Interlocked.Exchange(ref systemWakePending,1);
                    Interlocked.Increment(ref generation); // invalidate any old off command immediately
                }
            }
        }
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        if(Active) {Wake("program-exit");DefWindowProc(Handle,0x112,new IntPtr(0xf170),new IntPtr(-1));}

        if(notification!=IntPtr.Zero)UnregisterPowerSettingNotification(notification);
        DestroyHandle();
    }
}
