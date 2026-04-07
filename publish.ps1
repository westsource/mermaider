param(
    [string]$Runtime = "win-x64",
    [switch]$Run,
    [switch]$English
)

$null = chcp 65001
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ProjectPath = $PSScriptRoot
$OutputPath = Join-Path $ProjectPath "bin\Release\net10.0\$Runtime\publish"

if ($English) {
    Write-Host "Building Mermaider..." -ForegroundColor Cyan
    Write-Host "Target platform: $Runtime" -ForegroundColor Gray
} else {
    Write-Host "正在打包 Mermaider..." -ForegroundColor Cyan
    Write-Host "目标平台: $Runtime" -ForegroundColor Gray
}

dotnet publish $ProjectPath `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -eq 0) {
    $ExePath = Join-Path $OutputPath "Mermaider.exe"
    $FileInfo = Get-Item $ExePath
    $SizeMB = [math]::Round($FileInfo.Length / 1MB, 2)
    
    if ($English) {
        Write-Host "`nBuild succeeded!" -ForegroundColor Green
        Write-Host "Output path: $ExePath" -ForegroundColor Yellow
        Write-Host "File size: $SizeMB MB" -ForegroundColor Yellow
        
        if ($Run) {
            Write-Host "`nLaunching application..." -ForegroundColor Cyan
            Start-Process $ExePath
        }
    } else {
        Write-Host "`n打包成功!" -ForegroundColor Green
        Write-Host "输出路径: $ExePath" -ForegroundColor Yellow
        Write-Host "文件大小: $SizeMB MB" -ForegroundColor Yellow
        
        if ($Run) {
            Write-Host "`n启动程序..." -ForegroundColor Cyan
            Start-Process $ExePath
        }
    }
} else {
    if ($English) {
        Write-Host "`nBuild failed!" -ForegroundColor Red
    } else {
        Write-Host "`n打包失败!" -ForegroundColor Red
    }
    exit 1
}
