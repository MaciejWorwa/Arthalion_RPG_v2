using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

class Program {
    static int checks;
    static Action onWait;
    static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    static void Equal(int expected,int actual,string name)=>Check(expected==actual,$"{name}: expected {expected}, actual {actual}");
    static object Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance).Invoke(obj,args);
    static void Field(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).SetValue(obj,value);
    static Unit MakeUnit(string tag="EnemyUnit"){
        var g=new GameObject("unit"){tag=tag};var u=g.Add(new Unit());u.Stats=g.Add(new Stats());g.Add(new Inventory());g.Add(new Weapon());return u;
    }
    static void Drain(IEnumerator r){while(r.MoveNext()){
        if(r.Current is IEnumerator nested)Drain(nested);
        else if(r.Current is Coroutine c)Drain(c.Routine);
        else if(r.Current is WaitUntil w){if(!w.Predicate())onWait?.Invoke();Check(w.Predicate(),"Decision resolves");}
    }}
    static DiceRollManager Dice(){
        var d=new GameObject("dice").Add(new DiceRollManager());
        Field(d,"_modifierSlider",new GameObject().Add(new UnityEngine.UI.Slider()));
        Field(d,"_modifierInputField",new GameObject().Add(new TMP_InputField()));
        Field(d,"instance",d);return d;
    }
    static void Main(){
        var dice=Dice();
        for(int fatigue=0;fatigue<=5;fatigue++){
            var u=MakeUnit();u.Fatiqued=fatigue;u.Stats.S=4;u.Stats.MeleeCombat=4;
            Equal(18-fatigue,dice.TestSkill(u.Stats,"combat","S","MeleeCombat",roll1:5,roll2:5)[2],"Fatigue each level");
            Equal(10,dice.TestSkill(u.Stats,"critical",null,roll1:5,roll2:5,applyFatigue:false)[2],"Table unaffected");
        }
        var reflex=MakeUnit();reflex.Stats.P=7;reflex.Stats.Zw=3;reflex.Stats.Reflex=1;reflex.Fatiqued=3;
        Equal(17,dice.TestSkill(reflex.Stats,"initiative",null,"Reflex",roll1:6,roll2:6)[2],"Initiative fatigue");
        var specialist=MakeUnit();specialist.Stats.Specialist=true;specialist.Stats.S=3;specialist.Fatiqued=4;
        UnityEngine.Random.Value=8;
        Equal(14,dice.TestSkill(specialist.Stats,"specialist","S",roll1:3,roll2:7)[2],"Specialist keeps fatigue");
        for(int damage=1;damage<=11;damage++){
            var target=MakeUnit();target.Stats.Resistance=new[]{"Physical"};var attacker=MakeUnit();
            CombatManager.Instance.ApplyDamageToTarget(damage,0,attacker.Stats,target.Stats,target,new Weapon());
            Equal((damage+1)/2,20-target.Stats.TempHealth,"Odd/even resistant damage");
        }
        var armored=MakeUnit();armored.Stats.Resistance=new[]{"Physical"};
        CombatManager.Instance.ApplyDamageToTarget(8,3,MakeUnit().Stats,armored.Stats,armored,new Weapon());
        Equal(17,armored.Stats.TempHealth,"Resistance follows armor");
        var poison=MakeUnit();poison.Stats.Resistance=new[]{"Zatrucie"};
        for(int level=1;level<=9;level++)Equal((level+1)/2,StatesManager.GetPoisonLevel(poison.Stats,level),"Poison round up");
        poison.Stats.Unaffected=new[]{"Poison"};Equal(0,StatesManager.GetPoisonLevel(poison.Stats,9),"Poison immunity priority");
        poison.Stats.Unaffected=Array.Empty<string>();poison.Stats.Undead=true;Equal(0,StatesManager.GetPoisonLevel(poison.Stats,9),"Undead poison immunity");
        var typed=MakeUnit();typed.Stats.Resistance=new[]{" Ogień ","pHySiCaL"};
        Check(typed.Stats.HasResistance("Fire"),"Polish fire type");Check(typed.Stats.HasResistance("Physical"),"Case insensitive type");
        var mental=MakeUnit();mental.Stats.Unmeaning=true;
        foreach(var attr in new[]{"Ch","Int","SW"}){
            Check(!DiceRollManager.CanPerformTest(mental.Stats,attr),"Mental attribute forbidden");
            var roll=dice.TestSkill(mental.Stats,"forbidden",attr,difficultyLevel:999,roll1:10,roll2:10);
            Equal(3,roll.Length,"Consistent result layout");Equal(0,roll[2],"No fake success");Check(!dice.IsDoubleDigit(roll[0],roll[1]),"No fake fumble");
        }
        foreach(var skill in new[]{"Cool","Spellcasting","Pray","Channeling","MagicLanguage"})
            Check(!DiceRollManager.CanPerformTest(mental.Stats,null,skill),"Mental skill forbidden without attribute");
        Check(DiceRollManager.CanPerformTest(mental.Stats,"Zw","Athletics"),"Physical test permitted");
        int[] callback=null;Drain(dice.WaitForRollValue(mental.Stats,"forbidden","SW",callback:r=>callback=r));
        Check(callback!=null && callback[2]==0 && !dice.IsWaitingForRoll,"Manual forbidden test does not open panel");
        var magic=new GameObject().Add(new MagicManager());
        var spell=new Spell{Name="Will spell",SaveAttribute="SW",SaveDifficulty=999,Duration=5,Type=new[]{"offensive"}};
        spell.Attributes.Add(new KeyValuePair<string,int>("Fatiqued",5));
        Drain((IEnumerator)Call(magic,"HandleSpellEffect",MakeUnit().Stats,mental.Stats,spell,1000,new[]{10,10,1000}));
        Equal(0,mental.Stats.ActiveSpellEffects.Count,"Mindless immunity before timed effect");
        Equal(0,mental.Fatiqued,"Mindless immunity before state");Equal(20,mental.Stats.TempHealth,"Mindless immunity before damage");
        var timed=MakeUnit();timed.Stats.Resistance=new[]{"Poison"};
        var poisonSpell=new Spell{Name="Timed poison",Duration=1};poisonSpell.Attributes.Add(new KeyValuePair<string,int>("Poison",3));
        Drain((IEnumerator)Call(magic,"HandleSpellEffect",MakeUnit().Stats,timed.Stats,poisonSpell,20,new[]{5,5,20}));
        Equal(2,timed.Poison,"Spell reduces poison level");Equal(2,timed.Stats.ActiveSpellEffects[0].StatModifiers["Poison"],"Timed effect stores effective poison delta");
        timed.Stats.UpdateSpellEffects();Equal(0,timed.Poison,"Timed poison expires without negative level");
        var fatigueSpell=new Spell{Name="Timed fatigue",Duration=1};fatigueSpell.Attributes.Add(new KeyValuePair<string,int>("Fatiqued",7));
        Drain((IEnumerator)Call(magic,"HandleSpellEffect",MakeUnit().Stats,timed.Stats,fatigueSpell,20,new[]{5,5,20}));
        Equal(5,timed.Fatiqued,"Spell clamps fatigue");Equal(5,timed.Stats.ActiveSpellEffects[0].StatModifiers["Fatiqued"],"Timed effect stores clamped fatigue delta");
        timed.Fatiqued=0;timed.Stats.UpdateSpellEffects();Equal(0,timed.Fatiqued,"Fatigue expiry stays in range after manual removal");
        foreach(var quality in new[]{"Słaba","Zwykła","Dobra","Najlepsza"}){
            var weapon=new Weapon{Quality=quality};Call(CombatManager.Instance,"DamageEquipmentOnFumble",MakeUnit().Stats,weapon,false);
            Check(weapon.Broken==(quality=="Słaba"||quality=="Zwykła"),"Weapon quality consequence");
        }
        var natural=new Weapon{Type=new[]{"natural-weapon"}};
        Call(CombatManager.Instance,"DamageEquipmentOnFumble",MakeUnit().Stats,natural,false);Check(!natural.Broken,"Natural weapon preserved");
        var empty=new Inventory();Check(Call(CombatManager.Instance,"GetArmorForDodgeFumble",empty,"head")==null,"No armor -> critical path");
        empty.EquippedArmors.Add(new Weapon{Name="Naturalny pancerz"});
        Check(Call(CombatManager.Instance,"GetArmorForDodgeFumble",empty,"head")==null,"Natural armor -> critical path");
        var bodyArmor=new Weapon{Name="Body armor",Type=new[]{"chain","torso"}};
        empty.EquippedArmors.Add(bodyArmor);
        empty.ArmorByLocation["torso"]=new List<Weapon>{bodyArmor};
        Check(Call(CombatManager.Instance,"GetArmorForDodgeFumble",empty,"head")==null,"Uncovered location -> critical even with other armor");
        Check(!bodyArmor.Broken,"Armor on another location remains intact");
        Check(ReferenceEquals(bodyArmor,Call(CombatManager.Instance,"GetArmorForDodgeFumble",empty,"torso")),"Only armor on hit location is selected");
        var attack=new GameObject().Add(new CombatManager());var op=MakeUnit();var moving=MakeUnit();
        attack.Attack(op,moving,true);attack.Attack(op,moving,true);Equal(1,attack.CoroutineCalls,"Pending opportunity reservation");
        var used=MakeUnit();used.HasUsedOpportunityAttack=true;attack.Attack(used,moving,true);Equal(1,attack.CoroutineCalls,"Used opportunity denied");
        attack.Attack(used,moving,false);Equal(2,attack.CoroutineCalls,"Normal attack after opportunity allowed");
        var ui=MakeUnit("PlayerUnit");Unit.SelectedUnit=ui.gameObject;
        var input=new GameObject("Fatiqued_input"){tag="State"};var inputField=input.Add(new TMP_InputField());
        inputField.text="8";StatesManager.Instance.SetUnitState(input);Equal(5,ui.Fatiqued,"Fatigue UI max");
        input.name="Fatigued_input";inputField.text="-2";StatesManager.Instance.SetUnitState(input);Equal(0,ui.Fatiqued,"Fatigue UI alias and minimum");
        input.name="Poison_input";inputField.text="3";ui.Stats.Resistance=new[]{"Poison"};StatesManager.Instance.SetUnitState(input);
        Equal(2,ui.Poison,"Manual poison level reduced");StatesManager.Instance.LoadUnitStates();Equal(2,ui.Poison,"Loading UI does not reapply resistance");
        foreach(bool tryExtinguish in new[]{false,true}){
            var burning=MakeUnit("PlayerUnit");burning.Ablaze=true;burning.Stats.Zw=5;UnityEngine.Random.Value=5;
            var states=new GameObject().Add(new StatesManager());onWait=()=>Call(states,"ChooseExtinguish",tryExtinguish);
            RoundsManager.Instance.Actions=0;Drain((IEnumerator)Call(states,"Ablaze",burning));
            Equal(15,burning.Stats.TempHealth,"Burn damage before decision even on success");
            Check(burning.Ablaze==!tryExtinguish,"Burn choice controls extinguishing");
            Equal(tryExtinguish?1:0,RoundsManager.Instance.Actions,"Only attempt costs action");
        }
        var failing=MakeUnit();failing.Ablaze=true;UnityEngine.Random.Value=1;RoundsManager.Instance.Actions=0;
        Drain((IEnumerator)Call(new StatesManager(),"Ablaze",failing));Equal(19,failing.Stats.TempHealth,"Failed extinguish has one damage application");
        Check(failing.Ablaze && !failing.CanDoAction,"Failed extinguish consumes action and keeps fire");
        var resistantBurn=MakeUnit("PlayerUnit");resistantBurn.Ablaze=true;resistantBurn.Stats.Resistance=new[]{"Fire"};UnityEngine.Random.Value=5;
        var bs=new StatesManager();onWait=()=>Call(bs,"ChooseExtinguish",false);Drain((IEnumerator)Call(bs,"Ablaze",resistantBurn));
        Equal(17,resistantBurn.Stats.TempHealth,"Burn resistance rounds up");Check(resistantBurn.Ablaze,"Fire resistance does not extinguish");
        var immuneBurn=MakeUnit();immuneBurn.Ablaze=true;immuneBurn.Stats.Unaffected=new[]{"Fire"};Drain((IEnumerator)Call(new StatesManager(),"Ablaze",immuneBurn));
        Equal(20,immuneBurn.Stats.TempHealth,"Fire immunity ignores damage");Check(!immuneBurn.Ablaze,"Fire immunity removes state");
        var rounds=new RoundsManager();var start=MakeUnit();start.Ablaze=true;RoundsManager.RoundNumber=7;
        rounds.BeginTurn(start);rounds.BeginTurn(start);Equal(1,rounds.CoroutineCalls,"Turn starts once per round");Equal(7,start.TurnStartedInRound,"Turn marker persists");
        var reloaded=new RoundsManager();reloaded.BeginTurn(start);Equal(0,reloaded.CoroutineCalls,"Persisted turn does not repeat burn");
        BugFixes(dice);
        Console.WriteLine($"PASS: {checks} production rule and bug regression checks (Unity UI/runtime dependencies stubbed).");
    }

    static void Apply(MagicManager magic, Unit caster, Unit target, Spell spell) =>
        Drain((IEnumerator)Call(magic,"HandleSpellEffect",caster.Stats,target.Stats,spell,20,new[]{5,5,20}));
    static Spell Effect(string name,string key,int value,int duration=1){
        var s=new Spell{Name=name,Duration=duration};s.Attributes.Add(new KeyValuePair<string,int>(key,value));return s;
    }
    static void BugFixes(DiceRollManager dice){
        GameManager.IsAutoDiceRollingMode=true;
        RoundsManager.Instance=new RoundsManager();RoundsManager.RoundNumber=10;
        var hardy=MakeUnit();hardy.Stats.Hardy=true;hardy.Stats.K=3;
        var result=dice.TestSkill(hardy.Stats,"hardy","K",roll1:2,roll2:7);
        Equal(14,result[2],"Hardy increases final result");Equal(2,result[0],"Hardy keeps physical die for doubles");
        result=dice.TestSkill(hardy.Stats,"hardy","K",roll1:2,roll2:4);
        Check(!dice.IsDoubleDigit(result[0],result[1]),"Hardy does not invent a double");
        result=dice.TestSkill(hardy.Stats,"hardy","K",roll1:4,roll2:4);
        Check(dice.IsDoubleDigit(result[0],result[1]),"Hardy preserves a real double");
        var specialist=MakeUnit();specialist.Stats.Specialist=true;specialist.Stats.Zr=8;specialist.Stats.MeleeCombat=5;
        UnityEngine.Random.Value=8;
        Equal(25,dice.TestSkill(specialist.Stats,"specialist","Zr","MeleeCombat",roll1:3,roll2:7)[2],"Specialist keeps base cap");
        specialist.Fatiqued=3;
        Equal(22,dice.TestSkill(specialist.Stats,"specialist","Zr","MeleeCombat",roll1:3,roll2:7)[2],"Specialist cap and fatigue together");

        UnityEngine.Random.Value=5;
        var magic=new MagicManager();var caster=MakeUnit();var resisted=MakeUnit();resisted.Stats.S=4;resisted.Stats.SW=5;
        var curse=Effect("Curse","S",-2,3);curse.SaveAttribute="SW";curse.SaveDifficulty=12;
        Apply(magic,caster,resisted,curse);
        Equal(0,resisted.Stats.ActiveSpellEffects.Count,"Successful save creates no timed effect");
        resisted.Stats.UpdateSpellEffects();Equal(4,resisted.Stats.S,"Resisted curse never increases strength at expiry");
        UnityEngine.Random.Value=1;Apply(magic,caster,resisted,curse);
        Equal(2,resisted.Stats.S,"Failed save applies modifier");
        resisted.Stats.ActiveSpellEffects[0].RemainingRounds=1;
        UnityEngine.Random.Value=5;Apply(magic,caster,resisted,curse);
        Equal(1,resisted.Stats.ActiveSpellEffects[0].RemainingRounds,"Resisting a recast does not refresh effect");
        UnityEngine.Random.Value=1;Apply(magic,caster,resisted,curse);
        Equal(3,resisted.Stats.ActiveSpellEffects[0].RemainingRounds,"Failed recast refreshes duration");
        Equal(2,resisted.Stats.S,"Refresh does not stack modifier");
        for(int i=0;i<3;i++)resisted.Stats.UpdateSpellEffects();Equal(4,resisted.Stats.S,"Applied curse restores base strength");

        UnityEngine.Random.Value=5;
        foreach(int health in new[]{0,16,18,20}){
            var target=MakeUnit();target.Stats.TempHealth=health;
            var heal=Effect("Kojący Chłód","TempHealth",4,0);heal.Strength=new[]{4};
            Apply(magic,caster,target,heal);Equal(Math.Min(health+4,20),target.Stats.TempHealth,"Healing clamps to maximum");
        }
        magic.CriticalCastingString="duration";
        var circle=Effect("Zimowy Krąg","Entangled",1,6);var first=MakeUnit();var second=MakeUnit();
        Apply(magic,caster,first,circle);Apply(magic,caster,second,circle);
        Equal(12,first.Stats.ActiveSpellEffects[0].RemainingRounds,"First critical duration");
        Equal(12,second.Stats.ActiveSpellEffects[0].RemainingRounds,"Second critical duration matches first");
        Equal(6,circle.Duration,"Duration never mutates spell definition");
        magic.CriticalCastingString="";
        var permanent=MakeUnit();permanent.Stats.Hardy=true;
        Apply(magic,caster,permanent,Effect("Hardy","Hardy",1));
        var serialized=new SpellEffectData(permanent.Stats.ActiveSpellEffects[0]);
        var options=new System.Text.Json.JsonSerializerOptions{IncludeFields=true};
        var json=System.Text.Json.JsonSerializer.Serialize(serialized,options);
        var loaded=System.Text.Json.JsonSerializer.Deserialize<SpellEffectData>(json,options).ToSpellEffect();
        Check(loaded.PreviousBoolValues["Hardy"],"Boolean baseline survives save serialization");
        permanent.Stats.ActiveSpellEffects[0]=loaded;permanent.Stats.UpdateSpellEffects();
        Check(permanent.Stats.Hardy,"Expiry keeps preexisting talent after reload");
        Apply(magic,caster,permanent,Effect("Suppress Hardy","Hardy",0));
        Check(!permanent.Stats.Hardy,"Boolean false effect applies");permanent.Stats.UpdateSpellEffects();
        Check(permanent.Stats.Hardy,"Boolean false effect restores true baseline");
        foreach(bool laterExpiresFirst in new[]{false,true}){
            var stacked=MakeUnit();
            Apply(magic,caster,stacked,Effect("A","Hardy",1,laterExpiresFirst?2:1));
            Apply(magic,caster,stacked,Effect("B","Hardy",1,laterExpiresFirst?1:2));
            stacked.Stats.UpdateSpellEffects();Check(stacked.Stats.Hardy,"Remaining boolean effect persists");
            stacked.Stats.UpdateSpellEffects();Check(!stacked.Stats.Hardy,"Last expiry restores original false in both orders");
        }
        var legacy=new SpellEffectData{SpellName="Old",RemainingRounds=1,StatModifiers=new List<Pair>{new Pair{Key="Hardy",Value=1}}}.ToSpellEffect();
        var legacyTarget=MakeUnit();legacyTarget.Stats.Hardy=true;legacyTarget.Stats.ActiveSpellEffects.Add(legacy);
        legacyTarget.Stats.UpdateSpellEffects();Check(!legacyTarget.Stats.Hardy,"Legacy effects without metadata still load and expire");

        ManualRolls(dice);
        GameManager.IsAutoDiceRollingMode=true;
        var combat=new CombatManager();var defender=MakeUnit();defender.Stats.Zr=5;defender.Stats.Zw=8;
        Drain((IEnumerator)Call(combat,"AutoDefense",defender,defender.Stats,new Weapon(),7,8,2,0,true));
        Check(combat.Choice=="dodge","Auto-defense counts modifiers once");
        defender.Stats.Zr=10;defender.Stats.MeleeCombat=5;defender.Stats.Zw=9;defender.Stats.Dodge=2;
        Drain((IEnumerator)Call(combat,"AutoDefense",defender,defender.Stats,new Weapon(),10,10,0,1,true));
        Check(combat.Choice=="dodge","Auto-defense respects base modifier cap");

        RoundsManager.Instance=new RoundsManager();StatesManager.Instance=new StatesManager();
        var finished=MakeUnit();Unit.SelectedUnit=finished.gameObject;
        RoundsManager.Instance.FinishTurn(finished,false);
        Check(finished.IsTurnFinished && !finished.CanDoAction && !finished.CanMove,"Finish clears action and movement");
        Equal(1,StatesManager.Instance.EndUpdates,"End states run once");
        RoundsManager.Instance.FinishTurn(finished,false);Equal(1,StatesManager.Instance.EndUpdates,"Repeated finish does not tick states again");
        Check(!RoundsManager.Instance.DoAction(finished),"Finished unit cannot spend an action");
        combat=new CombatManager();combat.Attack(finished,defender);Equal(0,combat.CoroutineCalls,"Finished unit cannot attack");
        combat.Attack(finished,defender,true);Equal(1,combat.CoroutineCalls,"Finished unit retains unused opportunity attack");
        finished.CanDoAction=true;finished.CanMove=true;
        Check(!RoundsManager.Instance.CanUnitAct(finished),"Toggles do not bypass finished turn");

        RoundsManager.Instance=new RoundsManager();StatesManager.Instance=new StatesManager();
        var outOfOrder=MakeUnit("PlayerUnit");outOfOrder.Ablaze=true;outOfOrder.Stats.Zw=5;
        Unit.SelectedUnit=outOfOrder.gameObject;InitiativeQueueManager.Instance.ActiveUnit=MakeUnit();
        Check(!RoundsManager.Instance.CanUnitAct(outOfOrder),"Manual turn blocks action while burn resolves");
        Equal(10,outOfOrder.TurnStartedInRound,"Manual turn gets current-round marker");
        combat=new CombatManager();combat.Attack(outOfOrder,defender);Equal(0,combat.CoroutineCalls,"Burn decision blocks attack");
        RoundsManager.Instance.FinishTurn(outOfOrder,false);Check(!outOfOrder.IsTurnFinished,"Burn decision blocks finishing");
        onWait=()=>Call(StatesManager.Instance,"ChooseExtinguish",false);
        Drain(RoundsManager.Instance.Started[0].Routine);
        Equal(15,outOfOrder.Stats.TempHealth,"Out-of-order unit takes start-of-turn damage");
        Check(RoundsManager.Instance.CanUnitAct(outOfOrder),"Action allowed after burn decision");
        RoundsManager.Instance.BeginTurn(outOfOrder);Equal(15,outOfOrder.Stats.TempHealth,"Burn does not tick twice");
        RoundsManager.Instance.FinishTurn(outOfOrder,false);Check(outOfOrder.IsTurnFinished,"Manual unit can finish after burn");

        MovementAndAI();CriticalCasting();
    }

    static void ManualRolls(DiceRollManager dice){
        GameManager.IsAutoDiceRollingMode=false;
        var panel=new GameObject();panel.Add(new TMP_Text());panel.SetActive(false);
        var g1=new GameObject();var f1=g1.Add(new TMP_InputField());g1.Add(new TMP_Text());g1.Add(new InputFieldFilter());
        var f2=new GameObject().Add(new TMP_InputField());
        Field(dice,"_applyRollResultPanel",panel);Field(dice,"_roll1InputField",f1);Field(dice,"_roll2InputField",f2);
        var a=MakeUnit("PlayerUnit");a.Stats.K=1;var b=MakeUnit("PlayerUnit");b.Stats.K=9;
        void Submit(int x,int y){f1.text=x.ToString();f2.text=y.ToString();dice.OnSubmitRoll();}
        int[] resultA=null,resultB=null;
        var ra=dice.WaitForRollValue(a.Stats,"A","K",callback:r=>resultA=r);
        var rb=dice.WaitForRollValue(b.Stats,"B","K",callback:r=>resultB=r);
        ra.MoveNext();rb.MoveNext();Submit(2,3);
        Check(rb.MoveNext() && dice.IsWaitingForRoll,"Next roll cannot acquire hidden but occupied panel");
        Check(!ra.MoveNext(),"First roll completes");Equal(6,resultA[2],"First roll reaches own callback");
        Check(dice.IsWaitingForRoll,"Waiting status includes queued roll before it opens its panel");
        rb.MoveNext();Submit(7,8);Check(!rb.MoveNext(),"Second roll completes");Equal(24,resultB[2],"Second roll reaches own callback");
        Check(!dice.IsWaitingForRoll,"Lock released after both rolls");

        resultA=null;resultB=null;
        ra=dice.WaitForRollValue(a.Stats,"A","K",callback:r=>resultA=r);
        rb=dice.WaitForRollValue(b.Stats,"B","K",callback:r=>resultB=r);
        ra.MoveNext();rb.MoveNext();dice.ForceCancelPendingRollInput(cancelQueuedRolls:false);
        Check(rb.MoveNext(),"Escape permits next queued roll");
        Check(!ra.MoveNext() && dice.IsWaitingForRoll && panel.activeSelf,"Cancelled owner cannot clear new roll");
        Submit(7,8);rb.MoveNext();Check(resultA==null && resultB[2]==24,"Escape cancels only active roll");

        resultA=null;resultB=null;
        ra=dice.WaitForRollValue(a.Stats,"A","K",callback:r=>resultA=r);
        rb=dice.WaitForRollValue(b.Stats,"B","K",callback:r=>resultB=r);
        ra.MoveNext();rb.MoveNext();dice.ForceCancelPendingRollInput();
        Check(!ra.MoveNext() && !rb.MoveNext() && !dice.IsWaitingForRoll,"Reset cancels active and queued rolls");
        Check(resultA==null && resultB==null,"Reset invokes no stale callback");

        IEnumerator nested=null;
        ra=dice.WaitForRollValue(a.Stats,"A","K",callback:r=>{nested=dice.WaitForRollValue(b.Stats,"B","K",callback:x=>resultB=x);nested.MoveNext();});
        ra.MoveNext();Submit(2,3);ra.MoveNext();
        Check(dice.IsWaitingForRoll && panel.activeSelf,"Callback can start another roll without context being cleared");
        Submit(7,8);nested.MoveNext();Equal(24,resultB[2],"Reentrant callback retains second result");

        ra=dice.WaitForRollValue(a.Stats,"A","K");ra.MoveNext();((IDisposable)ra).Dispose();
        Check(!dice.IsWaitingForRoll,"Disposing an active coroutine releases lock");
        int[] damage=null;ra=dice.WaitForRollValue(a.Stats,"obrażenia","K",callback:r=>damage=r);
        ra.MoveNext();Submit(13,0);ra.MoveNext();
        Check(damage.Length==1 && damage[0]==13,"Manual damage keeps one-die-sum protocol");
    }

    static void MovementAndAI(){
        RoundsManager.Instance=new RoundsManager();RoundsManager.RoundNumber=10;
        var mover=MakeUnit();Unit.SelectedUnit=mover.gameObject;
        var source=new GameObject().Add(new Tile{IsOccupied=true});
        var destination=new GameObject().Add(new Tile());destination.transform.position=new Vector3(1,0);
        GridManager.Instance.Tiles=new Tile[,]{{source,destination}};
        MovementManager.Instance=new MovementManager();
        MovementManager.Instance.MoveSelectedUnit(destination.gameObject,mover.gameObject);
        Check(source.IsOccupied && !destination.IsOccupied && mover.CanMove,"No path preserves occupied source and movement");
        MovementManager.Instance.Path=new List<Vector2>{new Vector2(1,0),new Vector2(2,0)};mover.Stats.TempSz=1;
        MovementManager.Instance.MoveSelectedUnit(destination.gameObject,mover.gameObject);
        Check(source.IsOccupied && mover.CanMove,"Out-of-range path preserves occupancy and movement");
        GameManager.IsAutoCombatMode=true;mover.Stats.TempSz=0;
        MovementManager.Instance.MoveSelectedUnit(destination.gameObject,mover.gameObject);
        Check(source.IsOccupied && MovementManager.Instance.CoroutineCalls==0,"Zero-speed AI movement is rejected safely");
        GameManager.IsAutoCombatMode=false;mover.Stats.TempSz=5;
        MovementManager.Instance.MoveSelectedUnit(destination.gameObject,mover.gameObject);
        Check(!source.IsOccupied && destination.IsOccupied && !mover.CanMove,"Accepted movement frees source and reserves destination");
        mover.IsTurnFinished=true;mover.CanMove=true;source.IsOccupied=true;
        MovementManager.Instance.MoveSelectedUnit(destination.gameObject,mover.gameObject);
        Check(source.IsOccupied,"Finished unit cannot move even if toggle reenabled");
        var thrower=MakeUnit();thrower.gameObject.GetComponent<Weapon>().Type=new[]{"ranged","throwing"};
        for(int strength=1;strength<=9;strength++){
            thrower.Stats.S=strength;
            var ai=new AutoCombatManager{Opponent=MakeUnit().gameObject,Distance=strength*3-0.1f};
            ai.Act(thrower);Drain(ai.Started[0].Routine);Check(ai.Choice=="attack","AI throwing range retains fraction for strengths 1-9");
            ai=new AutoCombatManager{Opponent=MakeUnit().gameObject,Distance=strength*3+0.1f};
            ai.Act(thrower);Drain(ai.Started[0].Routine);Check(ai.Choice=="move","AI moves when throwing target really is out of range");
        }
    }

    static void CriticalCasting(){
        GameManager.IsAutoDiceRollingMode=true;UnityEngine.Random.Value=5;
        foreach(string kind in new[]{"range","area_size"}){
            var caster=MakeUnit();Unit.SelectedUnit=caster.gameObject;
            var spell=caster.gameObject.Add(Effect("Critical "+kind,"S",1,0));spell.Range=kind=="range"?5:20;spell.AreaSize=1;spell.Targets=2;
            var first=MakeUnit();first.transform.position=new Vector3(7,0);
            var second=MakeUnit();second.transform.position=new Vector3(13,0);
            Physics2D.Targets.Clear();Physics2D.Radii.Clear();
            Physics2D.Targets[(Vector2)first.transform.position]=new[]{first.gameObject.Add(new Collider2D())};
            Physics2D.Targets[(Vector2)second.transform.position]=new[]{second.gameObject.Add(new Collider2D())};
            var magic=new MagicManager{RequestedCritical=kind};magic.Targets.Add(first.gameObject);magic.Targets.Add(second.gameObject);
            Drain(magic.CastSpell());foreach(var c in magic.Started.ToArray())Drain(c.Routine);
            Equal(1,first.Stats.S,"Critical cast reaches first target");
            Equal(kind=="range"?0:1,second.Stats.S,"Second target respects same critical parameters");
            Check(Physics2D.Radii.All(r=>r==(kind=="range"?1:2)),"Critical radius is constant per target");
            Equal(1,spell.AreaSize,"Area never mutates spell definition");
            Check(spell.Range==(kind=="range"?5:20),"Range never mutates spell definition");
        }
    }
}
