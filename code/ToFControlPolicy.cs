using System;
// Pure sensor state machine. All calls are serialized by SensorCoordinator.
public sealed class ToFControlPolicy {
 readonly ScreenConfig c;
 public string Mode {get;private set;}
 public bool Dark {get;private set;}
 public bool Battery {get;private set;}
 public bool Camera {get{return Mode=="ac-ir"||Mode=="battery-ir"||Mode=="battery-check"||Mode=="battery-final-face"||Mode=="dark-face";}}
 public bool Tof {get{return Mode=="battery-gate"||Mode=="battery-check"||Mode=="battery-hold"||Mode=="dark-scan"||Mode=="dark-confirm"||Mode=="dark-face";}}
 public bool AutoOffAllowed {get{return Mode=="ac-ir"||Mode=="battery-ir";}}
 public bool WakePending {get{return Mode=="dark-confirmed";}}
 public bool InitialGateTimedOut {get{return Mode=="battery-gate-timeout";}}
 public bool FinalAbsencePending {get{return Mode=="battery-final-absent";}}
 public int Interval {get;private set;}
 public int Count {get;private set;}
 public int Baseline {get;private set;}
 int attempts,lastTarget;long stamp;double last=-1,phaseAt,nextCheckAt,lastGoodAt,stableDeadline;
 public ToFControlPolicy(ScreenConfig config){c=config;Reset(false,0);}
 public static int Backoff(int attempt,int maximum){return (int)Math.Min(maximum,Math.Round(1000+5000*Math.Log(Math.Max(0,attempt)+1,2)));}
 public void Reset(bool dark,double now,bool battery=false){
  Dark=dark;Battery=battery;Mode=dark?(battery?"battery-off":"dark-scan"):(battery?"battery-gate":"ac-ir");
  Count=attempts=Baseline=lastTarget=0;last=-1;stamp=0;phaseAt=lastGoodAt=now;stableDeadline=-1;nextCheckAt=0;Interval=dark?1000:c.TofIntervalMs;
 }
 void EnterIr(double now,int distance,bool retry){
  Mode="battery-ir";if(distance>0)Baseline=distance;
  Count=attempts=0;last=-1;phaseAt=now;Interval=c.TofIntervalMs;nextCheckAt=now+(retry?30:0);
 }
 void Scan(double now){Mode="dark-scan";Count=0;attempts=Math.Min(attempts+1,1000000);Interval=Backoff(attempts,c.TofDarkMaxIntervalMs);phaseAt=now;}
 public void Advance(double now){
  if(Mode=="battery-gate"&&now-phaseAt>=c.BatteryTofInitialWaitSeconds){Mode="battery-gate-timeout";Count=0;}
  if(Mode=="battery-check"&&now-phaseAt>=10)EnterIr(now,0,true);
  if(Mode=="battery-hold"&&now-lastGoodAt>Math.Max(15,Interval/1000.0*3+5))EnterIr(now,0,false);
  if(Mode=="battery-hold"&&now>=stableDeadline-Math.Max(c.IrStartupGraceSeconds,c.BatteryFinalNoFaceSeconds)){
   Mode="battery-final-face";if(lastTarget>0)Baseline=lastTarget;Count=0;
  }
  if(Mode=="dark-confirm"&&now-phaseAt>=10)Scan(now);
  if(Mode=="dark-face"&&now-phaseAt>=c.TofFaceTimeoutSeconds)Scan(now);
  if(Mode=="dark-confirmed"&&now-phaseAt>10)Scan(now);
 }
 public void Face(double now,double presentSeconds=0,double absentSeconds=-1,bool ready=false){
  if(Mode=="battery-final-face"){
   if(presentSeconds>=0){EnterIr(now,0,false);return;}
   if(ready&&absentSeconds>=c.BatteryFinalNoFaceSeconds&&now>=stableDeadline)Mode="battery-final-absent";
   return;
  }
  if(Mode=="dark-face"&&presentSeconds>=0){Mode="dark-confirmed";phaseAt=now;return;}
  if(Mode=="battery-check"&&presentSeconds<c.BatteryFaceConfirmSeconds){EnterIr(now,0,false);return;}
  if(Mode=="battery-ir"&&presentSeconds>=c.BatteryFaceConfirmSeconds&&now>=nextCheckAt){
   Mode="battery-check";Count=0;phaseAt=now;last=-1;Interval=c.TofIntervalMs;
  }
 }
 public void Fault(double now){
  if(InitialGateTimedOut||FinalAbsencePending)return; // Late helper errors must not undo a completed decision.
  if(Battery&&!Dark){if(Mode=="battery-gate"){Count=0;last=-1;}else EnterIr(now,0,true);}
  else Reset(Dark,now,Battery);
 }
 public void Observe(double now,long timestamp,bool fresh,int distance,int status,int score){
  Advance(now);if(!Tof)return;
  bool distinct=timestamp>stamp;
  bool spaced=last<0||now-last>=Interval/1000.0*.85;
  bool gap=last>=0&&now-last>Math.Max(3,Interval/1000.0*3);
  if(distinct)stamp=timestamp;
  bool valid=fresh&&distinct&&spaced;
  bool target=valid&&distance>0&&status==1&&score>0;
  if(target)lastTarget=distance;
  bool near=target&&distance<=1000;
  bool inGate=target&&distance<=c.TofDistanceMm;
  if(valid)last=now;
  if(gap)Count=0;
  if(Battery){
   if(Mode=="battery-gate"){
    if(!inGate){Count=0;return;}
    if(++Count>=c.TofSamples)EnterIr(now,distance,false);
   }else if(Mode=="battery-check"){
    if(!valid){Count=0;return;}
    if(!inGate||Baseline<=0||Math.Abs((long)distance-Baseline)>=c.BatteryTofChangeMm){EnterIr(now,target?distance:0,true);return;}
    if(++Count>=3){Mode="battery-hold";Count=attempts=0;Interval=1000;lastGoodAt=now;stableDeadline=now+c.BatteryTofStableSeconds;}
   }else if(Mode=="battery-hold"){
    if(!valid)return; // Missing reports never count as absent faces; Advance bounds sensor outages.
    if(!inGate||Math.Abs((long)distance-Baseline)>=c.BatteryTofChangeMm){EnterIr(now,target?distance:0,false);return;}
    lastGoodAt=now;attempts=Math.Min(attempts+1,1000000);Interval=Backoff(attempts,c.BatteryTofMaxIntervalMs);
   }
   return;
  }
  if(Mode=="dark-face"||Mode=="dark-confirmed")return;
  if(Mode=="dark-confirm"){
   if(!near||gap){if(Count==0&&now-phaseAt<8)return;Scan(now);return;}
   if(++Count>=5){Mode="dark-face";phaseAt=now;Interval=c.TofIntervalMs;}
  }else if(near){Mode="dark-confirm";Count=0;Interval=200;phaseAt=now;}
  else Scan(now);
 }
}
