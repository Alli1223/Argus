using System.Diagnostics;
using System.Security.Cryptography;
using Argus.Contracts.Agent;

namespace Argus.Agent.Updates;

internal static class UpdateFiles
{
    /// <summary>
    /// Writes a downloaded build to <paramref name="path"/> if it has exactly the offered size and SHA-256.
    /// Returns why it was refused, or null. Nothing is left behind when it is refused.
    /// </summary>
    public static async Task<string?> SaveCheckedAsync(Stream source, AgentUpdateOffer offer, string path, CancellationToken cancellationToken)
    {
        var partial = path + ".part";
        try
        {
            string actual;
            long size = 0;
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81_920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > offer.Size)
                    {
                        return $"The download is larger than the {offer.Size} bytes offered.";
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            if (size != offer.Size)
            {
                return $"The download has {size} bytes, not the {offer.Size} offered.";
            }

            if (!string.Equals(actual, offer.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return "The download does not match the offered SHA-256.";
            }

            File.Move(partial, path, overwrite: true);
            return null;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>Runs an agent program's version command and returns what it prints, or null if it does not answer.</summary>
    public static async Task<string?> VersionOfAsync(string program, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("version");

        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return null;
        }
    }
}
