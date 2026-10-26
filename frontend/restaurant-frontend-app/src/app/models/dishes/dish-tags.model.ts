// Allergens a dish contains and diets it fits. The API sends them as lower-case slugs.

export const DISH_ALLERGENS = [
  'gluten',
  'dairy',
  'eggs',
  'nuts',
  'peanuts',
  'fish',
  'shellfish',
  'soy',
  'sesame',
] as const;

export const DISH_DIETS = ['vegetarian', 'vegan', 'gluten-free', 'lactose-free'] as const;

export type DishAllergen = (typeof DISH_ALLERGENS)[number];
export type DishDiet = (typeof DISH_DIETS)[number];

export const ALLERGEN_LABELS: Record<DishAllergen, string> = {
  gluten: 'Gluten',
  dairy: 'Dairy',
  eggs: 'Eggs',
  nuts: 'Nuts',
  peanuts: 'Peanuts',
  fish: 'Fish',
  shellfish: 'Shellfish',
  soy: 'Soy',
  sesame: 'Sesame',
};

export const DIET_LABELS: Record<DishDiet, string> = {
  vegetarian: 'Vegetarian',
  vegan: 'Vegan',
  'gluten-free': 'Gluten-free',
  'lactose-free': 'Lactose-free',
};
