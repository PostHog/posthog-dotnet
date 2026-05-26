using PostHog;

namespace GroupCollectionTests;

public class TheTryAddGroupMethod
{
    [Fact]
    public void StoresGroupByGroupType()
    {
        var groups = new GroupCollection();
        var group = new Group("company", "acme");

        var added = groups.TryAdd(group);

        Assert.True(added);
        Assert.True(groups.Contains("company"));
        Assert.False(groups.Contains("acme"));
        Assert.True(groups.TryGetGroup("company", out var storedGroup));
        Assert.Same(group, storedGroup);
    }

    [Fact]
    public void RejectsDuplicateGroupTypeWithDifferentGroupKey()
    {
        var groups = new GroupCollection();

        Assert.True(groups.TryAdd(new Group("company", "acme")));
        Assert.False(groups.TryAdd(new Group("company", "initech")));
        Assert.Single(groups);
        Assert.True(groups.TryGetGroup("company", out var storedGroup));
        Assert.Equal("acme", storedGroup.GroupKey);
    }
}
