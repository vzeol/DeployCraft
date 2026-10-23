using System.Security.Cryptography;

namespace DeployCraft.Server.Library;

/// <summary>Download of a library installer (hash checked) and its copy into the library folder, with progress.</summary>
internal static class InstallerDownload
{
    /// <summary>Downloads the installer into <paramref name="temp"/> then checks its SHA-256 hash.</summary>
    /// <param name="report">Progress: stage, bytes done, total bytes.</param>
    public static async Task FetchAsync(HttpClient http, LibraryInstaller installer, string temp, Action<string, long, long> report)
    {
        using var response = await http.GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        var downloading = Msg.Get("PackageLibrary_StageDownload");
        var checking = Msg.Get("PackageLibrary_StageCheck");
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(temp))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) report(downloading, done, total.Value);
            }
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var check = File.OpenRead(temp))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await check.ReadAsync(buffer)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                done += read;
                report(checking, done, check.Length);
            }
        }
        var hash = Convert.ToHexString(sha.GetHashAndReset());
        if (!hash.Equals(installer.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Msg.Get("PackageLibrary_HashMismatch"));
    }

    /// <summary>Copies into the library folder, following the progress (bytes done, total bytes).</summary>
    public static void Copy(string source, string destination, Action<long, long> report)
    {
        using var input = File.OpenRead(source);
        using var output = File.Create(destination);
        var buffer = new byte[1024 * 1024];
        long done = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            done += read;
            report(done, input.Length);
        }
    }
}
