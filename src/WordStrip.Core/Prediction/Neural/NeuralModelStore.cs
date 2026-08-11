using WordStrip.Core.Settings;

namespace WordStrip.Core.Prediction.Neural;

/// <summary>Progress of a model download, for the settings window.</summary>
public readonly record struct ModelDownloadProgress(string FileName, long BytesReceived, long BytesTotal)
{
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Clamp((double)BytesReceived / BytesTotal, 0, 1);
}

/// <summary>
/// Where the neural model lives on disk, and the only code in WordStrip that reaches the network.
///
/// <para><b>Nothing here runs unless the user asks.</b> The application makes no network calls of its own —
/// that has been true since the first version and remains true. This downloads only when the user presses a
/// button in Settings, having been shown the model's name, publisher, licence, size and what it will be used
/// for. Nothing about the user is sent: these are plain HTTPS GETs for public files, with no identifiers, no
/// analytics, and nothing about what they have typed.</para>
///
/// <para>Files land beside the rest of the user's data, so "delete the WordStrip folder" remains a complete
/// answer to "remove everything this app has put on my machine".</para>
/// </summary>
public sealed class NeuralModelStore
{
    private readonly NeuralModelDescriptor _descriptor;
    private readonly string _directory;

    public NeuralModelStore(NeuralModelDescriptor? descriptor = null, string? directory = null)
    {
        _descriptor = descriptor ?? NeuralModelCatalog.DistilGpt2;
        _directory = directory ?? Path.Combine(UserDataLocation.Directory, "model");
    }

    public NeuralModelDescriptor Descriptor => _descriptor;

    public string Directory => _directory;

    public string PathOf(string fileName) => Path.Combine(_directory, fileName);

    /// <summary>
    /// Whether every file is present.
    ///
    /// <para>Presence alone is the test, because a file only arrives here by being renamed into place after
    /// downloading completely — an interrupted download leaves a <c>.part</c> file and never becomes this
    /// one. Comparing against expected sizes as well was tempting and actively worse: the published sizes
    /// are recorded by hand, one of them was wrong on the first attempt, and a stale constant would declare
    /// a perfectly good model missing with no way for the user to tell why.</para>
    /// </summary>
    public bool IsDownloaded
    {
        get
        {
            foreach (var (_, fileName, _) in _descriptor.Files)
            {
                var file = new FileInfo(PathOf(fileName));
                if (!file.Exists || file.Length == 0) return false;
            }

            return true;
        }
    }

    /// <summary>Total bytes already on disk, so a part-finished download can report where it got to.</summary>
    public long BytesOnDisk
    {
        get
        {
            long total = 0;
            foreach (var (_, fileName, _) in _descriptor.Files)
            {
                var file = new FileInfo(PathOf(fileName));
                if (file.Exists) total += file.Length;
            }

            return total;
        }
    }

    /// <summary>
    /// Fetches whatever is missing. Safe to call again after a failure — completed files are skipped and a
    /// partial one is discarded rather than resumed, because a wrong byte in the middle of a model is far
    /// harder to diagnose than downloading it twice.
    /// </summary>
    public async Task DownloadAsync(
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        System.IO.Directory.CreateDirectory(_directory);

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WordStrip/1.0 (local model download)");

        foreach (var (url, fileName, expectedBytes) in _descriptor.Files)
        {
            var destination = PathOf(fileName);
            var existing = new FileInfo(destination);
            if (existing.Exists && existing.Length == expectedBytes)
            {
                progress?.Report(new ModelDownloadProgress(fileName, expectedBytes, expectedBytes));
                continue;
            }

            var temporary = destination + ".part";

            try
            {
                using (var response = await http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    var total = response.Content.Headers.ContentLength ?? expectedBytes;

                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var target = File.Create(temporary);

                    var buffer = new byte[81920];
                    long received = 0;
                    int read;

                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new ModelDownloadProgress(fileName, received, total));
                    }
                }

                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination);
            }
            catch
            {
                // Never leave a half-written file where a complete one is expected.
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
                throw;
            }
        }
    }

    /// <summary>Deletes the model. The counterpart to downloading it, and the honest meaning of "I changed my mind".</summary>
    public void Delete()
    {
        try
        {
            if (System.IO.Directory.Exists(_directory)) System.IO.Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Most likely the model is still loaded and its file is locked; it goes on the next attempt.
        }
    }
}
