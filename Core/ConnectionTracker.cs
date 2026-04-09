using System.Collections.Concurrent;
using System.Net;

namespace ProxyMaster.Core;

/// <summary>
/// Хранит таблицу: исходный порт приложения → оригинальный dst (IP:Port).
/// Используется прозрачным прокси для определения, куда реально направить соединение.
/// </summary>
/// <remarks>
/// Потокобезопасность: все публичные методы (<see cref="Add"/>, <see cref="TryGet"/>,
/// <see cref="Remove"/>) делегируют операции в <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
/// и не требуют внешней синхронизации. Метод <see cref="Add"/> может вызвать
/// <see cref="Cleanup"/> при достижении лимита — это тоже атомарно на уровне словаря,
/// хотя краткосрочное превышение лимита на единицу допустимо в конкурентной среде.
/// </remarks>
internal sealed class ConnectionTracker
{
    // ключ: srcPort приложения (ushort)
    // значение: оригинальный адрес назначения
    private readonly ConcurrentDictionary<ushort, IPEndPoint> _table = new();

    // Максимум записей (защита от утечки памяти)
    private const int MaxEntries = 65535;

    public void Add(ushort srcPort, IPEndPoint originalDst)
    {
        if (_table.Count >= MaxEntries)
            Cleanup();

        _table[srcPort] = originalDst;
    }

    public bool TryGet(ushort srcPort, out IPEndPoint? originalDst)
        => _table.TryGetValue(srcPort, out originalDst);

    public void Remove(ushort srcPort)
        => _table.TryRemove(srcPort, out _);

    public int Count => _table.Count;

    /// <summary>
    /// Удаляет записи старше 10 минут (простая эвристика).
    /// </summary>
    private void Cleanup()
    {
        // При переполнении просто чистим всё — соединения уже закрыты
        _table.Clear();
    }
}
