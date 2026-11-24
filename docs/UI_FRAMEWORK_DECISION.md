# החלטת תכנון - Framework לממשק Windows 11

## המצב הקיים

היישום הנוכחי הוא WinForms על .NET 10. הוא קל יחסית ופשוט לפריסה, אך כדי להגיע לממשק שנראה ומתנהג כמו Windows 11 File Explorer נדרשים רכיבי Fluent, ניווט, Breadcrumbs, רשימות Details ולוחות פרטים.

## אפשרויות

### WinUI 3
יתרונות:
- רכיבי Windows מודרניים ו-NavigationView.
- התאמה טבעית ל-Fluent Design ול-Mica.
- תכנון טוב למסכי Sidebar + Command Bar + Content.

חסרונות:
- Windows App SDK מוסיף תלות הפצה.
- פרסום unpackaged/self-contained מגדיל משמעותית את הפלט ועלול להגדיל startup וזיכרון.
- נדרשת בחירת מודל התקנה: MSIX/framework-dependent או self-contained.

### WinForms הנוכחי
יתרונות:
- כבר עובד.
- פריסה פשוטה יחסית.
- אין צורך להחליף את שכבת ההפעלה.

חסרונות:
- קשה יותר להגיע למראה ולתחושת שימוש מדויקים של Explorer.
- בניית רכיבים מודרניים דורשת יותר עיצוב ידני.

## החלטה מומלצת

לא לבצע מעבר מלא לפני Benchmark. ליצור Proof of Concept קטן ב-WinUI 3 הכולל:
1. חלון עם NavigationView, TitleBar ו-Mica.
2. עמוד Processes עם רשימת Details.
3. עמוד Diagnosis עם Finding ו-InfoBar.
4. Tray/Background lifecycle בסיסי.
5. אותו Core ואותם Collectors של האפליקציה הקיימת.

למדוד מול WinForms הקיים על מחשב חלש:
- Cold start עד חלון אינטראקטיבי.
- Idle Private Bytes אחרי 2 דקות.
- Working Set אחרי 10 דקות.
- Average CPU בזמן Tray.
- גודל חבילת ההפצה.
- זמן התקנה ועדכון.

## מודל פריסה

אם WinUI 3 נבחר:
- להעדיף packaged MSIX עם Windows App SDK framework dependency, בתנאי שמודל ההתקנה וה-runtime dependency ברור למשתמש.
- לא לבחור unpackaged self-contained רק כדי לשמור על EXE יחיד, בלי לבדוק מחיר בזיכרון ובזמן טעינה.
- אם ההתקנה/ה-runtime מכבידים על חוויית המשתמש או על מחשבים חלשים, להשאיר את האפליקציה ב-WinForms ולהוסיף Fluent styling מדוד.

## כלל הכרעה

WinUI 3 ייבחר רק אם מבחן על מחשב חלש מראה שמחיר הזיכרון וזמן ההפעלה סביר ביחס לתועלת העיצובית. ה-UI לא יקבל רשות לצרוך משאבים באופן שפוגע במטרה המרכזית של RAM Sentinel.
