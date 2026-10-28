using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Arthalion.AI
{
    // Inactive templates own independent equipment. A reset performs no disk IO and
    // does not run death rewards, drop inventory, or change the original save files.
    public sealed class TrainingBattleSnapshot : IDisposable
    {
        private readonly GameObject _root;
        private readonly List<GameObject> _templates = new List<GameObject>();
        private readonly Dictionary<int, List<SpellEffect>> _effects = new Dictionary<int, List<SpellEffect>>();
        private readonly Dictionary<int, bool> _active = new Dictionary<int, bool>();
        private readonly Transform _liveParent;

        public TrainingBattleSnapshot()
        {
            _root = new GameObject("AI episode templates"); _root.SetActive(false);
            Unit[] units = UnitsManager.Instance.AllUnits.Where(u => u != null).ToArray();
            _liveParent = units.First().transform.parent;
            var roots = units.Where(u => u.transform.parent == null || u.transform.parent.GetComponentInParent<Unit>() == null);
            foreach (Unit unit in units)
            {
                _effects[unit.UnitId] = CloneEffects(unit.Stats.ActiveSpellEffects);
                _active[unit.UnitId] = unit.gameObject.activeSelf;
            }
            foreach (Unit unit in roots)
            {
                GameObject template = UnityEngine.Object.Instantiate(unit.gameObject, _root.transform);
                template.name = unit.gameObject.name;
                foreach (Unit copy in template.GetComponentsInChildren<Unit>(true)) CloneEquipment(copy);
                _templates.Add(template);
            }
        }

        private static List<SpellEffect> CloneEffects(IEnumerable<SpellEffect> effects)
        {
            return effects.Select(e => new SpellEffect(e.SpellName, e.RemainingRounds,
                e.StatModifiers != null ? new Dictionary<string, int>(e.StatModifiers) : new Dictionary<string, int>())
                { PreviousBoolValues = new Dictionary<string, bool>(e.PreviousBoolValues) }).ToList();
        }

        private static void CloneEquipment(Unit unit)
        {
            Inventory inventory = unit.GetComponent<Inventory>();
            Weapon[] oldHands = (Weapon[])inventory.EquippedWeapons.Clone();
            Weapon[] oldArmor = inventory.EquippedArmors.ToArray();
            var map = new Dictionary<Weapon, Weapon>();
            foreach (Weapon source in inventory.AllWeapons.Concat(oldHands).Concat(oldArmor).Where(w => w != null).Distinct())
            {
                var item = new GameObject("AI equipment " + source.Name);
                item.transform.SetParent(unit.transform, false);
                Weapon copy = item.AddComponent<Weapon>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), copy);
                copy.WeaponsWithReloadLeft = new Dictionary<int, int>(source.WeaponsWithReloadLeft);
                map.Add(source, copy);
            }
            inventory.AllWeapons = inventory.AllWeapons.Where(w => w != null).Select(w => map[w]).ToList();
            inventory.EquippedWeapons = oldHands.Select(w => w != null ? map[w] : null).ToArray();
            inventory.EquippedArmors = oldArmor.Where(w => w != null).Select(w => map[w]).ToList();
        }

        public IEnumerator Restore(int seed, bool randomPositions, bool randomEquipment)
        {
            CombatManager.Instance.StopAllCoroutines();
            MovementManager.Instance.StopAllCoroutines();
            AutoCombatManager.Instance.StopAllCoroutines();
            AutoCombatManager.Instance.ResetActionTracking();
            MagicManager.Instance.StopAllCoroutines();
            InitiativeQueueManager.Instance.CancelPendingSelectUnitByQueue();
            RoundsManager.Instance.ResetRoundFlowState(true);
            Unit.SelectedUnit = Unit.LastSelectedUnit = null;
            var old = UnitsManager.Instance.AllUnits.Where(u => u != null).ToArray();
            UnitsManager.Instance.AllUnits.Clear();
            InitiativeQueueManager.Instance.InitiativeQueue.Clear();
            foreach (Unit unit in old)
            {
                if (unit == null || old.Any(other => other != null && other != unit && unit.transform.IsChildOf(other.transform))) continue;
                unit.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(unit.gameObject);
            }
            // Destroy is deferred; wait before occupancy/path queries.
            yield return null;
            var random = new System.Random(seed);
            foreach (GameObject template in _templates)
            {
                GameObject live = UnityEngine.Object.Instantiate(template, _liveParent);
                live.SetActive(false);
                foreach (Unit unit in live.GetComponentsInChildren<Unit>(true))
                {
                    unit.RestoredByLearning = true;
                    unit.Stats = unit.GetComponent<Stats>();
                    unit.Stats.ActiveSpellEffects = CloneEffects(_effects[unit.UnitId]);
                    unit.gameObject.SetActive(_active[unit.UnitId]);
                    unit.IsSelected = false; unit.IsTurnFinished = false; unit.TurnStartedInRound = 0;
                    unit.HasUsedOpportunityAttack = false; unit.LastAttackerStats = null;
                    unit.AISelectedHand = 0;
                    unit.CanDoAction = !unit.Unconscious;
                    unit.CanMove = !unit.Unconscious && !unit.Entangled && !unit.Grappled && unit.Stats.Sz > 0;
                    unit.IsRunning = unit.IsCharging = unit.IsFlying = unit.IsRetreating = false;
                    Inventory inventory = unit.GetComponent<Inventory>();
                    inventory.ArmorByLocation = new Dictionary<string, List<Weapon>>();
                    foreach (string location in new[] { "head", "arms", "torso", "legs" })
                        inventory.ArmorByLocation[location] = inventory.EquippedArmors.Where(w => w.Type.Contains(location)).ToList();
                    if (randomEquipment)
                    {
                        Weapon[] options = inventory.AllWeapons.Where(w => CombatActionRules.CanEquip(unit, w)).ToArray();
                        if (options.Length > 0) InventoryManager.Instance.EquipForAI(unit, options[random.Next(options.Length)], 0, false);
                    }
                    UnitsManager.Instance.AllUnits.Add(unit);
                }
                live.SetActive(true);
                foreach (Unit unit in live.GetComponentsInChildren<Unit>(true)) unit.gameObject.SetActive(_active[unit.UnitId]);
            }
            Physics2D.SyncTransforms(); GridManager.Instance.CheckTileOccupancy();
            if (randomPositions)
            {
                // Release all units first, then sample without replacement from legal cells.
                foreach (Unit unit in UnitsManager.Instance.AllUnits.Where(CombatActionRules.IsPresent))
                    GridManager.Instance.ResetTileOccupancy(unit.transform.position);
                List<Vector2> positions = GridManager.Instance.AvailablePositions();
                Unit[] visible = UnitsManager.Instance.AllUnits.Where(CombatActionRules.IsPresent).OrderBy(u => u.UnitId).ToArray();
                if (positions.Count < visible.Length) throw new InvalidOperationException("Not enough free cells to reset the training battle.");
                for (int i = positions.Count - 1; i > 0; i--) { int j = random.Next(i + 1); Vector2 p = positions[i]; positions[i] = positions[j]; positions[j] = p; }
                for (int i = 0; i < visible.Length; i++) visible[i].transform.position = positions[i];
            }
            Physics2D.SyncTransforms(); GridManager.Instance.CheckTileOccupancy();
            foreach (Unit unit in UnitsManager.Instance.AllUnits.Where(CombatActionRules.IsPresent))
                InitiativeQueueManager.Instance.AddUnitToInitiativeQueue(unit);
            yield return null;
            InitiativeQueueManager.Instance.UpdateInitiativeQueue();
        }

        public void Dispose() { if (_root != null) UnityEngine.Object.Destroy(_root); }
    }
}
