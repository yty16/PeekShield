using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PeekShield.Models;

namespace PeekShield.Services;

public class WhitelistItem
{
    public WhitelistEntry Entry = new();
    public FaceVerifier Verifier = new();
}

public class WhitelistService
{
    private readonly PeekShieldSettings _settings;
    private readonly List<WhitelistItem> _items = new();
    private readonly object _lock = new();

    public WhitelistService(PeekShieldSettings settings)
    {
        _settings = settings;
    }

    private static string Dir(string id) => Path.Combine(Platform.EnrollDir, "whitelist", Sanitize(id));

    private static string Sanitize(string id)
    {
        var sb = new StringBuilder();
        foreach (var c in id)
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
        return sb.Length > 0 ? sb.ToString() : "default";
    }

    public void Reload()
    {
        lock (_lock)
        {
            _items.Clear();
            foreach (var e in _settings.Whitelist)
            {
                var it = new WhitelistItem { Entry = e, Verifier = new FaceVerifier() };
                it.Verifier.Load(Dir(e.Id));
                _items.Add(it);
            }
        }
    }

    public WhitelistItem? GetItem(string id)
    {
        lock (_lock) return _items.FirstOrDefault(x => x.Entry.Id == id);
    }

    public void AddItem(WhitelistEntry e)
    {
        lock (_lock)
        {
            if (_items.Any(x => x.Entry.Id == e.Id)) return;
            _items.Add(new WhitelistItem { Entry = e, Verifier = new FaceVerifier() });
        }
    }

    public void Persist(string id)
    {
        lock (_lock)
        {
            var it = _items.FirstOrDefault(x => x.Entry.Id == id);
            if (it != null) it.Verifier.Save(Dir(id));
        }
    }

    public void DeleteData(string id)
    {
        try { var d = Dir(id); if (Directory.Exists(d)) Directory.Delete(d, true); }
        catch { }
    }

    public void RemoveItem(string id)
    {
        lock (_lock)
        {
            var it = _items.FirstOrDefault(x => x.Entry.Id == id);
            if (it != null)
            {
                it.Verifier.Clear();
                DeleteData(id);
                _items.Remove(it);
            }
        }
        var e = _settings.Whitelist.FirstOrDefault(x => x.Id == id);
        if (e != null) { _settings.Whitelist.Remove(e); _settings.Save(); }
    }

    public void Rename(string id, string name)
    {
        var e = _settings.Whitelist.FirstOrDefault(x => x.Id == id);
        if (e != null)
        {
            e.Name = name;
            _settings.Save();
            lock (_lock)
            {
                var it = _items.FirstOrDefault(x => x.Entry.Id == id);
                if (it != null) it.Entry.Name = name;
            }
        }
    }

    public string? Match(float[] embedding, double threshold)
    {
        if (embedding == null || embedding.Length != FaceVerifier.Dim) return null;
        lock (_lock)
        {
            foreach (var it in _items)
            {
                if (!it.Entry.Enabled) continue;
                if (it.Verifier.IsEnrolled && it.Verifier.Verify(embedding, threshold).isOwner)
                    return it.Entry.Name;
            }
        }
        return null;
    }
}
