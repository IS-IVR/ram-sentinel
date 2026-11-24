# ארכיטקטורה - RAM Sentinel

## מטרת המוצר

לאתר ולצמצם גורמים מדידים שמאטים את תגובתיות Windows. המוצר אינו RAM Cleaner ואינו מנסה להגדיל מלאכותית את כמות הזיכרון הפנוי.

## שכבות ארכיטקטורה

### 1. Collectors - איסוף נתונים

אוספים מקומיים וקריאה בלבד:
- Memory: Total / Available / Commit / Commit Limit / System Cache / Kernel pools.
- CPU: GetSystemTimes, TotalProcessorTime לפי תהליך.
- Disk: נפח פנוי, latency ו-queue באמצעות PDH בקצב נמוך.
- Startup: Run keys, Startup folders ובהמשך Logon Scheduled Tasks.
- Power: Power mode / AC state כאשר אפשר לקרוא זאת באופן אמין.

ה-Collectors חייבים להיות קלים, עם timeouts וטיפול בכשל. אין סריקות דיסק רקורסיביות או WMI polling תכוף.

### 2. Diagnosis Engine - מנוע אבחון

מקבל חלונות מדידה ומזהה:
- CPU saturation.
- Memory pressure ו-Commit pressure.
- Disk latency / queue.
- Startup load.
- Low system-drive free space.
- מגמת גידול בזיכרון פרטי של תהליך.

אין להסיק צוואר בקבוק ממדידה בודדת. נדרשים משך, סף והקשר למשימה.

### 3. Recommendation Engine - המלצות

כל המלצה כוללת:
- ראיות/מדדים.
- סיבת ההמלצה.
- Confidence.
- תועלת אפשרית והחיסרון.
- הרשאות נדרשות.
- פעולה הפיכה ודרך Restore.

### 4. Action Engine - שינוי מבוקר

לא לבצע שינוי בזמן האבחון. כל שינוי נוצר כ-ActionPlan, מוצג למשתמש וממתין לאישור. לפני שינוי שומרים Backup מקומי. יש Audit log עם תאריך, ערך ישן, ערך חדש ותוצאת פעולה.

### 5. Verification Engine - מדידה אחרי שינוי

חוזרים על אותה משימה באותם תנאים. מדווחים Median של מספר ריצות ולא תוצאה מיטבית יחידה. אם אין שיפור מעבר לרעש המדידה, אומרים שאין הוכחת שיפור.

## ארכיטקטורה נוכחית בקוד

### RamSentinel.Core
- MemorySnapshot
- MemoryPressureEvaluator
- CpuUsageCalculator

### RamSentinel.App
- WindowsMemoryProvider
- WindowsCpuUsageProvider
- StartupInventoryProvider (קריאה בלבד)
- StartupManager
- MainForm

### RamSentinel.Core.Tests
בדיקות ללא Framework חיצוני ללוגיקת לחץ זיכרון ולחישוב CPU.

## איסוף CPU לפי תהליך

לכל PID נשמרים Process StartTime, TotalProcessorTime וזמן הדגימה. החישוב:

CPU% = DeltaProcessCpuTime / (DeltaWallTime * LogicalProcessorCount) * 100

הדגימה הראשונה משמשת כ-baseline. תהליכים שיצאו מוסרים מה-cache. האחוז מנורמל לכל קיבולת CPU של המכונה, כך שתהליך המשתמש בליבה לוגית אחת במחשב עם ארבע ליבות לוגיות יציג בערך 25%.

## Startup Inventory

הגרסה הנוכחית קוראת בלבד:
- HKCU Run.
- HKLM Run.
- Startup Folder של המשתמש.
- Startup Folder משותפת.

פקודות Registry מוצגות כמו שנשמרו. זיהוי קובץ היעד הוא הערכה בסיסית בלבד, ולא הוכחה שהפריט נחוץ או בטוח. אין עדיין השבתה מתוך האפליקציה.

בשלב הבא ניתן יהיה להשבית פריטי HKCU בלבד:
1. שמירת path, value name, kind, raw command ונתוני המקור.
2. כתיבת Backup לפני שינוי.
3. אישור משתמש.
4. השבתה של פריט בודד בלבד.
5. Restore מדויק, בדיקת התנגשות ויומן פעולה.

פריטי HKLM, Services, Drivers ו-Scheduled Tasks לא יושבתו אוטומטית. Machine-wide changes דורשים תכנון הרשאות ושחזור נפרד.

## תדירות איסוף

- כשהחלון מוסתר: מדדי מערכת כל 30 שניות.
- כשהחלון פתוח: מדדי מערכת כל 10 שניות.
- רשימת תהליכים כל 30 שניות.
- Startup Inventory נסרק בעת פתיחת הלשונית או רענון ידני.
- מדדי Disk דרך PDH, כשימומשו, ייאספו לכל היותר פעם בשנייה ובדרך כלל כל 5-10 שניות.
- אין polling מהיר או timers תכופים כשהיישום במגש.

## אבחון דיסק

PDH מספק ממשק איסוף מוני ביצועים. יש למדוד Avg. Disk sec/Read, Avg. Disk sec/Write, Current Disk Queue Length ו-Disk Transfers/sec לפי זמינות המונים. אין להתייחס ל-spike רגעי כהוכחה. יש לדרוש עומס ממושך וקשר למשימה. WPR/WPA יהיו מסלול מתקדם לאבחון עמוק ולא רכיב שפועל תמיד.

## פעולות Windows מובנות

- Startup apps: הצגה וניהול מבוקר.
- Storage Sense: פתיחת הגדרות והצגת המלצות, בלי מחיקת קבצים אוטומטית.
- Optimize Drives: פתיחת כלי Windows, בלי להפעיל defrag ידני עיוור על כל כונן.
- Visual Effects: הצעת פתיחת Performance Options.
- Power mode: הצעת שינוי רק אחרי הסבר על חום, חשמל וסוללה. אין להפעיל Best Performance כברירת מחדל.

## קווי בטיחות

- לא מרוקנים Standby List/File Cache.
- לא משתמשים ב-EmptyWorkingSet/SetProcessWorkingSetSize לתהליכים אחרים.
- לא Process.Kill ולא TerminateProcess.
- לא משנים Page File, Defender, Firewall, Windows Update, SysMain או שירותי מערכת.
- לא מוחקים קבצים אישיים.
- כל Action חייב להיות opt-in, reversible ומתועד.

## מדדי הצלחה

- Median sign-in-to-idle.
- Median app launch time, לפחות שלוש ריצות.
- Idle CPU לאחר settling period.
- Disk latency במהלך workload.
- RAM Available ו-Commit בזמן multitasking.
- צריכת RAM/CPU של RAM Sentinel עצמה.
