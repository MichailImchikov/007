using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// Glowing ability item lying on the map: floating icon, light beam, ground ring and colored light.
    /// Bots grab it by walking over it; the player takes it automatically into an empty slot,
    /// or presses X to replace the current ability.
    /// </summary>
    public class AbilityPickup : MonoBehaviour
    {
        public AbilityType type;
        public AbilityType shown;
        /// <summary>Removed on another machine (host broadcast).</summary>
        public void RemoteGone()
        {
            if (taken) return;
            taken = true;
            ProcAudio.PlayAt(Sfx.AbilityPickup, transform.position, 0.8f, 0.03f);
            FX.Ensure().SparkBurst(transform.position + Vector3.up, Vector3.up, 20, 6f);
            Destroy(gameObject);
        }

        /// <summary>Range stand: comes back at the same spot right after being taken.</summary>
        public bool persistent;
        /// <summary>Network id (host-assigned).</summary>
        public int netId;

        Transform icon;
        Light glow;
        float phase;
        bool taken;

        public string Label => AbilityDB.Get(shown).name;

        public static AbilityPickup Create(Vector3 pos, AbilityType type, AbilityType shown)
        {
            var go = new GameObject("AbilityPickup_" + shown);
            go.transform.position = pos;
            var p = go.AddComponent<AbilityPickup>();
            p.type = type;
            p.shown = shown;
            p.Build();
            return p;
        }

        static Material beamMat;

        void Build()
        {
            var info = AbilityDB.Get(shown);
            Color c = info.color;
            icon = AbilityProps.Root("IconPivot", transform);
            icon.localPosition = new Vector3(0f, 1f, 0f);
            AbilityProps.BuildIcon(shown, icon);

            var ring = AbilityProps.Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.02f, 0f), new Vector3(1.1f, 0.01f, 1.1f), AbilityProps.Mat(c, 2.5f, 0.3f), null, false);
            AbilityProps.Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.025f, 0f), new Vector3(0.85f, 0.01f, 0.85f), AbilityProps.Mat(new Color(0.05f, 0.05f, 0.06f), 0f, 0.6f), null, false);

            if (!beamMat) beamMat = FX.MakeParticleMat("AbilityBeam", FX.SoftCircle(32, 1.2f), true, Color.white, true);
            var beam = new GameObject("Beam");
            beam.transform.SetParent(transform, false);
            var lr = beam.AddComponent<LineRenderer>();
            lr.sharedMaterial = beamMat;
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(0f, 0.05f, 0f));
            lr.SetPosition(1, new Vector3(0f, 4.5f, 0f));
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 0.15f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            lr.colorGradient = g;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;

            var lgo = new GameObject("Glow");
            lgo.transform.SetParent(transform, false);
            lgo.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            glow = lgo.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = c; glow.range = 5f; glow.intensity = 2.5f; glow.shadows = LightShadows.None;
            phase = Random.value * 10f;
        }

        void Update()
        {
            if (taken) return;
            float dt = Time.deltaTime;
            phase += dt;
            icon.localPosition = new Vector3(0f, 1f + Mathf.Sin(phase * 2.2f) * 0.12f, 0f);
            icon.localRotation = Quaternion.Euler(0f, phase * 90f, 0f);
            glow.intensity = 2.2f + Mathf.Sin(phase * 4f) * 0.8f;

            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || !c.abilities) continue;
                Vector3 d = c.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 1.6f) continue;
                d.y = 0f;
                float r2 = d.sqrMagnitude;
                var u = c.abilities;
                if (c.isPlayer)
                {
                    if (r2 < 1.6f * 1.6f)
                    {
                        if (u.Type == AbilityType.None) { Take(u); return; }
                        u.NearbyPickup = this;
                    }
                    else if (u.NearbyPickup == this) u.NearbyPickup = null;
                }
                else if (u.CanUseAbilities && r2 < 1.1f * 1.1f && (u.Type == AbilityType.None || Random.value < 0.35f * dt * 10f)) { Take(u); return; }
            }
        }

        public void Take(AbilityUser u)
        {
            if (taken) return;
            if (Net.IsClient)
            {
                // ask the host; it grants the ability if nobody was faster
                if (!u.IsPlayer) return;
                taken = true;
                if (u.NearbyPickup == this) u.NearbyPickup = null;
                NetGame.RequestPickup(this);
                ProcAudio.PlayAt(Sfx.AbilityPickup, transform.position, 0.8f, 0.03f);
                FX.Ensure().SparkBurst(transform.position + Vector3.up, Vector3.up, 20, 6f);
                Destroy(gameObject);
                return;
            }
            taken = true;
            if (u.NearbyPickup == this) u.NearbyPickup = null;
            if (u.Remote) NetGame.GrantPickup(u, type, shown);
            else u.Give(type, shown);
            NetGame.PickupGone(this);
            ProcAudio.PlayAt(Sfx.AbilityPickup, transform.position, 0.8f, 0.03f);
            FX.Ensure().SparkBurst(transform.position + Vector3.up, Vector3.up, 20, 6f);
            if (AbilitySpawner.Instance)
            {
                if (persistent) AbilitySpawner.Instance.RespawnStand(type, shown, transform.position);
                else AbilitySpawner.Instance.OnTaken(this);
            }
            Destroy(gameObject);
        }
    }

    /// <summary>Keeps a few ability pickups on the map at random walkable spots; respawns taken ones elsewhere.</summary>
    public class AbilitySpawner : MonoBehaviour
    {
        public static AbilitySpawner Instance { get; private set; }

        public int count = 6;
        public float respawnMin = 8f, respawnMax = 16f;

        readonly List<AbilityPickup> active = new List<AbilityPickup>();
        Vector3[] verts;
        int[] tris;
        float[] cumArea;
        float totalArea;
        Vector3 fallbackCenter;

        public static AbilitySpawner Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("AbilitySpawner").AddComponent<AbilitySpawner>();
            return Instance;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        IEnumerator Start()
        {
            yield return null; // NavMesh is baked in MatchManager.Awake
            var tri = NavMesh.CalculateTriangulation();
            verts = tri.vertices;
            tris = tri.indices;
            int n = tris.Length / 3;
            cumArea = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = verts[tris[i * 3]], b = verts[tris[i * 3 + 1]], c = verts[tris[i * 3 + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (Vector3.Cross(b - a, c - a).normalized.y < 0.8f) area = 0f; // skip ramps / stairs
                totalArea += area;
                cumArea[i] = totalArea;
            }
            fallbackCenter = PlayerHealth.Local ? PlayerHealth.Local.transform.position : Vector3.zero;
            if (Net.IsClient) yield break; // the host spawns pickups and tells us
            if (DroneMode.Active) yield break; // drone war: the drone is the only ability
            if (!MatchManager.Instance) { BuildStand(); yield break; } // shooting range: every ability on a stand
            for (int i = 0; i < count; i++) SpawnOne();
        }

        /// <summary>Shooting range: two rows of every ability behind the start, each one restocked instantly.</summary>
        void BuildStand()
        {
            var types = new List<AbilityType>();
            foreach (AbilityType t in System.Enum.GetValues(typeof(AbilityType)))
                if (t != AbilityType.None && !string.IsNullOrEmpty(AbilityDB.Get(t).name)) types.Add(t);
            Vector3 origin = fallbackCenter;
            Vector3 fwd = PlayerHealth.Local ? PlayerHealth.Local.transform.forward : Vector3.forward;
            fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            int perRow = Mathf.CeilToInt(types.Count / 2f);
            const float spacing = 1.8f;
            for (int i = 0; i < types.Count; i++)
            {
                int row = i / perRow, col = i % perRow;
                Vector3 p = origin - fwd * (6f + row * 3f) + right * ((col - (perRow - 1) * 0.5f) * spacing);
                if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit h, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) p = h.point;
                // the trap is shown as itself here so it can be picked on purpose
                var pk = AbilityPickup.Create(p + Vector3.up * 0.02f, types[i], types[i]);
                pk.persistent = true;
            }
        }

        public void RespawnStand(AbilityType type, AbilityType shown, Vector3 pos) => StartCoroutine(RespawnStandLater(type, shown, pos));

        IEnumerator RespawnStandLater(AbilityType type, AbilityType shown, Vector3 pos)
        {
            yield return new WaitForSeconds(1.5f);
            var pk = AbilityPickup.Create(pos, type, shown);
            pk.persistent = true;
        }

        public void OnTaken(AbilityPickup p)
        {
            active.Remove(p);
            StartCoroutine(RespawnLater());
        }

        IEnumerator RespawnLater()
        {
            yield return new WaitForSeconds(Random.Range(respawnMin, respawnMax));
            SpawnOne();
        }

        void SpawnOne()
        {
            active.RemoveAll(a => !a);
            if (active.Count >= count) return;
            for (int attempt = 0; attempt < 90; attempt++)
            {
                if (!RandomPoint(out var p)) continue;
                bool ok = true;
                foreach (var a in active) if (a && (a.transform.position - p).sqrMagnitude < 9f * 9f) { ok = false; break; }
                if (ok) foreach (var c in Combatant.All) if (c && c.alive && (c.transform.position - p).sqrMagnitude < 5f * 5f) { ok = false; break; }
                // need head room (not under stairs / tables)
                if (ok && Physics.Raycast(p + Vector3.up * 0.2f, Vector3.up, 2.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) ok = false;
                if (!Reachable(p)) continue; // never on roofs / containers / shelves you can't get to
                if (!ok && attempt < 75) continue;
                var type = AbilityDB.RandomSpawn(out var shown);
                var pk = AbilityPickup.Create(p + Vector3.up * 0.02f, type, shown);
                active.Add(pk);
                NetGame.PickupSpawned(pk);
                return;
            }
            StartCoroutine(RespawnLater()); // no good point this time — try again later
        }

        readonly List<Vector3> anchors = new List<Vector3>();
        NavMeshPath path;

        /// <summary>
        /// A pickup point must be walkable from a spawn point (the NavMesh also covers container roofs, shelves,
        /// crane beams and closed rooftops — islands nobody can reach), free of props and not lying on breakable cover.
        /// </summary>
        public bool Reachable(Vector3 p)
        {
            if (totalArea <= 1f) return true; // shooting range fallback: raycast placement
            if (anchors.Count == 0)
            {
                foreach (var sp in FindObjectsByType<SpawnPoint>())
                    if (NavMesh.SamplePosition(sp.transform.position, out var h, 2.5f, NavMesh.AllAreas)) anchors.Add(h.position);
                if (anchors.Count == 0 && PlayerHealth.Local && NavMesh.SamplePosition(PlayerHealth.Local.transform.position, out var ph, 3f, NavMesh.AllAreas))
                    anchors.Add(ph.position);
                if (anchors.Count == 0) return true;
            }
            if (!NavMesh.SamplePosition(p, out var hit, 0.4f, NavMesh.AllAreas)) return false;
            p = hit.position;
            path ??= new NavMeshPath();
            bool connected = false;
            // two anchors are enough: maps are one connected area, a single unlucky spawn must not veto a point
            for (int i = 0; i < anchors.Count && i < 3 && !connected; i++)
                connected = NavMesh.CalculatePath(anchors[(i * 5) % anchors.Count], p, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            if (!connected) return false;
            // standing room: no collider in the body volume
            if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            // not on top of a crate / sandbags (they break and the pickup would float)
            if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var down, 0.8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && (down.collider.GetComponentInParent<Destructible>() || down.rigidbody)) return false;
            return true;
        }

        bool RandomPoint(out Vector3 p)
        {
            p = Vector3.zero;
            if (totalArea > 1f)
            {
                float r = Random.value * totalArea;
                int lo = 0, hi = cumArea.Length - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cumArea[mid] < r) lo = mid + 1; else hi = mid; }
                Vector3 a = verts[tris[lo * 3]], b = verts[tris[lo * 3 + 1]], c = verts[tris[lo * 3 + 2]];
                float u = Random.value, v = Random.value;
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                p = a + (b - a) * u + (c - a) * v;
                return true;
            }
            // no NavMesh (shooting range): drop onto whatever is below a random point near the start
            Vector2 rnd = Random.insideUnitCircle * 25f;
            Vector3 o = fallbackCenter + new Vector3(rnd.x, 20f, rnd.y);
            if (Physics.Raycast(o, Vector3.down, out RaycastHit h, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && h.normal.y > 0.8f)
            {
                p = h.point;
                return true;
            }
            return false;
        }

        public AbilityPickup Nearest(Vector3 pos, float maxDist)
        {
            AbilityPickup best = null;
            float bd = maxDist * maxDist;
            foreach (var a in active)
            {
                if (!a) continue;
                float d = (a.transform.position - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }
    }
}
