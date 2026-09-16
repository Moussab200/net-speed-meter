# نشر Net Speed Meter لمكان محمي + توقيع رقمي
# ------------------------------------------------------------------
# بيبني، بيوقّع، وبينسخ لـ Program Files (مجلد الكتابة عليه بدها أدمن).
# لازم يتشغّل بصلاحيات أدمن - بيرفع نفسه لحاله إذا ما كان.
#
#   powershell -ExecutionPolicy Bypass -File deploy.ps1
# ------------------------------------------------------------------

$ErrorActionPreference = 'Stop'

$SrcDir     = $PSScriptRoot
$SrcExe     = Join-Path $SrcDir 'NetSpeedMeter.exe'
$InstallDir = 'C:\Program Files\Net Speed Meter'
$InstallExe = Join-Path $InstallDir 'NetSpeedMeter.exe'
$CertSubject = 'CN=Net Speed Meter (Personal)'
$HashRef    = Join-Path $env:APPDATA 'NetSpeedMeter\release.sha256'

function Say($m, $c = 'Gray') { Write-Host $m -ForegroundColor $c }

# --- لازم أدمن للكتابة على Program Files ---
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Say "[!] بدها صلاحيات أدمن - رح أطلبها منك..." Yellow
    Start-Process powershell -Verb RunAs -ArgumentList @(
        '-ExecutionPolicy','Bypass','-NoExit','-File',"`"$PSCommandPath`"") | Out-Null
    return
}

# --- 0) بوابة الجودة - ما بننشر إذا في أي فشل ---
Say "`n[0/6] بوابة الجودة..." Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $SrcDir 'check-all.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) {
    Say "[!] بوابة الجودة فشلت - النشر انلغى" Red
    Say "    شغّل check-all.ps1 لتشوف التفاصيل" Yellow
    return
}
Say "      كل الفحوصات نجحت" Green

# --- 1) بناء نسخة جديدة ---
Say "`n[1/6] بناء نسخة جديدة..." Cyan
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }

Push-Location $SrcDir
$icon = if (Test-Path (Join-Path $SrcDir 'icon.ico')) { '/win32icon:icon.ico' } else { '' }
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:app.manifest $icon `
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /out:NetSpeedMeter.exe /warn:4 NetSpeedMeter.cs SpeedTest.cs
Pop-Location
if ($LASTEXITCODE -ne 0) { Say "[!] فشل البناء" Red; return }
Say "      تم - $([math]::Round((Get-Item $SrcExe).Length/1KB,1)) KB" Green

# --- 2) شهادة التوقيع (بتنعمل مرة وحدة وبتضل) ---
Say "[2/6] تجهيز شهادة التوقيع..." Cyan
$cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $CertSubject -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate -Subject $CertSubject `
        -Type CodeSigningCert -CertStoreLocation Cert:\CurrentUser\My `
        -KeyUsage DigitalSignature -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears(10)
    # نضيفها للجهات الموثوقة حتى ويندوز يعتبر التوقيع Valid مش UnknownError
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root','CurrentUser')
    $store.Open('ReadWrite'); $store.Add($cert); $store.Close()
    Say "      انعملت شهادة جديدة (صالحة 10 سنين)" Green
} else {
    Say "      الشهادة موجودة أصلاً" Green
}

# --- 3) توقيع الملف ---
Say "[3/6] توقيع الملف..." Cyan
$sig = Set-AuthenticodeSignature -FilePath $SrcExe -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com' `
        -ErrorAction SilentlyContinue
if (-not $sig -or $sig.Status -ne 'Valid') {
    # لو ما في إنترنت للطابع الزمني، منوقّع بدونه
    $sig = Set-AuthenticodeSignature -FilePath $SrcExe -Certificate $cert -HashAlgorithm SHA256
}
# ملاحظة: أول ما تنضاف الشهادة لمخزن الجهات الموثوقة، العملية الحالية بتضل
# ماسكة نتيجة فحص الثقة القديمة بالذاكرة، فبتقرا UnknownError رغم إن التوقيع
# سليم فعلاً. أي عملية جديدة (متل verify.ps1) بتشوفه Valid عادي.
$final = Get-AuthenticodeSignature $SrcExe
if ($final.Status -eq 'Valid') {
    Say "      حالة التوقيع: Valid" Green
} else {
    Say "      حالة التوقيع: $($final.Status) - غالباً ذاكرة الثقة بهالجلسة" Yellow
    Say "      شغّل verify.ps1 بعدين للتأكد الفعلي" DarkGray
}

