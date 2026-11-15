# Starts the Kimodo bridge server for Unity (http://127.0.0.1:7861).
# Text encoder (LLM2Vec on Llama-3-8B-Instruct, runs on the CPU, needs ~16 GB of free RAM):
#  • local (no Hugging Face account): put the files into $Root\text_encoders\ (see the layout below) — used when present;
#  • otherwise it is downloaded from Hugging Face (gated meta-llama repo: needs access + hf_login.ps1).
# Layout of $Root\text_encoders:
#   meta-llama\Meta-Llama-3-8B-Instruct\                          (weights, e.g. from the public mirror NousResearch/Meta-Llama-3-8B-Instruct)
#   McGill-NLP\LLM2Vec-Meta-Llama-3-8B-Instruct-mntp\              (LLM2Vec MNTP adapter + config / tokenizer)
#   McGill-NLP\LLM2Vec-Meta-Llama-3-8B-Instruct-mntp-supervised\   (LLM2Vec supervised adapter)
param([string]$Root = "E:\Kimodo", [int]$Port = 7861, [string]$Model = "kimodo-soma-rp")

$env:HF_HOME = Join-Path $Root "hf_home"
$env:TEXT_ENCODER_MODE = "local"
$env:TEXT_ENCODER_DEVICE = "cpu"
$env:LOCAL_CACHE = "True"
$env:PYTHONUNBUFFERED = "1"
$env:HF_HUB_DISABLE_SYMLINKS_WARNING = "1"

$encoders = Join-Path $Root "text_encoders"
if (Test-Path (Join-Path $encoders "meta-llama\Meta-Llama-3-8B-Instruct\config.json")) {
    # the adapters name their base model "meta-llama/Meta-Llama-3-8B-Instruct": run from this folder so that
    # relative path resolves to the local copy, and stay offline (no token needed)
    $env:TEXT_ENCODERS_DIR = $encoders
    $env:HF_HUB_OFFLINE = "1"
    Set-Location $encoders
    Write-Host "Text encoder: local ($encoders)"
}

$py = Join-Path $Root "venv\Scripts\python.exe"
& $py (Join-Path $PSScriptRoot "kimodo_bridge.py") --port $Port --model $Model
