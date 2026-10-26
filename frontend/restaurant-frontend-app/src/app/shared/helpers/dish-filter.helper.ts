import {
  ALLERGEN_LABELS,
  DIET_LABELS,
  DishAllergen,
  DishDiet,
} from '@app/models/dishes/dish-tags.model';

export interface DishFilter {
  /** The dish must fit ALL of these diets. */
  diets: DishDiet[];
  /** The dish must contain NONE of these allergens. */
  excludedAllergens: DishAllergen[];
}

export interface TaggedDish {
  allergens?: readonly string[] | null;
  diets?: readonly string[] | null;
}

export const EMPTY_DISH_FILTER: DishFilter = { diets: [], excludedAllergens: [] };

export function isDishFilterActive(filter: DishFilter): boolean {
  return filter.diets.length > 0 || filter.excludedAllergens.length > 0;
}

export function matchesDishFilter(dish: TaggedDish, filter: DishFilter): boolean {
  const dishDiets = dish.diets ?? [];
  const dishAllergens = dish.allergens ?? [];

  const fitsEveryDiet = filter.diets.every((diet) => dishDiets.includes(diet));
  const hasNoExcludedAllergen = filter.excludedAllergens.every(
    (allergen) => !dishAllergens.includes(allergen)
  );

  return fitsEveryDiet && hasNoExcludedAllergen;
}

/** Readable name of a tag; an unknown tag is shown as it came. */
export function tagLabel(tag: string): string {
  const labels: Record<string, string> = { ...DIET_LABELS, ...ALLERGEN_LABELS };
  return labels[tag] ?? tag;
}
