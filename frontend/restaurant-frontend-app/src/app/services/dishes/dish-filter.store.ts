import { computed, Injectable, signal } from '@angular/core';

import {
  DISH_ALLERGENS,
  DISH_DIETS,
  DishAllergen,
  DishDiet,
} from '@app/models/dishes/dish-tags.model';
import {
  DishFilter,
  EMPTY_DISH_FILTER,
  isDishFilterActive,
} from '@app/shared/helpers/dish-filter.helper';

export const DISH_FILTER_STORAGE_KEY = 'green-tasty.dish-filter';

/**
 * What the visitor wants to eat or avoid. It is shared by every menu and remembered in the browser,
 * so a guest does not have to choose again on the next visit.
 */
@Injectable({ providedIn: 'root' })
export class DishFilterStore {
  readonly filter = signal<DishFilter>(this.restore());
  readonly isActive = computed(() => isDishFilterActive(this.filter()));

  toggleDiet(diet: DishDiet): void {
    this.update({ ...this.filter(), diets: this.toggled(this.filter().diets, diet) });
  }

  toggleAllergen(allergen: DishAllergen): void {
    this.update({
      ...this.filter(),
      excludedAllergens: this.toggled(this.filter().excludedAllergens, allergen),
    });
  }

  clear(): void {
    this.update(EMPTY_DISH_FILTER);
  }

  private update(filter: DishFilter): void {
    this.filter.set(filter);
    try {
      localStorage.setItem(DISH_FILTER_STORAGE_KEY, JSON.stringify(filter));
    } catch {
      // Private mode or blocked storage: the filter still works until the page is closed.
    }
  }

  private toggled<T>(values: T[], value: T): T[] {
    return values.includes(value) ? values.filter((entry) => entry !== value) : [...values, value];
  }

  private restore(): DishFilter {
    try {
      const raw = localStorage.getItem(DISH_FILTER_STORAGE_KEY);
      if (!raw) {
        return EMPTY_DISH_FILTER;
      }

      const parsed = JSON.parse(raw) as Partial<DishFilter>;
      return {
        diets: (parsed.diets ?? []).filter((diet): diet is DishDiet =>
          (DISH_DIETS as readonly string[]).includes(diet)
        ),
        excludedAllergens: (parsed.excludedAllergens ?? []).filter(
          (allergen): allergen is DishAllergen =>
            (DISH_ALLERGENS as readonly string[]).includes(allergen)
        ),
      };
    } catch {
      return EMPTY_DISH_FILTER;
    }
  }
}
