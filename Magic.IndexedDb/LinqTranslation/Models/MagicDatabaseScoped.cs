using Magic.IndexedDb.LinqTranslation.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.LinqTranslation.Models;

internal class MagicDatabaseScoped : IMagicDatabaseScoped
{
    IndexedDbSet _IndexedDbSet;
    IndexedDbManager _Manager;
    public MagicDatabaseScoped(IndexedDbManager manager, IndexedDbSet indexedDbSet)
    {
        _IndexedDbSet = indexedDbSet;
        _Manager = manager;
    }

    public async Task DeleteAsync()
    {
        await _Manager.DeleteDbAsync(_IndexedDbSet.DatabaseName);
    }

    public async Task CloseAsync()
    {
        await _Manager.CloseDbAsync(_IndexedDbSet.DatabaseName);
    }

    public async Task<bool> IsOpenAsync()
    {
        return await _Manager.IsDbOpen(_IndexedDbSet.DatabaseName);
    }

    public async Task<bool> DoesExistAsync()
    {
        return await _Manager.DoesDbExist(_IndexedDbSet.DatabaseName);
    }

    public async Task OpenAsync()
    {
        await _Manager.OpenDbAsync(_IndexedDbSet.DatabaseName);
    }
}