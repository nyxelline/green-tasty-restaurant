import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DishCardComponent } from './dish-card.component';
import { DishListResponse } from '@app/models/dishes/dish-list-response.model';
import { DishState } from '@app/models/enums/dish-state.model';

describe('DishCardComponent', () => {
  let component: DishCardComponent;
  let fixture: ComponentFixture<DishCardComponent>;

  const dish: DishListResponse = {
    id: 'dish-1',
    name: 'Khachapuri',
    imageUrl: 'assets/img/dish.png',
    price: 15,
    state: DishState.Available,
    weight: '350',
    dishType: 'MainCourse',
    description: 'Georgian cheese bread',
    calories: '500',
    proteins: '20',
    carbohydrates: '45',
    fats: '25',
    vitamins: 'A, B',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DishCardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(DishCardComponent);
    component = fixture.componentInstance;

    fixture.componentRef.setInput('dish', dish);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('diet and allergen tags', () => {
    const root = (): HTMLElement => fixture.nativeElement as HTMLElement;

    it('shows nothing extra for a dish without tags', () => {
      expect(root().querySelector('.dish-tags')).toBeNull();
      expect(root().querySelector('.dish-allergens')).toBeNull();
    });

    it('shows the diets and lists the allergens in readable form', () => {
      fixture.componentRef.setInput('dish', {
        ...dish,
        diets: ['vegetarian', 'gluten-free'],
        allergens: ['dairy', 'eggs'],
      });
      fixture.detectChanges();

      const tags = Array.from(root().querySelectorAll('.dish-tag')).map((tag) => tag.textContent?.trim());
      expect(tags).toEqual(['Vegetarian', 'Gluten-free']);
      expect(root().querySelector('.dish-allergens')?.textContent?.trim()).toBe('Contains: Dairy, Eggs');
    });

    it('shows no allergen line when the dish declares none', () => {
      fixture.componentRef.setInput('dish', { ...dish, diets: ['vegan'], allergens: [] });
      fixture.detectChanges();

      expect(root().querySelector('.dish-allergens')).toBeNull();
      expect(root().querySelectorAll('.dish-tag').length).toBe(1);
    });
  });
});
