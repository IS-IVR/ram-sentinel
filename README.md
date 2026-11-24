# RAM Sentinel

תוכנת Windows קטנה שרצה באזור ההתראות ומטרתה לשפר את תגובתיות המחשב באמצעות זיהוי וניהול של עומס משאבים אמיתי.

## עקרון המוצר

RAM Sentinel אינה מנסה להציג מספר RAM פנוי גדול ככל האפשר. Windows משתמשת בחלק מה-RAM למטמון כדי להאיץ גישה לקבצים ולנתונים. דפים ברשימת Standby נחשבים לזיכרון זמין וניתנים לשימוש חוזר לפי הצורך.

ניקוי Cache או Trim כפוי של Working Sets עלולים לגרום לטעינות חוזרות וליותר פעילות דיסק. לכן התוכנה לא מבצעת אותם.

המטרה היא לשפר את מהירות העבודה בפועל:
1. לזהות לחץ RAM מתמשך ולא קפיצה רגעית.
2. לאתר את התהליכים שצורכים הרבה זיכרון.
3. להציג גם Commit, Cache ונתוני Kernel כדי להבין את מצב הזיכרון הרחב.
4. לאפשר למשתמש לבקש מתוכנת משתמש להיסגר בדרך הרגילה, אחרי אישור.
5. בהמשך, לזהות אפליקציות הפעלה מיותרות ומגמות של זליגת זיכרון.

**אין הבטחה להאצת כל פעולה במחשב:** אם צוואר הבקבוק הוא CPU, דיסק, GPU, חום או רשת, פינוי RAM לא יפתור אותו. בהמשך נוסיף אבחון משאבים כדי למנוע פעולות לא רלוונטיות.

## מה קיים בגרסה הנוכחית

- קריאת זיכרון פיזי דרך `GlobalMemoryStatusEx`.
- קריאת Commit, System Cache, Kernel Nonpaged Pool, מספר תהליכים ו-Threads דרך `GetPerformanceInfo`.
- חישוב Normal / Warning / High לפי שילוב מדדים.
- מצב High מתקבל רק אחרי 30 שניות של לחץ קריטי רציף.
- רשימת 20 התהליכים המובילים לפי Working Set ו-Private Memory, כולל PID.
- בקשת סגירה רגילה לתוכנה שנבחרה בלבד. אין `Process.Kill`; אין סגירה של תהליכי מערכת מוגנים; התוכנה מאמתת PID, שם וזמן התחלת תהליך לפני הפעולה.
- התראה באזור ההתראות בעת כניסה ללחץ זיכרון מתמשך.
- הפעלה עם הכניסה ל-Windows לפי בחירת המשתמש.
- חלון בעברית ובכיווניות RTL.

## מה התוכנה לא עושה

- לא מרוקנת Standby List או File Cache.
- לא מקטינה בכוח Working Sets של תהליכים אחרים.
- לא הורגת תהליכים ולא מסיימת שירותים.
- לא סוגרת תוכנות אוטומטית ברקע.
- לא מבטיחה האצה כאשר צוואר הבקבוק אינו זיכרון.

## טכנולוגיה

- C# ו-.NET 10 LTS.
- Windows Forms.
- Win32 `GlobalMemoryStatusEx` ו-`GetPerformanceInfo`.
- ללא חבילות NuGet חיצוניות.
- ללא שירות Windows, ללא הרשאות מנהל וללא חיבור אינטרנט בזמן הריצה.

## דרישות

- Windows 10 או Windows 11, ‏64-bit.
- לפיתוח: .NET 10 SDK.
- למשתמש קצה: EXE עצמאי שמפורסם עם Runtime מובנה.

## הרצה בפיתוח

```powershell
dotnet run --project .\src\RamSentinel.App\RamSentinel.App.csproj
```

החלון מתחיל ממוזער לאזור ההתראות. לחיצה כפולה על האייקון פותחת אותו.

## Build ובדיקות

```powershell
dotnet restore .\ram-sentinel.sln
dotnet build .\ram-sentinel.sln -c Release
dotnet run --project .\tests\RamSentinel.Core.Tests\RamSentinel.Core.Tests.csproj -c Release
```

## פרסום EXE עצמאי

```powershell
dotnet publish .\src\RamSentinel.App\RamSentinel.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o .\publish
```

## GitHub Actions

כל Push או Pull Request ל-`main` מריץ ב-Windows:
1. התקנת .NET 10 SDK.
2. Restore ו-Build של כל ה-Solution.
3. הרצת בדיקות לוגיקת לחץ הזיכרון.
4. Publish עצמאי ל-`win-x64`.
5. העלאת Artifact בשם `ram-sentinel-win-x64`.

## מבנה הפרויקט

```text
ram-sentinel/
├── .github/workflows/build.yml
├── docs/
│   ├── ARCHITECTURE.md
│   ├── PRIVACY.md
│   └── ROADMAP.md
├── src/
│   ├── RamSentinel.Core/
│   └── RamSentinel.App/
└── tests/
    └── RamSentinel.Core.Tests/
```

## פרטיות

הניטור מתבצע מקומית. אין שליחת מדדי זיכרון, שמות תהליכים או מידע אישי לשרת כלשהו. פירוט נוסף נמצא ב-`docs/PRIVACY.md`.

## מצב הפרויקט

MVP מתקדם בבנייה. בדיקות יחידה בסיסיות ללוגיקת לחץ הזיכרון נוספו ל-CI. יש לבדוק את התוכנה במחשב Windows אמיתי, למדוד את צריכת המשאבים של RAM Sentinel עצמה ולבצע בדיקת יציבות ממושכת לפני הפעלה קבועה.
