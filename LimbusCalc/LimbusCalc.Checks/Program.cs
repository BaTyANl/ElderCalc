using LimbusCalc.Calculation;

bool allOk = true;

// --- Проверка 1: лист "Формула" из damage.xlsx ---------------------------------
// Crit=0.2 (P2) и Weight=1 (G9) теперь живут на самой монете. В скилле одна монета:
// остальные колонки на листе были пустыми слотами (Coin pos = 0), а не решкой.
DamageInput sheet = new();
Skill sheetSkill = new() { Name = "Формула", BaseRoll = 13 };
sheetSkill.Coins.Add(new Coin
{
    Active = true,
    Power = 13,
    ModDyn = 2.5,
    OffenseDefenseDiff = 5,
    HasCrit = true,
    Crit = 0.2,
    Weight = 1,
});
sheet.Skills.Add(sheetSkill);

DamageResult sheetResult = DamageCalculator.Calculate(sheet);
// Лист даёт 88.8333; урон монеты теперь округляется вниз, поэтому ожидаем 88.
allOk &= Check("Итог по листу", sheetResult.Total, 88.0);
allOk &= Check("Coin roll (D7)", sheetResult.Coins[0].Roll, 26.0);
allOk &= Check("ModStat(5; 0,2) (D6)", DamageCalculator.ModStat(5, 0.2), 1.3666666666666667);
allOk &= Check("ModStat(16; 0,2) (E6)", DamageCalculator.ModStat(16, 0.2), 1.5902439024390245);

// --- Проверка 2: решка сохраняет бросок предыдущей монеты ----------------------
// База 13, три монеты по 5: орёл -> 18, решка -> остаётся 18, орёл -> 23.
DamageInput tails = new();
Skill tailsSkill = new() { Name = "Орёл/решка/орёл", BaseRoll = 13 };
tailsSkill.Coins.Add(new Coin { Active = true, Power = 5 });
tailsSkill.Coins.Add(new Coin { Active = false, Power = 5 });
tailsSkill.Coins.Add(new Coin { Active = true, Power = 5 });
tails.Skills.Add(tailsSkill);

DamageResult tailsResult = DamageCalculator.Calculate(tails);
allOk &= Check("Бросок монеты 1 (орёл)", tailsResult.Coins[0].Roll, 18.0);
allOk &= Check("Бросок монеты 2 (решка)", tailsResult.Coins[1].Roll, 18.0);
allOk &= Check("Бросок монеты 3 (орёл)", tailsResult.Coins[2].Roll, 23.0);
allOk &= Check("Итог 18+18+23", tailsResult.Total, 59.0);

// --- Проверка 3: крит считается по каждой монете отдельно ----------------------
// Одинаковые монеты, но у второй крит выключен, у третьей — свой множитель.
DamageInput crit = new();
Skill critSkill = new() { BaseRoll = 10 };
critSkill.Coins.Add(new Coin { Active = false, HasCrit = true, Crit = 0.5 });
critSkill.Coins.Add(new Coin { Active = false, HasCrit = false, Crit = 0.5 });
critSkill.Coins.Add(new Coin { Active = false, HasCrit = true, Crit = 0.25 });
crit.Skills.Add(critSkill);

DamageResult critResult = DamageCalculator.Calculate(crit);
allOk &= Check("Крит 0,5 включён", critResult.Coins[0].Damage, 15.0);
allOk &= Check("Крит выключен", critResult.Coins[1].Damage, 10.0);
allOk &= Check("Крит 0,25 включён", critResult.Coins[2].Damage, 12.0);   // 12,5 вниз до 12

// --- Проверка 4: вес умножает урон своей монеты, а не итог ---------------------
DamageInput weights = new();
Skill weightSkill = new() { BaseRoll = 10 };
weightSkill.Coins.Add(new Coin { Active = false, Weight = 2 });
weightSkill.Coins.Add(new Coin { Active = false, Weight = 3 });
weights.Skills.Add(weightSkill);

DamageResult weightResult = DamageCalculator.Calculate(weights);
allOk &= Check("Вес 2 на первой монете", weightResult.Coins[0].Damage, 20.0);
allOk &= Check("Вес 3 на второй", weightResult.Coins[1].Damage, 30.0);
allOk &= Check("Итог 20+30", weightResult.Total, 50.0);

// --- Проверка 5: нулевой бросок даёт единицу, которую множит вес --------------
DamageInput floor = new();
Skill floorSkill = new() { BaseRoll = 0 };
floorSkill.Coins.Add(new Coin { Active = true, Power = 0, Weight = 3 });
floorSkill.Coins.Add(Bonused(new Coin { Active = true, Power = 0, Weight = 3 }, Flat(5)));
floorSkill.Coins.Add(Bonused(new Coin { Active = true, Power = 0 }, Percent(50)));
floorSkill.Coins.Add(new Coin { Active = true, Power = -4 });
floor.Skills.Add(floorSkill);

