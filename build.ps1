param([switch]$Installer, [switch]$Verify)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'BambuddyDesktop.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile
$version = $project.Project.PropertyGroup.Version
$sdkVersion = ($project.Project.ItemGroup.PackageReference | Where-Object Include -EQ 'Microsoft.Web.WebView2').Version
$portable = Join-Path $projectRoot 'dist\portable'
New-Item -ItemType Directory -Force $portable | Out-Null

dotnet build $projectFile -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$binaryDirectory = Join-Path $projectRoot 'bin\Release\net48'
foreach ($name in @('Bambuddy Desktop.exe', 'Bambuddy Desktop.exe.config',
    'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $binaryDirectory $name) -Destination $portable -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'app.ico') -Destination (Join-Path $portable "Bambuddy Desktop $version.ico") -Force
foreach ($name in @('README.md', 'LICENSE', 'THIRD-PARTY.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $portable -Force
}
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
foreach ($name in @('LICENSE.txt', 'NOTICE.txt')) {
    Copy-Item -LiteralPath (Join-Path $packages "microsoft.web.webview2\$sdkVersion\$name") -Destination (Join-Path $portable "WebView2-$name") -Force
}

if ($Verify) {
    foreach ($test in @('--self-test', '--browser-test')) {
        $process = Start-Process -FilePath (Join-Path $portable 'Bambuddy Desktop.exe') -ArgumentList $test -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(30000)) { Stop-Process -Id $process.Id; throw "Timed out: $test" }
        if ($process.ExitCode -ne 0) { throw "Failed: $test" }
    }
    foreach ($name in @('self-test.txt', 'browser-test.txt')) {
        $resultFile = Join-Path $portable $name
        Get-Content -LiteralPath $resultFile
        Move-Item -LiteralPath $resultFile -Destination (Join-Path $projectRoot "dist\$name") -Force
    }
}

Compress-Archive -Path (Join-Path $portable '*') -DestinationPath (Join-Path $projectRoot 'dist\Bambuddy Desktop Portable.zip') -Force
if ($Installer) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
    if (-not $compiler) { $compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install Inno Setup 6 or add ISCC.exe to PATH.' }
    & $compiler /Q "/DAppVersion=$version" (Join-Path $projectRoot 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
}
