using Buffero.Core.Configuration;

namespace Buffero.Tests;

public sealed class HotkeyBindingTests
{
    [Fact]
    public void Default_UsesAltP()
    {
        var binding = HotkeyBinding.Default;

        Assert.False(binding.Ctrl);
        Assert.True(binding.Alt);
        Assert.False(binding.Shift);
        Assert.Equal("P", binding.Key);
    }

    [Fact]
    public void Normalize_AllowsLetterKeys()
    {
        var binding = new HotkeyBinding { Alt = true, Key = "p" };

        binding.Normalize();

        Assert.Equal("P", binding.Key);
    }
}
