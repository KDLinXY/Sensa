$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$dist = Join-Path $root 'dist'
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $dist, $build | Out-Null

$common = @(
    (Join-Path $root 'src\NativeMouse.cs'),
    (Join-Path $root 'src\NativeCursor.cs'),
    (Join-Path $root 'src\GlobalHotkeys.cs'),
    (Join-Path $root 'src\RazerDpi.cs'),
    (Join-Path $root 'src\UpdateManager.cs'),
    (Join-Path $root 'src\Profiles.cs'),
    (Join-Path $root 'src\MainWindow.cs')
)
$refs = @(
    (Join-Path $framework 'WPF\PresentationFramework.dll'),
    (Join-Path $framework 'WPF\PresentationCore.dll'),
    (Join-Path $framework 'WPF\WindowsBase.dll'),
    (Join-Path $framework 'System.Xaml.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'System.Drawing.dll')
)
$refArgs = $refs | ForEach-Object { '/reference:' + $_ }

$appExe = Join-Path $dist 'Sensa.exe'
$testExe = Join-Path $build 'Sensa.Tests.exe'
$icon = Join-Path $root 'assets\Sensa.ico'
$appSources = $common + (Join-Path $root 'src\Program.cs')
$testSources = $appSources + (Join-Path $root 'tests\SelfTests.cs')

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /utf8output `
    "/out:$appExe" "/win32icon:$icon" /main:Sensa.Program $refArgs $appSources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }

& $csc /nologo /target:exe /platform:anycpu /optimize+ /utf8output `
    "/out:$testExe" /main:Sensa.SelfTests $refArgs $testSources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }

& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Self tests failed.' }

$uiExe = Join-Path $build 'Sensa.UiRender.exe'
$uiSources = $appSources + (Join-Path $root 'tests\UiRender.cs')
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /utf8output `
    "/out:$uiExe" /main:Sensa.UiRender $refArgs $uiSources
if ($LASTEXITCODE -ne 0) { throw 'UI test build failed.' }
& $uiExe (Join-Path $build 'ui-render.png')
if ($LASTEXITCODE -ne 0) { throw 'UI render test failed.' }

Write-Host ('Built: ' + $appExe)
