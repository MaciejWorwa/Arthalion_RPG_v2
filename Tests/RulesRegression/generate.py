from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
out = Path(__file__).parent

def method(file, signature):
    source = (root / file).read_text(encoding='utf-8-sig')
    start = source.index(signature)
    brace = source.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

def methods(file, signatures):
    return '\n'.join(method(file, s) for s in signatures)

dice = (root / 'Assets/Scripts/Managers/DiceRollManager.cs').read_text(encoding='utf-8-sig')
(out / 'DiceRollManager.cs').write_text(dice, encoding='utf-8')
header = 'using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Reflection; using TMPro; using UnityEngine;\n'
stats = methods('Assets/Scripts/Stats.cs', ['private static bool HasDamageType(', 'public void UpdateSpellEffects('])
state = methods('Assets/Scripts/Managers/StatesManager.cs', ['public static int GetPoisonLevel(', 'private IEnumerator Ablaze(', 'private void ChooseExtinguish(', 'public void SetUnitState(', 'public void LoadUnitStates('])
combat = methods('Assets/Scripts/Managers/CombatManager.cs', ['public void ApplyDamageToTarget(', 'private static void DamageEquipmentOnFumble(', 'private Weapon GetArmorForDodgeFumble(', 'public void Attack('])
combat += '\n' + method('Assets/Scripts/Managers/CombatManager.cs', 'private IEnumerator AutoDefense(')
magic = methods('Assets/Scripts/Managers/MagicManager.cs', ['private IEnumerator HandleSpellEffect(', 'public IEnumerator CastSpell('])
rounds = methods('Assets/Scripts/Managers/RoundsManager.cs', ['public void BeginTurn(', 'public bool CanUnitAct(', 'public bool DoAction(', 'public void FinishTurn(Unit unit,', 'public void FinishTurn()', 'private IEnumerator ResolveUnitTurnStart('])
state += '\n' + method('Assets/Scripts/Managers/StatesManager.cs', 'public IEnumerator ResolveTurnStart(')
movement = method('Assets/Scripts/Managers/MovementManager.cs', 'public void MoveSelectedUnit(')
grid = method('Assets/Scripts/Managers/GridManager.cs', 'public void ResetTileOccupancy(')
auto = method('Assets/Scripts/Managers/AutoCombatManager.cs', 'public void Act(')
data = methods('Assets/Scripts/Managers/DataManager.cs', ['public class SpellEffectData', 'public class Pair\n'])
parts = [('Stats', stats), ('StatesManager', state), ('CombatManager', combat), ('MagicManager', magic), ('RoundsManager', rounds), ('MovementManager', movement), ('GridManager', grid), ('AutoCombatManager', auto)]
(out / 'ProductionMethods.cs').write_text(header + '\n'.join('partial class '+name+' {\n'+body+'\n}\n' for name, body in parts) + data, encoding='utf-8')
(out / 'ProductionSpellEffect.cs').write_text((root/'Assets/Scripts/SpellEffect.cs').read_text(encoding='utf-8-sig'), encoding='utf-8')
(out / 'RulesRegression.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><NoWarn>CS0649;CS0169;CS0414</NoWarn></PropertyGroup></Project>')

# The runtime popup copies these exact source buttons; verify the serialized source hierarchy.
scene = (root / 'Assets/Scenes/BattleScene.unity').read_text(encoding='utf-8-sig')
blocks = {m.group(1): m.group(2) for m in re.finditer(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)', scene, re.M | re.S)}
for button_id, expected in [('630321749', 'yes_button'), ('1688185981', 'no_button')]:
    game_object = re.search(r'm_GameObject: \{fileID: (\d+)\}', blocks[button_id]).group(1)
    assert 'm_Name: '+expected in blocks[game_object]
    assert 'm_Calls: []' in blocks[button_id]
assert 'm_Name: Entangling_Panel' in blocks['1868122763']
print('Popup source bindings and button names: PASS')

# Save schema and loading must retain every new state, including action use.
unit = (root / 'Assets/Scripts/Unit.cs').read_text(encoding='utf-8-sig')
data = (root / 'Assets/Scripts/Managers/DataManager.cs').read_text(encoding='utf-8-sig')
for field in ['Fatiqued', 'TurnStartedInRound', 'HasUsedOpportunityAttack', 'CanDoAction']:
    assert re.search(r'public (?:int|bool) '+field+r'\b', unit)
    assert re.search(r'public (?:int|bool) '+field+r'\b', data)
assert 'unit.HasUsedOpportunityAttack = false;' in (root/'Assets/Scripts/Managers/RoundsManager.cs').read_text(encoding='utf-8-sig')
print('Save schema and round reset: PASS')
