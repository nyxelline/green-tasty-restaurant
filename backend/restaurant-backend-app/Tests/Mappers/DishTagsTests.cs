using APIHandler.Constants.Dishes;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Mappers;
using APIHandler.Models.Dishes;

namespace APIHandler.Tests.Mappers;

public class DishTagsTests
{
    [Fact]
    public void NormalizeAllergens_TrimsLowerCasesAndRemovesDuplicates()
    {
        var result = DishTags.NormalizeAllergens([" Nuts ", "DAIRY", "nuts", ""]);

        Assert.Equal(new[] { "nuts", "dairy" }, result.ToArray());
    }

    [Fact]
    public void NormalizeAllergens_Null_ReturnsEmptyList()
    {
        Assert.Empty(DishTags.NormalizeAllergens(null));
    }

    [Fact]
    public void NormalizeAllergens_UnknownTag_Throws()
    {
        var exception = Assert.Throws<ValidationException>(() => DishTags.NormalizeAllergens(["gluten", "pollen"]));

        Assert.Contains("pollen", exception.Message);
    }

    [Fact]
    public void NormalizeDiets_AcceptsAllKnownDiets()
    {
        var result = DishTags.NormalizeDiets(["vegetarian", "vegan", "gluten-free", "lactose-free"]);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void NormalizeDiets_AnAllergenIsNotADiet()
    {
        Assert.Throws<ValidationException>(() => DishTags.NormalizeDiets(["nuts"]));
    }
}

public class DishTagsMappingTests
{
    private static Dish SampleDish() => new()
    {
        Id = "dish-1",
        Name = "Cheesecake",
        Description = "Sweet",
        Weight = "140 g",
        Calories = "380",
        Proteins = "6",
        Fats = "20",
        Carbohydrates = "40",
        Vitamins = "A",
        LocationId = "loc-1",
        Allergens = ["gluten", "dairy"],
        Diets = ["vegetarian"]
    };

    [Fact]
    public void ToDishResponse_CarriesTags()
    {
        var response = DishMapper.ToDishResponse(SampleDish());

        Assert.Equal(new[] { "gluten", "dairy" }, response.Allergens.ToArray());
        Assert.Equal(new[] { "vegetarian" }, response.Diets.ToArray());
    }

    [Fact]
    public void ToDishListResponse_CarriesTags()
    {
        var response = DishMapper.ToDishListResponse(SampleDish());

        Assert.Equal(new[] { "gluten", "dairy" }, response.Allergens.ToArray());
        Assert.Equal(new[] { "vegetarian" }, response.Diets.ToArray());
    }

    [Fact]
    public void GuestMenuDishMapper_CarriesTags()
    {
        var response = GuestMenuDishMapper.Map(SampleDish());

        Assert.Equal(new[] { "gluten", "dairy" }, response.Allergens.ToArray());
        Assert.Equal(new[] { "vegetarian" }, response.Diets.ToArray());
    }

    [Fact]
    public void Mappers_DishWithoutTags_GiveEmptyLists()
    {
        var dish = SampleDish();
        dish.Allergens = null!;
        dish.Diets = null!;

        Assert.Empty(DishMapper.ToDishResponse(dish).Allergens);
        Assert.Empty(DishMapper.ToDishListResponse(dish).Diets);
        Assert.Empty(GuestMenuDishMapper.Map(dish).Allergens);
    }

    [Fact]
    public void FromCreateRequest_NormalizesTags()
    {
        var request = new CreateDishRequest
        {
            Name = "Soup", Description = "Hot", Weight = "300 g", Calories = "200", Proteins = "5",
            Fats = "8", Carbohydrates = "20", Vitamins = "C", LocationIds = [],
            Allergens = [" Soy "], Diets = ["VEGAN"]
        };

        var dish = DishMapper.FromCreateRequest(request);

        Assert.Equal(new[] { "soy" }, dish.Allergens.ToArray());
        Assert.Equal(new[] { "vegan" }, dish.Diets.ToArray());
    }

    [Fact]
    public void FromCreateRequest_WithoutTags_GivesEmptyLists()
    {
        var request = new CreateDishRequest
        {
            Name = "Soup", Description = "Hot", Weight = "300 g", Calories = "200", Proteins = "5",
            Fats = "8", Carbohydrates = "20", Vitamins = "C", LocationIds = []
        };

        var dish = DishMapper.FromCreateRequest(request);

        Assert.Empty(dish.Allergens);
        Assert.Empty(dish.Diets);
    }

    [Fact]
    public void FromCreateRequest_UnknownTag_Throws()
    {
        var request = new CreateDishRequest
        {
            Name = "Soup", Description = "Hot", Weight = "300 g", Calories = "200", Proteins = "5",
            Fats = "8", Carbohydrates = "20", Vitamins = "C", LocationIds = [], Allergens = ["moon-dust"]
        };

        Assert.Throws<ValidationException>(() => DishMapper.FromCreateRequest(request));
    }
}
