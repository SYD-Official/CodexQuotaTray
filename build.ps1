$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$buildDirectory = Join-Path $projectRoot 'build'
$distributionDirectory = Join-Path $projectRoot 'dist'
$appVersion = '2.6.0'
$outputName = "CodexQuotaTray-v$appVersion.exe"
$readmeOutputName = "CodexQuotaTray-v$appVersion-README.txt"
$iconPath = Join-Path $buildDirectory 'CodexQuotaTray.ico'
$uiAutomationClient = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll'
$uiAutomationTypes = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll'
$windowsBase = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'
$presentationCore = 'C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll'
$presentationFramework = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll'

if (-not (Test-Path $compiler)) {
    throw '.NET Framework C# compiler was not found.'
}

New-Item -ItemType Directory -Force $buildDirectory, $distributionDirectory | Out-Null

& $compiler /nologo /codepage:65001 /target:exe /out:"$buildDirectory\IconGenerator.exe" /reference:System.Drawing.dll "$projectRoot\tools\IconGenerator.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon generator compilation failed.' }
& "$buildDirectory\IconGenerator.exe" $iconPath
if ($LASTEXITCODE -ne 0) { throw 'Application icon generation failed.' }

$sources = Get-ChildItem (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler /nologo /codepage:65001 /target:winexe /optimize+ /win32manifest:"$projectRoot\app.manifest" /win32icon:"$iconPath" `
    /out:"$distributionDirectory\$outputName" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    /reference:"$uiAutomationClient" /reference:"$uiAutomationTypes" /reference:"$windowsBase" /reference:"$presentationCore" /reference:"$presentationFramework" $sources
if ($LASTEXITCODE -ne 0) { throw 'Main application compilation failed.' }

$testSources = @(
    "$projectRoot\src\Models.cs",
    "$projectRoot\src\AppSettings.cs",
    "$projectRoot\src\CodexCompletionMonitor.cs",
    "$projectRoot\src\SessionQuotaReader.cs",
    "$projectRoot\src\RingIconFactory.cs",
    "$projectRoot\src\UpdateService.cs",
    "$projectRoot\src\ThemePalette.cs",
    "$projectRoot\tests\ParserTests.cs"
)
& $compiler /nologo /codepage:65001 /target:exe /optimize+ /out:"$buildDirectory\ParserTests.exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $testSources
if ($LASTEXITCODE -ne 0) { throw 'Test program compilation failed.' }

$previewSources = @(
    "$projectRoot\src\AcrylicEffect.cs",
    "$projectRoot\src\LayeredTextOverlay.cs",
    "$projectRoot\src\AppSettings.cs",
    "$projectRoot\src\Models.cs",
    "$projectRoot\src\SessionQuotaReader.cs",
    "$projectRoot\src\UpdateService.cs",
    "$projectRoot\src\ThemePalette.cs",
    "$projectRoot\src\RingIconFactory.cs",
    "$projectRoot\src\QuotaPopupForm.cs",
    "$projectRoot\src\TaskbarWidgetForm.cs",
    "$projectRoot\src\AssemblyInfo.cs",
    "$projectRoot\tools\PreviewHarness.cs"
)
& $compiler /nologo /codepage:65001 /target:exe /optimize+ /win32manifest:"$projectRoot\app.manifest" /out:"$buildDirectory\PreviewHarness.exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    /reference:"$uiAutomationClient" /reference:"$uiAutomationTypes" /reference:"$windowsBase" /reference:"$presentationCore" /reference:"$presentationFramework" $previewSources
if ($LASTEXITCODE -ne 0) { throw 'Preview harness compilation failed.' }

$readmeTemplate = Get-Content (Join-Path $projectRoot 'README-delivery-template.txt') -Raw -Encoding UTF8
$outputPath = Join-Path $distributionDirectory $outputName
$readmeContent = $readmeTemplate `
    -replace '\{\{VERSION\}\}', $appVersion `
    -replace '\{\{FILE_NAME\}\}', $outputName `
    -replace '\{\{FILE_SIZE\}\}', (Get-Item $outputPath).Length.ToString() `
    -replace '\{\{SHA256\}\}', (Get-FileHash $outputPath -Algorithm SHA256).Hash
[System.IO.File]::WriteAllText(
    (Join-Path $distributionDirectory $readmeOutputName),
    $readmeContent,
    (New-Object System.Text.UTF8Encoding($true)))
Write-Host "Build complete: $distributionDirectory\$outputName"
