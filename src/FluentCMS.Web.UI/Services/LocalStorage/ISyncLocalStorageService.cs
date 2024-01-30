namespace FluentCMS.Web.UI.Services.LocalStorage;

public interface ISyncLocalStorageService
{
    void Clear();
    T GetItem<T>(string key);
    string GetItemAsString(string key);
    string Key(int index);
    bool ContainKey(string key);
    int Length();
    IEnumerable<string> Keys();
    void RemoveItem(string key);
    void RemoveItems(IEnumerable<string> keys);
    void SetItem<T>(string key, T data);
    void SetItemAsString(string key, string data);

    event EventHandler<ChangingEventArgs> Changing;
    event EventHandler<ChangedEventArgs> Changed;
}
