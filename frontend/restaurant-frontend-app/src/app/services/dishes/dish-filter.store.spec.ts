import { TestBed } from '@angular/core/testing';

import { DISH_FILTER_STORAGE_KEY, DishFilterStore } from './dish-filter.store';

describe('DishFilterStore', () => {
  beforeEach(() => localStorage.removeItem(DISH_FILTER_STORAGE_KEY));
  afterEach(() => localStorage.removeItem(DISH_FILTER_STORAGE_KEY));

  const createStore = (): DishFilterStore => {
    TestBed.resetTestingModule();
    return TestBed.inject(DishFilterStore);
  };

  it('starts empty and inactive', () => {
    const store = createStore();

    expect(store.filter()).toEqual({ diets: [], excludedAllergens: [] });
    expect(store.isActive()).toBeFalse();
  });

  it('toggles a diet on and off', () => {
    const store = createStore();

    store.toggleDiet('vegan');
    expect(store.filter().diets).toEqual(['vegan']);
    expect(store.isActive()).toBeTrue();

    store.toggleDiet('vegan');
    expect(store.filter().diets).toEqual([]);
    expect(store.isActive()).toBeFalse();
  });

  it('toggles an excluded allergen on and off', () => {
    const store = createStore();

    store.toggleAllergen('nuts');
    store.toggleAllergen('dairy');
    expect(store.filter().excludedAllergens).toEqual(['nuts', 'dairy']);

    store.toggleAllergen('nuts');
    expect(store.filter().excludedAllergens).toEqual(['dairy']);
  });

  it('clears everything', () => {
    const store = createStore();
    store.toggleDiet('vegan');
    store.toggleAllergen('soy');

    store.clear();

    expect(store.filter()).toEqual({ diets: [], excludedAllergens: [] });
  });

  it('remembers the choice for the next visit', () => {
    const first = createStore();
    first.toggleDiet('gluten-free');
    first.toggleAllergen('fish');

    const second = createStore();

    expect(second.filter()).toEqual({ diets: ['gluten-free'], excludedAllergens: ['fish'] });
  });

  it('ignores stored values that are not known tags', () => {
    localStorage.setItem(
      DISH_FILTER_STORAGE_KEY,
      JSON.stringify({ diets: ['vegan', 'carnivore'], excludedAllergens: ['nuts', 'pollen'] })
    );

    const store = createStore();

    expect(store.filter()).toEqual({ diets: ['vegan'], excludedAllergens: ['nuts'] });
  });

  it('starts empty when the stored value is broken', () => {
    localStorage.setItem(DISH_FILTER_STORAGE_KEY, '{not json');

    const store = createStore();

    expect(store.filter()).toEqual({ diets: [], excludedAllergens: [] });
  });
});
