# Roadmap - RAM Sentinel

## M0 - ניטור קיים

- [x] ניטור RAM / Available / Commit / Cache / Kernel.
- [x] ניטור CPU כולל ולפי תהליך.
- [x] PDH Disk latency / queue ושטח פנוי בכונן Windows.
- [x] Startup Inventory לקריאה בלבד עבור Run keys ו-Startup folders.
- [x] רשימת תהליכים שמשלבת CPU ו-RAM.
- [x] חלון Tray בעברית.
- [x] בדיקות לוגיקה בסיסיות ו-GitHub Actions.
- [ ] בדיקת 24 שעות על מחשב Windows חלש.

## M1 - מנוע אבחון אמין

- [x] יצירת Finding model אחיד: Category, Status, Confidence, Evidence, Duration, Impact, Recommendation, Risk ו-RestorePlan.
- [x] Detector ללחץ RAM מתמשך לפי Available / Memory Load / Commit. Paging לא ייקבע עד לאיסוף Hard Faults אמין.
- [x] Detector ל-CPU מתמשך עם שיוך לתהליך מוביל כאשר קיימות מספיק דגימות. זיהוי Thread עדיין לא ממומש.
- [x] Detector ל-Disk עם latency, queue ו-active time לאורך זמן.
- [x] Storage detector שמפריד מחסור שטח מצוואר בקבוק דיסק ומשלב נפח מוחלט ואחוז פנוי.
- [x] Startup impact נשאר "לא נמדד" עד שתתווסף מדידת Logon אמיתית.
- [x] בדיקות Fixtures למדדים תקינים, חסרים, חריגים, Spikes ופערי דגימה.
- [ ] מנוע Correlation מלא בין RAM / CPU / Disk ודירוג Confidence משולב.
- [x] היסטוריה מקומית בזיכרון בלבד ל-30 דקות, עד 61 דגימות.
- [x] חיבור ממצאי האבחון למסך הראשי: ממצא מרכזי ועד שני ממצאים נוספים, בעברית פשוטה.

## M2 - Startup Manager בטוח

- [x] Inventory לקריאה בלבד של HKCU/HKLM Run ו-Startup folders.
- [ ] Publisher וזיהוי מקור אמין.
- [ ] השבתת HKCU entry אחד בכל פעולה.
- [ ] Backup מדויק של Registry/Shortcut לפני שינוי.
- [ ] Restore, collision check ו-Audit log.
- [ ] מדידת Logon-to-Idle על מספר כניסות.
- [ ] אין השבתת HKLM, שירותים, דרייברים או פריטי אבטחה אוטומטית.

## M3 - ממשק בסגנון Windows 11 Explorer

- [ ] מפרט UX מאושר ומתוחזק ב-docs/UX_DESIGN.md.
- [ ] Proof of Concept ב-WinUI 3 עם NavigationView / CommandBar / Breadcrumbs / Details Pane.
- [ ] Benchmark של WinUI 3 מול WinForms במחשב חלש.
- [ ] החלטה על Framework לפי cold start, idle memory, CPU ו-package size.
- [ ] Home, Diagnose, Processes, Startup, Storage, History, Settings.
- [ ] תמיכה מלאה בעברית RTL, Light/Dark, DPI ו-High Contrast.
- [ ] Virtualized Details List וטעינת נתונים הדרגתית.

## M4 - אופטימיזציות מונחות ואימות

- [ ] המלצות מבוססות Evidence ולא "טוויקים" גורפים.
- [ ] Storage Sense / Optimize Drives / Power settings דרך Windows.
- [ ] Visual Effects profile עם הסבר ושחזור.
- [ ] Before/After, לפחות שלוש ריצות ו-Median.
- [ ] לאמת שיפור בפתיחת תוכנה, זמן Logon, CPU, Disk ו-Multitasking.
- [ ] "לא נמצא שיפור מדיד" כאשר זה המצב.

## M5 - הפצה

- [ ] 24-hour soak test.
- [ ] בדיקות על 4/8/16 GB RAM ו-HDD/SSD.
- [ ] תקציב CPU/RAM עצמי.
- [ ] Installer, Uninstaller, Signing ו-Update.
- [ ] פרסום רק עם תוצאות Benchmark משוחזרות.

## M6 - Smart Application Lifecycle Manager

מפרט מלא: [APPLICATION_LIFECYCLE_MANAGER.md](APPLICATION_LIFECYCLE_MANAGER.md).

### M6.1 - איסוף וזיהוי לקריאה בלבד

