#requires -Version 5.1

<#
.SYNOPSIS
  构建 OmniConvert 的内置转换引擎(引擎二进制不入 Git 仓库)。

.DESCRIPTION
  当前引擎:
    - Poppler(pdftoppm.exe) —— PDF 渲染为图片(PNG/JPG)的核心引擎;
    - pdf2docx(嵌入式 Python) —— PDF 转可编辑 Word 的引擎,启动器
      scripts\pdf2docx-engine\convert.py 含两项后处理:
      清除数字与中文间的多余空格、通过 texttrace 恢复描边伪粗体。

  实现方式:
    - Poppler:从 oschwartz10612/poppler-windows 的 GitHub Release 下载官方 Windows 构建;
    - pdf2docx:下载 Python 嵌入式发行版 + pip 安装 pdf2docx(清华镜像,失败回退官方源),
      并做运行时清理(删除 __pycache__/*.dist-info/*.lib/*.pxd、重命名扩展模块),
      避免 PRI 资源索引警告。升级方式:删除 Tools\pdf2docx 后重跑本脚本。

.USAGE
  powershell -ExecutionPolicy Bypass -File scripts\restore-engines.ps1
#>

[CmdletBinding()]
param(
    [string]$ToolsDir = "",
    [string]$PopplerVersion = "26.02.0-0",
    [string]$PythonVersion = "3.12.8"
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

# ---- 就绪检查 ----
$pdftoppmPath = Join-Path $ToolsDir "poppler\Library\bin\pdftoppm.exe"
$cMapPath = Join-Path $ToolsDir "poppler\share\poppler\cMap\Adobe-GB1\UniGB-UCS2-H"
$popplerReady = (Test-Path -LiteralPath $pdftoppmPath) -and (Test-Path -LiteralPath $cMapPath)

$pdf2docxDir = Join-Path $ToolsDir "pdf2docx"
$pdf2docxPython = Join-Path $pdf2docxDir "python\python.exe"
$pdf2docxLauncher = Join-Path $pdf2docxDir "convert.py"
$pdf2docxPackage = Join-Path $pdf2docxDir "python\Lib\site-packages\pdf2docx\__init__.py"
$pdf2docxReady = (Test-Path -LiteralPath $pdf2docxPython) -and (Test-Path -LiteralPath $pdf2docxLauncher) -and (Test-Path -LiteralPath $pdf2docxPackage)

if ($popplerReady -and $pdf2docxReady) {
    Write-Host "所有引擎已就绪:"
    Write-Host "  Poppler:  $pdftoppmPath"
    Write-Host "  pdf2docx: $pdf2docxPython"
    exit 0
}

$workDir = Join-Path $env:TEMP ("OmniConvert-Engines-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

try {
    # ---- Poppler(PDF→图片) ----
    if (-not $popplerReady) {
        $zipUrl = "https://github.com/oschwartz10612/poppler-windows/releases/download/v$PopplerVersion/Release-$PopplerVersion.zip"
        $zipPath = Join-Path $workDir "poppler.zip"
        $extractRoot = Join-Path $workDir "extract"
        New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

        Download-File -Uri $zipUrl -Destination $zipPath

        Write-Host "解压 Poppler ..."
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

        $exe = Get-ChildItem -LiteralPath $extractRoot -Recurse -Filter "pdftoppm.exe" -File | Select-Object -First 1
        if (-not $exe) {
            throw "解压结果中未找到 pdftoppm.exe,下载包可能不完整。"
        }

        # 布局约定: Tools\poppler 完整保留原压缩包根目录结构
        # (Library\bin\pdftoppm.exe 与 share\poppler\cMap 数据并排)。
        # 注意:share 目录包含 Adobe-GB1 等 CMap 语言包,缺失会导致
        # 未嵌入字体的 PDF 渲染为空白(实测: 班级课表 PDF)。
        $libraryDir = Split-Path -Parent $exe.Directory
        $sourceRoot = Split-Path -Parent $libraryDir
        $destRoot = Join-Path $ToolsDir "poppler"
        New-Item -ItemType Directory -Path $destRoot -Force | Out-Null

        Write-Host "移动 Poppler(含 share 数据)到 $destRoot ..."
        Get-ChildItem -LiteralPath $sourceRoot -Force | ForEach-Object {
            $destination = Join-Path $destRoot $_.Name
            if (Test-Path -LiteralPath $destination) {
                Remove-Item -LiteralPath $destination -Recurse -Force
            }
            Move-Item -LiteralPath $_.FullName -Destination $destRoot
        }

        if (-not (Test-Path -LiteralPath (Join-Path $destRoot "Library\bin\pdftoppm.exe"))) {
            throw "Poppler 移动后校验失败,请删除 $destRoot 后重试。"
        }
        if (-not (Test-Path -LiteralPath (Join-Path $destRoot "share\poppler\cMap\Adobe-GB1\UniGB-UCS2-H"))) {
            throw "Poppler CMap 语言包数据缺失,请删除 $destRoot 后重试。"
        }

        Write-Host "Poppler 引擎安装完成: $(Join-Path $destRoot 'Library\bin\pdftoppm.exe')"
    }

    # ---- pdf2docx(PDF→Word,嵌入式 Python) ----
    if (-not $pdf2docxReady) {
        # 每次构建全新安装,保证可重复
        if (Test-Path -LiteralPath $pdf2docxDir) {
            Remove-Item -LiteralPath $pdf2docxDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $pdf2docxDir -Force | Out-Null

        $pythonZipUrl = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
        $pythonZipPath = Join-Path $workDir "python-embed.zip"
        Download-File -Uri $pythonZipUrl -Destination $pythonZipPath

        $pythonDir = Join-Path $pdf2docxDir "python"
        Write-Host "解压 Python $PythonVersion 嵌入式发行版 ..."
        Expand-Archive -LiteralPath $pythonZipPath -DestinationPath $pythonDir -Force

        $pythonExe = Join-Path $pythonDir "python.exe"
        if (-not (Test-Path -LiteralPath $pythonExe)) {
            throw "解压结果中未找到 python.exe。"
        }

        # 启用 site-packages(嵌入式发行版默认禁用)
        $pthFile = Get-ChildItem -LiteralPath $pythonDir -Filter "python*._pth" -File | Select-Object -First 1
        if (-not $pthFile) {
            throw "未找到 python*._pth 文件。"
        }
        $zipLine = (Get-Content -LiteralPath $pthFile.FullName | Select-Object -First 1)
        [System.IO.File]::WriteAllLines($pthFile.FullName, @($zipLine, ".", "Lib\site-packages", "import site"), [System.Text.UTF8Encoding]::new($false))

        # 引导 pip
        $getPipPath = Join-Path $workDir "get-pip.py"
        Download-File -Uri "https://bootstrap.pypa.io/get-pip.py" -Destination $getPipPath
        Write-Host "安装 pip ..."
        & $pythonExe $getPipPath --no-warn-script-location
        if ($LASTEXITCODE -ne 0) {
            throw "pip 安装失败(退出码 $LASTEXITCODE)。"
        }

        # 安装 pdf2docx(清华镜像优先,失败回退官方源)
        Write-Host "安装 pdf2docx(清华镜像)..."
        & $pythonExe -m pip install --no-warn-script-location -i https://pypi.tuna.tsinghua.edu.cn/simple pdf2docx
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "镜像安装失败,回退官方 PyPI 重试..."
            & $pythonExe -m pip install --no-warn-script-location pdf2docx
            if ($LASTEXITCODE -ne 0) {
                throw "pdf2docx 安装失败(退出码 $LASTEXITCODE)。"
            }
        }

        # 运行时清理:删除开发期文件(运行不需要),同时避免 PRI 资源索引
        # 把 *.cp312-win_amd64.pyd / *.cython-30.pxd 之类名称误判为资源限定符。
        $sitePackages = Join-Path $pythonDir "Lib\site-packages"
        Get-ChildItem -LiteralPath $sitePackages -Recurse -Directory -Filter "__pycache__" -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $sitePackages -Recurse -Directory -Filter "*.dist-info" -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $sitePackages -Recurse -File -Filter "*.lib" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $sitePackages -Recurse -File -Filter "*.pxd" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue

        # 扩展模块去掉平台标签(Python 扩展后缀列表含 ".pyd" 回退,可正常导入)
        Get-ChildItem -LiteralPath $sitePackages -Recurse -File -Filter "*.cp312-win_amd64.pyd" -ErrorAction SilentlyContinue | ForEach-Object {
            Rename-Item -LiteralPath $_.FullName -NewName ($_.Name -replace '\.cp312-win_amd64\.pyd$', '.pyd')
        }

        # MSIX/OPC 打包限制与体积精简:以下内容运行时均不需要。
        # ★ 关键:docx\templates\default-docx-template 内含 [Content_Types].xml,
        #   该名称是 MSIX/OPC 包的保留名(且 [ ] 在 OPC 部件名 URI 中非法),
        #   会导致 MSIX 打包报 0x8007007b(ERROR_INVALID_NAME)。
        $cleanupPaths = @(
            (Join-Path $sitePackages "docx\templates\default-docx-template"),
            (Join-Path $sitePackages "pip"),
            (Join-Path $pythonDir "Scripts"),
            (Join-Path $pythonDir "share"),
            (Join-Path $sitePackages "pymupdf\mupdf-devel"),
            (Join-Path $sitePackages "lxml\isoschematron")
        )
        foreach ($cleanupPath in $cleanupPaths) {
            if (Test-Path -LiteralPath $cleanupPath) {
                Remove-Item -LiteralPath $cleanupPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        Get-ChildItem -LiteralPath (Join-Path $sitePackages "numpy") -Recurse -Directory -Filter "tests" -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        # 部署启动器(含空格/伪粗体后处理)
        $launcherSource = Join-Path (Split-Path -Parent $PSScriptRoot) "scripts\pdf2docx-engine\convert.py"
        if (-not (Test-Path -LiteralPath $launcherSource)) {
            throw "找不到启动器源文件: $launcherSource"
        }
        Copy-Item -LiteralPath $launcherSource -Destination (Join-Path $pdf2docxDir "convert.py") -Force

        # 冒烟校验:能导入 pdf2docx 即视为安装成功
        Write-Host "校验 pdf2docx 引擎 ..."
        & $pythonExe -B -c "import pdf2docx; print('pdf2docx', pdf2docx.__version__)"
        if ($LASTEXITCODE -ne 0) {
            throw "pdf2docx 引擎校验失败(退出码 $LASTEXITCODE)。"
        }

        Write-Host "pdf2docx 引擎安装完成: $pdf2docxDir"
    }
}
finally {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
