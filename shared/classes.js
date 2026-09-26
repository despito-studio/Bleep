export const CHARACTER_CLASSES = {
  engineer: {
    id: 'engineer',
    name: 'Victor',
    title: 'Охоронець станції',
    role: 'ОХОРОНЕЦЬ',
    gender: 'male',
    genderLabel: 'Чоловік',
    color: '#1e423f',
    badge: 'ОХОРОНЕЦЬ',
    perk: 'Потужний ліхтар (+25% дальність) та швидка витривалість',
    description: 'Офіцер безпеки станції у фірмовій темно-бірюзовій формі з золотими ґудзиками та кашкетом із зіркою. Потужний ліхтар пробиває темряву на 30 метрів.',
    bonuses: {
      flashlightDist: 30,
      flashlightAngle: Math.PI / 5.8,
      staminaRecovery: 1.25,
      speedMultiplier: 1.0,
      stunBonus: 1.0,
      sanityResistance: 1.0,
      radioCooldown: 3000,
    },
  },
  operator: {
    id: 'operator',
    name: 'Vivian',
    title: 'Телеоператорка',
    role: 'ОПЕРАТОРКА',
    gender: 'female',
    genderLabel: 'Жінка',
    color: '#4a1824',
    badge: 'ОПЕРАТОРКА',
    perk: 'Світловий шок (+35% стан диктора) та стійкий розсудок',
    description: 'Хронікерка телецентру з платиновим каре, золотими сережками-кільцями, перлинним кольє та сигаретою. Її спалах камери довше стримує диктора.',
    bonuses: {
      flashlightDist: 24,
      flashlightAngle: Math.PI / 6.5,
      staminaRecovery: 1.0,
      speedMultiplier: 1.0,
      stunBonus: 1.35,
      sanityResistance: 0.75, // 25% less sanity drain
      radioCooldown: 3000,
    },
  },
  journalist: {
    id: 'journalist',
    name: 'Edgar',
    title: 'Репортер розслідувань',
    role: 'ЖУРНАЛІСТ',
    gender: 'male',
    genderLabel: 'Чоловік',
    color: '#242726',
    badge: 'РЕПОРТЕР',
    perk: 'Швидкий спринт (+15% швидкість) та нічне чуття касет',
    description: 'Згорблений спецкор у масивному твідовому піджаку з наддовгою вінтажною краваткою з орнаментом. Швидкий у бігу та помічає золотаве сяйво касет.',
    bonuses: {
      flashlightDist: 24,
      flashlightAngle: Math.PI / 6.5,
      staminaRecovery: 1.0,
      speedMultiplier: 1.15, // 5.2 m/s sprint
      stunBonus: 1.0,
      sanityResistance: 1.0,
      radioCooldown: 3000,
    },
  },
  dispatcher: {
    id: 'dispatcher',
    name: 'Hazel',
    title: 'Чергова радіовежі',
    role: 'ДИСПЕТЧЕРКА',
    gender: 'female',
    genderLabel: 'Жінка',
    color: '#d95328',
    badge: 'ДИСПЕТЧЕРКА',
    perk: 'Акустичний слух (чує ворога з 22м) та рація 1.5 сек',
    description: 'Диспетчерка вежі у вінтажній психоделічній сукні з хвилястим візерунком, довгим волоссям та крилатим чубчиком. Рація перезаряджається вдвічі швидше.',
    bonuses: {
      flashlightDist: 24,
      flashlightAngle: Math.PI / 6.5,
      staminaRecovery: 1.0,
      speedMultiplier: 1.0,
      stunBonus: 1.0,
      sanityResistance: 1.0,
      radioCooldown: 1500, // Twice as fast radio ping
      monsterHearingRange: 22,
    },
  },
};

export const CLASS_KEYS = Object.keys(CHARACTER_CLASSES);
export const DEFAULT_CLASS = 'engineer';

export function getClassData(classId) {
  return CHARACTER_CLASSES[classId] || CHARACTER_CLASSES[DEFAULT_CLASS];
}
