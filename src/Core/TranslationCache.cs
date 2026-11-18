namespace AltyaziCevirici.Core;

/// <summary>Small bounded LRU cache keyed by <see cref="TextUtil.Key"/>.</summary>
public sealed class TranslationCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, string Value)>> _map = new();
    private readonly LinkedList<(string Key, string Value)> _lru = new();
    private readonly object _lock = new();

    public TranslationCache(int capacity = 2000) => _capacity = Math.Max(1, capacity);

    public int Count { get { lock (_lock) return _map.Count; } }

    /// <summary>Drops everything (glossary or language changed).</summary>
    public void Clear()
    {
        lock (_lock) { _map.Clear(); _lru.Clear(); }
    }

    public bool TryGet(string text, out string value)
    {
        var key = TextUtil.Key(text);
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    public void Set(string text, string translation)
    {
        var key = TextUtil.Key(text);
        if (key.Length == 0) return;
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _map.Remove(key);
            }
            var node = _lru.AddFirst((key, translation));
            _map[key] = node;
            while (_map.Count > _capacity)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }
}
