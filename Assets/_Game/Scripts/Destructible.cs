using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>
    /// Breakable cover (wooden crates, sandbag walls). Takes damage from bullets, knives and explosions,
    /// darkens and wobbles as it weakens, then bursts into physical debris. A carving NavMeshObstacle
    /// keeps bots pathing around it while intact (MatchManager bakes the NavMesh without these colliders),
    /// so the walkable area opens up once the cover is gone. Cover stacked on top falls down.
    /// </summary>
    public class Destructible : MonoBehaviour, IDamageable
    {
        public enum Kind { Wood, Sand }
        public static readonly List<Destructible> All = new List<Destructible>();

        public Kind kind = Kind.Wood;
        public float maxHealth = 120f;
        /// <summary>Visual pieces that fly off when broken (sandbag bags). Empty = procedural chunks from own mesh.</summary>
        public Transform[] pieces;
        /// <summary>Renderer that visualizes damage (defaults to own renderer).</summary>
        public Renderer visual;

        public float Health { get; private set; }
        public bool Broken { get; private set; }
        /// <summary>Network id: scene cover gets one from its position at load, built walls from their creator.</summary>
        public int netId;

        Collider col;
        NavMeshObstacle obstacle;
        MaterialPropertyBlock mpb;
        Color baseColor = Color.white;
        Renderer[] tintRenderers;
        float wobble;
        Vector3 restPos;
        Quaternion restRot;
        Transform wobbleTarget;
        bool restSaved;

        void Awake()
        {
            Health = maxHealth;
            col = GetComponent<Collider>();
            if (!visual) visual = GetComponent<Renderer>();
            var list = new List<Renderer>();
            if (visual && visual.enabled) list.Add(visual);
            if (pieces != null) foreach (var p in pieces) if (p && p.TryGetComponent(out Renderer r)) list.Add(r);
            tintRenderers = list.ToArray();
            if (tintRenderers.Length > 0 && tintRenderers[0].sharedMaterial && tintRenderers[0].sharedMaterial.HasProperty("_BaseColor"))
                baseColor = tintRenderers[0].sharedMaterial.GetColor("_BaseColor");
            mpb = new MaterialPropertyBlock();
            wobbleTarget = pieces != null && pieces.Length > 0 && pieces[0] ? pieces[0].parent : transform;
            if (GetComponent<Rigidbody>()) wobbleTarget = null; // physics props move on their own
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            // bots walk around intact cover; the hole closes when it breaks
            if (col && !GetComponent<NavMeshObstacle>())
            {
                obstacle = gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                if (col is BoxCollider bc) { obstacle.center = bc.center; obstacle.size = bc.size; }
                else { obstacle.center = Vector3.zero; obstacle.size = Vector3.one; }
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                obstacle.carvingMoveThreshold = 0.2f;
            }
        }

        /// <summary>Colliders are excluded from the NavMesh bake (obstacles carve instead).</summary>
        public static void SetCollidersForBake(bool enabled)
        {
            foreach (var d in All) if (d && d.col && !d.Broken) d.col.enabled = enabled;
        }

        public DamageResult TakeDamage(DamageInfo info)
        {
            if (Broken) return default;
            if (Net.IsClient && netId != 0)
            {
                // the host owns cover health: show the splinters now, the host decides when it breaks
                NetGame.SendCoverDamage(this, info);
                HitFX(info);
                wobble = Mathf.Min(1f, wobble + 0.3f);
                return default;
            }
            float amount = info.amount;
            if (info.weapon == "Граната" || info.weapon == "Ягодичная ракета") amount *= 1.6f; // explosives shred cover
            if (kind == Kind.Sand && (info.weapon == "Нож" || info.weapon == "Меч")) amount *= 1.5f;
            Health -= amount;
            wobble = Mathf.Min(1f, wobble + Mathf.Clamp(amount / 40f, 0.15f, 1f));
            if (!info.fromNetwork) HitFX(info);
            UpdateTint();
            NetGame.CoverChanged(this);
            if (Health <= 0f) Break(info.direction.sqrMagnitude > 0.01f ? info.direction : Vector3.down, info.point);
            return default; // not "applied": cover hits don't show hitmarkers
        }

        void HitFX(DamageInfo info)
        {
            var p = info.point;
            Vector3 n = info.normal.sqrMagnitude > 0.01f ? info.normal : -info.direction;
            if (kind == Kind.Wood) FX.Ensure().Chunks(p, n, 4, new Color(0.55f, 0.4f, 0.24f), new Color(0.38f, 0.27f, 0.15f), 3.5f);
            else FX.Ensure().DustBurst(p, n, 3, new Color(0.62f, 0.56f, 0.42f, 0.55f), new Color(0.5f, 0.45f, 0.33f, 0.45f), 0.35f);
        }

        /// <summary>Host → client: replicated health.</summary>
        public void SetNetHealth(float h)
        {
            if (Broken) return;
            if (h < Health - 0.5f) wobble = Mathf.Min(1f, wobble + 0.25f);
            Health = h;
            UpdateTint();
        }

        void UpdateTint()
        {
            float h = Mathf.Clamp01(Health / maxHealth);
            Color c = Color.Lerp(baseColor * new Color(0.45f, 0.4f, 0.38f, 1f), baseColor, h);
            mpb.SetColor("_BaseColor", c);
            foreach (var r in tintRenderers) if (r) r.SetPropertyBlock(mpb);
        }

        void Update()
        {
            if (wobble <= 0f || Broken || !wobbleTarget) return;
            if (!restSaved) { restSaved = true; restPos = wobbleTarget.localPosition; restRot = wobbleTarget.localRotation; }
            wobble = Mathf.MoveTowards(wobble, 0f, Time.deltaTime * 4f);
            float t = Time.time * 45f;
            wobbleTarget.localPosition = restPos + new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t * 1.3f)) * 0.012f * wobble;
            wobbleTarget.localRotation = restRot * Quaternion.Euler(Mathf.Sin(t * 0.9f) * 2.5f * wobble, 0f, Mathf.Cos(t) * 2.5f * wobble);
            if (wobble <= 0f) { wobbleTarget.localPosition = restPos; wobbleTarget.localRotation = restRot; }
        }

        public void Break(Vector3 dir, Vector3 point)
        {
            if (Broken) return;
            Broken = true;
            Health = 0f;
            NetGame.CoverBroken(this, dir, point);
            Bounds b = col ? col.bounds : new Bounds(transform.position, Vector3.one);
            LoosenStackAbove(b);

            if (col) col.enabled = false;
            if (obstacle) obstacle.enabled = false;
            var rb = GetComponent<Rigidbody>();
            if (rb) { rb.isKinematic = true; rb.detectCollisions = false; }

            var debris = new List<Rigidbody>();
            if (pieces != null && pieces.Length > 0)
            {
                foreach (var p in pieces)
                {
                    if (!p) continue;
                    p.SetParent(null, true);
                    var bc = p.gameObject.AddComponent<BoxCollider>();
                    var prb = p.gameObject.AddComponent<Rigidbody>();
                    prb.mass = 4f;
                    debris.Add(prb);
                }
            }
            else
            {
                if (visual) visual.enabled = false;
                SpawnChunks(b, debris);
            }

            foreach (var d in debris)
            {
                Vector3 away = (d.position - point); away.y = Mathf.Abs(away.y) + 0.4f;
                d.linearVelocity = (dir.normalized * 2.5f + away.normalized * Random.Range(1.5f, 4f)) * (kind == Kind.Sand ? 0.5f : 1f);
                d.angularVelocity = Random.insideUnitSphere * 8f;
                var snd = d.gameObject.AddComponent<PhysicsDebris>();
                snd.sound = kind == Kind.Wood ? Sfx.ImpactConcrete : Sfx.Land; snd.volume = 0.25f; snd.maxSounds = 1;
            }

            Vector3 c = b.center;
            if (kind == Kind.Wood)
            {
                FX.Ensure().Chunks(c, Vector3.up, 30, new Color(0.6f, 0.45f, 0.27f), new Color(0.35f, 0.24f, 0.13f), 6f);
                FX.Ensure().DustBurst(c, Vector3.up, 8, new Color(0.55f, 0.48f, 0.38f, 0.5f), new Color(0.45f, 0.4f, 0.32f, 0.4f), 0.9f);
                ProcAudio.PlayAt(Sfx.WoodBreak, c, 0.9f, 0.1f);
            }
            else
            {
                FX.Ensure().Puff(c, Vector3.up, 14, 0.9f, new Vector2(0.5f, 2.5f), new Vector2(1f, 2.2f), new Vector2(1.5f, 3f),
                                 new Color(0.66f, 0.6f, 0.46f, 0.55f), new Color(0.55f, 0.5f, 0.38f, 0.45f));
                FX.Ensure().Chunks(c, Vector3.up, 25, new Color(0.7f, 0.63f, 0.45f), new Color(0.55f, 0.48f, 0.33f), 4f);
                ProcAudio.PlayAt(Sfx.SandBreak, c, 0.9f, 0.1f);
            }
            AIEvents.Emit(c, 15f, null);
            StartCoroutine(CleanUp(debris));
        }

        void SpawnChunks(Bounds b, List<Rigidbody> outList)
        {
            Material m = visual ? visual.sharedMaterial : null;
            Vector3 size = transform.lossyScale;
            int n = 3;
            Vector3 cell = size / n;
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    for (int z = 0; z < n; z++)
                    {
                        if (Random.value < 0.35f) continue;
                        // planks: flatten each chunk along a random axis
                        Vector3 s = cell * 0.92f;
                        int ax = Random.Range(0, 3);
                        s[ax] *= 0.3f;
                        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        go.name = "Debris";
                        go.transform.position = transform.TransformPoint(new Vector3((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f, (z + 0.5f) / n - 0.5f));
                        go.transform.rotation = transform.rotation * Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(-15f, 15f), Random.Range(-15f, 15f));
                        go.transform.localScale = s;
                        var r = go.GetComponent<Renderer>();
                        r.sharedMaterial = m;
                        r.SetPropertyBlock(mpb);
                        var rb = go.AddComponent<Rigidbody>();
                        rb.mass = 1.2f;
                        outList.Add(rb);
                    }
        }

        void LoosenStackAbove(Bounds b)
        {
            var hits = Physics.OverlapBox(new Vector3(b.center.x, b.max.y + 0.15f, b.center.z), new Vector3(b.extents.x * 0.9f, 0.2f, b.extents.z * 0.9f),
                                          Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (!h || h.transform.IsChildOf(transform)) continue;
                var d = h.GetComponentInParent<Destructible>();
                if (!d || d.Broken || d == this) continue;
                d.Loosen();
            }
        }

        /// <summary>Lost its support: becomes a physics body and tumbles down.</summary>
        public void Loosen()
        {
            if (Broken || GetComponent<Rigidbody>()) return;
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = kind == Kind.Sand ? 60f : 15f;
            rb.angularVelocity = Random.insideUnitSphere * 1.5f;
            gameObject.isStatic = false;
            wobbleTarget = null;
        }

        IEnumerator CleanUp(List<Rigidbody> debris)
        {
            yield return new WaitForSeconds(7f + Random.value * 2f);
            float t = 0f;
            var scales = new List<Vector3>();
            foreach (var d in debris) scales.Add(d ? d.transform.localScale : Vector3.zero);
            while (t < 1f)
            {
                t += Time.deltaTime / 1.2f;
                for (int i = 0; i < debris.Count; i++)
                    if (debris[i]) debris[i].transform.localScale = scales[i] * (1f - t);
                yield return null;
            }
            foreach (var d in debris) if (d) Destroy(d.gameObject);
            if (pieces != null && pieces.Length > 0 && transform.parent) Destroy(transform.parent.gameObject);
            else Destroy(gameObject);
        }
    }
}
