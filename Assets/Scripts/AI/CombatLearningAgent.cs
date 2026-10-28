using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace Arthalion.AI
{
    // One trajectory per team; the current unit is explicitly identified in observations.
    // Parameter selection is a learned subdecision, never a hard-coded target ranking.
    public sealed class CombatLearningAgent : Agent
    {
        private ReinforcementLearningManager _owner;
        private BufferSensorComponent _unitsSensor, _weaponsSensor, _effectsSensor, _spellsSensor;
        private List<Unit> _units;
        private Unit _unit;
        private CombatCommand _command;
        private HashSet<int> _legal = new HashSet<int>();
        private int _selected = -1, _phase;
        private CombatActionKind _kind;
        private bool _pending;
        private System.Random _random = new System.Random(1);
        public bool PlayerSide { get; private set; }

        public void Configure(ReinforcementLearningManager owner, bool playerSide,
            BufferSensorComponent units, BufferSensorComponent weapons, BufferSensorComponent effects, BufferSensorComponent spells)
        {
            _owner = owner; PlayerSide = playerSide;
            _unitsSensor = units; _weaponsSensor = weapons; _effectsSensor = effects; _spellsSensor = spells;
        }

        public void SetEpisodeSeed(int seed) { _random = new System.Random(seed); }
        public void ClearTerminalObservation() { _unit = null; _units = null; _command = null; }

        private int Decide(IEnumerable<int> choices, int phase)
        {
            _legal = new HashSet<int>(choices); _phase = phase; _selected = -1;
            if (_legal.Count == 0) throw new InvalidOperationException("Empty legal action mask.");
            if (_legal.Any(i => i < 0 || i >= CombatLearningContract.ActionOptions))
                throw new InvalidOperationException("Action index exceeds the model contract.");
            _pending = true;
            RequestDecision();
            Academy.Instance.EnvironmentStep();
            _pending = false;
            if (!_legal.Contains(_selected)) throw new InvalidOperationException("Policy returned a masked action or did not respond.");
            _owner.RecordPolicyDecision();
            return _selected;
        }

        public CombatCommand Choose(Unit unit, HashSet<string> visitedLoadouts)
        {
            _owner.PrepareTeamDecision(unit);
            _unit = unit; _command = null;
            _units = UnitsManager.Instance.AllUnits.Where(u => u != null).OrderBy(u => u.UnitId).ToList();
            if (_units.Count > CombatLearningContract.MaxUnits) throw new InvalidOperationException("AI unit capacity exceeded. Increase capacity and retrain.");
            var groups = new Dictionary<int, Dictionary<int, CombatCommand>>();
            foreach (CombatActionKind kind in Enum.GetValues(typeof(CombatActionKind)))
            {
                var commands = CombatActionRules.BuildParameters(unit, kind, _units, _owner.Spells, visitedLoadouts);
                if (commands.Count > 0) groups.Add((int)kind, commands);
            }
            _kind = (CombatActionKind)Decide(groups.Keys, 0);
            var parameters = groups[(int)_kind];
            int index = parameters.Count == 1 ? parameters.Keys.First() : Decide(parameters.Keys, 1);
            _command = parameters[index];
            if (_kind == CombatActionKind.CastSpell)
            {
                var criticals = new Dictionary<int, string> { { 0, "" } };
                SpellData spell = _command.Spell;
                if (!spell.Type.Contains("self-only") && spell.AreaSize == 0) criticals.Add(1, "target");
                if (spell.Type.Contains("offensive") && !spell.Type.Contains("no-damage")) criticals.Add(2, "damage");
                if (spell.Range != 1.5f) criticals.Add(3, "range");
                if (spell.AreaSize != 0) criticals.Add(4, "area_size");
                if (spell.Duration != 0) criticals.Add(5, "duration");
                _command.CriticalChoice = criticals[Decide(criticals.Keys, 3)];
                int maxTargets = Math.Max(1, spell.Targets) * (_command.CriticalChoice == "target" ? 2 : 1);
                while (_command.SpellTargets.Count < maxTargets)
                {
                    var targets = CombatActionRules.SpellTargetChoices(unit, spell, _units, _command.SpellTargets);
                    var keys = targets.Keys.ToList();
                    if (_command.SpellTargets.Count > 0) keys.Add(0);
                    if (keys.Count == 0) break;
                    int selected = Decide(keys, 2);
                    if (selected == 0) break;
                    _command.SpellTargets.Add(targets[selected]);
                }
            }
            return _command;
        }

        public int ChooseReaction(Unit unit, int phase, int[] choices)
        {
            _owner.PrepareTeamDecision(unit);
            _unit = unit; _command = null;
            _units = UnitsManager.Instance.AllUnits.Where(u => u != null).OrderBy(u => u.UnitId).ToList();
            return Decide(choices, phase);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (_pending) _selected = actions.DiscreteActions[0];
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            // During terminal serialization there may be no current request.
            for (int i = 0; i < CombatLearningContract.ActionOptions; i++)
                actionMask.SetActionEnabled(0, i, _pending ? _legal.Contains(i) : i == 0);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            // Used only by the explicit smoke-test/random baseline option. Training refuses
            // to silently fall back to this when the Python trainer is missing.
            int[] options = _legal.OrderBy(i => i).ToArray();
            var output = actionsOut.DiscreteActions;
            output[0] = options.Length == 0 ? 0 : options[_random.Next(options.Length)];
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (_units != null)
                _units = UnitsManager.Instance.AllUnits.Where(u => u != null).OrderBy(u => u.UnitId).ToList();
            if (_unit == null || _units == null)
            {
                sensor.AddObservation(new float[CombatObservations.VectorSize]);
                return;
            }
            sensor.AddObservation(_phase / 3f);
            sensor.AddObservation((int)_kind / (float)Enum.GetValues(typeof(CombatActionKind)).Length);
            sensor.AddObservation(Mathf.Max(0, _owner.MaxRounds - RoundsManager.RoundNumber) / (float)_owner.MaxRounds);
            sensor.AddObservation(InventoryManager.Instance.SelectedHand);
            sensor.AddObservation(GridManager.Width / (float)CombatLearningContract.BoardWidth);
            sensor.AddObservation(GridManager.Height / (float)CombatLearningContract.BoardHeight);
            sensor.AddObservation(_command != null ? _command.SpellTargets.Count / 8f : 0f);
            sensor.AddObservation(_command != null ? CriticalIndex(_command.CriticalChoice) / 5f : 0f);
            int attackerSlot = Unit.SelectedUnit != null ? _units.FindIndex(u => u.gameObject == Unit.SelectedUnit) : -1;
            sensor.AddObservation(attackerSlot / (float)CombatLearningContract.MaxUnits);
            sensor.AddObservation(new float[7]);
            int active = _units.IndexOf(_unit);
            sensor.AddObservation(CombatObservations.UnitRow(_unit, active, PlayerSide, true, false));
            sensor.AddObservation(CombatObservations.Board());
            int weaponCount = 0, effectCount = 0;
            for (int i = 0; i < _units.Count; i++)
            {
                Unit u = _units[i];
                _unitsSensor.AppendObservation(CombatObservations.UnitRow(u, i, PlayerSide, i == active,
                    _command != null && _command.SpellTargets.Contains(u.gameObject)));
                Inventory inventory = u.GetComponent<Inventory>();
                for (int w = 0; w < inventory.AllWeapons.Count; w++)
                {
                    if (++weaponCount > CombatLearningContract.MaxWeapons) throw new InvalidOperationException("AI weapon observation capacity exceeded.");
                    _weaponsSensor.AppendObservation(CombatObservations.WeaponRow(inventory.AllWeapons[w], inventory, i, w));
                }
                foreach (SpellEffect effect in u.Stats.ActiveSpellEffects)
                {
                    if (++effectCount > CombatLearningContract.MaxEffects) throw new InvalidOperationException("AI effect observation capacity exceeded.");
                    _effectsSensor.AppendObservation(CombatObservations.EffectRow(effect, i));
                }
            }
            int knownSpells = 0;
            for (int i = 0; i < _owner.Spells.Count; i++)
                if (CombatActionRules.KnowsSpell(_unit, _owner.Spells[i]))
                {
                    if (++knownSpells > CombatLearningContract.MaxSpells) throw new InvalidOperationException("Known spell observation capacity exceeded.");
                    _spellsSensor.AppendObservation(CombatObservations.SpellRow(_owner.Spells[i], i, _unit, _command?.Spell == _owner.Spells[i]));
                }
        }

        private static int CriticalIndex(string choice)
        {
            return Array.IndexOf(new[] { "", "target", "damage", "range", "area_size", "duration" }, choice);
        }
    }
}
