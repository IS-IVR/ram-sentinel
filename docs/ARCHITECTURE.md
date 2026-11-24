# ארכיטקטורה - RAM Sentinel

## מטרת המוצר

לאתר ולצמצם גורמים מדידים שמאטים את תגובתיות Windows. המוצר אינו RAM Cleaner ואינו מנסה להגדיל מלאכותית את כמות הזיכרון הפנוי.

## שכבות ארכיטקטורה

### 1. Collectors - איסוף נתונים

אוספים מקומיים וקריאה בלבד:
- Memory: Total / Available / Commit / Commit Limit / System Cache / Kernel pools.
- CPU: GetSystemTimes ו-TotalProcessorTime לפי תהליך.
- Disk: נפח פנוי, latency ו-queue באמצעות PDH בקצב נמוך.
- Startup: Run keys, Startup folders ובהמשך Logon Scheduled Tasks.
- Power: Power mode / AC state כאשר אפשר לקרוא זאת באופן אמין.

כל Collector מופרד, מחזיר NotAvailable במקרה כשל ואינו מפיל את האפליקציה. אין סריקות דיסק רקורסיביות או WMI polling תכוף.

### 2. Diagnosis Engine - מנוע אבחון

מקבל חלונות מדידה ומזהה CPU saturation, Memory/Paging pressure, Disk latency, Startup load ו-Low Storage. אין להסיק צוואר בקבוק ממדידה בודדת.

הגדרה מלאה נמצאת ב-[DIAGNOSTICS_ENGINE.md](DIAGNOSTICS_ENGINE.md).

### 3. Recommendation Engine - מנוע המלצות

כל המלצה כוללת:
- ראיות/מדדים והמשך הזמן.
- סיבת ההמלצה ורמת ביטחון.
- תועלת אפשרית וחיסרון.
- הרשאות נדרשות.
- פעולה הפיכה ודרך Restore.

המערכת מציגה Finding ראשי אחד ועד שתי המלצות משניות, לא רשימת אזהרות אינסופית.

### 4. Action Engine - שינוי מבוקר

כל שינוי נוצר כ-ActionPlan, מוצג למשתמש וממתין לאישור. לפני שינוי שומרים Backup מקומי. יש Audit log עם תאריך, ערך ישן, ערך חדש ותוצאת פעולה.

### 5. Verification Engine - מדידה אחרי שינוי

חוזרים על אותה משימה באותם תנאים. מדווחים Median של מספר ריצות ולא תוצאה מיטבית יחידה. אם אין שיפור מעבר לרעש המדידה, אומרים שאין הוכחת שיפור.

## מבנה פרויקטים יעד

- RamSentinel.Core: מודלים, מדדים וחישובים ללא תלות ב-Windows UI.
- RamSentinel.Windows: Collectors ו-Windows APIs (PDH, Process, Registry, Storage).
- RamSentinel.Diagnostics: Detectors, correlation, confidence ו-Findings.
- RamSentinel.Actions: ActionPlan, Backup, Restore ו-Audit.
- RamSentinel.App.WinUI: UI מודרני, אם יעבור Benchmark.
- RamSentinel.App.Legacy: היישום הקיים עד להכרעת UI Framework.
- RamSentinel.Tests: בדיקות יחידה, סימולציות Fixtures ובדיקות אינטגרציה.

אין צורך ב-Windows Service נפרד בשלב הראשון.

## אבחון

כל Detector מחזיר Finding מובנה ולא מבצע פעולה. Finding כולל Category, Status, Confidence, Evidence, Duration, Impact, Recommendation, Risk, Reversible ו-RestorePlan.

RAM, CPU, Disk, Startup ו-Storage נבדקים לפי שילוב מדדים לאורך זמן. ראה [DIAGNOSTICS_ENGINE.md](DIAGNOSTICS_ENGINE.md).

## Startup Inventory

הגרסה הנוכחית קוראת בלבד:
- HKCU Run.
- HKLM Run.
- Startup Folder של המשתמש.
- Startup Folder משותפת.

רשימת Startup אינה מדידת Startup Impact. יש להציג "לא נמדד" עד שיש נתוני CPU/Disk בזמן Logon. בשלב הבא ניתן להשבית HKCU entry בודד עם גיבוי מדויק ו-Restore. HKLM, Services, Drivers ו-Scheduled Tasks לא ינוהלו אוטומטית.

## Smart Application Lifecycle Manager

מנהל מחזור החיים הוא שכבת ניהול נפרדת ממנוע האבחון. הוא צורך את מצב לחץ הזיכרון ממנוע האבחון ואת תצפיות התהליכים, אך אינו משנה את Windows כחלק מאיסוף או סיווג.

שני workflows נפרדים:

1. **תהליכי המשך לאחר יציאת התוכנה הראשית:** מעקב אחר קשרי תהליך אב/בן בזמן שהתוכנה פועלת, זיהוי תהליכי עזר שנותרו לאחר סגירתה, אימות זהות וזמן יצירה, בדיקת פעילות וראיות לסיום העבודה. אין לסגור תהליך לפי שם, PID או חוסר פעילות קצר בלבד.
2. **תוכנות משתמש שאינן בחזית:** מדידת הזמן מאז שהחלון היה בחזית, הצלבה עם CPU ו-I/O לאורך מספר דגימות, מצב לחץ הזיכרון המתמשך ופרופיל התוכנה. ברירת המחדל היא הצעת סגירה בלבד באישור המשתמש.

