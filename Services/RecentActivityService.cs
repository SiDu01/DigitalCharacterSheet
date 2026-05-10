using System.Text.Json;
using DigitalCharacterSheet.Models;
using Microsoft.Maui.Storage;

namespace DigitalCharacterSheet.Services;

public sealed class RecentActivityService
{
    private const string PreferenceKey = "digitalCharacterSheet.recentActivity";
    private const string BookmarkPreferenceKey = "digitalCharacterSheet.bookmarks";
    private const int MaxEntries = 24;
    private const int MaxBookmarks = 64;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private List<RecentActivityEntry>? _entries;
    private List<RecentActivityEntry>? _bookmarks;

    public IReadOnlyList<RecentActivityEntry> GetRecentEntries(string? entryType = null, int count = 6)
    {
        EnsureLoaded();
        return _entries!
            .Where(entry => string.IsNullOrWhiteSpace(entryType) || string.Equals(entry.EntryType, entryType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.ViewedAtUtc)
            .Take(Math.Max(1, count))
            .Select(Clone)
            .ToList();
    }

    public void Track(string entryType, int entityId, string title, string subtitle, string route)
    {
        if (entityId <= 0 || string.IsNullOrWhiteSpace(entryType) || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        EnsureLoaded();
        _entries!.RemoveAll(entry => string.Equals(entry.EntryType, entryType, StringComparison.OrdinalIgnoreCase) && entry.EntityId == entityId);
        _entries.Insert(0, new RecentActivityEntry
        {
            EntryType = entryType.Trim(),
            EntityId = entityId,
            Title = title.Trim(),
            Subtitle = subtitle.Trim(),
            Route = route.Trim(),
            ViewedAtUtc = DateTime.UtcNow
        });
        _entries = _entries
            .OrderByDescending(entry => entry.ViewedAtUtc)
            .Take(MaxEntries)
            .ToList();
        Save();
    }

    public IReadOnlyList<RecentActivityEntry> GetBookmarks(string? entryType = null, int count = 12)
    {
        EnsureBookmarksLoaded();
        return _bookmarks!
            .Where(entry => string.IsNullOrWhiteSpace(entryType) || string.Equals(entry.EntryType, entryType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.BookmarkedAtUtc ?? entry.ViewedAtUtc)
            .Take(Math.Max(1, count))
            .Select(Clone)
            .ToList();
    }

    public bool IsBookmarked(string entryType, int entityId)
    {
        if (entityId <= 0 || string.IsNullOrWhiteSpace(entryType))
        {
            return false;
        }

        EnsureBookmarksLoaded();
        return _bookmarks!.Any(entry => Matches(entry, entryType, entityId));
    }

    public bool ToggleBookmark(string entryType, int entityId, string title, string subtitle, string route)
    {
        if (entityId <= 0 || string.IsNullOrWhiteSpace(entryType) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        EnsureBookmarksLoaded();
        var bookmarks = _bookmarks!;
        var existing = bookmarks.FirstOrDefault(entry => Matches(entry, entryType, entityId));
        if (existing is not null)
        {
            bookmarks.Remove(existing);
            SaveBookmarks();
            return false;
        }

        bookmarks.Insert(0, new RecentActivityEntry
        {
            EntryType = entryType.Trim(),
            EntityId = entityId,
            Title = title.Trim(),
            Subtitle = subtitle.Trim(),
            Route = route.Trim(),
            ViewedAtUtc = DateTime.UtcNow,
            BookmarkedAtUtc = DateTime.UtcNow
        });
        _bookmarks = bookmarks
            .OrderByDescending(entry => entry.BookmarkedAtUtc ?? entry.ViewedAtUtc)
            .Take(MaxBookmarks)
            .ToList();
        SaveBookmarks();
        return true;
    }

    private void EnsureLoaded()
    {
        if (_entries is not null)
        {
            return;
        }

        var json = Preferences.Default.Get(PreferenceKey, "");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                _entries = JsonSerializer.Deserialize<List<RecentActivityEntry>>(json, _jsonOptions);
            }
            catch (JsonException)
            {
                _entries = null;
            }
        }

        _entries ??= [];
    }

    private void Save()
    {
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(_entries, _jsonOptions));
    }

    private void EnsureBookmarksLoaded()
    {
        if (_bookmarks is not null)
        {
            return;
        }

        var json = Preferences.Default.Get(BookmarkPreferenceKey, "");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                _bookmarks = JsonSerializer.Deserialize<List<RecentActivityEntry>>(json, _jsonOptions);
            }
            catch (JsonException)
            {
                _bookmarks = null;
            }
        }

        _bookmarks ??= [];
    }

    private void SaveBookmarks()
    {
        Preferences.Default.Set(BookmarkPreferenceKey, JsonSerializer.Serialize(_bookmarks, _jsonOptions));
    }

    private static bool Matches(RecentActivityEntry entry, string entryType, int entityId)
    {
        return string.Equals(entry.EntryType, entryType, StringComparison.OrdinalIgnoreCase) && entry.EntityId == entityId;
    }

    private static RecentActivityEntry Clone(RecentActivityEntry entry)
    {
        return new RecentActivityEntry
        {
            EntryType = entry.EntryType,
            EntityId = entry.EntityId,
            Title = entry.Title,
            Subtitle = entry.Subtitle,
            Route = entry.Route,
            ViewedAtUtc = entry.ViewedAtUtc,
            BookmarkedAtUtc = entry.BookmarkedAtUtc
        };
    }
}
