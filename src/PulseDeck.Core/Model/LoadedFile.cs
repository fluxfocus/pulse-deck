using PulseDeck.Core.Vcd;

namespace PulseDeck.Core.Model;

public enum LoadStatus { NotLoaded, Ok, FileMissing, ParseError }

/// <summary>
/// One VCD source file tracked by the session. Reloading re-parses the same path in place —
/// <see cref="Document"/> is swapped wholesale, but identity (<see cref="FileAlias"/>, <see cref="FilePath"/>)
/// is stable so the signal tree can diff old vs. new content against it.
/// </summary>
public sealed class LoadedFile : ObservableObject
{
    public string FilePath { get; }

    private string _fileAlias;
    public string FileAlias { get => _fileAlias; set => SetField(ref _fileAlias, value); }

    private VcdDocument? _document;
    public VcdDocument? Document { get => _document; private set => SetField(ref _document, value); }

    private LoadStatus _status = LoadStatus.NotLoaded;
    public LoadStatus Status { get => _status; private set => SetField(ref _status, value); }

    private string? _lastError;
    public string? LastError { get => _lastError; private set => SetField(ref _lastError, value); }

    private DateTime? _lastLoadedUtc;
    public DateTime? LastLoadedUtc { get => _lastLoadedUtc; private set => SetField(ref _lastLoadedUtc, value); }

    public LoadedFile(string filePath, string? alias = null)
    {
        FilePath = filePath;
        _fileAlias = alias ?? Path.GetFileNameWithoutExtension(filePath);
    }

    public Task<bool> LoadAsync(CancellationToken ct = default) => Task.Run(Load, ct);

    public bool Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Document = null;
                Status = LoadStatus.FileMissing;
                LastError = "File not found.";
                return false;
            }

            var doc = VcdParser.ParseFile(FilePath);
            Document = doc;
            Status = LoadStatus.Ok;
            LastError = null;
            LastLoadedUtc = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            Status = LoadStatus.ParseError;
            LastError = ex.Message;
            return false;
        }
    }
}
