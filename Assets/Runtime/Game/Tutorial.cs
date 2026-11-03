using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// The tutorial stage (owner, 2026-10-07: the first stage, Basic, Fast and Rat zombies and Boss 1, with gameplay
    /// instructions). One instruction at a time on screen; each step brings its own zombies or items ahead of the
    /// player along the way to the Boss, and moves on when the player has done it. The stage itself is a normal boss
    /// stage: killing Boss 1 at the end wins it. No other zombies come (StageRuntime skips the random spawning).
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        public static Tutorial Current { get; private set; }

        private class Step
        {
            public string Touch, Pc;
            public Action Begin;
            public Func<bool> Done;
        }

        private StageRuntime _stage;
        private readonly List<Step> _steps = new List<Step>();
        private readonly List<Zombie> _wave = new List<Zombie>();
        private int _index = -1;
        private float _stepTime;
        private Vector3 _start;
        private Vector3 _forward;
        private bool _wasReloading;
        private int _grenadesAtStart;

        /// <summary>The step on screen (1-based) and how many there are.</summary>
        public int StepNumber => Mathf.Max(0, _index) + 1;
        public int StepCount => _steps.Count;

        /// <summary>The instruction on screen, for touch or for keyboard and mouse.</summary>
        public string Instruction(bool touch)
        {
            var i = Mathf.Max(0, _index);
            return i < _steps.Count ? (touch ? _steps[i].Touch : _steps[i].Pc) : null;
        }

        private static Survivor Player => GameSession.Instance != null ? GameSession.Instance.LocalSurvivor : null;

        public void Setup(StageRuntime stage)
        {
            _stage = stage;
            Current = this;
            var layout = stage.Layout;
            _forward = layout.BossPosition - layout.PlayerSpawn;
            _forward.y = 0f;
            _forward = _forward.sqrMagnitude > 0.01f ? _forward.normalized : Vector3.right;
            var tuning = GameSession.Instance != null ? GameSession.Instance.Tuning : new GameTuning();

            Add("Move: drag anywhere on the left half of the screen.",
                "Move: W A S D (or the arrow keys).",
                () => _start = Player.transform.position,
                () => Distance(_start) > 4f);
            Add("Aim and shoot: drag the fire stick on the right towards a zombie. Your character always faces where you aim.",
                "Aim with the mouse and hold the left button to shoot. Your character always faces where you aim.",
                () => Spawn(ZombieType.Basic, 12f, -1.5f, 1.5f),
                WaveDead);
            Add("Reload: tap Reload. Your gun also reloads by itself when the magazine is empty.",
                "Reload: press R. Your gun also reloads by itself when the magazine is empty.",
                () => _wasReloading = false,
                Reloaded);
            Add("A rifle! Walk over it and tap Pick up.",
                "A rifle! Walk over it and press E to pick it up.",
                () =>
                {
                    LootItem.SpawnLongGun(Ahead(4f, 0f), tuning.Firearm(WeaponId.AR), 30, null);
                    LootItem.SpawnAmmo(Ahead(4f, 1.5f), AmmoType.AR, 60, false);
                },
                () => Player.LongGun != null);
            Add("Switch weapons: tap the weapon button and choose the rifle.",
                "Switch weapons: press Q and choose the rifle.",
                null,
                () => Player.Selected == Slot.LongGun && !Player.Switching);
            Add("Fast zombies are red and run at you. Keep shooting!",
                "Fast zombies are red and run at you. Keep shooting!",
                () => Spawn(ZombieType.Fast, 16f, -2f, 0f, 2f),
                WaveDead);
            Add("Rats are small and quick. Don't let them reach you.",
                "Rats are small and quick. Don't let them reach you.",
                () => Spawn(ZombieType.Rat, 14f, -2f, -0.7f, 0.7f, 2f),
                WaveDead);
            Add("Knife: choose the knife with the weapon button and slash zombies up close. It never runs out.",
                "Knife: press Q, choose the knife, and click to slash zombies up close. It never runs out.",
                () => Spawn(ZombieType.Basic, 9f, 0f),
                () => Player.Selected == Slot.Knife && WaveDead());
            Add("Grenade: pick it up, choose it with the weapon button, then drag the fire stick to aim and let go to throw it into the group.",
                "Grenade: pick it up (E), choose it with Q, aim with the mouse and click to throw it into the group.",
                () =>
                {
                    LootItem.Spawn(Ahead(3f, 0f), LootKind.Grenade);
                    _grenadesAtStart = -1;
                    Spawn(ZombieType.Basic, 16f, -1f, -0.4f, 0.4f, 1f);
                },
                () =>
                {
                    var g = Player.Inventory.Grenades;
                    if (_grenadesAtStart < 0 && g > 0) _grenadesAtStart = g;
                    return _grenadesAtStart > 0 && g < _grenadesAtStart && WaveDead();
                });
            Add("You're hurt. Pick up the health item, open the Bag and tap it to heal.",
                "You're hurt. Pick up the health item (E), open the Bag (F) and click it to heal.",
                () =>
                {
                    var p = Player;
                    p.TakeHit(p.MaxHp * 0.3f, p.transform.position - _forward, 0f);
                    LootItem.Spawn(Ahead(3f, 0f), LootKind.Health);
                },
                () => Player.Healing || (Player.Hp >= Player.MaxHp && Time.time - _stepTime > 1f));
            Add("The Boss is ahead. Find it and kill it to finish the tutorial.",
                "The Boss is ahead. Find it and kill it to finish the tutorial.",
                null,
                () => false);
            // The steps start once the player is in the stage (the stage is built first).
        }

        private void Add(string touch, string pc, Action begin, Func<bool> done) =>
            _steps.Add(new Step { Touch = touch, Pc = pc, Begin = begin, Done = done });

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void Next()
        {
            _index++;
            _stepTime = Time.time;
            _wave.Clear();
            if (_index < _steps.Count) _steps[_index].Begin?.Invoke();
        }

        private void Update()
        {
            var player = Player;
            if (player == null || player.IsDead || _index >= _steps.Count) return;
            if (_index < 0)
            {
                Next();
                return;
            }
            // Each instruction stays up at least a moment, so it can be read.
            if (Time.time - _stepTime < 1.5f) return;
            if (_steps[_index].Done()) Next();
        }

        private float Distance(Vector3 from)
        {
            var d = Player.transform.position - from;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>A point <paramref name="ahead"/> m from the player towards the Boss, <paramref name="side"/> m across.</summary>
        private Vector3 Ahead(float ahead, float side)
        {
            var across = Vector3.Cross(Vector3.up, _forward);
            var p = Player.transform.position + _forward * ahead + across * side;
            // Inside the map, and short of the Boss's arena.
            var layout = _stage.Layout;
            p.x = Mathf.Clamp(p.x, 3f, layout.Size.x - 3f);
            p.z = Mathf.Clamp(p.z, 3f, layout.Size.y - 3f);
            for (var i = 0; i < 40 && layout.BossArea.Contains(new Vector2(p.x, p.z)); i++) p -= _forward;
            return p;
        }

        /// <summary>Zombies of a type ahead of the player, one per side offset, coming straight for the player.</summary>
        private void Spawn(ZombieType type, float ahead, params float[] sides)
        {
            foreach (var side in sides)
            {
                var z = _stage.SpawnZombie(type, Ahead(ahead, side));
                if (z == null) continue;
                z.JoinChase(Player, 60f);
                _wave.Add(z);
            }
        }

        private bool WaveDead()
        {
            foreach (var z in _wave)
                if (z != null && !z.IsDead) return false;
            return true;
        }

        private bool Reloaded()
        {
            var gun = Player.ActiveFirearm;
            if (gun == null) return true;
            if (gun.Reloading) _wasReloading = true;
            return _wasReloading && !gun.Reloading;
        }
    }
}
