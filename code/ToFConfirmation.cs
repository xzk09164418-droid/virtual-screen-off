using System;
// A new, fresh sample is required for each consecutive observation.
public sealed class ToFConfirmation {
 readonly int limit,required,interval;
 double last=-1; long stamp=-1; int count;
 public int Count {get{return count;}}
 public ToFConfirmation(int mm,int samples,int intervalMs){limit=mm;required=samples;interval=intervalMs;}
 public bool Observe(double now,long timestamp,bool fresh,int distance,int state,int score){
  if(!fresh||distance<=0||distance>limit||state!=1||score<=0){count=0;last=-1;stamp=timestamp;return false;}
  if(last>=0&&now-last>Math.Max(3,interval/1000.0*3)){count=0;last=-1;}
  if(timestamp<=stamp)return false;
  if(last>=0&&now-last<interval/1000.0*0.9)return false;
  stamp=timestamp;last=now;count++;return count>=required;
 }
}
