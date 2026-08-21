#requires -Version 5.1

<#
.SYNOPSIS
  下载并解压 OmniConvert 的内置转换引擎(引擎二进制不入 Git 仓库)。

.DESCRIPTION
  当前引擎:
    - Poppler(pdftoppm.exe) —— PDF 渲染为图片(PNG/JPG)的核心引擎。
  后续引擎(FFmpeg 等)将在此脚本中逐步补充,目录结构保持不变(统一放在 Tools\ 下)。

  实现方式(照搬飞鼠格式 FlyingMouse Format 的引擎内置思路):
    1. 从 oschwartz10612/poppler-windows 的 GitHub Release 下载官方 Windows 构建;
    2. 解压;
    3. 移动为 Tools\poppler\Library 布局,供 EngineLocator 查找。

.USAGE
  powershell -ExecutionPolicy Bypass -File scripts\restore-engines.ps1
#>

[CmdletBinding()]
param(
    [string]$ToolsDir = "",
    [string]$PopplerVersion = "26.02.0-0"
)

$ErrorActionPreference = "Stop"

# 注意:ToolsDir 的解析必须放在脚本体内而不是参数默认值中——
# Windows PowerShell 5.1 在计算 param() 默认值表达式时 $PSScriptRoot 为空字符串。
if (-not $ToolsDir) {
    $ToolsDir = Join-Path (Split-Path -Parent $PSScriptRoot) "Tools"
}

function Download-File {
    param([string]$Uri, [string]$Destination)

    Write-Host "下载: $Uri"
    try {
        Start-BitsTransfer -Source $Uri -Destination $Destination -ErrorAction Stop
    }
    catch {
        Write-Warning "BITS 下载失败,改用 Invoke-WebRequest 重试(较慢)..."
        $ProgressPreference = "SilentlyContinue"
        Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing
    }
}

$pdftoppmPath = Join-Path $ToolsDir "poppler\Library\bin\pdftoppm.exe"
if (Test-Path -LiteralPath $pdftoppmPath) {
    Write-Host "Poppler 引擎已就绪: $pdftoppmPath"
    exit 0
}

$zipUrl = "https://github.com/oschwartz10612/poppler-windows/releases/download/v$PopplerVersion/Release-$PopplerVersion.zip"
$workDir = Join-Path $env:TEMP ("OmniConvert-Engines-" + [guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $workDir "poppler.zip"
$extractRoot = Join-Path $workDir "extract"
New-Item -ItemType Directory -Path $workDir -Force | Out-Null
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

try {
    Download-File -Uri $zipUrl -Destination $zipPath

    Write-Host "解压 Poppler ..."
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

    $exe = Get-ChildItem -LiteralPath $extractRoot -Recurse -Filter "pdftoppm.exe" -File | Select-Object -First 1
    if (-not $exe) {
        throw "解压结果中未找到 pdftoppm.exe,下载包可能不完整。"
    }

    # 布局约定: Tools\poppler\Library(含 bin\pdftoppm.exe 与 share 数据目录)
    $libraryDir = Split-Path -Parent $exe.Directory
    $destRoot = Join-Path $ToolsDir "poppler"
    New-Item -ItemType Directory -Path $destRoot -Force | Out-Null
    if (Test-Path -LiteralPath (Join-Path $destRoot "Library")) {
        Remove-Item -LiteralPath (Join-Path $destRoot "Library") -Recurse -Force
    }

    Write-Host "移动 Poppler 到 $destRoot\Library ..."
    Move-Item -LiteralPath $libraryDir -Destination (Join-Path $destRoot "Library")

    if (-not (Test-Path -LiteralPath (Join-Path $destRoot "Library\bin\pdftoppm.exe"))) {
        throw "Poppler 移动后校验失败,请删除 $destRoot 后重试。"
    }

    Write-Host "Poppler 引擎安装完成: $(Join-Path $destRoot 'Library\bin\pdftoppm.exe')"
}
finally {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
