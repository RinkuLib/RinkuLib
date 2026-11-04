using System.ComponentModel;
using BindingTree;
using System.Reflection;

namespace Rinku;

public interface ILifetimeOperations<TTarget> {
    static abstract void OnDisposed(TTarget target, EventHandler handler);
}

public static class NotificationPlans {
    public static ExecutorBuilder OnPropertyChanged<TSource>(this ExecutorBuilder builder, Variable owner, Variable<TSource> source, Variable<PropertyChangedEventHandler> handler) => Observe<TSource, INotifyPropertyChanged, PropertyChangedEventHandler>(builder, owner, source, handler);
    public static ExecutorBuilder OnListChanged<TSource>(this ExecutorBuilder builder, Variable owner, Variable<TSource> source, Variable<ListChangedEventHandler> handler) => Observe<TSource, IBindingList, ListChangedEventHandler>(builder, owner, source, handler);
    public static ExecutorBuilder OnTreeChanged<TItem>(this ExecutorBuilder builder, Variable owner, Variable<IBindingTreeSource<TItem>> source, Variable<Action<TreeChange<TItem>>> handler) => Observe<IBindingTreeSource<TItem>, IBindingTreeSource<TItem>, Action<TreeChange<TItem>>>(builder, owner, source, handler, typeof(NotificationOperations).GetMethod(nameof(NotificationOperations.AddTree))!.MakeGenericMethod(typeof(TItem)), typeof(NotificationOperations).GetMethod(nameof(NotificationOperations.RemoveTree))!.MakeGenericMethod(typeof(TItem)));
    public static ExecutorBuilder OnTreeActivation<TItem>(this ExecutorBuilder builder, Variable owner, Variable<IBindingTreeSource<TItem>> source, Variable<Action<ITreeEntry<TItem>>> handler) => Observe<IBindingTreeSource<TItem>, ITreeActivationSource<TItem>, Action<ITreeEntry<TItem>>>(builder, owner, source, handler, typeof(NotificationOperations).GetMethod(nameof(NotificationOperations.AddActivation))!.MakeGenericMethod(typeof(TItem)), typeof(NotificationOperations).GetMethod(nameof(NotificationOperations.RemoveActivation))!.MakeGenericMethod(typeof(TItem)));

    public static ExecutorBuilder Observe<TSource, TNotifications, THandler>(this ExecutorBuilder builder, Variable owner, Variable<TSource> source, Variable<THandler> handler, MethodInfo? add = null, MethodInfo? remove = null) where TNotifications : class where THandler : Delegate {
        Type[] parameters = [typeof(TNotifications), typeof(THandler)];
        add ??= typeof(NotificationOperations).GetMethod(nameof(NotificationOperations.Add), parameters)!;
        remove ??= typeof(NotificationOperations).GetMethod(nameof(NotificationOperations.Remove), parameters)!;
        var detach = ExecutorBuilder.Start<TNotifications?, THandler?, object?, EventArgs>(out var oldSource, out var oldHandler, out _, out _)
            .Then(remove, [oldSource, oldHandler])
            .Then(oldSource, (TNotifications?)null)
            .Then(oldHandler, (THandler?)null);
        return builder.Field<TNotifications?>(out var previous)
            .Field<THandler?>(out var previousHandler)
            .Then(remove, [previous, previousHandler])
            .Then(previous, () => source.Val as TNotifications)
            .Then(previousHandler, () => handler.Val)
            .Then(add, [previous, previousHandler])
            .Callback<EventHandler>(out var disposed, detach.ToScope(), previous, previousHandler)
            .Call(nameof(ILifetimeOperations<>.OnDisposed), [owner, disposed]);
    }
}
public static class NotificationOperations {
    public static void Add(ITreeNotifications? source, Action? handler) { if (source is not null) source.Updated += handler; }
    public static void Remove(ITreeNotifications? source, Action? handler) { if (source is not null) source.Updated -= handler; }
    public static void AddActivation<T>(ITreeActivationSource<T>? source, Action<ITreeEntry<T>>? handler) {
        if (source is not null) source.ActivationRequested += handler;
    }
    public static void RemoveActivation<T>(ITreeActivationSource<T>? source, Action<ITreeEntry<T>>? handler) {
        if (source is not null) source.ActivationRequested -= handler;
    }
    public static void AddTree<T>(IBindingTreeSource<T>? source, Action<TreeChange<T>>? handler) {
        if (source is not null) source.Changed += handler;
    }
    public static void RemoveTree<T>(IBindingTreeSource<T>? source, Action<TreeChange<T>>? handler) {
        if (source is not null) source.Changed -= handler;
    }
    public static void Add(INotifyPropertyChanged? source, PropertyChangedEventHandler? handler) {
        if (source is not null)
            source.PropertyChanged += handler;
    }
    public static void Remove(INotifyPropertyChanged? source, PropertyChangedEventHandler? handler) {
        if (source is not null)
            source.PropertyChanged -= handler;
    }
    public static void Add(IBindingList? source, ListChangedEventHandler? handler) {
        if (source is not null)
            source.ListChanged += handler;
    }
    public static void Remove(IBindingList? source, ListChangedEventHandler? handler) {
        if (source is not null)
            source.ListChanged -= handler;
    }
}
