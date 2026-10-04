export interface SkillLevel {
  /** Display value. Half levels (2.5, 3.5) describe players between two whole levels. */
  level: string;
  description: string;
  /** Comparable leagues; empty when no league maps cleanly to the level. */
  leagues: readonly string[];
}

/** Player-facing guide to the 0–7 skill range that events use. Events only store whole levels. */
export const SKILL_LEVELS: readonly SkillLevel[] = [
  { level: '0', description: 'Still learning how to skate.', leagues: [] },
  {
    level: '1',
    description:
      'Comfortable on skates, but still struggles with skating both forwards and backwards. Has basic skills to get around the ice, but struggles with stickhandling, passing and shooting.',
    leagues: [],
  },
  {
    level: '2',
    description:
      'Comfortable with skating forwards and turning, but still struggles with backward skating and stickhandling, passing and shooting.',
    leagues: [],
  },
  {
    level: '2.5',
    description: 'Level 2, but starting to get comfortable with stickhandling, passing and shooting.',
    leagues: [],
  },
  {
    level: '3',
    description: 'Comfortable with skating forwards and backwards, along with stickhandling, passing and shooting.',
    leagues: [],
  },
  {
    level: '3.5',
    description: 'Level 3, but starting to understand positioning and how to read the game.',
    leagues: [],
  },
  {
    level: '4',
    description:
      'Overall substantial skating, stickhandling, shooting, hockey IQ and experience. Most Level 4s would have played a good amount of organized hockey.',
    leagues: [],
  },
  {
    level: '5',
    description: 'Has very good skating, stickhandling, shooting, hockey IQ and experience.',
    leagues: ['NCAA D2/D3', 'CJHL', 'USHL', 'NAHL'],
  },
  {
    level: '6',
    description: 'Can play hockey at a very high level, almost at a professional level.',
    leagues: ['NCAA D1', 'CHL'],
  },
  {
    level: '7',
    description: 'Can consistently play hockey at a professional level.',
    leagues: ['NHL', 'AHL'],
  },
];
