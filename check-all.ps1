# بوابة الجودة الكاملة - شغّلها قبل أي نشر
# ------------------------------------------------------------------
# بتفحص كل صنف خطأ وقع فعلياً بهالمشروع، حتى ما يتكرر:
#   1. ترميز الملفات (BOM)  - كسر البناء والنصوص العربية مرتين
#   2. البناء بلا تحذيرات
#   3. صحة سكربتات PowerShell
#   4. اختبارات المنطق (43 اختبار)
#   5. فحوصات أمنية على الكود
#
#   powershell -ExecutionPolicy Bypass -File check-all.ps1
# ------------------------------------------------------------------

$root = $PSScriptRoot
$pass = 0; $fail = 0
function Ok($m)  { Write-Host "  PASS  $m" -ForegroundColor Green; $script:pass++ }
function Bad($m) { Write-Host "  FAIL  $m" -ForegroundColor Red;   $script:fail++ }
function Sec($m) { Write-Host "`n== $m ==" -ForegroundColor Cyan }

Write-Host "=== بوابة الجودة - Net Speed Meter ===" -ForegroundColor Cyan

# --- 1) الترميز ---
# csc.exe و PowerShell 5.1 بيقروا الملف بترميز النظام إذا ما في BOM،
# فالنص العربي بيتحوّل طلاسم وبينكسر البناء. صار فعلياً مرتين.
Sec "ترميز الملفات (BOM)"
foreach ($f in Get-ChildItem $root -Include *.cs,*.ps1 -File -Recurse) {
    $b = [System.IO.File]::ReadAllBytes($f.FullName)
    $hasBom = $b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF
    if ($hasBom) { Ok "$($f.Name) فيه BOM" } else { Bad "$($f.Name) بلا BOM - رح ينكسر النص العربي" }
}

# --- 2) صحة سكربتات PowerShell ---
Sec "صحة سكربتات PowerShell"
foreach ($f in Get-ChildItem $root -Filter *.ps1 -File) {
    $errs = $null
    [System.Management.Automation.PSParser]::Tokenize(
        (Get-Content $f.FullName -Raw), [ref]$errs) | Out-Null
    if ($errs.Count -eq 0) { Ok "$($f.Name) سليم" } else { Bad "$($f.Name): $($errs.Count) خطأ" }
}

# --- 3) البناء بلا تحذيرات ---
Sec "البناء بأقصى مستوى تحذيرات"
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $root
$out = & $csc /nologo /target:winexe /platform:anycpu /optimize+ `
    /win32manifest:app.manifest /win32icon:icon.ico /warn:4 `
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /out:"$env:TEMP\nsm-check.exe" NetSpeedMeter.cs SpeedTest.cs 2>&1
$code = $LASTEXITCODE
Pop-Location
if ($code -eq 0 -and -not ($out -match 'warning')) { Ok "بناء نظيف - صفر أخطاء وصفر تحذيرات" }
else { Bad "مشاكل بالبناء:"; $out | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkRed } }
Remove-Item "$env:TEMP\nsm-check.exe" -ErrorAction SilentlyContinue

# --- 4) اختبارات المنطق ---
Sec "اختبارات المنطق"
$testOut = & cmd /c "`"$root\run-tests.cmd`"" 2>&1
$testCode = $LASTEXITCODE
$summary = ($testOut | Select-String 'نجحت|فشل /' | Select-Object -Last 1)
if ($testCode -eq 0) { Ok "كل اختبارات المنطق نجحت — $summary" }
else {
    Bad "اختبارات فشلت:"
    $testOut | Select-String 'FAIL' | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkRed }
}

# --- 5) فحوصات أمنية على الكود ---
Sec "فحوصات أمنية"
$src = Get-Content "$root\NetSpeedMeter.cs","$root\SpeedTest.cs" -Raw

$urls = [regex]::Matches($src, 'https?://[a-zA-Z0-9./_\-]+') |
        ForEach-Object { ([Uri]$_.Value).Host } | Select-Object -Unique
$allowed = @('speed.cloudflare.com')
$badUrls = $urls | Where-Object { $allowed -notcontains $_ }
if (-not $badUrls) { Ok "ما في عناوين شبكة غير معروفة ($($urls -join ', '))" }
else { Bad "عناوين غير متوقعة: $($badUrls -join ', ')" }

if ($src -notmatch 'Process\.Start|ProcessStartInfo') { Ok "ما بيشغّل أي برنامج خارجي" }
else { Bad "في استدعاء لتشغيل عملية خارجية" }

if ($src -notmatch 'ServerCertificateValidationCallback') { Ok "ما في تجاوز للتحقق من شهادات TLS" }
else { Bad "في تجاوز للتحقق من الشهادات" }

if ($src -notmatch 'requestedExecutionLevel.*requireAdministrator') { Ok "ما بيطلب صلاحيات أدمن" }
else { Bad "بيطلب صلاحيات أدمن" }

$b64 = [regex]::Matches($src, '[A-Za-z0-9+/]{100,}={0,2}')
if ($b64.Count -eq 0) { Ok "ما في بيانات مشفّرة أو مخبّاة بالكود" }
else { Bad "في $($b64.Count) كتلة بيانات مشبوهة" }

# --- الخلاصة ---
Write-Host "`n=================================" -ForegroundColor Cyan
if ($fail -eq 0) {
    Write-Host "  كل الفحوصات نجحت ($pass)" -ForegroundColor Green
    Write-Host "  جاهز للنشر: deploy.ps1" -ForegroundColor Green
    exit 0
} else {
    Write-Host "  $fail فشل / $pass نجح" -ForegroundColor Red
    Write-Host "  لا تنشر قبل الإصلاح." -ForegroundColor Yellow
    exit 1
}
