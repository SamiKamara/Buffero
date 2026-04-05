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
    public void ToggleDefault_UsesAltL()
    {
        var binding = HotkeyBinding.ToggleDefault;

        Assert.False(binding.Ctrl);
        Assert.True(binding.Alt);
        Assert.False(binding.Shift);
        Assert.Equal("L", binding.Key);
    }

    [Fact]
    public void Normalize_AllowsLetterKeys()
    {
        var binding = new HotkeyBinding { Alt = true, Key = "p" };

        binding.Normalize();

        Assert.Equal("P", binding.Key);
    }

    [Fact]
    public void Normalize_UsesProvidedFallbackKey()
    {
        var binding = new HotkeyBinding { Alt = true, Key = "?" };

        binding.Normalize("L");

        Assert.Equal("L", binding.Key);
    }
}
