# Open Forge — збирання програми й інсталятора (як у «Тихому ПК»).
# Запускай подвійним кліком по build.bat. Потрібен лише Windows 10/11 — компілятор C# уже є в Windows (.NET Framework 4).
# Те саме робить GitHub Actions (.github/workflows/build.yml): Actions → «Збирання» → «Run workflow».
#   1) збирає OpenForge.exe вбудованим компілятором Windows (csc.exe), один файл без бібліотек;
#   2) інсталятор OpenForge-setup.exe — та сама програма під іншою назвою: файл з «setup» у назві ставить себе в папку користувача.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root
$log = Join-Path $root 'build_log.txt'
Set-Content -Path $log -Value ('Збирання ' + (Get-Date)) -Encoding UTF8
function Say([string]$t) { Write-Host $t; Add-Content -Path $log -Value $t -Encoding UTF8 }

try {
    # ── 1. Програма ──
    $exePath = Join-Path $root 'OpenForge.exe'
    if (Test-Path $exePath) {
        try { [IO.File]::Open($exePath, 'Open', 'ReadWrite', 'None').Close() }
        catch { throw 'Open Forge зараз запущений з цієї папки. Закрий його й запусти збирання ще раз.' }
    }
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (!(Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    if (!(Test-Path $csc)) { throw 'Не знайдено компілятор .NET Framework 4 (csc.exe).' }
    $cargs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/utf8output', '/win32icon:app.ico', '/out:OpenForge.exe',
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Management.dll',
        '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
    $cargs += @(Get-ChildItem $root -Filter *.cs | ForEach-Object { $_.Name })
    Say 'Збираю OpenForge.exe...'
    $ErrorActionPreference = 'Continue'
    $out = & $csc @cargs 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $out | ForEach-Object { Say ([string]$_) }
    if ($code -ne 0) { throw 'Помилка компіляції — подробиці вище і в build_log.txt' }
    Say 'Готово: OpenForge.exe'

    # ── 2. Інсталятор — та сама програма під назвою з «setup» ──
    Copy-Item $exePath (Join-Path $root 'OpenForge-setup.exe') -Force
    Say 'Готово: OpenForge-setup.exe — його й надсилай другові.'
}
catch {
    Say ('ПОМИЛКА: ' + $_.Exception.Message)
    exit 1
}
