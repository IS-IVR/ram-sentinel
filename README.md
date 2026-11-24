# RAM Sentinel

תוכנת Windows קלה שמטרתה לאתר מה מאט את המחשב, להציג המלצות מבוססות מדידה ולאפשר אופטימיזציות בטוחות והפיכות.

## לא מנקה RAM לשם ניקוי

Windows משתמשת בזיכרון גם כמטמון, ודפים רבים במטמון ניתנים לשימוש חוזר. ריקון יזום של Cache או Working Set עלול לגרום לטעינות חוזרות ולהאטה. לכן RAM Sentinel מתמקדת בעומס אמיתי ובשיפור תגובתיות העבודה.

## מצב הקוד הנוכחי

- ניטור RAM פיזי, Available, Commit, System Cache ו-Kernel Nonpaged.
- ניטור CPU כולל באמצעות GetSystemTimes.
- מדידת CPU לכל תהליך באמצעות הפרשי זמן המעבד בין דגימות.
- זיהוי Normal / Warning / High לפי לחץ זיכרון מתמשך.
- הצגת תהליכים בולטים לפי CPU ו-RAM.
- רשימת תוכנות הפעלה לקריאה בלבד: Run keys ו-Startup folders של המשתמש ושל המחשב.
- קישור ישיר להגדרות Startup של Windows.
- בקשת סגירה רגילה של תוכנת משתמש לאחר אישור מפורש ואימות זהות התהליך.
- ניטור רקע חסכוני: 30 שניות כשהחלון מוסתר, 10 שניות כשהחלון פתוח.
- הפעלה אופציונלית עם Windows.
- בדיקות אוטומטיות ללוגיקת לחץ זיכרון וחישוב CPU.

## הכיוון החדש

הפרויקט מתוכנן כמערכת אבחון ואופטימיזציה:
1. לזהות אם צוואר הבקבוק הוא RAM, CPU, דיסק, תוכנות הפעלה או אחסון.
2. להציג ראיות והסבר לכל המלצה.
3. לאפשר שינויים הפיכים בלבד, באישור המשתמש.
4. למדוד לפני ואחרי כדי לוודא שהיה שיפור אמיתי.

סדר הפיתוח המומלץ: אבחון דיסק ו-CPU לכל תהליך, מנהל Startup עם גיבוי ושחזור, אבחון נפח אחסון, פרופיל אפקטים חזותיים ומצב צריכת חשמל לפי בחירת המשתמש.

## מה לא נעשה

- לא מרוקנים Standby List או File Cache.
- לא מסיימים תהליכים או שירותי Windows בכוח.
- לא משביתים Defender, Windows Update, Firewall, SysMain או שירותים אקראיים.
- לא משביתים Page File.
- לא מוחקים קבצים אישיים או ערכי Registry באופן גורף.
- לא מבטיחים אחוז האצה קבוע.

## טכנולוגיה

- C# / .NET 10 LTS
- Windows Forms, ממשק עברי RTL
- Win32 APIs ומדידות מקומיות
- ללא שירות Windows נפרד וללא Telemetry

## Build ובדיקות

```powershell
dotnet restore .\ram-sentinel.sln
dotnet build .\ram-sentinel.sln -c Release
dotnet run --project .\tests\RamSentinel.Core.Tests\RamSentinel.Core.Tests.csproj -c Release
```

## פרסום EXE עצמאי

```powershell
dotnet publish .\src\RamSentinel.App\RamSentinel.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

## מסמכי הפרויקט

- [אסטרטגיית מוצר](docs/PRODUCT_STRATEGY.md)
- [ארכיטקטורה](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [פרטיות](docs/PRIVACY.md)

## הגבלת הבטחה

לא כל מחשב איטי בגלל RAM. אם צוואר הבקבוק הוא דיסק מכני איטי, CPU חלש, התחממות, דרייבר או רשת, המערכת תציג את האבחון המתאים ולא תציג "ניקוי זיכרון" כפתרון קסם.