DamageResult floorResult = DamageCalculator.Calculate(floor);
allOk &= Check("Ролл 0, вес 3", floorResult.Coins[0].Damage, 3.0);
allOk &= Check("Ролл 0, вес 3, flat 5", floorResult.Coins[1].Damage, 18.0);
allOk &= Check("Ролл 0, проценты мимо", floorResult.Coins[2].Damage, 1.0);
allOk &= Check("Ролл ниже нуля", floorResult.Coins[3].Damage, 1.0);

// --- Проверка 7: бонусы flat и процентный -------------------------------------
// Ролл 10 без прочих модификаторов: основа 10.
DamageInput bonus = new();
Skill bonusSkill = new() { BaseRoll = 10 };
bonusSkill.Coins.Add(Bonused(new Coin { Active = false, Weight = 3 }, Flat(5)));
bonusSkill.Coins.Add(Bonused(new Coin { Active = false }, Percent(25)));
bonusSkill.Coins.Add(Bonused(new Coin { Active = false, Weight = 2 }, Percent(25), Flat(5)));
bonusSkill.Coins.Add(Bonused(new Coin { Active = false }, Percent(33)));
bonusSkill.Coins.Add(Bonused(new Coin { Active = false }, Percent(5)));
bonusSkill.Coins.Add(Bonused(new Coin { Active = false }, Percent(-50)));
// Несколько бонусов одного вида складываются: 10% + 15% дают те же 25%.
bonusSkill.Coins.Add(Bonused(new Coin { Active = false }, Percent(10), Percent(15)));
bonusSkill.Coins.Add(Bonused(new Coin { Active = false }, Flat(2), Flat(3)));
bonus.Skills.Add(bonusSkill);

DamageResult bonusResult = DamageCalculator.Calculate(bonus);
allOk &= Check("flat 5 при весе 3", bonusResult.Coins[0].Damage, 45.0);
allOk &= Check("+25% к десятке", bonusResult.Coins[1].Damage, 12.0);
allOk &= Check("(10 + 2 + 5) x 2", bonusResult.Coins[2].Damage, 34.0);
allOk &= Check("+33% -> прибавка 3", bonusResult.Coins[3].Damage, 13.0);
allOk &= Check("+5% -> прибавка 0", bonusResult.Coins[4].Damage, 10.0);
allOk &= Check("Минус 50% отнимает", bonusResult.Coins[5].Damage, 5.0);
allOk &= Check("10% + 15% как 25%", bonusResult.Coins[6].Damage, 12.0);
allOk &= Check("flat 2 + flat 3", bonusResult.Coins[7].Damage, 15.0);

// --- Проверка 8: clash count даёт по 3% к Mod stat -----------------------------
DamageInput clash = new();
Skill clashSkill = new() { BaseRoll = 100 };
clashSkill.Coins.Add(new Coin { Active = false, ClashCount = 0 });
clashSkill.Coins.Add(new Coin { Active = false, ClashCount = 1 });
clashSkill.Coins.Add(new Coin { Active = false, ClashCount = 5 });
clashSkill.Coins.Add(new Coin { Active = false, ClashCount = 5, HasCrit = true, Crit = 0.2 });
clash.Skills.Add(clashSkill);

DamageResult clashResult = DamageCalculator.Calculate(clash);
allOk &= Check("Без клэшей", clashResult.Coins[0].Damage, 100.0);
allOk &= Check("1 клэш -> +3%", clashResult.Coins[1].Damage, 103.0);
allOk &= Check("5 клэшей -> +15%", clashResult.Coins[2].Damage, 115.0);
allOk &= Check("5 клэшей и крит 0,2", clashResult.Coins[3].Damage, 135.0);
allOk &= Check("ModStat(0; 0; 5)", DamageCalculator.ModStat(0, 0, 5), 1.15);

// --- Проверка 9: сопротивления правят Mod stat --------------------------------
allOk &= Check("Сопротивление 1.25", DamageCalculator.ResistanceModifier(1.25), 0.25);
allOk &= Check("Сопротивление 1.5", DamageCalculator.ResistanceModifier(1.5), 0.5);
allOk &= Check("Сопротивление 0.6 (вдвое)", DamageCalculator.ResistanceModifier(0.6), -0.2);
allOk &= Check("Сопротивление 1.0", DamageCalculator.ResistanceModifier(1.0), 0.0);