# --- 4) النسخ للمجلد المحمي ---
Say "[4/6] تنزيل النسخة بالمجلد المحمي..." Cyan
Get-Process | Where-Object { $_.Path -eq $InstallExe -or $_.ProcessName -like '*Speed*' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item $SrcExe $InstallExe -Force
Say "      $InstallExe" Green

# نخزّن البصمة المرجعية لفحصها لاحقاً بـ verify.ps1
New-Item -ItemType Directory -Force (Split-Path $HashRef) | Out-Null
(Get-FileHash $InstallExe -Algorithm SHA256).Hash | Set-Content $HashRef -Encoding ASCII

# --- 5) تحديث الاختصارات ونقاط التشغيل ---
Say "[5/6] تحديث الاختصارات..." Cyan

# واجهة WScript.Shell بترمي استثناء إذا مسار الحفظ فيه حروف غير لاتينية
# (متل مجلد سطح المكتب "سطح المكتب" تبع OneDrive). الحل: ننشئ الاختصار
# بمسار مؤقت إنجليزي، وبعدين ننقله بـ .NET يلي بيتعامل مع Unicode صح.
function New-Lnk([string]$Dest, [string]$Target, [string]$WorkDir) {
    $tmp = Join-Path $env:TEMP ('nsm_' + [Guid]::NewGuid().ToString('N') + '.lnk')
    $sh = New-Object -ComObject WScript.Shell
    $s = $sh.CreateShortcut($tmp)
    $s.TargetPath = $Target
    $s.WorkingDirectory = $WorkDir
    $s.IconLocation = ($Target + ',0')
    $s.Description = 'Net Speed Meter'
    $s.Save()
    if ([System.IO.File]::Exists($Dest)) { [System.IO.File]::Delete($Dest) }
    [System.IO.File]::Move($tmp, $Dest)
}

# اختصار قائمة ابدأ (حتى بحث ويندوز يلاقيه)
try {
    $startLink = Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) 'Programs\Net Speed Meter.lnk'
    New-Lnk $startLink $InstallExe $InstallDir
    Say "      اختصار قائمة ابدأ جاهز" Green
} catch { Say "      [!] تعذّر اختصار قائمة ابدأ: $($_.Exception.Message)" Yellow }

# اختصار سطح المكتب - بننشئه **قبل** ما نشيل النسخة القديمة، حتى لو فشل
# ما يضل المستخدم بلا أيقونة على سطح المكتب
$desk = [Environment]::GetFolderPath('Desktop')
try {
    New-Lnk (Join-Path $desk 'Net Speed Meter.lnk') $InstallExe $InstallDir
    Say "      اختصار سطح المكتب جاهز" Green

    $oldExe = Join-Path $desk 'Net Speed Meter.exe'
    if ([System.IO.File]::Exists($oldExe)) {
        [System.IO.File]::Delete($oldExe)
        Say "      انشالت نسخة exe القديمة من سطح المكتب" Green
    }
} catch { Say "      [!] تعذّر اختصار سطح المكتب: $($_.Exception.Message)" Yellow }

# لو التشغيل التلقائي مفعّل، نوجّهه للمكان الجديد
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty $runKey -ErrorAction SilentlyContinue).NetSpeedMeter) {
    Set-ItemProperty $runKey -Name NetSpeedMeter -Value ('"' + $InstallExe + '"')
    Say "      التشغيل التلقائي انوجّه للمكان الجديد" Green
}
# نشيل الاختصار القديم من قائمة ابدأ الشخصية إذا كان موجود
$oldStart = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Net Speed Meter.lnk'
if ([System.IO.File]::Exists($oldStart)) { [System.IO.File]::Delete($oldStart) }

# --- 6) تشغيل ---
Say "[6/6] تشغيل البرنامج..." Cyan
Start-Process $InstallExe
Start-Sleep -Seconds 2

Say "`n===== خلص =====" Green
Say "المكان المحمي : $InstallExe"
Say "التوقيع       : $((Get-AuthenticodeSignature $InstallExe).Status)"
Say "شغّال         : $((Get-Process | Where-Object { $_.ProcessName -like '*Speed*' }) -ne $null)"
Say "`nللفحص بأي وقت:  powershell -ExecutionPolicy Bypass -File `"$SrcDir\verify.ps1`"" Cyan
