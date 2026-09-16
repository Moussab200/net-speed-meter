# فحص سلامة Net Speed Meter
# ------------------------------------------------------------------
# بيفحص إذا الملف انستبدل أو انعدّل، وبيتأكد إن نقاط التشغيل ما انحرفت.
# ما بدّه صلاحيات أدمن - فحص قراءة فقط.
#
#   powershell -ExecutionPolicy Bypass -File verify.ps1
# ------------------------------------------------------------------

$InstallExe = 'C:\Program Files\Net Speed Meter\NetSpeedMeter.exe'
$HashRef    = Join-Path $env:APPDATA 'NetSpeedMeter\release.sha256'

$pass = 0; $fail = 0
function Ok($m)   { Write-Host "  [سليم]  $m" -ForegroundColor Green;  $script:pass++ }
function Bad($m)  { Write-Host "  [تحذير] $m" -ForegroundColor Red;    $script:fail++ }
function Note($m) { Write-Host "  [معلومة] $m" -ForegroundColor DarkGray }

Write-Host "`n=== فحص سلامة Net Speed Meter ===`n" -ForegroundColor Cyan

# --- 1) الملف موجود ---
Write-Host "[1] الملف التنفيذي" -ForegroundColor Cyan
if (-not (Test-Path $InstallExe)) {
    Bad "الملف مش موجود بـ $InstallExe"
    Write-Host "`nشغّل deploy.ps1 أول." -ForegroundColor Yellow
    return
}
Ok "موجود ($([math]::Round((Get-Item $InstallExe).Length/1KB,1)) KB)"

# --- 2) التوقيع الرقمي ---
Write-Host "`n[2] التوقيع الرقمي" -ForegroundColor Cyan
$sig = Get-AuthenticodeSignature $InstallExe
if ($sig.Status -eq 'Valid') {
    Ok "التوقيع سليم - الموقّع: $($sig.SignerCertificate.Subject)"
} elseif ($sig.Status -eq 'NotSigned') {
    Bad "الملف مش موقّع إطلاقاً - إما نسخة قديمة أو انستبدل"
} else {
    Bad "التوقيع غير صالح ($($sig.Status)) - الملف على الأغلب انعدّل"
}

# --- 3) البصمة مقابل المرجع ---
Write-Host "`n[3] بصمة الملف (SHA-256)" -ForegroundColor Cyan
if (Test-Path $HashRef) {
    $expected = (Get-Content $HashRef -Raw).Trim()
    $actual   = (Get-FileHash $InstallExe -Algorithm SHA256).Hash
    if ($expected -eq $actual) {
        Ok "مطابقة للنسخة المنشورة"
    } else {
        Bad "البصمة مختلفة! الملف انعدّل بعد النشر"
        Note "المتوقع: $expected"
        Note "الحالي : $actual"
    }
} else {
    Note "ما في بصمة مرجعية محفوظة - شغّل deploy.ps1 لتسجيلها"
}

# --- 4) حماية المجلد ---
Write-Host "`n[4] حماية المجلد من الكتابة" -ForegroundColor Cyan
$dir = Split-Path $InstallExe
$risky = (Get-Acl $dir).Access | Where-Object {
    $_.AccessControlType -eq 'Allow' -and
    $_.FileSystemRights -match 'FullControl|Modify|Write' -and
    $_.IdentityReference -match 'Everyone|Authenticated Users|Users|INTERACTIVE'
}
if ($risky) {
    Bad "في حسابات عادية بتقدر تكتب على المجلد:"
    $risky | ForEach-Object { Note "$($_.IdentityReference) → $($_.FileSystemRights)" }
} else {
    Ok "الكتابة محصورة بالأدمن - مالوير بحساب عادي ما بيقدر يستبدل الملف"
}

# --- 5) نقاط التشغيل ---
Write-Host "`n[5] نقاط التشغيل (تكشف إعادة توجيه خبيثة)" -ForegroundColor Cyan
$sh = New-Object -ComObject WScript.Shell

$run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue).NetSpeedMeter
if ($run) {
    if ($run.Trim('"') -eq $InstallExe) { Ok "التشغيل التلقائي يأشّر على المكان الصح" }
    else { Bad "التشغيل التلقائي يأشّر على مكان غريب: $run" }
} else { Note "التشغيل التلقائي مش مفعّل" }

# قراءة هدف الاختصار: WScript.Shell بيرجّع فاضي إذا مسار الاختصار فيه حروف
# غير لاتينية (سطح المكتب العربي تبع OneDrive)، فمننسخه لمسار مؤقت إنجليزي
# ومنقراه من هناك.
function Get-LnkTarget([string]$Path) {
    $sh2 = New-Object -ComObject WScript.Shell
    $t = ''
    try { $t = $sh2.CreateShortcut($Path).TargetPath } catch { }
    if ([string]::IsNullOrEmpty($t)) {
        $tmp = Join-Path $env:TEMP ('nsmchk_' + [Guid]::NewGuid().ToString('N') + '.lnk')
        try {
            [System.IO.File]::Copy($Path, $tmp, $true)
            $t = $sh2.CreateShortcut($tmp).TargetPath
        } catch { }
        finally { if ([System.IO.File]::Exists($tmp)) { [System.IO.File]::Delete($tmp) } }
    }
    return $t
}

foreach ($lnk in @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Net Speed Meter.lnk'),
    (Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) 'Programs\Net Speed Meter.lnk')
)) {
    if ([System.IO.File]::Exists($lnk)) {
        $t = Get-LnkTarget $lnk
        $name = Split-Path (Split-Path $lnk) -Leaf
        if ($t -eq $InstallExe) { Ok "اختصار ($name) يأشّر على المكان الصح" }
        elseif ([string]::IsNullOrEmpty($t)) { Note "اختصار ($name) موجود بس تعذّرت قراءة هدفه" }
        else { Bad "اختصار ($name) يأشّر على: $t" }
    }
}

# --- 6) نسخ زيادة ---
Write-Host "`n[6] نسخ إضافية (ممكن تلخبط أو تكون قديمة)" -ForegroundColor Cyan
$extra = @()
$deskExe = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Net Speed Meter.exe'
if (Test-Path -LiteralPath $deskExe) { $extra += $deskExe }
if ($extra.Count -eq 0) { Ok "ما في نسخ زيادة" }
else { $extra | ForEach-Object { Bad "نسخة زيادة: $_" } }

# --- الخلاصة ---
Write-Host "`n=================================" -ForegroundColor Cyan
if ($fail -eq 0) {
    Write-Host " كل الفحوصات سليمة ($pass)" -ForegroundColor Green
    Write-Host " البرنامج ما انمسّ." -ForegroundColor Green
} else {
    Write-Host " $fail تحذير / $pass سليم" -ForegroundColor Red
    Write-Host " راجع التحذيرات فوق." -ForegroundColor Yellow
}
Write-Host "=================================`n" -ForegroundColor Cyan
