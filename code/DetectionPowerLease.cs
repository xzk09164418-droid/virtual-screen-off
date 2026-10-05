using System;
using System.Runtime.InteropServices;
public sealed class DetectionPowerLease : IDisposable {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Reason {public uint Version,Flags;[MarshalAs(UnmanagedType.LPWStr)]public string Text;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr PowerCreateRequest(ref Reason reason);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool PowerSetRequest(IntPtr handle,int type);
 [DllImport("kernel32.dll")]static extern bool PowerClearRequest(IntPtr handle,int type);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 IntPtr handle;bool execution,system;
 public DetectionPowerLease(){var r=new Reason{Version=0,Flags=1,Text="ToF 人脸唤醒检测：保持检测进程运行，允许屏幕关闭"};handle=PowerCreateRequest(ref r);if(handle==IntPtr.Zero||handle==new IntPtr(-1)){handle=IntPtr.Zero;throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}execution=PowerSetRequest(handle,3);system=PowerSetRequest(handle,1);if(!execution||!system){int error=Marshal.GetLastWin32Error();Dispose();throw new System.ComponentModel.Win32Exception(error);}}
 public void Dispose(){if(handle==IntPtr.Zero)return;if(execution)PowerClearRequest(handle,3);if(system)PowerClearRequest(handle,1);CloseHandle(handle);handle=IntPtr.Zero;}
}
