using System.Net;

namespace CoopServer.Tests;

/// <summary>Unit tests: which addresses the allow-list lets in.</summary>
public class IpAllowListTests
{
    static bool Allows(string[] entries, string address) => new IpAllowList(entries).Allows(IPAddress.Parse(address));

    [Fact]
    public void AnEmptyListAllowsEveryone()
    {
        var list = new IpAllowList(Array.Empty<string>());
        Assert.True(list.Empty);
        Assert.True(list.Allows(IPAddress.Parse("203.0.113.9")));
        Assert.True(list.Allows(null));
        Assert.Equal("everyone", list.ToString());
    }

    [Fact]
    public void BlankEntriesAreSkipped() => Assert.True(new IpAllowList(new[] { "", "  " }).Empty);

    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20", true)]
    [InlineData("192.168.1.20", "192.168.1.21", false)]
    [InlineData("::1", "::1", true)]
    [InlineData("::1", "127.0.0.1", false)]
    public void ASingleAddressMatchesOnlyItself(string entry, string address, bool allowed) =>
        Assert.Equal(allowed, Allows(new[] { entry }, address));

    [Theory]
    [InlineData("192.168.1.0/24", "192.168.1.0", true)]
    [InlineData("192.168.1.0/24", "192.168.1.255", true)]
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("10.0.0.0/8", "10.255.3.4", true)]
    [InlineData("10.0.0.0/8", "11.0.0.1", false)]
    [InlineData("192.168.1.128/25", "192.168.1.200", true)]   // a prefix that isn't a whole number of bytes
    [InlineData("192.168.1.128/25", "192.168.1.127", false)]
    [InlineData("0.0.0.0/0", "8.8.8.8", true)]
    [InlineData("2001:db8::/32", "2001:db8:1234::1", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    public void ARangeMatchesItsAddresses(string entry, string address, bool allowed) =>
        Assert.Equal(allowed, Allows(new[] { entry }, address));

    [Fact]
    public void AnIPv4AddressArrivingAsIPv6CountsAsIPv4()
    {
        var list = new IpAllowList(new[] { "127.0.0.1" });
        Assert.True(list.Allows(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.False(list.Allows(IPAddress.Parse("::ffff:127.0.0.2")));
    }

    [Fact]
    public void AnyOfSeveralEntriesLetsIn()
    {
        var list = new IpAllowList(new[] { "10.0.0.5", "192.168.0.0/16" });
        Assert.True(list.Allows(IPAddress.Parse("10.0.0.5")));
        Assert.True(list.Allows(IPAddress.Parse("192.168.44.1")));
        Assert.False(list.Allows(IPAddress.Parse("10.0.0.6")));
        Assert.False(list.Allows(null));
        Assert.Equal("10.0.0.5, 192.168.0.0/16", list.ToString());
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("192.168.1.0/33")]
    [InlineData("192.168.1.0/-1")]
    [InlineData("192.168.1.0/abc")]
    [InlineData("::1/129")]
    public void ABadEntryIsRefused(string entry) =>
        Assert.Throws<FormatException>(() => new IpAllowList(new[] { entry }));
}
