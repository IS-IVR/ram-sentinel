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

- [ ] יצירת Finding model אחיד: Category, Status, Confidence, Evidence, Duration, Impact, Recommendation, Risk ו-RestorePlan.
- [ ] Detector ייעודי ל-RAM/Paging עם קורלציה ל-Disk.
- [ ] Detector ל-CPU כולל זיהוי תהליך/Thread מוביל.
- [ ] Detector ל-Disk עם latency, queue, active time וקורלציה לעומס.
- [ ] Storage detector שמפריד מחסור שטח מצוואר בקבוק דיסק.
- [ ] Startup impact יוצג "נמדד" רק לאחר מדידות Logon; אחרת "לא נמדד".
- [ ] בדיקות Fixtures למדדים תקינים, חסרים, חריגים ו-Spikes.
- [ ] מנוע Correlation ודירוג Confidence.
- [ ] היסטוריה מקומית ל-10-30 דקות.
- [ ] מסך "מה מאט את המחשב" עם Finding ראשי ועד שתי המלצות.

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

## עקרונות קבועים

1. מהירות עבודה בפועל חשובה יותר מכמות RAM פנוי.
2. לא מבצעים ניקוי RAM אגרסיבי.
3. לא משביתים רכיבי אבטחה/עדכונים/שירותי מערכת.
4. כל שינוי הפיך ומאושר.
5. Finding חייב להסתמך על מדידה מתמשכת וקורלציה.
6. התוכנה עצמה חייבת להיות כמעט בלתי מורגשת.
7. אין הבטחת אחוז האצה בלי בדיקות חוזרות.
