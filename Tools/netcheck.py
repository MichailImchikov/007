"""Verdict for a NetAutoTest ability run (see Assets/_Game/Scripts/Net/NetAutoTest.Abilities.cs).

usage: python netcheck.py <caster.log> <other.log> [--bot]
  caster.log  the machine that cast (the client for -netabil / -netabilbot, the host for -netabilhost)
  other.log   the other machine (its player is the target; with --bot the target is a host bot)
"""
import re
import sys
from datetime import datetime

LINE = re.compile(r"\[NetTest\] (\d\d:\d\d:\d\d\.\d) (STEP|STATE|KNIFE|TTT|PLAN)(.*)")


def ts(s):
    return datetime.strptime(s, "%H:%M:%S.%f")


def parse_state(rest):
    parts = rest.strip().split(" | ")
    me = dict(kv.split("=", 1) for kv in parts[0].split() if "=" in kv)
    proxies = {}
    for p in parts[1:]:
        toks = p.split()
        pid = toks[0][1:]
        proxies[pid] = dict(kv.split("=", 1) for kv in toks[1:] if "=" in kv)
    return me, proxies


def load(path):
    steps, states, events = [], [], []
    with open(path, "rb") as f:
        for raw in f:
            m = LINE.search(raw.decode("utf-8", "replace"))
            if not m:
                continue
            t, kind, rest = ts(m.group(1)), m.group(2), m.group(3)
            if kind == "STATE":
                states.append((t,) + parse_state(rest))
            elif kind == "STEP":
                mm = re.match(r" (\d+) (\w+)(?: cast=(\w+))? target=(.*)#(\d+)", rest)
                if mm:
                    steps.append(dict(t=t, n=int(mm.group(1)), kind=mm.group(2), cast=mm.group(3), target=mm.group(5)))
            else:
                events.append((t, kind, rest.strip()))
    return steps, states, events


def fnum(d, k, default=0.0):
    try:
        return float(d.get(k, default))
    except ValueError:
        return default


def window(states, t0, t1):
    return [s for s in states if t0 <= s[0] <= t1]


