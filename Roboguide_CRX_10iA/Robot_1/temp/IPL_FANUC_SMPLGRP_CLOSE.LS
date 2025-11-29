/PROG  IPL_FANUC_SMPLGRP_CLOSE
/ATTR
OWNER		= MNEDITOR;
COMMENT		= "FANUC_SmplGrp";
PROG_SIZE	= 801;
CREATE		= DATE 20-04-25  TIME 19:55:36;
MODIFIED	= DATE 20-04-25  TIME 19:55:36;
FILE_NAME	= IPL_FANU;
VERSION		= 0;
LINE_COUNT	= 23;
MEMORY_SIZE	= 1245;
PROTECT		= READ_WRITE;
TCD:  STACK_SIZE	= 0,
      TASK_PRIORITY	= 50,
      TIME_SLICE	= 0,
      BUSY_LAMP_OFF	= 0,
      ABORT_REQUEST	= 0,
      PAUSE_REQUEST	= 0;
DEFAULT_GROUP	= *,*,*,*,*;
CONTROL_CODE	= 00000000 00000000;
LOCAL_REGISTERS	= 3,0,0;
/APPL
/APPL
/MN
   1:  !local register ;
   2:  !R[10001-10003] are usable ;
   3:  !---------- ;
   4:  !Copy to local variable ;
   5:  R[10001]=$[IPL_FANUC_SmplGrp_Cfg]OpenDo ;
   6:  R[10002]=$[IPL_FANUC_SmplGrp_Cfg]ClosDo ;
   7:  R[10003]=$[IPL_FANUC_SmplGrp_Cfg]StatDi ;
   8:   ;
   9:  !Check Index Number ;
  10:  IF R[10001]<=0,JMP LBL[1] ;
  11:  IF R[10002]<=0,JMP LBL[1] ;
  12:  IF R[10003]<=0,JMP LBL[1] ;
  13:   ;
  14:  !Check Gripper Status ;
  15:  IF (RI[R[10003]]=OFF) THEN ;
  16:  !Close Hand ;
  17:  RO[R[10001]]=OFF ;
  18:  RO[R[10002]]=ON ;
  19:  ELSE ;
  20:  !Status error ;
  21:  ENDIF ;
  22:   ;
  23:  LBL[1] ;
/POS
/END
