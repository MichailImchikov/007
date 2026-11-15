"""
Kimodo <-> Unity bridge: a local HTTP server that keeps an NVIDIA Kimodo model loaded and generates motions
on request from the Unity editor (Tools > FPS > Kimodo).

  GET  /health    -> {"status": "ok"|"loading"|"error", "model": ..., "fps": 30, "device": ..., "error": ...}
  POST /generate  -> body: {
        "name": "Jog_F",                      # output file stem
        "prompts": ["A person jogs forward."], # one or several (multi-prompt timeline)
        "durations": [4.0],                   # seconds per prompt (max 10 s each)
        "num_samples": 1, "seed": 42, "diffusion_steps": 100,
        "cfg": [2.0, 2.0],                    # [text_weight, constraint_weight] or null for model default
        "constraints": [ ... ],               # Kimodo constraint dicts (root2d, fullbody, end-effector), optional
        "postprocess": true,                  # foot-skate cleanup / constraint optimisation
        "out_dir": "C:/.../KimodoOut"         # where to write <name>[_NN].bvh / .npz
     }
     <- {"files": [{"bvh": ..., "npz": ...}], "fps": 30, "frames": N, "seconds": t}

Environment (set by run_bridge.ps1): HF_HOME (model cache + token), TEXT_ENCODER_MODE=local,
TEXT_ENCODER_DEVICE=cpu (the 8B text encoder does not fit next to the model in 16 GB of VRAM).
"""

import argparse
import os
import threading
import time
import traceback
from typing import List, Optional

import torch
import uvicorn
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from kimodo import load_model
from kimodo.constraints import load_constraints_lst
from kimodo.exports.bvh import save_motion_bvh
from kimodo.exports.motion_io import save_kimodo_npz
from kimodo.skeleton import SOMASkeleton30, global_rots_to_local_rots
from kimodo.tools import seed_everything

app = FastAPI(title="Kimodo Unity Bridge")
state = {"status": "loading", "model": None, "name": None, "fps": 30.0, "device": None, "error": None}
lock = threading.Lock()


class GenerateRequest(BaseModel):
    name: str = "motion"
    prompts: List[str]
    durations: List[float]
    num_samples: int = 1
    seed: Optional[int] = None
    diffusion_steps: int = 100
    cfg: Optional[List[float]] = None
    constraints: Optional[list] = None
    postprocess: bool = True
    num_transition_frames: int = 5
    out_dir: str


def load(model_name: str):
    try:
        device = "cuda:0" if torch.cuda.is_available() else "cpu"
        state["device"] = device
        t = time.time()
        model, resolved = load_model(model_name, device=device, default_family="Kimodo", return_resolved_name=True)
        state.update(model=model, name=resolved, fps=float(model.fps), status="ok")
        print(f"[bridge] loaded {resolved} on {device} in {time.time() - t:.1f}s", flush=True)
    except Exception as e:  # keep the server up so Unity can show the error
        state.update(status="error", error=f"{type(e).__name__}: {e}")
        traceback.print_exc()


@app.get("/health")
def health():
    return {k: state[k] for k in ("status", "name", "fps", "device", "error")}


@app.post("/generate")
def generate(req: GenerateRequest):
    if state["status"] != "ok":
        raise HTTPException(503, f"model not ready: {state['status']} {state['error'] or ''}")
    if len(req.prompts) != len(req.durations) or not req.prompts:
        raise HTTPException(400, "prompts and durations must be non-empty and of the same length")
    model = state["model"]
    fps = state["fps"]
    with lock:
        t0 = time.time()
        texts = [p.strip() if p.strip().endswith(".") else p.strip() + "." for p in req.prompts]
        num_frames = [max(2, int(round(d * fps))) for d in req.durations]
        constraint_lst = load_constraints_lst(req.constraints, model.skeleton) if req.constraints else []
        if req.seed is not None:
            seed_everything(req.seed)
        cfg_kwargs = {}
        if req.cfg:
            cfg_kwargs = {"cfg_type": "separated", "cfg_weight": [float(req.cfg[0]), float(req.cfg[-1])]}
        output = model(
            texts,
            num_frames,
            constraint_lst=constraint_lst,
            num_denoising_steps=req.diffusion_steps,
            num_samples=req.num_samples,
            multi_prompt=True,
            num_transition_frames=req.num_transition_frames,
            post_processing=req.postprocess and "g1" not in state["name"],
            return_numpy=True,
            **cfg_kwargs,
        )
        os.makedirs(req.out_dir, exist_ok=True)
        skeleton = model.skeleton
        if isinstance(skeleton, SOMASkeleton30):
            skeleton = skeleton.somaskel77.to(state["device"])
        n = int(output["posed_joints"].shape[0])
        files = []
        for i in range(n):
            stem = req.name if n == 1 else f"{req.name}_{i:02d}"
            single = {k: (v[i] if hasattr(v, "shape") and len(v.shape) > 0 and v.shape[0] == n else v) for k, v in output.items()}
            npz_path = os.path.join(req.out_dir, stem + ".npz")
            save_kimodo_npz(npz_path, single)
            bvh_path = os.path.join(req.out_dir, stem + ".bvh")
            joints_pos = torch.from_numpy(output["posed_joints"][i]).to(state["device"])
            joints_rot = torch.from_numpy(output["global_rot_mats"][i]).to(state["device"])
            local = global_rots_to_local_rots(joints_rot, skeleton)
            save_motion_bvh(bvh_path, local, joints_pos[:, skeleton.root_idx, :], skeleton=skeleton, fps=fps, standard_tpose=True)
            files.append({"bvh": bvh_path, "npz": npz_path})
        dt = time.time() - t0
        print(f"[bridge] {req.name}: {texts} -> {len(files)} file(s) in {dt:.1f}s", flush=True)
        return {"files": files, "fps": fps, "frames": int(output["posed_joints"].shape[1]), "seconds": dt}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=7861)
    ap.add_argument("--model", default=os.environ.get("KIMODO_MODEL", "kimodo-soma-rp"))  # latest SOMA-RP checkpoint
    args = ap.parse_args()
    threading.Thread(target=load, args=(args.model,), daemon=True).start()
    uvicorn.run(app, host=args.host, port=args.port, log_level="warning")


if __name__ == "__main__":
    main()
