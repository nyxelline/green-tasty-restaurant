using APIHandler.Exceptions;
using System.Collections.Generic;
using System.Linq;

namespace APIHandler.Constants.Dishes;

/// Allergens a dish contains and diets it fits. Stored as lower-case slugs.
public static class DishTags
{
    public static readonly IReadOnlyList<string> Allergens =
        ["gluten", "dairy", "eggs", "nuts", "peanuts", "fish", "shellfish", "soy", "sesame"];

    public static readonly IReadOnlyList<string> Diets =
        ["vegetarian", "vegan", "gluten-free", "lactose-free"];

    public static List<string> NormalizeAllergens(IEnumerable<string>? values) =>
        Normalize(values, Allergens, "allergen");

    public static List<string> NormalizeDiets(IEnumerable<string>? values) =>
        Normalize(values, Diets, "diet");

    /// Trims, lower-cases and de-duplicates the tags; an unknown tag is rejected.
    private static List<string> Normalize(IEnumerable<string>? values, IReadOnlyList<string> allowed, string label)
    {
        var result = new List<string>();
        if (values is null)
        {
            return result;
        }

        foreach (var raw in values)
        {
            var tag = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(tag))
            {
                continue;
            }

            if (!allowed.Contains(tag))
            {
                throw new ValidationException($"Unknown {label} '{raw}'. Allowed values: {string.Join(", ", allowed)}.");
            }

            if (!result.Contains(tag))
            {
                result.Add(tag);
            }
        }

        return result;
    }
}