// Пример из условия: slash 1.25 и gloom 1.5 дают вместе +0.75.
allOk &= Check(
    "slash 1.25 + gloom 1.5",
    DamageCalculator.ResistanceModifier(1.25) + DamageCalculator.ResistanceModifier(1.5),
    0.75);

// Второй пример: slash 0.6 и gloom 1.0 дают -0.2.
allOk &= Check(
    "slash 0.6 + gloom 1.0",
    DamageCalculator.ResistanceModifier(0.6) + DamageCalculator.ResistanceModifier(1.0),
    -0.2);

// Контрольный случай: грех 0.5 и тип 1.2 -> -0.25 + 0.2 = -0.05, Mod stat = 0.95.
DamageInput control = new();
control.Resistances[Element.Slash] = 1.2;
control.Resistances[Element.Gloom] = 0.5;
Skill controlSkill = new() { BaseRoll = 100, Type = Element.Slash, Sin = Element.Gloom };
controlSkill.Coins.Add(new Coin { Active = false });
control.Skills.Add(controlSkill);

DamageResult controlResult = DamageCalculator.Calculate(control);
allOk &= Check("Грех 0.5 и тип 1.2 -> Mod stat", controlResult.Coins[0].ModStat, 0.95);
allOk &= Check("Урон при Mod stat 0.95", controlResult.Coins[0].Damage, 95.0);

// --- Проверка 10: бонус масштабируется сопротивлением к своей цели -------------
DamageInput bonusRes = new();
bonusRes.Resistances[Element.Slash] = 0.5;
bonusRes.Resistances[Element.Wrath] = 2.0;
Skill bonusResSkill = new() { BaseRoll = 100, Type = Element.Pierce, Sin = Element.Envy };
bonusResSkill.Coins.Add(Bonused(new Coin { Active = false }, FlatOf(Element.Slash, 10)));
bonusResSkill.Coins.Add(Bonused(new Coin { Active = false }, FlatOf(Element.Wrath, 10)));
bonusResSkill.Coins.Add(Bonused(new Coin { Active = false }, FlatOf(Element.True, 10)));
bonusResSkill.Coins.Add(Bonused(new Coin { Active = false }, PercentOf(Element.Wrath, 10)));
bonusRes.Skills.Add(bonusResSkill);

DamageResult bonusResResult = DamageCalculator.Calculate(bonusRes);
allOk &= Check("flat 10 при сопр. 0.5", bonusResResult.Coins[0].Damage, 105.0);
allOk &= Check("flat 10 при сопр. 2.0", bonusResResult.Coins[1].Damage, 120.0);
allOk &= Check("flat 10 по true", bonusResResult.Coins[2].Damage, 110.0);
allOk &= Check("10% при сопр. 2.0 -> 20%", bonusResResult.Coins[3].Damage, 120.0);

// --- Проверка 11: вес как число целей и сопротивления подцелей ----------------
// Основная цель: slash 1.2 -> Mod stat 1.2 -> основа 120. Подцель без своих
// настроек берёт сопротивления основной, поэтому вес 2 даёт ровно вдвое.
DamageInput sub = new();
sub.Resistances[Element.Slash] = 1.2;
Skill subSkill = new() { BaseRoll = 100, Type = Element.Slash, Sin = Element.Envy };
subSkill.Coins.Add(new Coin { Active = false, Weight = 2 });

// У второй монеты подцель со своим сопротивлением: slash 0.6 -> Mod stat 0.8 -> 80.
Coin ownSubtarget = new() { Active = false, Weight = 2 };
ResistanceSet weaker = new();
weaker[Element.Slash] = 0.6;
ownSubtarget.SubtargetResistances.Add(weaker);
subSkill.Coins.Add(ownSubtarget);

// Третья: вес 3, из них две подцели со своими сопротивлениями.
Coin three = new() { Active = false, Weight = 3 };
ResistanceSet neutral = new();
ResistanceSet strong = new();
strong[Element.Slash] = 2.0;
three.SubtargetResistances.Add(neutral);
three.SubtargetResistances.Add(strong);
subSkill.Coins.Add(three);

sub.Skills.Add(subSkill);

DamageResult subResult = DamageCalculator.Calculate(sub);
allOk &= Check("Вес 2, подцель как основная", subResult.Coins[0].Damage, 240.0);
allOk &= Check("Вес 2, подцель слабее", subResult.Coins[1].Damage, 200.0);
// 120 по основной (slash 1.2) + 100 по нейтральной + 200 по slash 2.0.
allOk &= Check("Вес 3, разные цели", subResult.Coins[2].Damage, 420.0);
allOk &= Check("Mod stat по основной цели", subResult.Coins[1].ModStat, 1.2);

