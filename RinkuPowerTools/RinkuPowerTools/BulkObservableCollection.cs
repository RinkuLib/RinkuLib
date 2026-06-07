using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace RinkuPowerTools;

public class BulkObservableCollection<T> : ObservableCollection<T> {
    private bool _suppressNotification = false;

    public void AddRange(IEnumerable<T> items) {
        PauseListening();
        try {
            foreach (var item in items) {
                Add(item);
            }
        }
        finally {
            ResumeListening();
        }
    }
    public void PauseListening() => _suppressNotification = true;
    public void ResumeListening() {
        _suppressNotification = false;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e) {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }
}