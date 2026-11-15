# One-time Hugging Face login for Kimodo's text encoder (gated model meta-llama/Meta-Llama-3-8B-Instruct).
# 1. Sign in at https://huggingface.co and request access on https://huggingface.co/meta-llama/Meta-Llama-3-8B-Instruct
#    (accept Meta's Llama 3 license there).
# 2. Create a read token at https://huggingface.co/settings/tokens
# 3. Run this script and paste the token when asked. It is stored in $Root\hf_home\token (used by run_bridge.ps1).
param([string]$Root = "E:\Kimodo")
$env:HF_HOME = Join-Path $Root "hf_home"
& (Join-Path $Root "venv\Scripts\hf.exe") auth login
