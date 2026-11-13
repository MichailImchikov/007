using System.IO;
using UnityEngine;

namespace FPS
{
    /// <summary>Disco map: the head's volley warning and its targets travel host → clients (the blasts are ordinary Explosion messages).</summary>
    public partial class NetGame
    {
        public static void SendDiscoWarn()
        {
            if (!Ok || !I.Hosting) return;
            var w = I.Begin(Msg.DiscoVolley);
            w.Write((byte)0);
            I.Publish(I.End());
        }

        public static void SendDiscoVolley(Vector3[] targets, float[] delays)
        {
            if (!Ok || !I.Hosting || targets == null || delays == null) return;
            var w = I.Begin(Msg.DiscoVolley);
            int n = Mathf.Min(targets.Length, delays.Length, 255);
            w.Write((byte)n);
            for (int i = 0; i < n; i++) { WV(w, targets[i]); w.Write(delays[i]); }
            I.Publish(I.End());
        }

        void DispatchDisco(Msg m, BinaryReader r, bool fromHost)
        {
            if (m != Msg.DiscoVolley || !fromHost) return;
            var head = DiscoHead.Instance;
            int n = r.ReadByte();
            if (n == 0) { if (head) head.LocalWarn(); return; }
            var targets = new Vector3[n];
            var delays = new float[n];
            for (int i = 0; i < n; i++) { targets[i] = RV(r); delays[i] = r.ReadSingle(); }
            if (head) head.StartVolley(targets, delays, true);
        }
    }
}
