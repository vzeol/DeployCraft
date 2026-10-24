using System.Text;
using DeployCraft.Runner;

namespace DeployCraft.Tests;

/// <summary>Output of a program: OEM code page, UTF-16, or both mixed.</summary>
public class ProcessOutputTests
{
    static ProcessOutputTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static Encoding Oem => Encoding.GetEncoding(850);

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    [Fact]
    public void Oem_IsDecodedAsIs()
    {
        Assert.Equal("Opération réussie\r\n", ProcessOutput.Decode(Oem.GetBytes("Opération réussie\r\n"), Oem));
    }

    [Fact]
    public void Utf16_HasNoMoreSpacesBetweenLetters()
    {
        var bytes = Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes("c:\\windows\\notepad.exe : déjà installé\r\n"));
        Assert.Equal("c:\\windows\\notepad.exe : déjà installé\r\n", ProcessOutput.Decode(bytes, Oem));
    }

    [Fact]
    public void Utf16_KeepsCharactersBeyondLatin1()
    {
        var bytes = Encoding.Unicode.GetBytes("Cette action n’est valide que pour les produits installés.");
        Assert.Equal("Cette action n’est valide que pour les produits installés.", ProcessOutput.Decode(bytes, Oem));
    }

    [Fact]
    public void MixedOutput_DecodesEachPart()
    {
        var bytes = Concat(Oem.GetBytes("Début du script\r\n"), Encoding.Unicode.GetBytes("Message de msiexec\r\n"), Oem.GetBytes("Fin\r\n"));
        Assert.Equal("Début du script\r\nMessage de msiexec\r\nFin\r\n", ProcessOutput.Decode(bytes, Oem));
    }

    [Fact]
    public void Add_StopsAtTheLimit()
    {
        var output = new ProcessOutput(4);
        output.Add(Oem.GetBytes("abc"), 3);
        output.Add(Oem.GetBytes("def"), 3);
        Assert.Equal("abcd", output.Text(Oem));
    }

    [Theory]
    [InlineData("powershell", 65001)]
    [InlineData("POWERSHELL", 65001)]
    [InlineData("command", 850)]
    [InlineData("install", 850)]
    [InlineData("copyfiles", 850)]
    public void Encoding_IsUtf8OnlyForPowerShell(string type, int codePage) =>
        Assert.Equal(codePage, ProcessOutput.EncodingFor(type, Oem).CodePage);

    [Fact]
    public void Utf8AndUtf16_PreserveUnicodeWhenMixed()
    {
        var bytes = Concat(Encoding.UTF8.GetBytes("œ — é 😀\r\n"),
            Encoding.Unicode.GetBytes("Message de msiexec\r\n"), Encoding.UTF8.GetBytes("Fin : œ — é 😀\r\n"));

        Assert.Equal("œ — é 😀\r\nMessage de msiexec\r\nFin : œ — é 😀\r\n", ProcessOutput.Decode(bytes, Encoding.UTF8));
    }

    [Fact]
    public void Utf8Characters_CanArriveInSeparateChunks()
    {
        var output = new ProcessOutput(1024);
        foreach (var value in Encoding.UTF8.GetBytes("œ — é 😀")) output.Add([value], 1);

        Assert.Equal("œ — é 😀", output.Text(Encoding.UTF8));
    }
}
