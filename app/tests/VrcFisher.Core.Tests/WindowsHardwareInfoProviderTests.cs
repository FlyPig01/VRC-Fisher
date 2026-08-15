using VrcFisher.Infrastructure.Runtime;
using Xunit;

namespace VrcFisher.Core.Tests;

public sealed class WindowsHardwareInfoProviderTests
{
    [Theory]
    [InlineData("Windows 10 Pro", "25H2", "26200", "Windows 11 Pro 25H2 (26200)")]
    [InlineData("Windows 11 Home", "24H2", "26100", "Windows 11 Home 24H2 (26100)")]
    [InlineData("Windows 10 Pro", "22H2", "19045", "Windows 10 Pro 22H2 (19045)")]
    public void Build_number_determines_the_windows_product_generation(
        string product,
        string display,
        string build,
        string expected)
    {
        Assert.Equal(expected, WindowsHardwareInfoProvider.FormatWindowsVersion(product, display, build));
    }
}
