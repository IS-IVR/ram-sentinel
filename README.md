# RAM Sentinel

תוכנת Windows קלה לאבחון גורמים שמאטים את המחשב, ולהמלצה על שיפורים בטוחים והפיכים.

## עקרון המוצר

היעד אינו להציג יותר RAM פנוי. היעד הוא לשפר תגובתיות אמיתית: פתיחת תוכנות, עבודה בריבוי משימות, זמן עליית Windows ופעולות דיסק. כל המלצה נשענת על מדידה לאורך זמן, וכל שינוי צריך להיות מאושר, ניתן לשחזור ומאומת לפני/אחרי.

## יכולות איסוף קיימות

- RAM פיזי, Available, Commit, System Cache ו-Kernel Nonpaged.
- CPU כולל ולפי תהליך.
- זמני קריאה/כתיבה לדיסק, תור ופעילות באמצעות PDH.
- נפח פנוי בכונן המערכת.
- Startup Inventory לקריאה בלבד של Run keys ו-Startup folders.
- רשימת תהליכים המשלבת CPU ו-RAM.
- בקשת סגירה רגילה של אפליקציית משתמש לאחר אישור.
- ניטור חסכוני באזור ההתראות.

## התכנון החדש

מנוע אבחון ישלב חמש משפחות:
1. מחסור RAM אמיתי ו-Paging.
2. CPU עמוס, כולל תהליך או Thread מוביל.
3. Disk latency ו-Queue מתמשכים.
4. תוכנות הפעלה ותהליכי רקע עם השפעה שנמדדה.
5. מחסור בשטח אחסון.

לא נכריז על צוואר בקבוק לפי מדד בודד. כל Finding יציג Evidence, משך, Confidence והמלצה. פריטי Startup שלא נמדדה השפעתם יוצגו כ-"לא נמדד".

## הממשק המתוכנן

ממשק בסגנון Explorer של Windows 11:
- Sidebar לניווט.
- Title Bar, חיפוש ו-Command Bar.
- Breadcrumbs.
- רשימות Details קומפקטיות.
- Details Pane לפריט נבחר.
- Home עם הגורם העיקרי והמלצות ממוקדות.

מפרט UX: [docs/UX_DESIGN.md](docs/UX_DESIGN.md)  
מנוע אבחון: [docs/DIAGNOSTICS_ENGINE.md](docs/DIAGNOSTICS_ENGINE.md)  
החלטת UI Framework: [docs/UI_FRAMEWORK_DECISION.md](docs/UI_FRAMEWORK_DECISION.md)

## טכנולוגיה

- C# / .NET 10
- Windows APIs / PDH
- ניטור מקומי בלבד, ללא Telemetry.
- WinUI 3 ייבחן ב-Proof of Concept מול WinForms הקיים לפני הגירה, כדי לא להקריב זיכרון וזמן הפעלה בשביל מראה.

## מה לא נעשה

- ניקוי Standby List או File Cache.
- סיום כפוי של תהליכים או שירותים.
- השבתת Defender, Windows Update, Firewall או שירותי מערכת.
- ביטול Page File.
- מחיקת קבצים אישיים או Registry גורפת.
- הבטחת אחוז האצה קבוע.

## Build ובדיקות

```powershell
dotnet restore .\ram-sentinel.sln
dotnet build .\ram-sentinel.sln -c Release
dotnet run --project .\tests\RamSentinel.Core.Tests\RamSentinel.Core.Tests.csproj -c Release
```

## מסמכים

- [אסטרטגיית מוצר](docs/PRODUCT_STRATEGY.md)
- [ארכיטקטורה](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [פרטיות](docs/PRIVACY.md)


## מצב המימוש

מנוע אבחון Core ראשוני נוסף בפרויקט: הוא בודק לחץ RAM, עומס CPU, השהיית דיסק ומחסור בשטח אחסון על בסיס דגימות לאורך זמן. הוא מתעלם מקפיצות קצרות, מסמן נתונים חסרים כלא זמינים, ומחזיר ממצאים עם ראיות ורמת ביטחון. מדידת השפעת תוכנות הפעלה עדיין אינה קיימת ולכן מוצגת כ-"לא נמדד".

**חיבור לממשק:** מסך אבחון בעברית מציג ממצא מרכזי ועד שני ממצאים נוספים. מדידות אבחון נאספות כל 30 שניות ונשמרות בזיכרון בלבד למשך עד 30 דקות (מקסימום 61 דגימות). התוכנה נפתחת בחלון גלוי.

**גבולות בטיחות:** האבחון אינו סוגר תהליכים, משנה הגדרות Windows או מוחק קבצים. היסטוריית מדידות נמחקת בעת סגירת התוכנה. Paging ו-Startup Impact לא יוצגו כעובדה לפני שתתווסף מדידה אמינה שלהם.
