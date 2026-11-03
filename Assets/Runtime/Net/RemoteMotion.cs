using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// The other player's movement, played back smoothly (owner, 2026-10-07: watching the teammate move was always jittery;
    /// their updates arrive unevenly, and each was shown the moment it came). Every update carries the time it was sent;
    /// the character is shown where it was a short moment ago (<see cref="Delay"/>), so there is nearly always an update on
    /// either side to slide between at an even speed. The delay adapts to how unevenly updates arrive. When updates stop
    /// for a moment it carries on the same way briefly, then waits. Pure logic: the caller passes the clock.
    /// </summary>
    public class RemoteMotion
    {
        private struct Sample
        {
            public float T;
            public Vector3 P;
            public float Yaw;
        }

        public const float MinDelay = 0.06f;
        public const float MaxDelay = 0.3f;
        /// <summary>How long to carry on past the last update before waiting.</summary>
        public const float MaxExtrapolate = 0.12f;
        /// <summary>A jump this far between updates is a teleport (respawn): shown at once, not slid.</summary>
        public const float TeleportDistance = 5f;

        private readonly List<Sample> _samples = new List<Sample>();
        private bool _any;
        private float _offset;      // sender clock minus local clock, for the least delayed update
        private float _lastSent;
        private float _lastLocal;
        private float _interval = 0.05f;
        private float _lateness;    // how much later than the least delayed one updates typically arrive
        private float _delayTarget = 0.1f;
        private float _lastSampleAt = -1f;

        /// <summary>How far behind the newest update the character is shown, in seconds.</summary>
        public float Delay { get; private set; } = 0.1f;

        public bool HasData => _samples.Count > 0;

        public void Clear()
        {
            _samples.Clear();
            _any = false;
            _lastSampleAt = -1f;
        }

        /// <param name="sent">The sender's clock when it sent this.</param>
        /// <param name="now">The local clock now (on arrival).</param>
        public void Add(float sent, float now, Vector3 position, float yaw)
        {
            if (_any && sent <= _lastSent) return; // late or repeated
            var offset = sent - now;
            if (!_any)
            {
                _offset = offset;
                _any = true;
            }
            else
            {
                _interval = Mathf.Lerp(_interval, Mathf.Clamp(sent - _lastSent, 0.01f, 0.5f), 0.1f);
                // Follow the least delayed arrival; drift down slowly so a lasting slowdown is followed too.
                _offset = Mathf.Max(offset, _offset - (now - _lastLocal) * 0.01f);
                _lateness = Mathf.Lerp(_lateness, _offset - offset, 0.1f);
            }
            if (_samples.Count > 0 && (position - _samples[_samples.Count - 1].P).sqrMagnitude > TeleportDistance * TeleportDistance) _samples.Clear();
            _samples.Add(new Sample { T = sent, P = position, Yaw = yaw });
            _lastSent = sent;
            _lastLocal = now;
            _delayTarget = Mathf.Clamp(_interval * 1.5f + _lateness * 2f, MinDelay, MaxDelay);
            // Keep about a second and a half.
            while (_samples.Count > 2 && _samples[1].T < sent - 1.5f) _samples.RemoveAt(0);
        }

        /// <summary>Where to show the character at local time <paramref name="now"/>. False until the first update.</summary>
        public bool Get(float now, out Vector3 position, out float yaw)
        {
            position = Vector3.zero;
            yaw = 0f;
            if (_samples.Count == 0) return false;
            // The delay eases towards its target, so the playback never jumps: it grows by up to 15 % of real time (updates
            // turned uneven) and shrinks by 5 % (they settled).
            var firstCall = _lastSampleAt < 0f;
            var dt = firstCall ? 0f : Mathf.Clamp(now - _lastSampleAt, 0f, 0.25f);
            _lastSampleAt = now;
            Delay = firstCall ? _delayTarget : Mathf.MoveTowards(Delay, _delayTarget, dt * (_delayTarget > Delay ? 0.15f : 0.05f));
            var t = now + _offset - Delay;
            var first = _samples[0];
            if (t <= first.T || _samples.Count == 1)
            {
                position = first.P;
                yaw = first.Yaw;
                return true;
            }
            for (var i = 0; i + 1 < _samples.Count; i++)
            {
                var a = _samples[i];
                var b = _samples[i + 1];
                if (t > b.T) continue;
                var k = (t - a.T) / Mathf.Max(1e-4f, b.T - a.T);
                position = Vector3.Lerp(a.P, b.P, k);
                yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, k);
                return true;
            }
            // Past the newest update: carry on the same way for a moment, then wait.
            var last = _samples[_samples.Count - 1];
            var before = _samples[_samples.Count - 2];
            var span = Mathf.Max(1e-4f, last.T - before.T);
            var ahead = Mathf.Min(t - last.T, MaxExtrapolate);
            position = last.P + (last.P - before.P) / span * ahead;
            yaw = last.Yaw;
            return true;
        }
    }
}