הרכיבים המתוכננים:
- ProcessInventoryProvider ו-ProcessRelationshipTracker.
- ForegroundApplicationTracker.
- ApplicationProfileCatalog.
- ApplicationSessionTracker ו-ActivityClassifier.
- LifecyclePolicyEngine.
- GracefulShutdownService.
- LifecycleNotificationService ו-LifecycleHistoryStore.

**גבולות פעולה:**
- אין להסיק שתהליך מיותר רק משום שהתוכנה הראשית נסגרה, החלון ממוזער, או CPU נמוך.
- סגירה אוטומטית אפשרית רק עבור פרופיל מאומת, opt-in מפורש של המשתמש, קשר תהליכים שנצפה ואומת, ראיית סיום עבודה ומנגנון סגירה רגיל ומתועד עבור אותו פרופיל.
- תהליך שאין לו Adapter בטוח לא ייסגר אוטומטית. תהליך רקע ללא חלון אינו בהכרח מסוגל לקבל CloseMainWindow.
- אין Process.Kill, TerminateProcess, סגירת Job Object או הסלמה לכוח לאחר timeout.
- תוכנות AI יכולות לשמור מודלים בזיכרון במכוון. כברירת מחדל אין לפרוק מודל טעון אוטומטית.
- לפני כל פעולה יש לאמת מחדש PID, זמן יצירה וזהות executable. אם התהליך חזר לחזית או החל לעבוד, לבטל הצעה.
- Working Set ו-Private Bytes יוצגו בנפרד. אין להציג אותם כסכום RAM מובטח לפינוי.

היסטוריית סשנים תהיה בזיכרון כברירת מחדל. יישמרו מקומית רק העדפות משתמש, דחיות/התעלמויות והיסטוריית פעולות מינימלית. אין לשמור כותרות חלונות, שמות מסמכים, הקלדות, command-line arguments או מלאי מלא של תהליכים.

הפרטים המלאים, שלבי המימוש, תנאי הקבלה ותוכנית הבדיקות מוגדרים ב-[APPLICATION_LIFECYCLE_MANAGER.md](APPLICATION_LIFECYCLE_MANAGER.md).

## תדירות איסוף

- Tray: מדדי מערכת כל 30-60 שניות.
- חלון פתוח: 5-10 שניות לנתונים זולים.
- תהליכים: 10-30 שניות, לפי מצב.
- PDH: לעולם לא יותר מפעם בשנייה; בפועל 5-30 שניות לפי מצב.
- Startup Inventory: רק בעת פתיחת הדף או רענון ידני.
- Quick Diagnosis: דגימה זמנית קצרה בלבד.
- Lifecycle: שימוש חוזר ככל האפשר בדגימות התהליכים הקיימות; אירועי שינוי חלון קדמי במקום polling תכוף.

## ממשק משתמש

מפרט ה-UX נמצא ב-[UX_DESIGN.md](UX_DESIGN.md). הכיוון המומלץ הוא NavigationView, CommandBar, Breadcrumbs, Details Lists ו-Details Pane בסגנון Explorer. יש למדוד את WinUI 3 מול WinForms לפני מעבר.

עמוד עתידי בשם "ניהול תוכנות" יציג תוכנות פעילות, תוכנות ברקע ותהליכי המשך. הודעת חוסר פעילות תהיה לא-מודאלית ותציע בקשת סגירה רגילה, דחייה או התעלמות. אם המשתמש חוזר לתוכנה, ההצעה תבוטל.

## קווי בטיחות

- לא מרוקנים Standby List/File Cache.
- לא משתמשים ב-EmptyWorkingSet/SetProcessWorkingSetSize לתהליכים אחרים.
- לא Process.Kill ולא TerminateProcess.
- לא משנים Page File, Defender, Firewall, Windows Update, SysMain או שירותי מערכת.
- לא מוחקים קבצים אישיים.
- כל Action חייב להיות opt-in, reversible ומתועד.
- אי-זמינות של מדד אינה אפס ואינה ראיה להיעדר פעילות.
- תהליך לא מוכר, מוגן או בעל קשר תהליכים לא ודאי נשאר observation-only.

## מדדי הצלחה

- Median sign-in-to-idle.
- Median app launch time, לפחות שלוש ריצות.
- Idle CPU לאחר settling period.
- Disk latency במהלך workload.
- RAM Available ו-Commit בזמן multitasking.
- צריכת RAM/CPU של RAM Sentinel עצמה.
- שיעור סיווג שגוי של תהליך עובד כתהליך לא פעיל.
- שיעור ביטולי הצעות לאחר שהמשתמש חזר לתוכנה.
- הצלחה של בקשות סגירה רגילות ללא אובדן עבודה.
- משאבי RAM Sentinel שנוספו עקב ניטור מחזור החיים.
