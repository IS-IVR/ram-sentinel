# RAM Sentinel

תוכנת Windows זעירה לניטור זיכרון RAM, שרצה באזור ההתראות (System Tray).

> **עיקרון חשוב:** RAM Sentinel אינה מנסה לרוקן בכוח את זיכרון המטמון של Windows או לשנות Working Set של תהליכים אחרים. Windows משתמשת בזיכרון פנוי גם לצורכי Cache, וזיכרון כזה אינו בהכרח בזבוז. התערבות אגרסיבית עלולה לגרום לטעינה מחדש וליותר האטה.

## מה התוכנה עושה בגרסה הראשונה

- מציגה את סך ה-RAM, הזיכרון שבשימוש והזיכרון הזמין.
- מזהה לחץ זיכרון שנמשך לאורך זמן, ולא מתריעה על קפיצה רגעית.
- מציגה את 15 התהליכים שמשתמשים בכמות הזיכרון הגדולה ביותר.
- מציגה Working Set וזיכרון פרטי לכל תהליך.
- מאפשרת לפתוח את מנהל המשימות של Windows לבדיקה ולטיפול ידני.
- יכולה לעלות עם הכניסה ל-Windows, לפי בחירת המשתמש.
- פועלת מקומית בלבד: אין שרת, אין חשבון, אין Telemetry ואין שליחת נתונים.

## מה היא לא עושה

- לא מסיימת תהליכים אוטומטית.
- לא מסיימת שירותי Windows או תהליכי מערכת.
- לא מרוקנת Standby List או File Cache.
- לא משנה עדיפויות תהליכים ולא מבצעת Trim אגרסיבי ל-Working Set.

## טכנולוגיה

- C# ו-.NET 10 LTS.
- Windows Forms לצורך חלון קטן ואייקון באזור ההתראות.
- Win32 `GlobalMemoryStatusEx` לקריאת נתוני הזיכרון הפיזי.
- ללא חבילות NuGet חיצוניות בשלב הראשון.

## דרישות

- Windows 10 או Windows 11, ‏64-bit.
- לפיתוח: .NET 10 SDK.
- למשתמש קצה: קובץ EXE עצמאי שייבנה ב-Publish self-contained, ללא צורך בהתקנת Runtime נפרד.

## הרצה בפיתוח

```powershell
dotnet run --project .\src\RamSentinel.App\RamSentinel.App.csproj
```

החלון מתחיל ממוזער לאזור ההתראות. לחיצה כפולה על האייקון פותחת אותו.

## בנייה לקובץ EXE עצמאי

```powershell
dotnet publish .\src\RamSentinel.App\RamSentinel.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o .\publish
```

קובץ ההפעלה יופיע בתיקיית `publish`.

## Build אוטומטי

כל Push או Pull Request ל-`main` מפעיל GitHub Actions על Windows:
1. התקנת .NET 10 SDK.
2. Restore של ה-Solution.
3. Build במצב Release.
4. Publish עצמאי ל-`win-x64`.
5. העלאת Artifact בשם `ram-sentinel-win-x64`.

את התוצאה אפשר להוריד מהלשונית **Actions** של הריפו, מתוך הרצת Build שהסתיימה בהצלחה.

## מבנה הפרויקט

```text
ram-sentinel/
├── .github/workflows/build.yml
├── docs/
│   ├── ARCHITECTURE.md
│   ├── PRIVACY.md
│   └── ROADMAP.md
└── src/
    ├── RamSentinel.Core/
    │   ├── MemorySnapshot.cs
    │   └── MemoryPressureEvaluator.cs
    └── RamSentinel.App/
        ├── Platform/WindowsMemoryProvider.cs
        ├── Platform/StartupManager.cs
        ├── UI/MainForm.cs
        └── Program.cs
```

## מצב הפרויקט

MVP ראשוני. קוד המקור וה-CI הועלו לריפו. יש להמתין לתוצאת GitHub Actions ולבצע בדיקת הרצה בפועל על Windows לפני שימוש קבוע או הפצה.