- [ ] Process identity יציב: PID + creation time + executable identity.
- [ ] מעקב אחר יחסי אב/בן שנצפו בזמן שהתוכנה פועלת; אין להסיק קשר משם תהליך או מ-PPID בודד.
- [ ] Foreground tracking מבוסס Windows events ללא איסוף הקלדות או כותרות חלונות.
- [ ] CPU-time ו-process I/O deltas עם מדדים חסרים כ-unknown.
- [ ] בדיקות PID reuse, process exit, access denied ויחסי תהליכים לא ודאיים.
- [ ] אין הודעות ואין פעולות סגירה בשלב זה.

### M6.2 - סיווג חוסר פעילות והצעות

- [ ] ApplicationSession, ApplicationProfileCatalog ו-ActivityClassifier.
- [ ] זיהוי תוכנה שלא הייתה בחזית במשך 5 דקות כברירת מחדל, עם מספר דגימות תקינות.
- [ ] שימוש במצב לחץ RAM מתמשך ממנוע האבחון הקיים, ולא בסף RAM נפרד.
- [ ] זיהוי פעילות CPU/I/O ופעילות ייעודית לפי פרופיל; מדד חסר אינו אפס.
- [ ] הצעת סגירה לא-מודאלית עם זיכרון, זמן חוסר פעילות, סיכון והשלכות.
- [ ] פעולות "בקש סגירה רגילה", "הזכר לי בהמשך" ו"התעלם".
- [ ] ביטול מיידי של ההצעה כאשר המשתמש חוזר לתוכנה.
- [ ] ברירת מחדל: הצעה בלבד, ללא סגירה אוטומטית.

### M6.3 - סגירה רגילה באישור משתמש

- [ ] חילוץ מנגנון הסגירה הקיים ל-GracefulShutdownService משותף.
- [ ] אימות חוזר של PID, creation time, executable identity ומצב foreground לפני פעולה.
- [ ] טיפול בחלון שמבקש שמירה או מסרב להיסגר.
- [ ] timeout מסתיים ב-"עדיין פועל"; לעולם אין הסלמה לסיום כפוי.
- [ ] מדידה אחרי הפעולה של Working Set, Private Bytes ו-System Available Memory בנפרד.
- [ ] היסטוריית פעולה מקומית ומוגבלת.

### M6.4 - פרופילי תהליכי עזר וסגירה אוטומטית מבוקרת

- [ ] פרופילים מאומתים עם executable/publisher constraints ותפקידי helper.
- [ ] ראיית completion ייעודית לתוכנה; idle CPU/I/O לבדם אינם מספיקים.
- [ ] Graceful shutdown adapter מתועד ונבדק לכל פרופיל.
- [ ] Automatic cleanup כבוי כברירת מחדל ודורש opt-in מפורש לכל פרופיל.
- [ ] FFmpeg לא ייסגר בזמן render ולא על סמך שם התהליך בלבד.
- [ ] מודלי AI שנשמרים בזיכרון לא ייפרקו כברירת מחדל.
- [ ] פרופיל ללא adapter בטוח נשאר observation-only או דורש פעולה ידנית דרך התוכנה.

### M6.5 - בדיקות ויציבות

- [ ] בדיקות Windows אינטגרטיביות עבור כל adapter שמורשה לסגור.
- [ ] בדיקות FFmpeg בזמן עבודה, בסיום עבודה ובמצב המתנה.
- [ ] בדיקות runtimes של AI בזמן טעינת מודל, יצירה, idle עם מודל שמור וסגירה.
- [ ] בדיקות browsers, sync, audio/video, backup, accessibility ו-security tools.
- [ ] בדיקות false-positive, חזרה לחזית, PID reuse ונתונים חסרים.
- [ ] בדיקת soak של 24 שעות ותקציב RAM/CPU של המנהל.
- [ ] לפני/אחרי על מחשבים חלשים ועם כמויות RAM שונות.

## עקרונות קבועים

1. מהירות עבודה בפועל חשובה יותר מכמות RAM פנוי.
2. לא מבצעים ניקוי RAM אגרסיבי.
3. לא משביתים רכיבי אבטחה/עדכונים/שירותי מערכת.
4. כל שינוי הפיך ומאושר.
5. Finding חייב להסתמך על מדידה מתמשכת וקורלציה.
6. התוכנה עצמה חייבת להיות כמעט בלתי מורגשת.
7. אין הבטחת אחוז האצה בלי בדיקות חוזרות.
8. חוסר פעילות אינו הוכחה שתהליך מיותר.
9. אין סגירה כפויה; timeout אינו היתר להרוג תהליך.
10. סגירה אוטומטית מותרת רק לפרופיל מאומת, ב-opt-in, עם ראיית סיום ומנגנון סגירה מתועד.
