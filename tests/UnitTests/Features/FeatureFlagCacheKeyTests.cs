using PostHog;
using PostHog.Features;

namespace FeatureFlagCacheKeyTests;

public class TheGenerateMethod
{
    [Fact]
    public void GeneratesStableKeyForDistinctIdOnly()
    {
        var key1 = FeatureFlagCacheKey.Generate("user123", null, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, null);

        Assert.Equal(key1, key2);
        Assert.Equal("user123", key1);
    }

    [Fact]
    public void GeneratesDifferentKeysForDifferentDistinctIds()
    {
        var key1 = FeatureFlagCacheKey.Generate("user123", null, null);
        var key2 = FeatureFlagCacheKey.Generate("user456", null, null);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GeneratesStableKeyWithPersonProperties()
    {
        var properties = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com",
            ["age"] = 25
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", properties, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", properties, null);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GeneratesStableKeyRegardlessOfPropertyOrder()
    {
        var properties1 = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com",
            ["age"] = 25,
            ["country"] = "US"
        };

        var properties2 = new Dictionary<string, object?>
        {
            ["country"] = "US",
            ["email"] = "test@example.com",
            ["age"] = 25
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", properties1, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", properties2, null);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GeneratesDifferentKeysForDifferentPersonProperties()
    {
        var properties1 = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com",
            ["age"] = 25
        };

        var properties2 = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com",
            ["age"] = 30
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", properties1, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", properties2, null);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GeneratesDifferentKeysWhenPersonPropertiesPresent()
    {
        var properties = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com"
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", properties, null);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GeneratesStableKeyWithGroups()
    {
        var groups = new GroupCollection
        {
            { "company", "acme" },
            { "project", "123" }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GeneratesStableKeyWithGroupsRegardlessOfOrder()
    {
        var groups1 = new GroupCollection
        {
            { "company", "acme" },
            { "project", "123" }
        };

        var groups2 = new GroupCollection
        {
            { "project", "123" },
            { "company", "acme" }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups1);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups2);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GeneratesDifferentKeysForDifferentGroups()
    {
        var groups1 = new GroupCollection
        {
            { "company", "acme" }
        };

        var groups2 = new GroupCollection
        {
            { "company", "initech" }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups1);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GeneratesStableKeyWithGroupProperties()
    {
        var groups = new GroupCollection
        {
            new Group("company", "acme")
            {
                ["tier"] = "enterprise",
                ["region"] = "us-west"
            }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GeneratesStableKeyWithGroupPropertiesRegardlessOfOrder()
    {
        var groups1 = new GroupCollection
        {
            new Group("company", "acme")
            {
                ["tier"] = "enterprise",
                ["region"] = "us-west"
            }
        };

        var groups2 = new GroupCollection
        {
            new Group("company", "acme")
            {
                ["region"] = "us-west",
                ["tier"] = "enterprise"
            }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups1);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups2);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GeneratesDifferentKeysForDifferentGroupProperties()
    {
        var groups1 = new GroupCollection
        {
            new Group("company", "acme")
            {
                ["tier"] = "enterprise"
            }
        };

        var groups2 = new GroupCollection
        {
            new Group("company", "acme")
            {
                ["tier"] = "starter"
            }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups1);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GeneratesDifferentKeysWithAndWithoutGroupProperties()
    {
        var groups1 = new GroupCollection
        {
            { "company", "acme" }
        };

        var groups2 = new GroupCollection
        {
            new Group("company", "acme")
            {
                ["tier"] = "enterprise"
            }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups1);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GeneratesStableKeyWithAllParameters()
    {
        var personProperties = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com",
            ["age"] = 25
        };

        var groups = new GroupCollection
        {
            new Group("company", "acme")
            {
                ["tier"] = "enterprise"
            },
            { "project", "123" }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", personProperties, groups);
        var key2 = FeatureFlagCacheKey.Generate("user123", personProperties, groups);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void HandlesNullValuesInProperties()
    {
        var properties1 = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com",
            ["middle_name"] = null
        };

        var properties2 = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com",
            ["middle_name"] = null
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", properties1, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", properties2, null);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void HandlesComplexNestedProperties()
    {
        var properties = new Dictionary<string, object?>
        {
            ["metadata"] = new Dictionary<string, object>
            {
                ["source"] = "mobile",
                ["version"] = 2
            }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", properties, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", properties, null);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void TreatsEmptyPropertiesDictionarySameAsNull()
    {
        var emptyProps = new Dictionary<string, object?>();
        var key1 = FeatureFlagCacheKey.Generate("user123", emptyProps, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, null);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void TreatsEmptyGroupCollectionSameAsNull()
    {
        var emptyGroups = new GroupCollection();
        var key1 = FeatureFlagCacheKey.Generate("user123", null, emptyGroups);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, null);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GeneratesStableKeyWithMultipleGroupsEachWithProperties()
    {
        var groups = new GroupCollection
        {
            new Group("company", "acme")
            {
                ["tier"] = "enterprise",
                ["industry"] = "tech"
            },
            new Group("team", "engineering")
            {
                ["size"] = 50,
                ["location"] = "remote"
            }
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void HandlesDiversePropertyTypes()
    {
        var properties = new Dictionary<string, object?>
        {
            ["string"] = "value",
            ["int"] = 42,
            ["double"] = 98.5,
            ["bool"] = true,
            ["array"] = new[] { "vip", "beta" },
            ["null"] = null
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", properties, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", properties, null);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void HandlesSpecialCharactersInDistinctId()
    {
        var key1 = FeatureFlagCacheKey.Generate("user@123!#$%^&*()", null, null);
        var key2 = FeatureFlagCacheKey.Generate("user@123!#$%^&*()", null, null);

        Assert.Equal(key1, key2);
        Assert.Contains("user@123!#$%^&*()", key1, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlesEmptyStringDistinctId()
    {
        var key1 = FeatureFlagCacheKey.Generate("", null, null);
        var key2 = FeatureFlagCacheKey.Generate("", null, null);

        Assert.Equal(key1, key2);
        Assert.Equal("", key1);
    }

    [Fact]
    public void HandlesWhitespaceDistinctId()
    {
        var key1 = FeatureFlagCacheKey.Generate("   ", null, null);
        var key2 = FeatureFlagCacheKey.Generate("   ", null, null);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GeneratesDifferentKeysForDifferentGroupTypesWithSameKey()
    {
        var groups1 = new GroupCollection { { "company", "123" } };
        var groups2 = new GroupCollection { { "project", "123" } };

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups1);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void PropertyNamesAreCaseSensitive()
    {
        var props1 = new Dictionary<string, object?> { ["Email"] = "test@example.com" };
        var props2 = new Dictionary<string, object?> { ["email"] = "test@example.com" };

        var key1 = FeatureFlagCacheKey.Generate("user123", props1, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", props2, null);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void HandlesLargeNumberOfProperties()
    {
        var properties = new Dictionary<string, object?>();
        for (int i = 0; i < 100; i++)
        {
            properties[$"prop{i}"] = $"value{i}";
        }

        var key1 = FeatureFlagCacheKey.Generate("user123", properties, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", properties, null);

        Assert.Equal(key1, key2);
        Assert.NotEmpty(key1);
    }

    [Fact]
    public void HandlesLargeNumberOfGroups()
    {
        var groups = new GroupCollection();
        for (int i = 0; i < 20; i++)
        {
            groups.Add($"group{i}", $"key{i}");
        }

        var key1 = FeatureFlagCacheKey.Generate("user123", null, groups);
        var key2 = FeatureFlagCacheKey.Generate("user123", null, groups);

        Assert.Equal(key1, key2);
        Assert.NotEmpty(key1);
    }

    [Fact]
    public void GeneratesDifferentKeysForDifferentPropertyCounts()
    {
        var props1 = new Dictionary<string, object?> { ["email"] = "test@example.com" };
        var props2 = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com",
            ["name"] = "Test User"
        };

        var key1 = FeatureFlagCacheKey.Generate("user123", props1, null);
        var key2 = FeatureFlagCacheKey.Generate("user123", props2, null);

        Assert.NotEqual(key1, key2);
    }
}