def main():
    caster_log, other_log = sys.argv[1], sys.argv[2]
    bot = "--bot" in sys.argv
    csteps, cstates, cevents = load(caster_log)
    _, ostates, _ = load(other_log)
    if not cstates or not ostates:
        print("no STATE lines — did both machines run with -nettest and an -netabil flag?")
        return 1
    caster_id = cstates[-1][1].get("id")
    ok_all = True
    for i, st in enumerate(csteps):
        t0 = st["t"]
        t1 = csteps[i + 1]["t"] if i + 1 < len(csteps) else t0.replace(second=min(59, t0.second))  # placeholder
        if i + 1 >= len(csteps):
            from datetime import timedelta
            t1 = t0 + timedelta(seconds=25)
        ow = window(ostates, t0, t1)   # the target machine
        cw = window(cstates, t0, t1)   # the caster machine
        k = st["kind"]
        tid = st["target"]
        me_o = [s[1] for s in ow]                       # target's own state (other machine)
        cast_seen = [s[2].get(caster_id, {}) for s in ow]  # the caster as the target machine sees it
        tgt_seen = [s[2].get(tid, {}) for s in cw]     # the target as the caster sees it
        me_c = [s[1] for s in cw]

        def anyv(seq, key, val="1"):
            return any(d.get(key) == val for d in seq)

        def hp_drop(seq):
            hps = [fnum(d, "hp", 100) for d in seq if "hp" in d]
            return hps and (min(hps) < hps[0] - 1 or anyv(seq, "dead"))

        def moved(seq, need):
            pts = [(fnum(d, "x"), fnum(d, "z")) for d in seq if "x" in d]
            return pts and max(((x - pts[0][0]) ** 2 + (z - pts[0][1]) ** 2) ** 0.5 for x, z in pts) > need

        res, note = None, ""
        if st["cast"] == "False":
            res, note = False, "cast refused on the caster"
        elif bot:
            if k == "TicTacToe":
                boards = [d.get("tttBoard") for d in me_c if d.get("tttBoard")]
                r = [d.get("tttRes") for d in me_c if d.get("tttRes") not in (None, "0")]
                res = bool(boards) and anyv(tgt_seen, "prot")
                note = f"board {boards[-1] if boards else '-'} result {r[-1] if r else '-'}, bot untouchable={anyv(tgt_seen, 'prot')}"
            elif k == "LaserEyes":
                res = anyv(me_c, "laser") and hp_drop(tgt_seen)
                note = f"bot hp {tgt_seen[0].get('hp') if tgt_seen else '?'} -> min {min((fnum(d,'hp',100) for d in tgt_seen), default=0):.0f}"
            elif k == "ShadowClones":
                res = any(fnum(d, "clones") >= 1 for d in me_o)
                note = "clones on the host"
            elif k == "KnifeClash":
                res = anyv(me_c, "duel") and any(d.get("duelPhase") in ("2", "3") for d in me_c)
                note = f"authority here={anyv(me_c, 'duelMirror', '0')} bot untouchable={anyv(tgt_seen, 'prot')} bot died={anyv(tgt_seen, 'alive', '0')} caster died={anyv(me_c, 'dead')}"
        else:
            checks = {
                "Sidearm": (lambda: any(fnum(d, "forced") > 0 for d in me_o), "pistol forced on the target"),
                "Lag": (lambda: any(d.get("lag") == "1" and d.get("fps") == "10" for d in me_o), "target at 10 fps"),
                "EmptyMag": (lambda: any(d.get("mag") == "0" for d in me_o), "target magazine empty"),
                "FartBlind": (lambda: any(fnum(d, "smoke") >= 1 for d in me_o) and hp_drop(me_o), "cloud at the target, its hp drops"),
                "Hyperspeed": (lambda: anyv(cast_seen, "hyperFlag"), "target sees the trail flag"),
                "ShadowClones": (lambda: any(fnum(d, "clones") >= 1 for d in me_o), "clones on the target machine"),
                "LaserEyes": (lambda: anyv(cast_seen, "laserFlag") and hp_drop(me_o), "target sees beams, its hp drops"),
                "Rewind": (lambda: anyv(me_o, "rewind"), "target machine rewinds"),
                "Grapple": (lambda: moved(me_o, 2.5), "target pulled"),
                "Swap": (lambda: moved(me_o, 5), "target teleported"),
                "Invisibility": (lambda: anyv(cast_seen, "inv"), "caster invisible on the target machine"),
                "Phase": (lambda: anyv(cast_seen, "phase"), "caster phasing on the target machine"),
                "ChickenTrap": (lambda: anyv(cast_seen, "chick"), "caster a chicken on the target machine"),
                "Parry": (lambda: anyv(cast_seen, "sword"), "caster's sword on the target machine"),
                "Shrink": (lambda: any(fnum(d, "scale", 1) < 0.9 for d in cast_seen), "caster small on the target machine"),
                "BigHeads": (lambda: anyv(me_o, "bighead"), "target's head blown up"),
                "Screamer": (lambda: anyv(me_o, "scream"), "screamer on the target"),
                "Confuse": (lambda: anyv(me_o, "confused"), "target confused"),
                "Launch": (lambda: me_o and max(fnum(d, "y") for d in me_o) > fnum(me_o[0], "y") + 3, "target launched"),
                "BSOD": (lambda: anyv(me_o, "bsod"), "blue screen on the target"),
                "Spin": (lambda: anyv(me_o, "spin"), "target spinning"),
                "Build": (lambda: any(fnum(d, "pieces") >= 1 for d in me_o), "built piece on the target machine"),
                "ButtRocket": (lambda: hp_drop(me_o), "target hit by the rocket"),
                "SkyArena": (lambda: anyv(me_o, "arena"), "target on the arena"),
            }
            if k == "TicTacToe":
                boards = [d.get("tttBoard") for d in me_o if d.get("tttBoard")]
                r = [d.get("tttRes") for d in me_o if d.get("tttRes") not in (None, "0")]
                loser_dead = (r and r[-1] == "1" and anyv(me_o, "dead")) or (r and r[-1] == "2" and anyv(me_c, "dead")) or (r and r[-1] == "3")
                res = bool(boards) and bool(r) and bool(loser_dead) and anyv(cast_seen, "prot")
                note = f"mirror board {boards[-1] if boards else '-'} result {r[-1] if r else '-'} loser blown={bool(loser_dead)} untouchable={anyv(cast_seen, 'prot')}"
            elif k == "KnifeClash":
                phases = [d.get("duelPhase") for d in me_o if d.get("duelPhase")]
                died = "target" if anyv(me_o, "dead") else "caster" if anyv(me_c, "dead") else "nobody"
                res = bool(phases) and "2" in phases and died != "nobody"
                note = f"target mirror={me_o and next((d.get('duelMirror') for d in me_o if d.get('duelMirror')), '-')} phases={''.join(sorted(set(phases)))} died={died}"
            elif k in checks:
                fn, note = checks[k]
                res = bool(fn())
        if res is None:
            res, note = False, "no check for this step"
        ok_all &= bool(res)
        print(f"{'OK  ' if res else 'FAIL'} {st['n']:>2} {k:<13} {note}")
    knives = [e for e in cevents if e[1] == "KNIFE"]
    for e in knives:
        print("     knife:", e[2])
    print("ALL OK" if ok_all else "SOME FAILED")
    return 0 if ok_all else 1


if __name__ == "__main__":
    sys.exit(main())
