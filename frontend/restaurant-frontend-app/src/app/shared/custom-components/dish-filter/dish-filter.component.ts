import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';

import {
  ALLERGEN_LABELS,
  DIET_LABELS,
  DISH_ALLERGENS,
  DISH_DIETS,
  DishAllergen,
  DishDiet,
} from '@app/models/dishes/dish-tags.model';
import { DishFilterStore } from '@app/services/dishes/dish-filter.store';

@Component({
  selector: 'app-dish-filter',
  standalone: true,
  templateUrl: './dish-filter.component.html',
  styleUrl: './dish-filter.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DishFilterComponent {
  readonly store = inject(DishFilterStore);

  /** How many dishes the current filter hides. */
  readonly hiddenCount = input(0);

  readonly diets = DISH_DIETS;
  readonly allergens = DISH_ALLERGENS;
  readonly dietLabels = DIET_LABELS;
  readonly allergenLabels = ALLERGEN_LABELS;

  isDietOn(diet: DishDiet): boolean {
    return this.store.filter().diets.includes(diet);
  }

  isAllergenOn(allergen: DishAllergen): boolean {
    return this.store.filter().excludedAllergens.includes(allergen);
  }
}
