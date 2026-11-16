using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Shooting range: a knife sparring ring behind the ability stands (two rows at z ≈ -9 / -12). Three unarmed training partners (easy / normal / hard)
    /// answer every knife swing of the player in front of them, so the blades lock into a <see cref="KnifeDuel"/>.
    /// </summary>
    public static partial class RangeBuilder
    {
        static readonly Vector3 RingCenter = new Vector3(0f, 0f, -20.5f);

        static void BuildKnifeRing(Transform env, Transform bots)
        {
            var ring = new GameObject("KnifeRing").transform;
            ring.SetParent(env, false);
            var mat = Lit("RingMat", new Color(0.42f, 0.12f, 0.1f), 0f, 0.35f);
            var line = Lit("RingLine", new Color(0.92f, 0.86f, 0.7f), 0f, 0.3f);
            var post = Lit("RingPost", new Color(0.14f, 0.14f, 0.15f), 0.6f, 0.45f);
            var rope = Lit("RingRope", new Color(0.85f, 0.2f, 0.15f), 0f, 0.4f);
            var plate = Lit("SignPlate", new Color(0.08f, 0.08f, 0.09f), 0f, 0.3f);

            const float W = 9f, D = 6f; // ring footprint
            Vector3 c = RingCenter;
            // mat flush with the ground (no collider: nothing to trip over)
            Prim(PrimitiveType.Cube, "RingMat", ring, c + Vector3.up * 0.006f, new Vector3(W, 0.012f, D), mat, null, false, true);
            Prim(PrimitiveType.Cube, "RingLineN", ring, c + new Vector3(0f, 0.014f, D * 0.5f - 0.1f), new Vector3(W, 0.004f, 0.08f), line, null, false, true);
            Prim(PrimitiveType.Cube, "RingLineS", ring, c + new Vector3(0f, 0.014f, -D * 0.5f + 0.1f), new Vector3(W, 0.004f, 0.08f), line, null, false, true);
            Prim(PrimitiveType.Cylinder, "RingCircle", ring, c + new Vector3(0f, 0.013f, 0f), new Vector3(2.4f, 0.002f, 2.4f), line, null, false, true);

            // corner posts and ropes on three sides (the north side, toward the start, stays open)
            var corners = new[] { new Vector3(-W, 0f, -D), new Vector3(W, 0f, -D), new Vector3(W, 0f, D), new Vector3(-W, 0f, D) };
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cylinder, "Post" + i, ring, c + corners[i] * 0.5f + Vector3.up * 0.65f, new Vector3(0.14f, 0.65f, 0.14f), post, null, true, true);
            for (int h = 0; h < 2; h++)
            {
                float y = 0.55f + h * 0.45f;
                Prim(PrimitiveType.Cube, "RopeS" + h, ring, c + new Vector3(0f, y, -D * 0.5f), new Vector3(W, 0.035f, 0.035f), rope, null, false, true);
                Prim(PrimitiveType.Cube, "RopeW" + h, ring, c + new Vector3(-W * 0.5f, y, 0f), new Vector3(0.035f, 0.035f, D), rope, null, false, true);
                Prim(PrimitiveType.Cube, "RopeE" + h, ring, c + new Vector3(W * 0.5f, y, 0f), new Vector3(0.035f, 0.035f, D), rope, null, false, true);
            }

            // board behind the partners, facing the start
            Block("RingBoardPostL", ring, c.x - 2.6f, c.z - D * 0.5f - 0.6f, new Vector3(0.12f, 2.4f, 0.12f), post);
            Block("RingBoardPostR", ring, c.x + 2.6f, c.z - D * 0.5f - 0.6f, new Vector3(0.12f, 2.4f, 0.12f), post);
            Sign(ring, new Vector3(c.x, 2.75f, c.z - D * 0.5f - 0.6f), 0f, "ДУЭЛЬ НА НОЖАХ", new Color(1f, 0.82f, 0.4f), 0.42f, plate, 5.6f);
            Prim(PrimitiveType.Cube, "RingBoardText", ring, new Vector3(c.x, 2.1f, c.z - D * 0.5f - 0.62f), new Vector3(5.4f, 0.56f, 0.04f), plate, null, false, true);
            Sign(ring, new Vector3(c.x, 2.22f, c.z - D * 0.5f - 0.58f), 0f, "Встань перед манекеном и ударь ножом [F]", Color.white, 0.17f, null);
            Sign(ring, new Vector3(c.x, 1.98f, c.z - D * 0.5f - 0.58f), 0f, "Клинки сцепятся — жми F чаще соперника", new Color(1f, 1f, 1f, 0.8f), 0.15f, null);

            // three partners, one per difficulty
            var partners = new[] { ("Новичок", 4.4f, new Color(0.45f, 0.95f, 0.45f)), ("Боец", 6.1f, new Color(1f, 0.85f, 0.3f)), ("Мастер", 7.8f, new Color(1f, 0.35f, 0.3f)) };
            for (int i = 0; i < partners.Length; i++)
            {
                var (name, rate, col) = partners[i];
                Vector3 p = c + new Vector3((1 - i) * 3f, 0f, -0.9f); // seen from the open side: easy on the left
                var bot = MakeBot(bots, p, 0f, null);
                bot.name = "SparringPartner_" + name;
                bot.sparring = true;
                bot.duelRate = rate;
                bot.sparName = name;
                bot.respawnDelay = 3f;
                Sign(ring, p + new Vector3(0f, 0.32f, 1.05f), 0f, name.ToUpperInvariant(), col, 0.2f, plate, 1.3f);
            }
        }
    }
}
