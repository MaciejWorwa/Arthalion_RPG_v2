using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Arthalion.AI
{
    public static class CombatObservations
    {
        private static readonly HashSet<string> IgnoredStats = new HashSet<string> {
            "Id", "Overall", "Exp", "HighestDamageDealt", "TotalDamageDealt", "HighestDamageTaken", "TotalDamageTaken",
            "OpponentsKilled", "StrongestDefeatedOpponentOverall", "RoundsPlayed", "FortunateEvents", "UnfortunateEvents" };
        public static readonly FieldInfo[] StatFields = NumericFields(typeof(Stats)).Where(f => !IgnoredStats.Contains(f.Name)).ToArray();
        public static readonly FieldInfo[] UnitFields = NumericFields(typeof(Unit)).Where(f => f.Name != "UnitId" && f.Name != "IsSelected").ToArray();
        public static readonly FieldInfo[] WeaponFields = NumericFields(typeof(Weapon)).Where(f => f.Name != "Id" && f.Name != "Value" && f.Name != "BaseValue").ToArray();
        public static readonly FieldInfo[] SpellFields = NumericFields(typeof(SpellData)).Where(f => f.Name != "Id").ToArray();
        public const int UnitStrings = 64;
        public static int UnitSize => 9 + StatFields.Length + UnitFields.Length + UnitStrings * 4;
        public static int WeaponSize => 8 + WeaponFields.Length + 8 + 12 * 4;
        public static int EffectSize => 6 + StatFields.Length * 2;
        public static int SpellSize => 8 + SpellFields.Length + 8 + 16 * 4 + StatFields.Length;
        public static int VectorSize => 16 + UnitSize + CombatLearningContract.ActionOptions * 4;

        private static FieldInfo[] NumericFields(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(float) || f.FieldType == typeof(bool) || f.FieldType.IsEnum)
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();

        // Smooth, unbounded normalization: large or negative values are never silently clipped.
        public static float Scale(float value) => Mathf.Sign(value) * Mathf.Log(1 + Mathf.Abs(value)) / 4f;

        private static void Numbers(List<float> values, object instance, FieldInfo[] fields)
        {
            foreach (FieldInfo field in fields)
            {
                object value = field.GetValue(instance);
                values.Add(value is bool flag ? (flag ? 1 : 0) : Scale(Convert.ToSingle(value)));
            }
        }

        private static void Token(List<float> values, string value)
        {
            // Fixed, culture-independent categorical code. Do not use string.GetHashCode (process dependent).
            if (string.IsNullOrEmpty(value)) { values.AddRange(new float[4]); return; }
            uint a = 2166136261, b = 2246822519;
            unchecked { foreach (char c in value) { a = (a ^ c) * 16777619; b = (b ^ c) * 3266489917; } }
            values.Add((a & 65535) / 65535f); values.Add((a >> 16) / 65535f);
            values.Add((b & 65535) / 65535f); values.Add((b >> 16) / 65535f);
        }

        private static void Tokens(List<float> values, IEnumerable<string> tokens, int capacity)
        {
            string[] items = tokens.Where(t => !string.IsNullOrEmpty(t)).ToArray();
            if (items.Length > capacity) throw new InvalidOperationException($"Observation string capacity {capacity} exceeded ({items.Length}). Update the schema and retrain.");
            foreach (string token in items) Token(values, token);
            for (int i = items.Length; i < capacity; i++) Token(values, null);
        }

        public static float[] UnitRow(Unit unit, int slot, bool playerSide, bool active, bool spellSelected)
        {
            var values = new List<float>(UnitSize);
            values.AddRange(new[] { slot / (float)CombatLearningContract.MaxUnits, unit.CompareTag("PlayerUnit") == playerSide ? 1f : -1f,
                active ? 1f : 0f, spellSelected ? 1f : 0f,
                unit.transform.position.x / CombatLearningContract.BoardWidth, unit.transform.position.y / CombatLearningContract.BoardHeight,
                unit.Stats.MaxHealth == 0 ? 0 : unit.Stats.TempHealth / (float)unit.Stats.MaxHealth,
                CombatActionRules.IsPresent(unit) ? 1f : 0f, Scale(unit.UnitId) });
            Numbers(values, unit.Stats, StatFields); Numbers(values, unit, UnitFields);
            var tokens = new List<string> { "race:" + unit.Stats.Race, "type:" + unit.Stats.Type };
            Add(tokens, "magic", unit.Stats.Magic); Add(tokens, "resistance", unit.Stats.Resistance);
            Add(tokens, "slayer", unit.Stats.Slayer); Add(tokens, "specialist", unit.Stats.Specialist); Add(tokens, "unaffected", unit.Stats.Unaffected);
            Add(tokens, "primary", unit.Stats.PrimaryWeaponNames);
            if (unit.Stats.PrimaryWeaponAttributes != null)
                tokens.AddRange(unit.Stats.PrimaryWeaponAttributes.Select(p => "primaryAttribute:" + JsonUtility.ToJson(p)));
            Tokens(values, tokens, UnitStrings);
            return values.ToArray();
        }

        private static void Add(List<string> output, string key, IEnumerable<string> items)
        {
            if (items != null) output.AddRange(items.Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s, StringComparer.Ordinal).Select(s => key + ":" + s));
        }

        public static float[] WeaponRow(Weapon weapon, Inventory inventory, int owner, int index)
        {
            var values = new List<float>(WeaponSize);
            values.AddRange(new[] { owner / (float)CombatLearningContract.MaxUnits, index / (float)CombatLearningContract.MaxWeapons,
                inventory.EquippedWeapons[0] == weapon ? 1f : 0f, inventory.EquippedWeapons[1] == weapon ? 1f : 0f,
                inventory.EquippedArmors.Contains(weapon) ? 1f : 0f, 0f, 0f, 0f });
            Numbers(values, weapon, WeaponFields);
            if (weapon.Damage.Count > 8) throw new InvalidOperationException("A weapon has more than 8 damage components; update observation schema.");
            for (int i = 0; i < 8; i++) values.Add(i < weapon.Damage.Count ? Scale(weapon.Damage[i]) : 0);
            Tokens(values, new[] { "name:" + weapon.Name, "quality:" + weapon.Quality, "ammo:" + weapon.AmmoType }
                .Concat((weapon.Type ?? Array.Empty<string>()).Select(t => "type:" + t)), 12);
            return values.ToArray();
        }

        public static float[] EffectRow(SpellEffect effect, int owner)
        {
            var values = new List<float> { owner / (float)CombatLearningContract.MaxUnits, Scale(effect.RemainingRounds) };
            Token(values, effect.SpellName);
            foreach (FieldInfo field in StatFields) values.Add(effect.StatModifiers != null && effect.StatModifiers.TryGetValue(field.Name, out int n) ? Scale(n) : 0);
            foreach (FieldInfo field in StatFields) values.Add(effect.PreviousBoolValues != null && effect.PreviousBoolValues.TryGetValue(field.Name, out bool b) ? (b ? 1 : -1) : 0);
            return values.ToArray();
        }

        public static float[] SpellRow(SpellData spell, int index, Unit unit, bool selected)
        {
            var values = new List<float> { index / (float)CombatLearningContract.MaxSpells, CombatActionRules.KnowsSpell(unit, spell) ? 1 : 0,
                selected ? 1 : 0, 0, 0, 0, 0, 0 };
            Numbers(values, spell, SpellFields);
            if ((spell.Strength?.Length ?? 0) > 8) throw new InvalidOperationException("Spell strength exceeds schema capacity.");
            for (int i = 0; i < 8; i++) values.Add(i < (spell.Strength?.Length ?? 0) ? Scale(spell.Strength[i]) : 0);
            Tokens(values, new[] { spell.Name, spell.Arcane, spell.DamageType, spell.SaveAttribute, spell.SaveSkill }
                .Concat(spell.Type ?? Array.Empty<string>()), 16);
            foreach (FieldInfo field in StatFields)
                values.Add(Scale(spell.Attributes?.Where(a => a.Key == field.Name).Sum(a => a.Value) ?? 0));
            return values.ToArray();
        }

        public static float[] Board()
        {
            if (GridManager.Width > CombatLearningContract.BoardWidth || GridManager.Height > CombatLearningContract.BoardHeight)
                throw new InvalidOperationException("AI V1 supports boards up to 32 x 32. Expand the contract and retrain for larger boards.");
            var result = new float[CombatLearningContract.ActionOptions * 4];
            foreach (Tile tile in GridManager.Instance.Tiles)
            {
                if (tile == null) continue;
                Vector2 local = tile.transform.position - GridManager.Instance.transform.position;
                int slot = Mathf.RoundToInt(local.y) * CombatLearningContract.BoardWidth + Mathf.RoundToInt(local.x);
                result[slot * 4] = tile.gameObject.activeInHierarchy ? 1 : 0;
                result[slot * 4 + 1] = tile.IsOccupied ? 1 : 0;
                if (MapEditor.Instance == null) continue;
                foreach (GameObject element in MapEditor.Instance.AllElements)
                {
                    if (element == null) continue;
                    var obstacle = element.GetComponent<MapElement>();
                    var collider = element.GetComponent<Collider2D>();
                    if (obstacle == null || collider == null) continue;
                    bool contains;
                    if (collider is BoxCollider2D box)
                    {
                        Vector2 point = box.transform.InverseTransformPoint(tile.transform.position);
                        point -= box.offset;
                        contains = Mathf.Abs(point.x) <= box.size.x / 2 && Mathf.Abs(point.y) <= box.size.y / 2;
                    }
                    else
                    {
                        Vector3 point = tile.transform.position; point.z = collider.bounds.center.z;
                        contains = collider.bounds.Contains(point);
                    }
                    if (!contains) continue;
                    if (obstacle.IsHighObstacle) result[slot * 4 + 2] = 1;
                    if (obstacle.IsLowObstacle) result[slot * 4 + 3] = 1;
                }
            }
            return result;
        }

        public static string SchemaHash()
        {
            string text = CombatLearningContract.Version + ":" + CombatLearningContract.MaxUnits + ":" + CombatLearningContract.MaxWeapons + ":" +
                CombatLearningContract.MaxEffects + ":" + CombatLearningContract.MaxSpells + ":" + string.Join(",", StatFields.Concat(UnitFields).Concat(WeaponFields).Concat(SpellFields).Select(f => f.DeclaringType.Name + "." + f.Name)) +
                ":" + VectorSize + ":" + UnitSize + ":" + WeaponSize + ":" + EffectSize + ":" + SpellSize + ":" + string.Join(",", Enum.GetNames(typeof(CombatActionKind)));
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }
}
