import {
  EMPTY_DISH_FILTER,
  isDishFilterActive,
  matchesDishFilter,
  tagLabel,
} from './dish-filter.helper';

describe('dish filter helper', () => {
  const vegan = { diets: ['vegetarian', 'vegan'], allergens: [] };
  const cheesecake = { diets: ['vegetarian'], allergens: ['gluten', 'dairy', 'eggs'] };
  const salmon = { diets: ['gluten-free'], allergens: ['fish'] };

  describe('matchesDishFilter', () => {
    it('lets every dish through when nothing is selected', () => {
      expect(matchesDishFilter(cheesecake, EMPTY_DISH_FILTER)).toBeTrue();
      expect(matchesDishFilter({}, EMPTY_DISH_FILTER)).toBeTrue();
    });

    it('keeps only the dishes that fit the selected diet', () => {
      const filter = { diets: ['vegan' as const], excludedAllergens: [] };

      expect(matchesDishFilter(vegan, filter)).toBeTrue();
      expect(matchesDishFilter(cheesecake, filter)).toBeFalse();
    });

    it('requires a dish to fit all the selected diets', () => {
      const filter = { diets: ['vegetarian' as const, 'gluten-free' as const], excludedAllergens: [] };

      expect(matchesDishFilter(vegan, filter)).toBeFalse();
      expect(matchesDishFilter({ diets: ['vegetarian', 'gluten-free'] }, filter)).toBeTrue();
    });

    it('hides the dishes that contain an excluded allergen', () => {
      const filter = { diets: [], excludedAllergens: ['dairy' as const] };

      expect(matchesDishFilter(cheesecake, filter)).toBeFalse();
      expect(matchesDishFilter(salmon, filter)).toBeTrue();
    });

    it('hides a dish that contains any one of several excluded allergens', () => {
      const filter = { diets: [], excludedAllergens: ['fish' as const, 'eggs' as const] };

      expect(matchesDishFilter(salmon, filter)).toBeFalse();
      expect(matchesDishFilter(cheesecake, filter)).toBeFalse();
      expect(matchesDishFilter(vegan, filter)).toBeTrue();
    });

    it('combines diets and allergens', () => {
      const filter = { diets: ['vegetarian' as const], excludedAllergens: ['eggs' as const] };

      expect(matchesDishFilter(vegan, filter)).toBeTrue();
      expect(matchesDishFilter(cheesecake, filter)).toBeFalse();
    });

    it('treats a dish without tags as having none declared', () => {
      expect(matchesDishFilter({}, { diets: [], excludedAllergens: ['nuts'] })).toBeTrue();
      expect(matchesDishFilter({}, { diets: ['vegan'], excludedAllergens: [] })).toBeFalse();
    });
  });

  describe('isDishFilterActive', () => {
    it('is off for the empty filter and on when something is selected', () => {
      expect(isDishFilterActive(EMPTY_DISH_FILTER)).toBeFalse();
      expect(isDishFilterActive({ diets: ['vegan'], excludedAllergens: [] })).toBeTrue();
      expect(isDishFilterActive({ diets: [], excludedAllergens: ['soy'] })).toBeTrue();
    });
  });

  describe('tagLabel', () => {
    it('gives readable names and falls back to the raw tag', () => {
      expect(tagLabel('gluten-free')).toBe('Gluten-free');
      expect(tagLabel('shellfish')).toBe('Shellfish');
      expect(tagLabel('moon-dust')).toBe('moon-dust');
    });
  });
});
