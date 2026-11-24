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

### Phase 1 - תשתית תצפית לקריאה בלבד
- [x] מודלי תצפית וזהות תהליך יציבה המבוססת PID וזמן יצירה.
- [x] מעקב Core אחר קשר הורה-ילד רק לאחר שתי תצפיות רצופות של אותן זהויות תהליך.
- [x] איפוס רמת הביטחון לאחר שימוש חוזר ב-PID או פער איסוף ארוך.
- [x] בדיקות לתהליך הורה חסר, PID כפול, PID ממוחזר וסדר דגימות.
- [ ] Windows Process Inventory Provider: איסוף Parent PID, נתוני תהליך ונתוני I/O באופן חסכוני.
- [ ] Foreground-window event tracking ללא איסוף כותרות חלונות או קלט משתמש.
- [ ] חיבור האיסוף למחזור הדגימה הקיים, ללא לולאת סריקה נוספת.
- [ ] בדיקות אינטגרציה על Windows ובדיקת עומס ממושכת.

### Phase 2 - סיווג והצעות
- [ ] Application sessions ו-Activity Classifier המבוססים על כמה דגימות.
- [ ] שימוש בממצא לחץ הזיכרון הקיים בלבד, ללא סף RAM חלופי.
- [ ] התראות לאפליקציות לא פעילות רק כאשר כל תנאי הבטיחות מתקיימים.
- [ ] דחייה/התעלמות מקומית וביטול הצעה כשהמשתמש חוזר לאפליקציה.
- [ ] הצעות בלבד; ללא סגירה אוטומטית של אפליקציות.

### Phase 3 - סגירה רגילה באישור
- [ ] שירות Graceful Shutdown משותף עם אימות זהות מיד לפני הפעולה.
- [ ] שמירת אפשרות לאפליקציה לשמור או לבטל.
- [ ] Timeout אינו גורר סיום כפוי.
- [ ] אימות יציאה ומדידות זיכרון לפני/אחרי.

### Phase 4 - תהליכי עזר מוכרים
- [ ] פרופילים ייעודיים עם ראיית סיום עבודה ומנגנון כיבוי מתועד.
- [ ] ניקוי אוטומטי כבוי כברירת מחדל ומוגבל לפרופילים מאומתים בלבד.

## עקרונות קבועים

1. מהירות עבודה בפועל חשובה יותר מכמות RAM פנוי.
2. לא מבצעים ניקוי RAM אגרסיבי.
3. לא משביתים רכיבי אבטחה/עדכונים/שירותי מערכת.
4. כל שינוי הפיך ומאושר.
5. Finding חייב להסתמך על מדידה מתמשכת וקורלציה.
6. התוכנה עצמה חייבת להיות כמעט בלתי מורגשת.
7. אין הבטחת אחוז האצה בלי בדיקות חוזרות.
