# Installs NVIDIA Kimodo (text-to-motion) natively on Windows for the Unity bridge.
#   Repo:    https://github.com/nv-tlabs/kimodo  (code: Apache-2.0)
#   Weights: https://huggingface.co/nvidia (NVIDIA Open Model License), downloaded on first generation.
#   Text encoder: LLM2Vec on meta-llama/Meta-Llama-3-8B-Instruct (gated: request access on Hugging Face and run
#   hf_login.ps1 once).
# Layout: $Root\kimodo (repo), $Root\venv (Python 3.11 venv), $Root\hf_home (Hugging Face cache + token).
# Usage:  powershell -ExecutionPolicy Bypass -File install_kimodo.ps1 [-Root E:\Kimodo]

param([string]$Root = "E:\Kimodo")
$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $Root | Out-Null
$repo = Join-Path $Root "kimodo"
$venv = Join-Path $Root "venv"
$py = Join-Path $venv "Scripts\python.exe"

if (-not (Test-Path $repo)) {
    git clone --depth 1 https://github.com/nv-tlabs/kimodo.git $repo
}

if (-not (Test-Path $py)) {
    & py -3.11 -m venv $venv
}

& $py -m pip install --upgrade pip setuptools wheel
# PyTorch with CUDA 12.8 (required for RTX 50xx / Blackwell)
& $py -m pip install torch --index-url https://download.pytorch.org/whl/cu128
# CMake for the MotionCorrection C++ extension (built with the installed Visual Studio C++ tools)
& $py -m pip install cmake pybind11
# the real cmake.exe (the venv's Scripts\cmake.exe is a Python shim that breaks inside pip's isolated build)
$cmakeBin = Join-Path $venv "Lib\site-packages\cmake\data\bin"
$env:PATH = $cmakeBin + ";" + (Join-Path $venv "Scripts") + ";" + $env:PATH
$env:pybind11_DIR = (& $py -m pybind11 --cmakedir)
& $py -m pip install --no-build-isolation -e $repo
if ($LASTEXITCODE -ne 0) { throw "kimodo install failed" }
# HTTP bridge dependencies
& $py -m pip install fastapi "uvicorn[standard]"

& $py -c "import torch, kimodo, motion_correction; print('torch', torch.__version__, 'cuda', torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else '-'); print('kimodo OK')"
Write-Host "Kimodo installed in $Root"