// --- Проверка 12: Time Moratorium ---------------------------------------------
// Обычный расчёт идёт как есть, сверху прибавка за стаки, в конце — сопротивление
// цели к sloth. Ролл 100, навык slash/wrath, у цели slash 1.5 и sloth 0.5:
// Mod stat 1.5 -> 150, x1.15 или x1.30, затем x0.5.
static DamageInput Moratorium(bool on, int stacks)
{
    DamageInput input = new() { TimeMoratorium = on, TimeMoratoriumStacks = stacks };
    input.Resistances[Element.Slash] = 1.5;
    input.Resistances[Element.Sloth] = 0.5;
    Skill skill = new() { BaseRoll = 100, Type = Element.Slash, Sin = Element.Wrath };
    skill.Coins.Add(new Coin { Active = false });
    input.Skills.Add(skill);
    return input;
}

allOk &= Check("Без Moratorium", DamageCalculator.Calculate(Moratorium(false, 2)).Total, 150.0);
// В таблице монета показывает урон без моратория, а итог — уже с ним.
DamageResult shown = DamageCalculator.Calculate(Moratorium(true, 2));
allOk &= Check("Урон монеты без моратория", shown.Coins[0].BaseDamage, 150.0);
allOk &= Check("Итог до моратория", shown.TotalBase, 150.0);
allOk &= Check("Множитель моратория", shown.Total / shown.TotalBase, 97.0 / 150.0);
// 150 x 1.15 x 0.5 = 86.25 -> 86
allOk &= Check("Moratorium, 1 стак", DamageCalculator.Calculate(Moratorium(true, 1)).Total, 86.0);
// 150 x 1.30 x 0.5 = 97.5 -> 97
allOk &= Check("Moratorium, 2 стака", DamageCalculator.Calculate(Moratorium(true, 2)).Total, 97.0);

// Сопротивление к sloth = 1: остаётся только прибавка за стаки, 150 x 1.3 = 195.
DamageInput neutralSloth = Moratorium(true, 2);
neutralSloth.Resistances[Element.Sloth] = 1.0;
allOk &= Check("Moratorium при sloth 1.0", DamageCalculator.Calculate(neutralSloth).Total, 195.0);

// У подцели своё сопротивление к sloth: основная 0.5, подцель 2.0.
DamageInput subSloth = new() { TimeMoratorium = true, TimeMoratoriumStacks = 2 };
subSloth.Resistances[Element.Sloth] = 0.5;
Skill subSlothSkill = new() { BaseRoll = 100, Type = Element.Pierce, Sin = Element.Envy };
Coin twoTargets = new() { Active = false, Weight = 2 };
ResistanceSet slothStrong = new();
slothStrong[Element.Sloth] = 2.0;
twoTargets.SubtargetResistances.Add(slothStrong);
subSlothSkill.Coins.Add(twoTargets);
subSloth.Skills.Add(subSlothSkill);
// Основная: 100 x 1.3 x 0.5 = 65; подцель: 100 x 1.3 x 2.0 = 260; вместе 325.
allOk &= Check("Moratorium по подцелям", DamageCalculator.Calculate(subSloth).Total, 325.0);

// --- Проверка 13: отрицательная разница уровней по эталону MyCalculator --------
// diff / (|diff| + 25): при diff = -20 это -20/45, а не -20/|-20+25| = -4.
allOk &= Check("ModStat(-20; 0)", DamageCalculator.ModStat(-20, 0), 1.0 - 20.0 / 45.0);

Console.WriteLine();
Console.WriteLine(allOk ? "Все проверки пройдены" : "ЕСТЬ РАСХОЖДЕНИЯ");
return allOk ? 0 : 1;

static Coin Bonused(Coin coin, params CoinBonus[] bonuses)
{
    coin.Bonuses.AddRange(bonuses);
    return coin;
}

static CoinBonus Flat(double value) => new() { Kind = BonusKind.Flat, Value = value };

static CoinBonus Percent(double value) => new() { Kind = BonusKind.Percent, Value = value };

static CoinBonus FlatOf(Element target, double value) =>
    new() { Kind = BonusKind.Flat, Target = target, Value = value };

static CoinBonus PercentOf(Element target, double value) =>
    new() { Kind = BonusKind.Percent, Target = target, Value = value };

static bool Check(string name, double actual, double expected)
{
    bool ok = Math.Abs(actual - expected) < 1e-9;
    Console.WriteLine($"{(ok ? "OK  " : "ОШИБКА")} {name,-28} получено {actual,12:F6}   ожидалось {expected,12:F6}");
    return ok;
}
