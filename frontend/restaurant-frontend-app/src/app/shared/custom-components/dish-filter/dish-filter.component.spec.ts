import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DISH_FILTER_STORAGE_KEY, DishFilterStore } from '@app/services/dishes/dish-filter.store';

import { DishFilterComponent } from './dish-filter.component';

describe('DishFilterComponent', () => {
  let fixture: ComponentFixture<DishFilterComponent>;
  let store: DishFilterStore;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const chip = (name: string): HTMLButtonElement =>
    Array.from(root().querySelectorAll<HTMLButtonElement>('button.chip')).find(
      (button) => button.textContent?.trim() === name
    )!;

  beforeEach(async () => {
    localStorage.removeItem(DISH_FILTER_STORAGE_KEY);
    await TestBed.configureTestingModule({ imports: [DishFilterComponent] }).compileComponents();

    store = TestBed.inject(DishFilterStore);
    fixture = TestBed.createComponent(DishFilterComponent);
    fixture.detectChanges();
  });

  afterEach(() => localStorage.removeItem(DISH_FILTER_STORAGE_KEY));

  it('offers every diet and every allergen as a chip', () => {
    expect(root().querySelectorAll('button.chip').length).toBe(4 + 9);
    expect(chip('Vegan')).toBeTruthy();
    expect(chip('Peanuts')).toBeTruthy();
  });

  it('selects a diet when its chip is pressed', () => {
    chip('Vegan').click();
    fixture.detectChanges();

    expect(store.filter().diets).toEqual(['vegan']);
    expect(chip('Vegan').getAttribute('aria-pressed')).toBe('true');
    expect(chip('Vegetarian').getAttribute('aria-pressed')).toBe('false');
  });

  it('excludes an allergen when its chip is pressed', () => {
    chip('Nuts').click();
    fixture.detectChanges();

    expect(store.filter().excludedAllergens).toEqual(['nuts']);
    expect(chip('Nuts').classList).toContain('chip--on');
  });

  it('shows how many dishes are hidden only while a filter is active', () => {
    fixture.componentRef.setInput('hiddenCount', 3);
    fixture.detectChanges();
    expect(root().textContent).not.toContain('hidden');

    chip('Vegan').click();
    fixture.detectChanges();

    expect(root().textContent).toContain('3 dishes are hidden');
  });

  it('uses the singular form for one hidden dish', () => {
    chip('Vegan').click();
    fixture.componentRef.setInput('hiddenCount', 1);
    fixture.detectChanges();

    expect(root().textContent).toContain('1 dish is hidden');
  });

  it('clears the filters with the clear button', () => {
    chip('Vegan').click();
    chip('Soy').click();
    fixture.detectChanges();

    root().querySelector<HTMLButtonElement>('.dish-filter__clear')!.click();
    fixture.detectChanges();

    expect(store.isActive()).toBeFalse();
    expect(root().querySelector('.dish-filter__clear')).toBeNull();
  });

  it('reminds the visitor to ask the staff when unsure', () => {
    expect(root().textContent).toContain('ask the staff');
  });
});
