using System.IO;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// One character's replicated state (~50 bytes): transform, look direction and every public
    /// CharacterAnimator parameter (stance, lean, actions, ability poses) plus ability visibility flags.
    /// Captured from whatever drives a character locally (player or bot) and applied to its NetProxy elsewhere.
    /// </summary>
    public struct CharState
    {
        public const ushort Alive = 1, Grounded = 2, Holding = 4, Sword = 8, Invisible = 16, Chicken = 32, Snowman = 64,
                            Phasing = 128, ButtRocket = 256, DeadForward = 512, BigHead = 1024, Confused = 2048, Protected = 4096;

        public int id;
        public Vector3 pos;
        public float yaw, pitch;
        public Vector3 vel;     // local space, m/s
        public ushort flags;
        public float crouch, prone, lean, aim, pistol, knife, throwT, execA, execV, dead, bent, parry, slip, blind, lookYaw, bodyScale, headScale, health;
        public byte parryVariant, weapon;

        public bool Has(ushort f) => (flags & f) != 0;

        public static readonly string[] Weapons = { "M4A1", "SMG-9", "M870", "DMR-7", "P-17" };

        public static byte WeaponIndex(string n)
        {
            for (int i = 0; i < Weapons.Length; i++) if (Weapons[i] == n) return (byte)i;
            return 255;
        }

        static byte U(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
        static float U(byte b) => b / 255f;
        // -1 = "none", 0..1 = progress
        static byte O(float v) => v < 0f ? (byte)0 : (byte)(1 + Mathf.RoundToInt(Mathf.Clamp01(v) * 254f));
        static float O(byte b) => b == 0 ? -1f : (b - 1) / 254f;
        static byte S(float v) => (byte)Mathf.RoundToInt((Mathf.Clamp(v, -1f, 1f) * 0.5f + 0.5f) * 255f);
        static float S(byte b) => b / 255f * 2f - 1f;
        static short V(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 100f), short.MinValue, short.MaxValue);

        public void Write(BinaryWriter w)
        {
            w.Write((short)id);
            w.Write(pos.x); w.Write(pos.y); w.Write(pos.z);
            w.Write((ushort)Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 360f * 65535f));
            w.Write((sbyte)Mathf.Clamp(Mathf.RoundToInt(pitch), -90, 90));
            w.Write(V(vel.x)); w.Write(V(vel.y)); w.Write(V(vel.z));
            w.Write(flags);
            w.Write(U(crouch)); w.Write(U(prone)); w.Write(S(lean)); w.Write(U(aim)); w.Write(U(pistol));
            w.Write(O(knife)); w.Write(O(throwT)); w.Write(O(execA)); w.Write(O(execV));
            w.Write(U(dead)); w.Write(U(bent)); w.Write(O(parry)); w.Write(parryVariant); w.Write(U(slip)); w.Write(U(blind));
            w.Write(S(lookYaw / 90f)); w.Write(U(bodyScale / 4f)); w.Write(U(headScale / 4f)); w.Write(U(health)); w.Write(weapon);
        }

        public static CharState Read(BinaryReader r)
        {
            var s = new CharState();
            s.id = r.ReadInt16();
            s.pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            s.yaw = r.ReadUInt16() / 65535f * 360f;
            s.pitch = r.ReadSByte();
            s.vel = new Vector3(r.ReadInt16() / 100f, r.ReadInt16() / 100f, r.ReadInt16() / 100f);
            s.flags = r.ReadUInt16();
            s.crouch = U(r.ReadByte()); s.prone = U(r.ReadByte()); s.lean = S(r.ReadByte()); s.aim = U(r.ReadByte()); s.pistol = U(r.ReadByte());
            s.knife = O(r.ReadByte()); s.throwT = O(r.ReadByte()); s.execA = O(r.ReadByte()); s.execV = O(r.ReadByte());
            s.dead = U(r.ReadByte()); s.bent = U(r.ReadByte()); s.parry = O(r.ReadByte()); s.parryVariant = r.ReadByte(); s.slip = U(r.ReadByte()); s.blind = U(r.ReadByte());
            s.lookYaw = S(r.ReadByte()) * 90f; s.bodyScale = U(r.ReadByte()) * 4f; s.headScale = U(r.ReadByte()) * 4f; s.health = U(r.ReadByte()); s.weapon = r.ReadByte();
            return s;
        }

        /// <summary>Reads the state of a locally simulated character (local player or a bot on the host).</summary>
        public static CharState Capture(Combatant c)
        {
            var s = new CharState { id = c.netId, pos = c.transform.position, yaw = c.transform.eulerAngles.y, bodyScale = 1f, headScale = 1f, weapon = 255 };
            var anim = c.GetComponentInChildren<CharacterAnimator>();
            if (anim)
            {
                s.pitch = anim.aimPitch; s.vel = anim.localVelocity;
                s.crouch = anim.crouch; s.prone = anim.prone; s.lean = anim.lean; s.aim = anim.aim; s.pistol = anim.pistolHold;
                s.knife = anim.knife; s.throwT = anim.throwT; s.execA = anim.executeAttack; s.execV = anim.executeVictim;
                s.dead = anim.dead; s.bent = anim.bentOver; s.parry = anim.parry; s.parryVariant = (byte)anim.parryVariant; s.slip = anim.slip; s.blind = anim.blind;
                s.lookYaw = anim.lookYaw;
                if (anim.grounded) s.flags |= Grounded;
                if (anim.holdingWeapon) s.flags |= Holding;
                if (anim.deadDir > 0f) s.flags |= DeadForward;
            }
            if (c.alive) s.flags |= Alive;
            var u = c.abilities;
            if (u)
            {
                if (u.Parrying) s.flags |= Sword;
                if (u.Invisible) s.flags |= Invisible;
                if (u.IsChicken) s.flags |= Chicken;
                if (u.IsSnowman) s.flags |= Snowman;
                if (u.PhaseActive) s.flags |= Phasing;
                if (u.ButtRocketShown) s.flags |= ButtRocket;
                if (u.BigHead) s.flags |= BigHead;
                if (u.Confused) s.flags |= Confused;
                s.bodyScale = u.BodyScale;
                s.headScale = u.HeadScale;
            }
            var ph = c.GetComponent<PlayerHealth>();
            if (ph)
            {
                s.health = ph.Health / Mathf.Max(1f, ph.maxHealth);
                if (ph.Protected) s.flags |= Protected;
                var lo = ph.loadout;
                if (lo && lo.Active) s.weapon = WeaponIndex(lo.Active.weaponName);
                if (ph.playerCam) s.yaw = ph.playerCam.Yaw;
            }
            else
            {
                var bot = c.GetComponent<CombatBot>();
                if (bot) { s.health = bot.Health01; s.weapon = WeaponIndex(bot.weaponName); }
            }
            return s;
        }
    }
}
