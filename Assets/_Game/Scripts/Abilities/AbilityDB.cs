using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    public enum AbilityType
    {
        None, Grapple, Parry, ButtRocket, ChickenTrap, Invisibility, Phase, FartBlind, FartSmoke, Swap,
        Shrink, BigHeads, Screamer, Confuse, Launch, Minesweeper, Build, BSOD, Spin, SkyArena
    }

    public struct AbilityInfo
    {
        public string name, desc, glyph;
        public Color color;
        public float cooldown;   // seconds, counted from activation
        public float duration;   // for timed abilities
        public int uses;         // 0 = unlimited (limited-use mode later: set > 0)
        public float radius;     // area abilities: affects enemies within this distance
        public float weight;     // spawn weight
    }

    /// <summary>Static description of every ability: names, colors, cooldowns, durations, spawn weights.</summary>
    public static class AbilityDB
    {
        static readonly Dictionary<AbilityType, AbilityInfo> map = new Dictionary<AbilityType, AbilityInfo>
        {
            [AbilityType.Grapple] = new AbilityInfo
            {
                name = "Крюк-кошка", glyph = "К", color = new Color(1f, 0.55f, 0.12f), cooldown = 7f, weight = 1f,
                desc = "Попал во врага — притянет его к вам. Мимо — притянет вас к точке."
            },
            [AbilityType.Parry] = new AbilityInfo
            {
                name = "Меч парирования", glyph = "М", color = new Color(0.6f, 0.82f, 1f), cooldown = 16f, duration = 5f, weight = 1f,
                desc = "5 сек. отбиваете все пули спереди (рикошетом в стрелка). Стрелять нельзя, ЛКМ — удар мечом."
            },
            [AbilityType.ButtRocket] = new AbilityInfo
            {
                name = "Гранатомёт", glyph = "Р", color = new Color(1f, 0.22f, 0.15f), cooldown = 12f, weight = 1f,
                desc = "Мощный реактивный заряд прямо в прицел. Откуда он вылетает — лучше не спрашивать."
            },
            [AbilityType.ChickenTrap] = new AbilityInfo
            {
                name = "Ловушка", glyph = "?", color = Color.white, duration = 8f, weight = 0.9f,
                desc = "Ко-ко-ко! Вы курица: вид от третьего лица, управление инвертировано."
            },
            [AbilityType.Invisibility] = new AbilityInfo
            {
                name = "Невидимость", glyph = "Н", color = new Color(0.3f, 1f, 0.95f), cooldown = 22f, duration = 10f, weight = 1f,
                desc = "10 сек. ваше тело невидимо. Оружие — нет. Выстрелы выдают вас."
            },
            [AbilityType.Phase] = new AbilityInfo
            {
                name = "Сквозь стены", glyph = "С", color = new Color(0.72f, 0.38f, 1f), cooldown = 20f, duration = 6f, weight = 1f,
                desc = "6 сек. летаете сквозь стены (Space/Ctrl — вверх/вниз). Потом вас отбросит обратно."
            },
            [AbilityType.FartBlind] = new AbilityInfo
            {
                name = "Громадный пук", glyph = "П", color = new Color(0.55f, 0.78f, 0.16f), cooldown = 15f, weight = 0.6f,
                desc = "Мощный выброс газов. Что-то произойдёт. Вопрос — что."
            },
            [AbilityType.FartSmoke] = new AbilityInfo
            {
                name = "Громадный пук", glyph = "П", color = new Color(0.55f, 0.78f, 0.16f), cooldown = 15f, duration = 12f, weight = 0.6f,
                desc = "Мощный выброс газов. Что-то произойдёт. Вопрос — что."
            },
            [AbilityType.Swap] = new AbilityInfo
            {
                name = "Рокировка", glyph = "О", color = new Color(1f, 0.3f, 0.8f), cooldown = 14f, weight = 0.9f,
                desc = "Мгновенно меняет вас местами с врагом в прицеле."
            },
            [AbilityType.Shrink] = new AbilityInfo
            {
                name = "Мини-я", glyph = "↓", color = new Color(0.45f, 0.95f, 0.4f), cooldown = 24f, duration = 12f, weight = 0.8f,
                desc = "12 сек. вы крошечный — в вас трудно попасть. Оружие остаётся полноразмерным."
            },
            [AbilityType.BigHeads] = new AbilityInfo
            {
                name = "Большие головы", glyph = "Г", color = new Color(1f, 0.6f, 0.72f), cooldown = 20f, duration = 12f, radius = 18f, weight = 0.8f,
                desc = "Врагам рядом раздувает головы — хедшоты проще простого."
            },
            [AbilityType.Screamer] = new AbilityInfo
            {
                name = "Скример", glyph = "!", color = new Color(0.9f, 0.12f, 0.1f), cooldown = 18f, radius = 12f, weight = 0.8f,
                desc = "Всем врагам рядом — орущее лицо прямо в глаза."
            },
            [AbilityType.Confuse] = new AbilityInfo
            {
                name = "Путаница", glyph = "↔", color = new Color(1f, 0.55f, 0.1f), cooldown = 18f, duration = 7f, radius = 14f, weight = 0.8f,
                desc = "У врагов рядом всё управление задом наперёд."
            },
            [AbilityType.Launch] = new AbilityInfo
            {
                name = "Катапульта", glyph = "↑", color = new Color(0.35f, 0.6f, 1f), cooldown = 16f, radius = 10f, weight = 0.8f,
                desc = "Враги рядом улетают высоко в небо."
            },
            [AbilityType.Minesweeper] = new AbilityInfo
            {
                name = "Сапёр", glyph = "#", color = new Color(0.7f, 0.72f, 0.75f), cooldown = 25f, radius = 12f, weight = 0.7f,
                desc = "Все рядом — и вы тоже! — не сдвинутся, пока не решат сапёра 4×4. Ошибка — взрыв."
            },
            [AbilityType.Build] = new AbilityInfo
            {
                name = "Стройка", glyph = "Д", color = new Color(0.85f, 0.6f, 0.3f), cooldown = 25f, duration = 12f, weight = 0.9f,
                desc = "12 сек. строите: ЛКМ — стена, ПКМ — пандус, R — повернуть. До 6 построек."
            },
            [AbilityType.BSOD] = new AbilityInfo
            {
                name = "Синий экран", glyph = ":(", color = new Color(0.1f, 0.5f, 0.95f), cooldown = 20f, duration = 4.5f, radius = 14f, weight = 0.7f,
                desc = "Враги рядом ловят синий экран смерти и зависают."
            },
            [AbilityType.Spin] = new AbilityInfo
            {
                name = "Карусель", glyph = "@", color = new Color(0.95f, 0.85f, 0.25f), cooldown = 18f, duration = 3f, radius = 14f, weight = 0.8f,
                desc = "У врагов рядом камеру крутит на 360° — снова и снова, 3 секунды."
            },
            [AbilityType.SkyArena] = new AbilityInfo
            {
                name = "Небесный тир", glyph = "+", color = new Color(0.85f, 0.15f, 1f), cooldown = 1f, duration = 20f, uses = 1, weight = 0.35f,
                desc = "Одноразовая. Все остальные — на тёмную арену: только бег и ножи, мгновенное воскрешение, скорость ×2. Вы сверху с пулемётом. 20 сек."
            },
        };

        public static AbilityInfo Get(AbilityType t) => map.TryGetValue(t, out var i) ? i : default;

        public static bool IsFart(AbilityType t) => t == AbilityType.FartBlind || t == AbilityType.FartSmoke;

        /// <summary>Weighted random ability for a pickup; trap returns a disguise of a real one.</summary>
        public static AbilityType RandomSpawn(out AbilityType shown)
        {
            float total = 0f;
            foreach (var kv in map) total += kv.Value.weight;
            float r = Random.value * total;
            AbilityType pick = AbilityType.Grapple;
            foreach (var kv in map)
            {
                r -= kv.Value.weight;
                if (r <= 0f) { pick = kv.Key; break; }
            }
            shown = pick == AbilityType.ChickenTrap ? RandomDisguise() : pick;
            return pick;
        }

        public static AbilityType RandomDisguise()
        {
            var real = new[]
            {
                AbilityType.Grapple, AbilityType.Parry, AbilityType.ButtRocket, AbilityType.Invisibility, AbilityType.Phase, AbilityType.FartBlind,
                AbilityType.Swap, AbilityType.Shrink, AbilityType.BigHeads, AbilityType.Screamer,
                AbilityType.Confuse, AbilityType.Launch, AbilityType.Minesweeper, AbilityType.Build, AbilityType.BSOD, AbilityType.Spin, AbilityType.SkyArena
            };
            return real[Random.Range(0, real.Length)];
        }

        /// <summary>Weapon names that are not bullets (cannot be parried).</summary>
        public static bool IsBullet(string weapon)
            => !(weapon == "Граната" || weapon == "Нож" || weapon == "Меч" || weapon == "Ягодичная ракета" || weapon == "Рикошет" || weapon == "Сапёр" || string.IsNullOrEmpty(weapon));
    }
}
